using System.Net;
using System.Security.Cryptography;
using System.Text;
using Matawaka.Workbench.App;

internal static class Program
{
    private static readonly byte[] GoodBytes = Encoding.UTF8.GetBytes("matawaka-v052-good-artifact\n");
    private static readonly string GoodSha = Convert.ToHexString(SHA256.HashData(GoodBytes)).ToLowerInvariant();

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public int Calls { get; private set; }

        public CountingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Headers.Authorization is not null) throw new Exception("authorization header unexpectedly present");
            if (request.Headers.Contains("Cookie")) throw new Exception("cookie header unexpectedly present");
            return await _handler(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] bytes)
        => new(status) { Content = new ByteArrayContent(bytes) };

    private static string NewRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "matawaka-v052-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ws", "Workbench", "artifacts"));
        Directory.CreateDirectory(Path.Combine(root, "downloads"));
        return root;
    }

    private static ArtifactAcquisitionRequestV052 Request(
        string destination,
        string suffix,
        long? expectedBytes = null,
        string? expectedSha = null,
        int maxRedirects = 0,
        int timeoutSeconds = 10,
        int ttlSeconds = 30,
        string source = "https://artifact.test/files/a.bin",
        IReadOnlyList<ArtifactAcquisitionRouteRuleV052>? routes = null)
    {
        var item = new ArtifactAcquisitionItemV052(
            "artifact-" + suffix,
            source,
            suffix + ".bin",
            expectedBytes ?? GoodBytes.LongLength,
            expectedSha ?? GoodSha,
            routes ?? new[] { new ArtifactAcquisitionRouteRuleV052("artifact.test", "/files/") });
        return new ArtifactAcquisitionRequestV052(
            BoundedArtifactAcquisitionV052Service.RequestSchema,
            "acqreq-" + suffix,
            new[] { item },
            destination,
            expectedBytes ?? GoodBytes.LongLength,
            maxRedirects,
            timeoutSeconds,
            ttlSeconds);
    }

    private static async Task<(ArtifactAcquisitionGrantV052 Grant, string Workspace)> GrantAsync(
        BoundedArtifactAcquisitionV052Service service,
        string root,
        ArtifactAcquisitionRequestV052 request)
    {
        var workspace = Path.Combine(root, "ws");
        var preview = service.Preview(workspace, request, default);
        if (preview.NetworkAccessPerformed || preview.FilesystemMutationPerformed || preview.ContainsArtifactBytes || !preview.ReadyForExplicitAcquisitionAuthority)
            throw new Exception("preview widened effects/authority");
        var granted = await service.GrantAsync(workspace, preview, default);
        if (granted.Receipt.NetworkAccessPerformed || granted.Receipt.ArtifactBytesWritten || granted.Receipt.BearerPlaintextPersisted || granted.Grant.DownloadPerformed)
            throw new Exception("grant creation performed forbidden effects");
        return (granted.Grant, workspace);
    }

    private static async Task RequireFailure(
        Func<Task> action,
        string classification)
    {
        try
        {
            await action();
            throw new Exception("expected failure did not occur: " + classification);
        }
        catch (ArtifactAcquisitionExceptionV052 ex) when (ex.Classification == classification)
        {
        }
    }

    public static async Task Main()
    {
        // Happy path: exact HTTPS request -> bytes -> size -> SHA -> atomic final promotion.
        var happyRoot = NewRoot("happy");
        var happyHandler = new CountingHandler((request, _) =>
        {
            if (request.RequestUri?.AbsoluteUri != "https://artifact.test/files/a.bin") throw new Exception("happy exact source drift");
            return Task.FromResult(Bytes(HttpStatusCode.OK, GoodBytes));
        });
        using (var service = new BoundedArtifactAcquisitionV052Service(happyHandler))
        {
            var req = Request(Path.Combine(happyRoot, "downloads"), "happy");
            var (grant, workspace) = await GrantAsync(service, happyRoot, req);
            var executed = await service.AcquireAsync(workspace, grant, default);
            var final = Path.Combine(happyRoot, "downloads", "happy.bin");
            if (happyHandler.Calls != 1 || !File.Exists(final) || File.ReadAllBytes(final).AsSpan().SequenceEqual(GoodBytes) is false)
                throw new Exception("happy artifact not materialized exactly");
            if (!executed.Receipt.AllArtifactsSha256Verified || executed.Receipt.State != "ACQUISITION_VERIFIED")
                throw new Exception("happy final verification state missing");
            if (executed.Receipt.ExtractionPerformed || executed.Receipt.ProcessExecutionPerformed || executed.Receipt.RuntimeStartPerformed ||
                executed.Receipt.BenchmarkPerformed || executed.Receipt.ModelRequestPerformed || executed.Receipt.GameAccessPerformed)
                throw new Exception("happy path widened post-download effects");
            var lease = await service.ReadLeaseStateAsync(workspace, grant.LeaseId, default);
            if (!lease.Completed || lease.RemainingCalls != 0 || lease.Failed)
                throw new Exception("happy lease did not terminate one-shot");
        }

        // Existing exact file is verified/reused without network access.
        var reuseRoot = NewRoot("reuse");
        File.WriteAllBytes(Path.Combine(reuseRoot, "downloads", "reuse.bin"), GoodBytes);
        var reuseHandler = new CountingHandler((_, _) => throw new Exception("network must not be called for exact existing artifact"));
        using (var service = new BoundedArtifactAcquisitionV052Service(reuseHandler))
        {
            var req = Request(Path.Combine(reuseRoot, "downloads"), "reuse");
            var (grant, workspace) = await GrantAsync(service, reuseRoot, req);
            var executed = await service.AcquireAsync(workspace, grant, default);
            if (reuseHandler.Calls != 0 || !executed.Receipt.AllArtifactsSha256Verified || executed.Receipt.NetworkAccessPerformed)
                throw new Exception("exact existing reuse performed network or failed verification");
            if (!executed.Receipt.Items.Single().ExistingVerifiedReused)
                throw new Exception("existing exact artifact not classified as reused");
        }

        // Different existing final file is never overwritten.
        var differentRoot = NewRoot("different");
        var differentPath = Path.Combine(differentRoot, "downloads", "different.bin");
        File.WriteAllText(differentPath, "different", Encoding.UTF8);
        var differentBefore = File.ReadAllBytes(differentPath);
        var differentHandler = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, GoodBytes)));
        using (var service = new BoundedArtifactAcquisitionV052Service(differentHandler))
        {
            var req = Request(Path.Combine(differentRoot, "downloads"), "different");
            var (grant, workspace) = await GrantAsync(service, differentRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "EXISTING_DIFFERENT_FILE");
            if (!File.ReadAllBytes(differentPath).AsSpan().SequenceEqual(differentBefore) || differentHandler.Calls != 0)
                throw new Exception("different existing file was mutated or network started");
        }

        // Wrong initial host/path is refused at preview, before authority/network.
        var policyRoot = NewRoot("policy");
        using (var service = new BoundedArtifactAcquisitionV052Service(new CountingHandler((_, _) => throw new Exception())))
        {
            var bad = Request(
                Path.Combine(policyRoot, "downloads"), "policy",
                source: "https://evil.test/files/a.bin",
                routes: new[] { new ArtifactAcquisitionRouteRuleV052("artifact.test", "/files/") });
            try
            {
                _ = service.Preview(Path.Combine(policyRoot, "ws"), bad, default);
                throw new Exception("bad source policy preview accepted");
            }
            catch (ArtifactAcquisitionExceptionV052 ex) when (ex.Classification == "SOURCE_POLICY_REFUSED") { }
        }

        // Redirect target outside exact reviewed routes is refused.
        var redirectRoot = NewRoot("redirect");
        var redirectHandler = new CountingHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.test/object.bin");
            return Task.FromResult(response);
        });
        using (var service = new BoundedArtifactAcquisitionV052Service(redirectHandler))
        {
            var req = Request(Path.Combine(redirectRoot, "downloads"), "redirect", maxRedirects: 1);
            var (grant, workspace) = await GrantAsync(service, redirectRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "REDIRECT_POLICY_REFUSED");
            if (File.Exists(Path.Combine(redirectRoot, "downloads", "redirect.bin"))) throw new Exception("redirect refusal promoted final file");
        }

        // Per-request MaxRedirects=0 must be authoritative, not merely preview decoration.
        var redirectZeroRoot = NewRoot("redirectzero");
        var redirectZeroHandler = new CountingHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/files/a.bin")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("https://artifact.test/files/b.bin");
                return Task.FromResult(response);
            }
            return Task.FromResult(Bytes(HttpStatusCode.OK, GoodBytes));
        });
        using (var service = new BoundedArtifactAcquisitionV052Service(redirectZeroHandler))
        {
            var req = Request(Path.Combine(redirectZeroRoot, "downloads"), "redirectzero", maxRedirects: 0);
            var (grant, workspace) = await GrantAsync(service, redirectZeroRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "REDIRECT_LIMIT_EXCEEDED");
            if (redirectZeroHandler.Calls != 1) throw new Exception("MaxRedirects=0 was not enforced on first redirect");
        }

        // Content above exact/total byte ceiling is aborted and never promoted.
        var ceilingRoot = NewRoot("ceiling");
        var tooLarge = GoodBytes.Concat(new byte[] { 0x42 }).ToArray();
        var ceilingHandler = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, tooLarge)));
        using (var service = new BoundedArtifactAcquisitionV052Service(ceilingHandler))
        {
            var req = Request(Path.Combine(ceilingRoot, "downloads"), "ceiling");
            var (grant, workspace) = await GrantAsync(service, ceilingRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "BYTE_CEILING_EXCEEDED");
            if (File.Exists(Path.Combine(ceilingRoot, "downloads", "ceiling.bin"))) throw new Exception("byte ceiling promoted final file");
        }

        // Short response reaches EOF but size mismatch leaves only unverified partial evidence.
        var sizeRoot = NewRoot("sizemismatch");
        var shortBytes = GoodBytes[..^1];
        var sizeHandler = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, shortBytes)));
        using (var service = new BoundedArtifactAcquisitionV052Service(sizeHandler))
        {
            var req = Request(Path.Combine(sizeRoot, "downloads"), "sizemismatch");
            var (grant, workspace) = await GrantAsync(service, sizeRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "SIZE_MISMATCH");
            var final = Path.Combine(sizeRoot, "downloads", "sizemismatch.bin");
            var partial = final + "." + grant.LeaseId + ".partial";
            if (File.Exists(final) || !File.Exists(partial)) throw new Exception("size mismatch did not preserve only partial evidence");
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "AUTHORITY_TERMINAL_FAILED");
            if (sizeHandler.Calls != 1) throw new Exception("failed one-shot authority retried network");
        }

        // Same-size hash mismatch leaves partial and never promotes.
        var hashRoot = NewRoot("hashmismatch");
        var wrongSameSize = GoodBytes.ToArray();
        wrongSameSize[0] ^= 0x01;
        var hashHandler = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, wrongSameSize)));
        using (var service = new BoundedArtifactAcquisitionV052Service(hashHandler))
        {
            var req = Request(Path.Combine(hashRoot, "downloads"), "hashmismatch");
            var (grant, workspace) = await GrantAsync(service, hashRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "HASH_MISMATCH");
            var final = Path.Combine(hashRoot, "downloads", "hashmismatch.bin");
            if (File.Exists(final) || !File.Exists(final + "." + grant.LeaseId + ".partial"))
                throw new Exception("hash mismatch final/partial boundary failed");
        }

        // Bearer mismatch refused before network and does not consume the real grant.
        var bearerRoot = NewRoot("bearer");
        var bearerHandler = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, GoodBytes)));
        using (var service = new BoundedArtifactAcquisitionV052Service(bearerHandler))
        {
            var req = Request(Path.Combine(bearerRoot, "downloads"), "bearer");
            var (grant, workspace) = await GrantAsync(service, bearerRoot, req);
            var forged = grant with { Bearer = grant.Bearer[..^1] + (grant.Bearer[^1] == '0' ? "1" : "0") };
            await RequireFailure(() => service.AcquireAsync(workspace, forged, default), "AUTHORITY_BEARER_MISMATCH");
            if (bearerHandler.Calls != 0) throw new Exception("forged bearer reached network");
            var executed = await service.AcquireAsync(workspace, grant, default);
            if (!executed.Receipt.AllArtifactsSha256Verified || bearerHandler.Calls != 1)
                throw new Exception("real bearer was damaged by forged attempt");
        }

        // TTL expiry refuses before network.
        var expiryRoot = NewRoot("expiry");
        var expiryHandler = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, GoodBytes)));
        using (var service = new BoundedArtifactAcquisitionV052Service(expiryHandler))
        {
            var req = Request(Path.Combine(expiryRoot, "downloads"), "expiry", ttlSeconds: 1);
            var (grant, workspace) = await GrantAsync(service, expiryRoot, req);
            await Task.Delay(1200);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "AUTHORITY_EXPIRED");
            if (expiryHandler.Calls != 0) throw new Exception("expired authority reached network");
        }

        // Per-request timeout must remain bound independently of a longer authority TTL.
        var timeoutRoot = NewRoot("timeout");
        var timeoutHandler = new CountingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            return Bytes(HttpStatusCode.OK, GoodBytes);
        });
        using (var service = new BoundedArtifactAcquisitionV052Service(timeoutHandler))
        {
            var req = Request(Path.Combine(timeoutRoot, "downloads"), "timeout", timeoutSeconds: 1, ttlSeconds: 10);
            var (grant, workspace) = await GrantAsync(service, timeoutRoot, req);
            await RequireFailure(() => service.AcquireAsync(workspace, grant, default), "NETWORK_TIMEOUT");
        }

        // Destination root inside Workbench Git repository is refused at preview.
        var insideRoot = NewRoot("insidegit");
        using (var service = new BoundedArtifactAcquisitionV052Service(new CountingHandler((_, _) => throw new Exception())))
        {
            var inside = Path.Combine(insideRoot, "ws", "Workbench", "downloads");
            Directory.CreateDirectory(inside);
            var req = Request(inside, "insidegit");
            try
            {
                _ = service.Preview(Path.Combine(insideRoot, "ws"), req, default);
                throw new Exception("inside-Git destination accepted");
            }
            catch (ArtifactAcquisitionExceptionV052 ex) when (ex.Classification == "DESTINATION_POLICY_REFUSED") { }
        }

        // Exact destination serialization: two concurrent leases for same final path cannot corrupt each other.
        var concurrentRoot = NewRoot("concurrent");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerA = new CountingHandler(async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Bytes(HttpStatusCode.OK, GoodBytes);
        });
        var handlerB = new CountingHandler((_, _) => Task.FromResult(Bytes(HttpStatusCode.OK, GoodBytes)));
        using (var serviceA = new BoundedArtifactAcquisitionV052Service(handlerA))
        using (var serviceB = new BoundedArtifactAcquisitionV052Service(handlerB))
        {
            var req = Request(Path.Combine(concurrentRoot, "downloads"), "concurrent");
            var (grantA, workspace) = await GrantAsync(serviceA, concurrentRoot, req);
            var previewB = serviceB.Preview(workspace, req with { RequestId = "acqreq-concurrentb" }, default);
            var grantB = (await serviceB.GrantAsync(workspace, previewB, default)).Grant;
            var runA = serviceA.AcquireAsync(workspace, grantA, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await RequireFailure(() => serviceB.AcquireAsync(workspace, grantB, default), "ACQUISITION_DESTINATION_BUSY");
            if (handlerB.Calls != 0) throw new Exception("busy destination reached second network request");
            release.TrySetResult();
            _ = await runA;
        }

        Console.WriteLine(
            "V052_ARTIFACT_ACQUISITION_RUNTIME_PASS happy=true existingReuse=true noOverwrite=true sourcePolicy=true " +
            "redirectPolicy=true maxRedirectsBound=true byteCeiling=true sizeMismatch=true hashMismatch=true " +
            "bearer=true expiry=true timeoutBound=true externalToGit=true concurrency=true oneShot=true " +
            "noExtraction=true noExecution=true noRuntime=true noBenchmark=true noModelRequest=true noGame=true secrets=false");
    }
}
