using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Matawaka.Workbench.App;

internal static class Program
{
    private static readonly byte[] A = Encoding.UTF8.GetBytes("matawaka-v052-a\n");
    private static readonly byte[] B = Encoding.UTF8.GetBytes("matawaka-v052-second-artifact\n");
    private static string Sha(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _callback;
        public int Calls { get; private set; }
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) => _callback = callback;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
                throw new Exception("unexpected credential-bearing request state");
            return Task.FromResult(_callback(request));
        }
    }

    private static HttpResponseMessage Ok(byte[] bytes)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static string Root(string suffix)
    {
        var root = Path.Combine(Path.GetTempPath(), "matawaka-v052-extra-" + suffix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ws", "Workbench", "artifacts"));
        Directory.CreateDirectory(Path.Combine(root, "downloads"));
        return root;
    }

    private static async Task<ArtifactAcquisitionGrantV052> Grant(
        BoundedArtifactAcquisitionV052Service service,
        string workspace,
        ArtifactAcquisitionRequestV052 request)
    {
        var preview = service.Preview(workspace, request, default);
        if (preview.NetworkAccessPerformed || preview.FilesystemMutationPerformed || !preview.ReadyForExplicitAcquisitionAuthority)
            throw new Exception("preview effect boundary failed");
        return (await service.GrantAsync(workspace, preview, default)).Grant;
    }

    private static ArtifactAcquisitionItemV052 Item(string id, string uri, string name, byte[] bytes)
        => new(
            "artifact-" + id,
            uri,
            name,
            bytes.LongLength,
            Sha(bytes),
            new[] { new ArtifactAcquisitionRouteRuleV052("artifact.test", "/files/") });

    private static void MakeJunction(string junction, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("/d");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add($"mklink /J \"{junction}\" \"{target}\"");
        using var process = Process.Start(psi) ?? throw new Exception("failed to launch mklink");
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (process.ExitCode != 0 || !Directory.Exists(junction))
            throw new Exception("failed to create qualification junction: " + output);
        if ((new DirectoryInfo(junction).Attributes & FileAttributes.ReparsePoint) == 0)
            throw new Exception("qualification junction was not observed as reparse point");
    }

    public static async Task Main()
    {
        // Multi-artifact set: aggregate authority covers the exact immutable set and completes only when both verify.
        var multiRoot = Root("multi");
        var multiWorkspace = Path.Combine(multiRoot, "ws");
        var multiHandler = new Handler(request => request.RequestUri?.AbsolutePath switch
        {
            "/files/a.bin" => Ok(A),
            "/files/b.bin" => Ok(B),
            _ => throw new Exception("unexpected multi-artifact route")
        });
        using (var service = new BoundedArtifactAcquisitionV052Service(multiHandler))
        {
            var artifacts = new[]
            {
                Item("multi-a", "https://artifact.test/files/a.bin", "multi-a.bin", A),
                Item("multi-b", "https://artifact.test/files/b.bin", "multi-b.bin", B)
            };
            var request = new ArtifactAcquisitionRequestV052(
                BoundedArtifactAcquisitionV052Service.RequestSchema,
                "acqreq-multi",
                artifacts,
                Path.Combine(multiRoot, "downloads"),
                A.LongLength + B.LongLength,
                0,
                10,
                30);
            var grant = await Grant(service, multiWorkspace, request);
            var result = await service.AcquireAsync(multiWorkspace, grant, default);
            if (!result.Receipt.AllArtifactsSha256Verified || result.Receipt.Items.Count != 2 || multiHandler.Calls != 2)
                throw new Exception("multi-artifact set did not complete exactly");
            if (result.Receipt.NetworkBytesObserved != A.LongLength + B.LongLength)
                throw new Exception("aggregate network bytes not exact");
            foreach (var item in result.Receipt.Items)
                if (!item.ExpectedSizeMatched || !item.ExpectedSha256Matched || !item.FinalPathPromoted)
                    throw new Exception("multi-artifact member lacked exact verification/promotion");
        }

        // Allowed redirect is manually followed exactly once and remains under reviewed host/path policy.
        var redirectRoot = Root("allowed-redirect");
        var redirectWorkspace = Path.Combine(redirectRoot, "ws");
        var redirectHandler = new Handler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/files/start.bin")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("https://artifact.test/files/final.bin?token=opaque");
                return response;
            }
            if (request.RequestUri?.AbsolutePath == "/files/final.bin") return Ok(A);
            throw new Exception("unexpected allowed redirect route");
        });
        using (var service = new BoundedArtifactAcquisitionV052Service(redirectHandler))
        {
            var artifact = Item("allowed-redirect", "https://artifact.test/files/start.bin", "allowed-redirect.bin", A);
            var request = new ArtifactAcquisitionRequestV052(
                BoundedArtifactAcquisitionV052Service.RequestSchema,
                "acqreq-allowed-redirect",
                new[] { artifact },
                Path.Combine(redirectRoot, "downloads"),
                A.LongLength,
                1,
                10,
                30);
            var grant = await Grant(service, redirectWorkspace, request);
            var result = await service.AcquireAsync(redirectWorkspace, grant, default);
            var evidence = result.Receipt.Items.Single();
            if (!result.Receipt.AllArtifactsSha256Verified || redirectHandler.Calls != 2 || evidence.RedirectsObserved != 1)
                throw new Exception("allowed redirect was not exactly bounded/revalidated");
        }

        // Destination junction/reparse is refused during no-effect preview before any grant or network access.
        var reparseRoot = Root("reparse");
        var reparseWorkspace = Path.Combine(reparseRoot, "ws");
        var realDestination = Path.Combine(reparseRoot, "real-downloads");
        var junction = Path.Combine(reparseRoot, "junction-downloads");
        Directory.CreateDirectory(realDestination);
        MakeJunction(junction, realDestination);
        var reparseHandler = new Handler(_ => throw new Exception("reparse refusal must happen before network"));
        using (var service = new BoundedArtifactAcquisitionV052Service(reparseHandler))
        {
            var request = new ArtifactAcquisitionRequestV052(
                BoundedArtifactAcquisitionV052Service.RequestSchema,
                "acqreq-reparse",
                new[] { Item("reparse", "https://artifact.test/files/a.bin", "reparse.bin", A) },
                junction,
                A.LongLength,
                0,
                10,
                30);
            try
            {
                _ = service.Preview(reparseWorkspace, request, default);
                throw new Exception("junction/reparse destination was accepted");
            }
            catch (ArtifactAcquisitionExceptionV052 ex) when (ex.Classification == "DESTINATION_REPARSE_REFUSED")
            {
            }
            if (reparseHandler.Calls != 0 || File.Exists(Path.Combine(realDestination, "reparse.bin")))
                throw new Exception("reparse refusal leaked network/filesystem effect");
        }

        Console.WriteLine(
            "V052_ARTIFACT_ACQUISITION_EXTRA_PASS multiArtifact=true aggregateBytes=true allowedRedirect=true " +
            "redirectCountBound=true junctionReparseRefused=true previewNoEffect=true providerNeutral=true");
    }
}
