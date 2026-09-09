using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Matawaka.V0601PublicationPreflight;
namespace Matawaka.V0601FixedPublisher;
internal static class NetworkTests
{
    private static readonly string Git=GitRead.Locate();
    private const string Dummy="github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_NETWORK_FIXTURE";
    private static readonly List<object> Results=new();
    private static async Task<byte[]> Backend(string cwd,string[] args,byte[]? input=null,string? protocol=null)
    {
        var p=new ProcessStartInfo(Git){WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var k in p.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(k);
        p.Environment["GIT_CONFIG_NOSYSTEM"]="1";p.Environment["GIT_CONFIG_GLOBAL"]=OperatingSystem.IsWindows()?"NUL":"/dev/null";if(protocol is not null)p.Environment["GIT_PROTOCOL"]=protocol;
        foreach(var arg in args)p.ArgumentList.Add(arg);using var c=Process.Start(p)!;using var output=new MemoryStream();var o=c.StandardOutput.BaseStream.CopyToAsync(output);var e=c.StandardError.ReadToEndAsync();if(input is not null)await c.StandardInput.BaseStream.WriteAsync(input);c.StandardInput.BaseStream.Close();await Task.WhenAll(o,e,c.WaitForExitAsync());Safe.Need(c.ExitCode==0,"FIXTURE_BACKEND_FAILED_"+c.ExitCode);return output.ToArray();
    }
    private sealed class Server:IDisposable
    {
        private readonly TcpListener listener=new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource stop=new();
        private readonly RSA key=RSA.Create(2048);
        private readonly X509Certificate2 cert;
        private readonly string remote;
        private readonly string mode;
        private readonly Task loop;
        internal int Requests,ReceiveRequests,ReceiveRpcRequests,PublicCredentialLeaks,AuthenticationRefusals,TlsRefusals;
        internal bool ReceivedUpdate,RedirectFollowed;
        internal string? ServerFailure;
        internal string Endpoint {get;}
        internal string CaPem=>cert.ExportCertificatePem();
        internal Server(string repo,string behavior)
        {
            remote=repo;mode=behavior;
            var req=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");san.AddIpAddress(IPAddress.Loopback);req.CertificateExtensions.Add(san.Build());
            using var ephemeral=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(1));
            // Schannel server credentials on Windows cannot use some ephemeral private keys.
            // Materialize only this generated fixture key for the certificate lifetime; no trust-store installation.
            cert=X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.Exportable);
            listener.Start();Endpoint="https://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/Matawaka/workbench.git";loop=Run();
        }
        private async Task Run()
        {
            while(!stop.IsCancellationRequested){try{using var client=await listener.AcceptTcpClientAsync(stop.Token);using var ssl=new SslStream(client.GetStream(),false);await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=cert,ClientCertificateRequired=false},stop.Token);await Request(ssl);}catch(Exception e) when(e is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException or SocketException){if(e is InvalidDataException)ServerFailure=e.Message;if(e is System.Security.Authentication.AuthenticationException){TlsRefusals++;ServerFailure=e.Message+" / "+e.InnerException?.Message;}}catch(Exception e){ServerFailure=e.GetType().Name;return;}}
        }
        private async Task<string> Line(Stream s)
        {
            var result=new List<byte>();var b=new byte[1];
            while(result.Count<=65536){var n=await s.ReadAsync(b,stop.Token);if(n==0)throw new IOException();result.Add(b[0]);if(result.Count>1&&result[^2]==13&&result[^1]==10)return Encoding.ASCII.GetString(result.Take(result.Count-2).ToArray());}throw new IOException();
        }
        private async Task Reply(Stream s,string status,string type,byte[] body,string extra="")
        {
            var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n"+extra+"\r\n");await s.WriteAsync(header,stop.Token);await s.WriteAsync(body,stop.Token);await s.FlushAsync(stop.Token);
        }
        private async Task Request(Stream s)
        {
            var first=await Line(s);var parts=first.Split(' ');if(parts.Length!=3)throw new IOException();var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string l;
            while((l=await Line(s)).Length>0){var colon=l.IndexOf(':');if(colon<1)throw new IOException();headers[l[..colon]]=l[(colon+1)..].Trim();if(headers.Count>100)throw new IOException();}
            Requests++;var path=parts[1];var receive=path.Contains("git-receive-pack",StringComparison.Ordinal);var auth=headers.GetValueOrDefault("Authorization");
            if(receive&&parts[0]=="POST")ReceiveRpcRequests++;
            if(!receive&&auth is not null)PublicCredentialLeaks++;
            if(path.Contains("/leak",StringComparison.Ordinal))RedirectFollowed=true;
            if(mode=="redirect"){await Reply(s,"302 Found","text/plain",Array.Empty<byte>(),"Location: "+Endpoint+"/leak\r\n");return;}
            if(mode=="rate-limit"){await Reply(s,"429 Too Many Requests","text/plain",Array.Empty<byte>(),"Retry-After: 0\r\n");return;}
            var expectedAuthValue=Credential.Header(Dummy)["Authorization: ".Length..];
            if(receive){ReceiveRequests++;if(auth!=expectedAuthValue){AuthenticationRefusals++;await Reply(s,"401 Unauthorized","text/plain",Array.Empty<byte>(),"WWW-Authenticate: Basic realm=fixture\r\n");return;}}
            if(mode=="post-readback-failure"&&ReceivedUpdate&&!receive){await Reply(s,"503 Service Unavailable","text/plain",Array.Empty<byte>());return;}
            if(headers.GetValueOrDefault("Expect")?.Equals("100-continue",StringComparison.OrdinalIgnoreCase)==true)await s.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(),stop.Token);
            byte[] body=Array.Empty<byte>();
            if(headers.TryGetValue("Content-Length",out var nText)){var n=int.Parse(nText,System.Globalization.CultureInfo.InvariantCulture);if(n<0||n>4*1024*1024)throw new IOException();body=new byte[n];await s.ReadExactlyAsync(body,stop.Token);}
            else if(headers.GetValueOrDefault("Transfer-Encoding")?.Contains("chunked",StringComparison.OrdinalIgnoreCase)==true){using var ms=new MemoryStream();while(true){var n=Convert.ToInt32((await Line(s)).Split(';')[0],16);if(n==0){while((await Line(s)).Length>0){}break;}if(n<0||ms.Length+n>4*1024*1024)throw new IOException();var chunk=new byte[n];await s.ReadExactlyAsync(chunk,stop.Token);ms.Write(chunk);if(await Line(s)!="")throw new IOException();}body=ms.ToArray();}
            var service=receive?"receive-pack":"upload-pack";var advertise=parts[0]=="GET"&&path.Contains("/info/refs?service=git-",StringComparison.Ordinal);
            var args=advertise?new[]{service,"--stateless-rpc","--advertise-refs",remote}:new[]{service,"--stateless-rpc",remote};
            var bytes=await Backend(Path.GetDirectoryName(remote)!,args,advertise?null:body,headers.GetValueOrDefault("Git-Protocol"));
            if(receive&&!advertise){ReceivedUpdate=true;if(mode=="lost-receive-response")return;}
            if(advertise){var label=Encoding.ASCII.GetBytes("# service=git-"+service+"\n");var pre=Encoding.ASCII.GetBytes((label.Length+4).ToString("x4")+Encoding.ASCII.GetString(label)+"0000");bytes=pre.Concat(bytes).ToArray();}
            await Reply(s,"200 OK","application/x-git-"+service+(advertise?"-advertisement":"-result"),bytes);
        }
        public void Dispose(){stop.Cancel();listener.Stop();try{loop.Wait(3000);}catch{}cert.Dispose();key.Dispose();stop.Dispose();}
    }
    private static async Task Case(string mode,bool trust=true)
    {
        using var f=new V2Tests.F();var temp=Path.Combine(Path.GetTempPath(),"publisher-https-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);var remote=Path.Combine(temp,"remote.git");
        try{
            await Backend(temp,new[]{"init","--bare","-q",remote});await Backend(f.Root,new[]{"push","-q",new Uri(remote+Path.DirectorySeparatorChar).AbsoluteUri,f.Proof.Accepted.Second+":"+PublishPlan.MainRef});
            using var server=new Server(remote,mode);var ca=Path.Combine(temp,"ca.pem");File.WriteAllText(ca,server.CaPem,new UTF8Encoding(false));if(trust)Environment.SetEnvironmentVariable("FIXTURE_TLS_CA",ca);
            var expected=await f.Read();const string pre="artifacts/publication-v0601/network-preflight.json";var b=Files.Json(new{Schema="matawaka.workbench-v0601-publication-preflight/v0.2",Status=V2.Status,Snapshot=expected,PublicationAuthorityCreated=false,RetryAuthorityCreated=false,NetworkReadPerformed=false,RemoteWritePerformed=false});Files.New(Safe.Under(f.Root,pre),b);
            var p=new PublishPlan(f.Proof,Git,server.Endpoint,Path.Combine(temp,"stage"),Path.Combine(f.Root,"artifacts/publication-v0601"),new(pre,Safe.Hash(b),b.Length),Path.GetDirectoryName(Path.GetDirectoryName(Git))!);
            var before=V2.StoreDigest(f.Root);using var sourceLocks=new ReadLocks();sourceLocks.Tree(Safe.Under(f.Root,".git"));
            if(mode is "normal" or "lost-receive-response" or "post-readback-failure"){
                bool rejected=false;try{await FixedPublisher.Execute(p,expected,f.Read,PublishPlan.Confirm,TimeSpan.Zero,Credential.Header(Dummy),"fixture","fixture");}catch(InvalidDataException ex){
                    rejected=true;Console.WriteLine("FIXTURE_ENGINE_REFUSAL "+ex.Message+" requests="+server.Requests+" receive="+server.ReceiveRequests+" authRefusals="+server.AuthenticationRefusals+" tlsRefusals="+server.TlsRefusals+" serverFailure="+server.ServerFailure);
                    if(mode=="normal"){
                        if(File.Exists(p.Outcome))Console.WriteLine(Safe.Utf8.GetString(Safe.Read(p.Outcome)));
                        var diagnostic=new IsolatedTransport(p with{StageRoot=Path.Combine(temp,"read-diagnostic")});diagnostic.Create();var detail=await diagnostic.Read();
                        var text=Encoding.UTF8.GetString(detail.Error).Replace(Dummy,"<redacted>").Replace(Credential.Header(Dummy),"<redacted>");Console.WriteLine("FIXTURE_PUBLIC_READ_NATIVE_ERROR exit="+detail.ExitCode+" "+text[..Math.Min(text.Length,4096)]);
                    }
                }
                Safe.Need(rejected==(mode!="normal"),"HTTPS_ORCHESTRATION_OUTCOME");
                var main=Encoding.ASCII.GetString(await Backend(temp,new[]{"--git-dir="+remote,"rev-parse",PublishPlan.MainRef})).Trim();Safe.Need(main==f.Proof.Accepted.Head&&server.ReceivedUpdate,"HTTPS_REAL_RECEIVE_PACK_UPDATED");
                Safe.Need(server.PublicCredentialLeaks==0&&server.ReceiveRequests==2&&server.ReceiveRpcRequests==1&&server.AuthenticationRefusals==0,"CREDENTIAL_EXACT_ONE_RECEIVE_RPC_NO_RETRY");
                if(rejected){var outcome=Safe.Parse(Safe.Read(p.Outcome));Safe.False(outcome,"RemoteMutationProvenAbsent","PublicationSuccessClaimed","RetryAuthorized");Safe.True(outcome,"PushMayHaveStarted");bool replay=false;try{await FixedPublisher.Execute(p,expected,f.Read,PublishPlan.Confirm,TimeSpan.Zero,Credential.Header(Dummy),"fixture","fixture");}catch(InvalidDataException){replay=true;}Safe.Need(replay,"HTTPS_UNCERTAIN_REPLAY_REFUSED");}
            }else{
                var t=new IsolatedTransport(p);t.Create();var response=await t.Push(Credential.Header(Dummy));Safe.Need(response.ExitCode!=0,"HTTPS_HOSTILE_RESPONSE_REFUSED");
                await Task.Delay(100);Safe.Need(!server.RedirectFollowed,"CREDENTIAL_REDIRECT_NOT_FOLLOWED");Safe.Need(server.Requests==(trust?1:0),"NO_HTTP_RETRY_OR_UNTRUSTED_TLS_DISCLOSURE");
            }
            Safe.Need(V2.StoreDigest(f.Root)==before,"HTTPS_SOURCE_GIT_STORE_UNCHANGED");
            Results.Add(new{Id="https-"+mode+(trust?"":"-untrusted-ca"),Passed=true,Requests=server.Requests,ReceiveRequests=server.ReceiveRequests,ReceiveRpcRequests=server.ReceiveRpcRequests,PublicCredentialLeaks=server.PublicCredentialLeaks,AuthenticationRefusals=server.AuthenticationRefusals,RedirectFollowed=server.RedirectFollowed,SourceGitStoreUnchanged=true,ExistingSourceGitFilesReadLocked=true});Console.WriteLine("PASS HTTPS "+mode+" trust="+trust);
        }finally{Environment.SetEnvironmentVariable("FIXTURE_TLS_CA",null);try{Directory.Delete(temp,true);}catch{}}
    }
    internal static async Task Main()
    {
        using var tools=new ReadLocks();var pins=ToolTree.Embedded();ToolTree.Verify(Path.GetDirectoryName(Path.GetDirectoryName(Git))!,pins,tools);Console.WriteLine("EXACT_MINGIT_DISTRIBUTION_READ_LOCKED "+pins.Length);
        await Case("normal");await Case("redirect");await Case("rate-limit");await Case("normal-untrusted",false);await Case("lost-receive-response");await Case("post-readback-failure");
        File.WriteAllBytes("publisher-https-qualification.json",Files.Json(new{Schema="matawaka.workbench-fixed-publisher-https-qualification/v0.1",Passed=true,Checks=Results,RealNativeGitSmartHttps=true,ExactMinGitFilesReadLocked=pins.Length,OnlyLoopbackFixtures=true,ProductionRemoteContacted=false,RealCredentialsUsed=false,CertificateInstalledInTrustStore=false,FixturePrivateKeyMaterialization="Windows-compatible fixture-only certificate lifetime; no production TLS override",ProductionPublicationPerformed=false}));Console.WriteLine("PUBLISHER_HTTPS_QUALIFICATION_PASS "+Results.Count);
    }
}
