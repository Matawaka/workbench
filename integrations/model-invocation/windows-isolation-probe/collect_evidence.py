"""Create-only sanitized evidence export from this fixed local test campaign.

Reads generated probe results, never raw application/game/model logs. Does not
invoke anything. It does not authenticate a reviewer or create authority.
"""
import argparse
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent

def need(ok, reason):
    if not ok:
        raise ValueError(reason)

def read(path):
    raw = path.read_bytes()
    need(len(raw) < 100_000, "bounded evidence file")
    return json.loads(raw), hashlib.sha256(raw).hexdigest()

def case(folder, label):
    record, result_sha = read(folder / "NATIVE-RESULT.json")
    bundle, bundle_sha = read(folder / "BUNDLE.json")
    allowed = {"schema","status","stage","nativeCode","elapsedSeconds","profileCreated","profileRemoved",
        "processCreated","processExited","exitCode","cleanupSucceeded","token","jobLimitsVerified",
        "loopbackControl","unexpectedConnection","sentinelsUnchanged","stdoutSha256","stderrSha256","child",
        "modelStarted","gameAccessed","globalWindowsPolicyChanged","productionIsolationProvider","realLease","display"}
    need(set(record) <= allowed, "unknown result fields refused")
    for flag in ("modelStarted","gameAccessed","globalWindowsPolicyChanged","productionIsolationProvider","realLease","display"):
        need(record[flag] is False, "unexpected effect")
    need(record["cleanupSucceeded"] is True, "unresolved cleanup")
    need(not record["profileCreated"] or record["profileRemoved"], "profile retained")
    need(not record["processCreated"] or record["processExited"], "process retained")
    need(record["status"] == "FAIL_CLOSED", "campaign outcome changed; review required")
    # Fixed fields only; never copy arbitrary source JSON recursively.
    token = record.get("token")
    if token is not None:
        need(set(token) == {"AppContainer","Capabilities","PackageMatches","LowIntegrity","Elevated","InOwnedJob"}, "token envelope")
        need(all(type(v) is bool for k,v in token.items() if k != "Capabilities") and type(token["Capabilities"]) is int, "token types")
    child = record.get("child")
    if child is not None:
        allowed_child = {"schema","readAllowed","readDenied","writeDenied","childDenied","networkDenied","socketCode","childErrorCode","socketFailure"}
        need(set(child) <= allowed_child, "child envelope")
        need(child["schema"] in ("workbench.appcontainer-probe-child/v0.2","workbench.appcontainer-probe-child/v0.3"), "child schema")
        for k in ("readAllowed","readDenied","writeDenied","childDenied","networkDenied"):
            need(type(child[k]) is bool, "child boolean")
        for k in ("socketCode","childErrorCode"):
            need(child[k] is None or type(child[k]) is int, "child code")
        need(child.get("socketFailure") in (None,"TIMEOUT"), "unexpected child diagnostic")
    need(record["stage"] in {"MANAGED_UnauthorizedAccessException","CREATE_APPCONTAINER_PROCESS_REFUSED","TOKEN_SIZE_QUERY",
        "TOKEN_SIZE_QUERY_20","MANAGED_JsonReaderException","CHILD_EXIT_NONZERO"}, "stage requires review")
    return {"case":label,"result_sha256":result_sha,"bundle_sha256":bundle_sha,
        "source_sha256":bundle["source_sha256"],"candidate_dll_sha256":bundle["files"]["Workbench.AppContainerProbe.dll"],"observed":record}

def main():
    p=argparse.ArgumentParser();p.add_argument("--root",required=True);p.add_argument("--reproduction",required=True)
    args=p.parse_args();root=Path(args.root);reproduction=Path(args.reproduction)
    output=HERE / "NATIVE-EVIDENCE.json"
    need(not output.exists(), "create-only export")
    qualified, qualified_sha=read(reproduction / "REPRODUCTION.json")
    need(qualified["status"]=="PURE_REPRODUCTION_PASS_NATIVE_NOT_INVOKED", "pure result")
    for name, expected in qualified["source_sha256"].items():
        need(name in ("NativeBoundary.cs","Probe.cs","prepare.py","qualify.py"), "source filename")
        need(hashlib.sha256((HERE/name).read_bytes()).hexdigest()==expected,"current source drift")
    records=[case(root / ("windows-isolation-qualification-"+n),"development-"+n) for n in ("03","05","06","07","08","11","13","14")]
    records += [case(reproduction / ("windows-isolation-qualification-"+n),"final-"+n) for n in ("repository","clean")]
    final=records[-2:]
    for record in final:
        observed=record["observed"]
        need(observed["jobLimitsVerified"] is True and observed["sentinelsUnchanged"] is True,"final parent boundary")
        need(observed["child"]["socketFailure"]=="TIMEOUT" and observed["child"]["networkDenied"] is False,"network uncertainty changed")
    result={"schema":"workbench.native-isolation-evidence/v0.1",
        "status":"PARTIAL_NATIVE_BOUNDARY_OBSERVED_NETWORK_PROOF_INCOMPLETE",
        "predecessor":"34e2af4e59ad5ba0ca209aa6334f0c7debb2bb24",
        "observed_workbench_main":"58b9430fc544998a8e40ba00b6757cc630ba9081",
        "pure_reproduction_sha256":qualified_sha,"pure_reproduction":qualified,"native_cases":records,
        "native_attempts":len(records),"profiles_created":sum(x["observed"]["profileCreated"] for x in records),
        "profiles_removed":sum(x["observed"]["profileRemoved"] for x in records),
        "test_processes_created":sum(x["observed"]["processCreated"] for x in records),
        "test_processes_exited":sum(x["observed"]["processExited"] for x in records),
        "model_started":False,"production_isolation_qualified":False,"authority_created":False,
        "historical_failures_rewritten":False,"raw_logs_included":False,"local_paths_included":False,
        "identity_claim_created":False,"independent_security_review":False}
    with output.open("x",encoding="utf-8") as stream:
        json.dump(result,stream,indent=2);stream.write("\n")
    print(json.dumps({"status":result["status"],"attempts":len(records),"profiles_removed":result["profiles_removed"],"processes_exited":result["test_processes_exited"]}))

if __name__=="__main__":
    main()
