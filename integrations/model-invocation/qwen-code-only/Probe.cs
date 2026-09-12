using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matawaka.Workbench.App;
using static Matawaka.Workbench.App.QwenIsolatedOneShotAdapter;

internal static class Program
{
    private static int passed, failed;
    private static ModelInvocationSourceBinding source = null!;
    private static LocalModelInvocationRequestV055 Request(ModelInvocationSourceBinding s) => new(
        "matawaka.local-model-invocation-request/v0.55", s.BindingId, "fixture-runtime.json", new string('b',64),
        "llama-cli.exe", s.ExpectedExecutableSha256, "fixture-model.json", new string('d',64),
        "synthetic-model", s.ExpectedModelSha256, s.InvocationProfileId, "Synthetic cautious hint request.",
        s.MaxRequestBytes, s.MaxStdoutBytes, s.MaxStderrBytes, s.MaxOutputChars, s.MaxOutputTokens,
        s.TimeoutSeconds, s.TtlSeconds);
    private static QwenIsolationRequirements Policy() => new("matawaka.qwen-isolation-requirements/v0.1",
        99, false, 1, false, false, false, false, false);
    private static QwenCodeOnlyPlan Plan(int budget = 30000) => Review(source, Request(source), Policy(), budget);
    private static QwenSyntheticHostEvidence Host(QwenCodeOnlyPlan p) => new("SYNTHETIC_TEST_ONLY",
        p.ReviewDigest, p.ExecutableDigest, p.ModelDigest, true, true, true, false, false);
    private static void Assert(bool condition) { if (!condition) throw new Exception("ASSERTION"); }
    private static void Reject(string expected, Action action)
    {
        try { action(); }
        catch (QwenAdapterRefusal e) when (e.Message == expected) { return; }
        throw new Exception("EXPECTED_REFUSAL_NOT_OBSERVED");
    }
    private static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + " " + e.GetType().Name); }
    }
    private static void LeaseTest(string name, Action<QwenOneShotRehearsal,QwenCodeOnlyPlan,QwenSyntheticHostEvidence> action)
        => Test(name, () => { var p = Plan(); using var lease = new QwenOneShotRehearsal(p,1000); action(lease,p,Host(p)); });
    private static void Begin(QwenOneShotRehearsal lease, QwenSyntheticHostEvidence h) => lease.Begin(lease.Ticket,1000,h);
    private static void Output(QwenOneShotRehearsal lease) => lease.ObserveStream(1001, Encoding.UTF8.GetBytes("Synthetic hint."));
    private static JsonObject Json<T>(T value) => JsonNode.Parse(JsonSerializer.Serialize(value))!.AsObject();

    public static int Main(string[] args)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(args[0]));
        source = ParseSource(fixture.RootElement.GetProperty("model_source_binding").GetRawText());
        Test("selected-profile-preserved-not-permit", () => {
            var p=Plan(); Assert(!p.ModelRequestAuthorized && !p.DisplayPermitCreated && !p.ProcessNetworkIsolationProven);
            Assert(p.EffectiveBudgetMilliseconds==30000 && p.ModelDigest==ModelSha256);
            Assert(p.Status=="CODE_ONLY_PROFILE_REVIEW_NOT_LIVE_ADMISSION");
            Reject("HOST_ISOLATION_PROVIDER_NOT_QUALIFIED",()=>RequireLiveAdmission(p));
        });
        Test("legacy-fixture-is-not-rewritten",()=>Assert(source.InvocationProfileId==ProfileId && source.RequireProcessNetworkIsolation));
        Test("minimum-deadline",()=>Assert(Plan(1500).EffectiveBudgetMilliseconds==1500));
        Test("shorter-model-deadline",()=> { var s=source with {TimeoutSeconds=1}; Assert(Review(s,Request(s),Policy(),30000).EffectiveBudgetMilliseconds==1000); });
        Test("shorter-ttl",()=> { var s=source with {TtlSeconds=1}; Assert(Review(s,Request(s),Policy(),30000).EffectiveBudgetMilliseconds==1000); });
        Test("review-deterministic",()=>Assert(Plan().ReviewDigest==Plan().ReviewDigest));
        Test("request-text-binds-review",()=>Assert(Plan().ReviewDigest!=Review(source,Request(source) with {RequestUtf8="Different synthetic request."},Policy(),30000).ReviewDigest));
        Test("deadline-binds-review",()=>Assert(Plan().ReviewDigest!=Plan(29999).ReviewDigest));
        Test("manifest-binds-review",()=>Assert(Plan().ReviewDigest!=Review(source,Request(source) with {RuntimeTreeManifestSha256=new string('e',64)},Policy(),30000).ReviewDigest));

        var sourceCases = new (string,ModelInvocationSourceBinding)[] {
            ("SCHEMA",source with {Schema="next"}), ("SOURCE_NOT_AUTHORITY",source with {MaxCalls=2}),
            ("SOURCE_NOT_AUTHORITY",source with {SourceAuthorityEffect="EXECUTE"}),
            ("REQUEST_BINDING",source with {BindingId="x\n"}),
            ("SOURCE_PROVENANCE",source with {SourceFrontier=new string('0',40)}),
            ("SOURCE_PROVENANCE",source with {SourceRepository="owner/repo\n"}),
            ("SOURCE_PROVENANCE",source with {SourceArtifactSha256="bad"}),
            ("SOURCE_PROVENANCE",source with {RequestEnvelopeSha256=null!}),
            ("PROFILE_SUBSTITUTION",source with {InvocationProfileId="FIXTURE_STDIO_V1"}),
            ("ARTIFACT_BINDING",source with {ExpectedExecutableSha256=new string('0',64)}),
            ("ARTIFACT_BINDING",source with {ExpectedModelSha256=new string('e',64)}),
            ("ISOLATION_REQUIREMENTS",source with {RequireProcessNetworkIsolation=false}),
            ("BOUND_WIDENING",source with {MaxRequestBytes=65537}),
            ("BOUND_WIDENING",source with {MaxStdoutBytes=65537}),
            ("BOUND_WIDENING",source with {MaxStderrBytes=65537}),
            ("BOUND_WIDENING",source with {MaxOutputChars=601}),
            ("BOUND_WIDENING",source with {MaxOutputTokens=161}),
            ("BOUND_WIDENING",source with {TimeoutSeconds=61}),
            ("BOUND_WIDENING",source with {TtlSeconds=61}),
            ("BOUND_WIDENING",source with {MaxOutputTokens=0}) };
        for(int i=0;i<sourceCases.Length;i++) { var (code,s)=sourceCases[i]; Test("source-refusal-"+i,()=>Reject(code,()=>Review(s,Request(s),Policy(),30000))); }
        var policies = new[] {Policy() with {RequestedGpuLayers=0},Policy() with {CpuFallbackAllowed=true},
            Policy() with {MaximumProcesses=2},Policy() with {NetworkAllowed=true},Policy() with {ServerAllowed=true},
            Policy() with {ToolsAllowed=true},Policy() with {GameAccessAllowed=true},Policy() with {DisplayAllowed=true}};
        for(int i=0;i<policies.Length;i++) { var p=policies[i]; Test("isolation-policy-"+i,()=>Reject("ISOLATION_REQUIREMENTS",()=>Review(source,Request(source),p,30000))); }
        foreach(int ms in new[]{0,-1,30001,int.MaxValue}) Test("foreground-"+ms,()=>Reject("FOREGROUND_BUDGET",()=>Plan(ms)));
        Test("request-bounds-mismatch",()=>Reject("BOUND_BINDING",()=>Review(source,Request(source) with {MaxOutputTokens=159},Policy(),30000)));
        Test("executable-substitution",()=>Reject("ARTIFACT_BINDING",()=>Review(source,Request(source) with {ExpectedExecutableSha256=new string('e',64)},Policy(),30000)));
        Test("server-executable-refused",()=>Reject("ARTIFACT_LOCATOR",()=>Review(source,Request(source) with {ExecutableRelativePath="llama-server.exe"},Policy(),30000)));
        Test("prompt-unicode-bytes",()=>Reject("REQUEST_UTF8_BOUND",()=>Review(source,Request(source) with {RequestUtf8=new string('я',40000)},Policy(),30000)));
        Test("prompt-invalid-surrogate",()=>Reject("UTF8_INVALID",()=>Review(source,Request(source) with {RequestUtf8="\ud800"},Policy(),30000)));
        Test("prompt-nul",()=>Reject("REQUEST_TEXT",()=>Review(source,Request(source) with {RequestUtf8="a\0b"},Policy(),30000)));
        Test("locator-invalid-surrogate",()=>Reject("UTF8_INVALID",()=>Review(source,Request(source) with {RuntimeTreeManifestPath="\ud800"},Policy(),30000)));
        Test("caller-isolation-bool-not-accepted",()=> {var j=Json(source);j["ProcessNetworkIsolationProven"]=true;Reject("ENVELOPE_KEYS",()=>ParseSource(j.ToJsonString()));});
        Test("source-missing-field",()=> {var j=Json(source);j.Remove("MaxCalls");Reject("ENVELOPE_KEYS",()=>ParseSource(j.ToJsonString()));});
        Test("source-duplicate-field",()=> {var j=JsonSerializer.Serialize(source);Reject("ENVELOPE_KEYS",()=>ParseSource(j.Insert(1,"\"MaxCalls\":1,")));});
        Test("source-reordered",()=> {var j=Json(source);var reverse=new JsonObject();foreach(var kv in j.Reverse())reverse[kv.Key]=kv.Value?.DeepClone();Assert(Plan().ReviewDigest==Review(ParseSource(reverse.ToJsonString()),Request(source),Policy(),30000).ReviewDigest);});
        Test("request-extra-field",()=> {var j=Json(Request(source));j["NetworkAllowed"]=true;Reject("ENVELOPE_KEYS",()=>ParseRequest(j.ToJsonString()));});
        Test("policy-extra-field",()=> {var j=Json(Policy());j["IsolationProven"]=true;Reject("ENVELOPE_KEYS",()=>ParseRequirements(j.ToJsonString()));});
        Test("bool-is-not-integer",()=> {var j=Json(source);j["MaxCalls"]=true;Reject("ENVELOPE_MALFORMED",()=>ParseSource(j.ToJsonString()));});
        Test("oversize-json",()=>Reject("ENVELOPE_SIZE",()=>ParseSource(new string(' ',262145))));
        Test("array-json",()=>Reject("ENVELOPE_OBJECT",()=>ParseSource("[]")));
        Test("invalid-json",()=>Reject("ENVELOPE_MALFORMED",()=>ParseSource("{")));

        LeaseTest("one-shot-completion",(l,p,h)=>{Begin(l,h);Output(l);var c=l.Finish(1002,0,true,4,h);Assert(c.Text=="Synthetic hint." && !c.ModelRequestPerformed && !c.ProcessNetworkIsolationProven && !c.ContentReviewComplete && !c.DisplayPermitCreated);Reject("NOT_RUNNING",()=>l.Finish(1002,0,true,4,h));Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));});
        LeaseTest("rollback-before-issued-consumes",(l,p,h)=>{Reject("TEMPORAL_REFUSAL",()=>l.Begin(l.Ticket,999,h));Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));});
        LeaseTest("expiry-equality-consumes",(l,p,h)=>{Reject("TEMPORAL_REFUSAL",()=>l.Begin(l.Ticket,31000,h));Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));});
        LeaseTest("forward-jump-consumes",(l,p,h)=>{Reject("TEMPORAL_REFUSAL",()=>l.Begin(l.Ticket,long.MaxValue,h));Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));});
        LeaseTest("rollback-during-running",(l,p,h)=>{Begin(l,h);Output(l);Reject("TEMPORAL_REFUSAL",()=>l.ObserveStream(1000,Array.Empty<byte>()));Assert(l.State=="REFUSED_SYNTHETIC_ONLY");});
        LeaseTest("completion-at-expiry",(l,p,h)=>{Begin(l,h);Output(l);Reject("TEMPORAL_REFUSAL",()=>l.Finish(31000,0,true,4,h));});
        LeaseTest("completion-before-expiry",(l,p,h)=>{Begin(l,h);Output(l);l.Finish(30999,0,true,4,h);});
        LeaseTest("equal-clock-observations",(l,p,h)=>{Begin(l,h);l.ObserveStream(1000,Encoding.UTF8.GetBytes("x"));l.Finish(1000,0,true,1,h);});
        LeaseTest("cancel-before-start",(l,p,h)=>{Reject("REQUEST_CANCELLED_OR_SUPERSEDED",()=>l.Begin(l.Ticket,1000,h,cancelled:true));Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));});
        LeaseTest("superseded-before-start",(l,p,h)=>Reject("REQUEST_CANCELLED_OR_SUPERSEDED",()=>l.Begin(l.Ticket,1000,h,superseded:true)));
        LeaseTest("cancel-running",(l,p,h)=>{Begin(l,h);Output(l);l.Cancel();Reject("NOT_RUNNING",()=>l.Finish(1002,0,true,4,h));});
        LeaseTest("foreign-ticket",(l,p,h)=>{using var other=new QwenOneShotRehearsal(p,1000);Reject("TICKET_UNAVAILABLE",()=>l.Begin(other.Ticket,1000,h));Begin(l,h);});
        LeaseTest("reopen-cannot-reuse-ticket",(l,p,h)=>{using var other=new QwenOneShotRehearsal(p,1000);Reject("TICKET_UNAVAILABLE",()=>other.Begin(l.Ticket,1000,h));});
        LeaseTest("concurrent-begin-one-winner",(l,p,h)=>{int wins=0,refusals=0;Parallel.For(0,20,_=>{try{Begin(l,h);Interlocked.Increment(ref wins);}catch(QwenAdapterRefusal e)when(e.Message=="TICKET_UNAVAILABLE"){Interlocked.Increment(ref refusals);}});Assert(wins==1&&refusals==19);});
        LeaseTest("concurrent-boundary-no-revival",(l,p,h)=>{int wins=0;Parallel.For(0,20,i=>{try{l.Begin(l.Ticket,i%2==0?31000:30999,h);Interlocked.Increment(ref wins);}catch(QwenAdapterRefusal){}});Assert(wins<=1);Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));});
        LeaseTest("simulated-network-refusal",(l,p,h)=>Reject("SYNTHETIC_ISOLATION_REFUSAL",()=>Begin(l,h with {NetworkDenied=false})));
        LeaseTest("simulated-child-process-refusal",(l,p,h)=>Reject("SYNTHETIC_ISOLATION_REFUSAL",()=>Begin(l,h with {SingleOwnedProcess=false})));
        LeaseTest("simulated-gpu-offload-refusal",(l,p,h)=>Reject("SYNTHETIC_ISOLATION_REFUSAL",()=>Begin(l,h with {AllModelLayersOnGpu=false})));
        LeaseTest("simulated-cpu-fallback-refusal",(l,p,h)=>Reject("SYNTHETIC_ISOLATION_REFUSAL",()=>Begin(l,h with {CpuFallback=true})));
        LeaseTest("simulated-tools-refusal",(l,p,h)=>Reject("SYNTHETIC_ISOLATION_REFUSAL",()=>Begin(l,h with {ToolsEnabled=true})));
        LeaseTest("fake-real-attestation-refused",(l,p,h)=>Reject("SYNTHETIC_BINDING",()=>Begin(l,h with {EvidenceClass="REAL_OS_PROOF"})));
        LeaseTest("review-drift-refused",(l,p,h)=>Reject("SYNTHETIC_BINDING",()=>Begin(l,h with {ReviewDigest=new string('0',64)})));
        LeaseTest("binary-drift-refused",(l,p,h)=>Reject("SYNTHETIC_BINDING",()=>Begin(l,h with {ExecutableDigest=new string('e',64)})));
        LeaseTest("model-drift-refused",(l,p,h)=>Reject("SYNTHETIC_BINDING",()=>Begin(l,h with {ModelDigest=new string('e',64)})));
        LeaseTest("terminal-cpu-fallback-refused",(l,p,h)=>{Begin(l,h);Output(l);Reject("SYNTHETIC_ISOLATION_REFUSAL",()=>l.Finish(1002,0,true,4,h with {CpuFallback=true}));});
        LeaseTest("stdout-aggregate-limit",(l,p,h)=>{Begin(l,h);l.ObserveStream(1001,new byte[65536]);Reject("STDOUT_LIMIT",()=>l.ObserveStream(1001,new byte[1]));});
        LeaseTest("stderr-aggregate-limit",(l,p,h)=>{Begin(l,h);l.ObserveStream(1001,new byte[65536],true);Reject("STDERR_LIMIT",()=>l.ObserveStream(1001,new byte[1],true));});
        LeaseTest("partial-utf8-chunks",(l,p,h)=>{Begin(l,h);var b=Encoding.UTF8.GetBytes("Привет");l.ObserveStream(1001,b.AsSpan(0,1));l.ObserveStream(1001,b.AsSpan(1));Assert(l.Finish(1002,0,true,4,h).Text=="Привет");});
        LeaseTest("malformed-output-utf8",(l,p,h)=>{Begin(l,h);l.ObserveStream(1001,new byte[]{0xff});Reject("UTF8_INVALID",()=>l.Finish(1002,0,true,1,h));});
        LeaseTest("token-overflow",(l,p,h)=>{Begin(l,h);Output(l);Reject("TOKEN_LIMIT",()=>l.Finish(1002,0,true,161,h));});
        LeaseTest("unknown-token-count",(l,p,h)=>{Begin(l,h);Output(l);Reject("TOKEN_LIMIT",()=>l.Finish(1002,0,true,0,h));});
        LeaseTest("token-equality",(l,p,h)=>{Begin(l,h);Output(l);l.Finish(1002,0,true,160,h);});
        LeaseTest("nonzero-exit",(l,p,h)=>{Begin(l,h);Output(l);Reject("TERMINAL_PROCESS_REFUSAL",()=>l.Finish(1002,1,true,4,h));});
        LeaseTest("owned-process-still-alive",(l,p,h)=>{Begin(l,h);Output(l);Reject("TERMINAL_PROCESS_REFUSAL",()=>l.Finish(1002,0,false,4,h));});
        int shapeCase=0;
        foreach(var output in new[]{""," x","x ","x\0",new string('x',601)}) LeaseTest("output-shape-"+shapeCase++,(l,p,h)=>{Begin(l,h);l.ObserveStream(1001,Encoding.UTF8.GetBytes(output));Reject("OUTPUT_TEXT_REFUSAL",()=>l.Finish(1002,0,true,4,h));});
        LeaseTest("char-bound-equality",(l,p,h)=>{Begin(l,h);l.ObserveStream(1001,Encoding.UTF8.GetBytes(new string('x',600)));l.Finish(1002,0,true,160,h);});
        LeaseTest("dispose-is-terminal",(l,p,h)=>{l.Dispose();Reject("TICKET_UNAVAILABLE",()=>Begin(l,h));l.Dispose();});
        Test("overflow-origin",()=>Reject("TEMPORAL_ORIGIN",()=>new QwenOneShotRehearsal(Plan(),long.MaxValue)));
        Test("public-api-has-no-grant-or-execute",()=>Assert(!typeof(QwenIsolatedOneShotAdapter).GetMethods().Any(m=>m.Name is "Grant" or "Invoke" or "Launch")));
        Console.WriteLine(JsonSerializer.Serialize(new {status=failed==0?"CODE_ONLY_QWEN_PROBE_PASS":"CODE_ONLY_QWEN_PROBE_FAIL",passed,failed,
            liveModel=false,processStart=false,realLease=false,processNetworkIsolationProven=false,display=false}));
        return failed==0?0:1;
    }
}
