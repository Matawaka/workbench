"""Read-only consistency verifier. Evidence validation creates no authority."""
import argparse
import copy
import hashlib
import json
import re
from pathlib import Path

HERE=Path(__file__).resolve().parent
HASH=re.compile(r"[0-9a-f]{64}\Z")

def need(ok,reason):
    if not ok:raise ValueError(reason)

def unique(pairs):
    value={}
    for key,item in pairs:
        need(key not in value,"duplicate JSON field")
        value[key]=item
    return value

def read(path):
    raw=path.read_bytes();need(len(raw)<100_000,"evidence bound")
    return json.loads(raw,object_pairs_hook=unique,parse_constant=lambda _:(_ for _ in ()).throw(ValueError("nonfinite JSON")))

def validate(value,check_source=True):
    expected={"schema","status","predecessor","observed_workbench_main","pure_reproduction_sha256","pure_reproduction",
        "native_cases","native_attempts","profiles_created","profiles_removed","test_processes_created","test_processes_exited",
        "model_started","production_isolation_qualified","authority_created","historical_failures_rewritten","raw_logs_included",
        "local_paths_included","identity_claim_created","independent_security_review"}
    need(type(value) is dict and set(value)==expected,"closed evidence envelope")
    need(value["schema"]=="workbench.native-isolation-evidence/v0.1","schema")
    need(value["status"]=="PARTIAL_NATIVE_BOUNDARY_OBSERVED_NETWORK_PROOF_INCOMPLETE","partial status")
    need(value["predecessor"]=="34e2af4e59ad5ba0ca209aa6334f0c7debb2bb24","predecessor")
    need(value["observed_workbench_main"]=="58b9430fc544998a8e40ba00b6757cc630ba9081","observed main")
    for field in ("model_started","production_isolation_qualified","authority_created","historical_failures_rewritten",
        "raw_logs_included","local_paths_included","identity_claim_created","independent_security_review"):
        need(value[field] is False,"non-effect: "+field)
    pure=value["pure_reproduction"]
    need(pure["status"]=="PURE_REPRODUCTION_PASS_NATIVE_NOT_INVOKED","pure result")
    for field in ("native_invoked","model_started","production_qualification"):need(pure[field] is False,"pure non-effect")
    need(set(pure["source_sha256"])=={"NativeBoundary.cs","Probe.cs","prepare.py","qualify.py"},"source list")
    for name,sha in pure["source_sha256"].items():
        need(type(sha) is str and HASH.fullmatch(sha),"source hash")
        if check_source:need(hashlib.sha256((HERE/name).read_bytes()).hexdigest()==sha,"source drift")
    for context in ("repository","clean_unpacked"):
        bundle=pure[context]
        need(bundle["pure_tests"]=={"status":"NATIVE_PROBE_PURE_CONTROLS_PASS","passed":79,"nativeCalls":False},"pure count/status")
        need(bundle["native_trial_invoked"] is False and bundle["model_started"] is False,"build stage not native")
    need(pure["repository"]["source_sha256"]==pure["clean_unpacked"]["source_sha256"],"source equality")
    need(set(pure["mutants"])=={"token-capability","job-process-count","receipt-boolean"},"mutants")
    need(all(x["expected_red"] is True for x in pure["mutants"].values()),"mutant outcome")
    cases=value["native_cases"]
    need([x["case"] for x in cases]==["development-"+x for x in ("03","05","06","07","08","11","13","14")]+["final-repository","final-clean"],"case order/identity")
    for case in cases:
        need(set(case)=={"case","result_sha256","bundle_sha256","source_sha256","candidate_dll_sha256","observed"},"closed case")
        observed=case["observed"]
        keys={"schema","status","stage","nativeCode","elapsedSeconds","profileCreated","profileRemoved","processCreated",
            "processExited","exitCode","cleanupSucceeded","token","loopbackControl","unexpectedConnection","sentinelsUnchanged",
            "stdoutSha256","stderrSha256","child","modelStarted","gameAccessed","globalWindowsPolicyChanged","productionIsolationProvider","realLease","display"}
        need(set(observed) in (keys,keys|{"jobLimitsVerified"}),"closed native observation")
        need(observed["schema"]=="workbench.appcontainer-probe-result/v0.1","native schema")
        for field in ("profileCreated","profileRemoved","processCreated","processExited","cleanupSucceeded","loopbackControl","unexpectedConnection","sentinelsUnchanged"):
            need(type(observed[field]) is bool,"native boolean type")
        if "jobLimitsVerified" in observed:need(type(observed["jobLimitsVerified"]) is bool,"job boolean type")
        for field in ("nativeCode","exitCode"):need(observed[field] is None or type(observed[field]) is int,"native integer type")
        token=observed["token"]
        if token is not None:
            need(set(token)=={"AppContainer","Capabilities","PackageMatches","LowIntegrity","Elevated","InOwnedJob"},"closed token")
            need(type(token["Capabilities"]) is int,"capability integer type")
            need(all(type(v) is bool for k,v in token.items() if k!="Capabilities"),"token boolean type")
        child=observed["child"]
        if child is not None:
            child_keys={"schema","readAllowed","readDenied","writeDenied","childDenied","networkDenied","socketCode","childErrorCode"}
            need(set(child) in (child_keys,child_keys|{"socketFailure"}),"closed child")
            for field in ("readAllowed","readDenied","writeDenied","childDenied","networkDenied"):need(type(child[field]) is bool,"child boolean type")
            for field in ("socketCode","childErrorCode"):need(child[field] is None or type(child[field]) is int,"child integer type")
        need(observed["status"]=="FAIL_CLOSED","historical failure preserved")
        need(observed["cleanupSucceeded"] is True,"cleanup")
        need(not observed["profileCreated"] or observed["profileRemoved"] is True,"profile cleanup")
        need(not observed["processCreated"] or observed["processExited"] is True,"process cleanup")
        for field in ("modelStarted","gameAccessed","globalWindowsPolicyChanged","productionIsolationProvider","realLease","display"):
            need(observed[field] is False,"native effect")
        for field in ("result_sha256","bundle_sha256","candidate_dll_sha256"):
            need(type(case[field]) is str and HASH.fullmatch(case[field]),"case hash")
    for case,context in zip(cases[-2:],("repository","clean_unpacked")):
        need(case["source_sha256"]==pure[context]["source_sha256"],"native/source binding")
        need(case["candidate_dll_sha256"]==pure[context]["files"]["Workbench.AppContainerProbe.dll"],"native/candidate binding")
        observed=case["observed"]
        need(observed["token"]=={"AppContainer":True,"Capabilities":0,"PackageMatches":True,"LowIntegrity":True,"Elevated":False,"InOwnedJob":True},"final token")
        for field in ("jobLimitsVerified","loopbackControl","sentinelsUnchanged"):need(observed[field] is True,"final parent proof")
        need(observed["unexpectedConnection"] is False and observed["exitCode"]==2,"final outcome")
        need(observed["child"]=={"schema":"workbench.appcontainer-probe-child/v0.3","readAllowed":True,"readDenied":True,
            "writeDenied":True,"childDenied":True,"networkDenied":False,"socketCode":None,"childErrorCode":367,"socketFailure":"TIMEOUT"},"final child diagnostic")
    counts={"native_attempts":len(cases),"profiles_created":sum(x["observed"]["profileCreated"] for x in cases),
        "profiles_removed":sum(x["observed"]["profileRemoved"] for x in cases),
        "test_processes_created":sum(x["observed"]["processCreated"] for x in cases),
        "test_processes_exited":sum(x["observed"]["processExited"] for x in cases)}
    for key,actual in counts.items():need(type(value[key]) is int and value[key]==actual,"derived count")

def main():
    p=argparse.ArgumentParser();p.add_argument("--self-test",action="store_true");args=p.parse_args()
    value=read(HERE/"NATIVE-EVIDENCE.json");validate(value);passed=1
    if args.self_test:
        mutations=[lambda x:x.update(authority_created=True),lambda x:x.update(production_isolation_qualified=True),
            lambda x:x.update(status="PASS"),lambda x:x.update(model_started=True),lambda x:x.update(independent_security_review=True),
            lambda x:x.update(predecessor=x["observed_workbench_main"]),lambda x:x.update(native_attempts=9),
            lambda x:x.update(profiles_removed=9),lambda x:x.update(extra="silent-extension"),
            lambda x:x["native_cases"][-1]["observed"].update(cleanupSucceeded=False),
            lambda x:x["native_cases"][-1]["observed"].update(profileRemoved=False),
            lambda x:x["native_cases"][-1]["observed"].update(processExited=False),
            lambda x:x["native_cases"][-1]["observed"]["child"].update(networkDenied=True),
            lambda x:x["native_cases"][-1]["observed"]["child"].update(socketCode=10013),
            lambda x:x["native_cases"][-1]["observed"]["token"].update(Capabilities=1),
            lambda x:x["native_cases"][-1].update(candidate_dll_sha256="0"*64),
            lambda x:x["native_cases"][0]["observed"].update(status="PASS"),
            lambda x:x["pure_reproduction"]["repository"]["pure_tests"].update(passed=80),
            lambda x:x["pure_reproduction"]["mutants"]["token-capability"].update(expected_red=False),
            lambda x:x["native_cases"][-1]["observed"]["child"].update(readAllowed=1),
            lambda x:x["native_cases"][-1]["observed"]["token"].update(Capabilities=False),
            lambda x:x["native_cases"][-1]["observed"].update(cleanupSucceeded=1),
            lambda x:x["native_cases"][-1].update(extra=True),
            lambda x:x["native_cases"][-1]["observed"].update(extra=True)]
        for mutate in mutations:
            changed=copy.deepcopy(value);mutate(changed);refused=False
            try:validate(changed,False)
            except(ValueError,KeyError,TypeError):refused=True
            need(refused,"mutated evidence accepted");passed+=1
        try:unique([("same",1),("same",2)])
        except ValueError:passed+=1
        else:raise ValueError("duplicate accepted")
    print(json.dumps({"status":"EVIDENCE_CONSISTENCY_PASS_NOT_AUTHORITY","passed":passed,"native_invoked":False,"model_started":False}))

if __name__=="__main__":main()
