#!/usr/bin/env python3
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
AUDIT_PATH = HERE / "audit.json"

EXPECTED_ORIGIN = "65b0b49a513a6b782760a7626d6b768bf7bb7f91"
EXPECTED_TAG = "workbench-v0.54.2-accepted"
EXPECTED_BINDINGS = {
    "src/Matawaka.Workbench.App/BoundedArtifactAcquisitionV052Service.cs": (
        "624846a956807eaa35d5d8cdef144be196295561", "EXACT_ARTIFACT_ACQUISITION"
    ),
    "src/Matawaka.Workbench.App/BoundedRuntimeExecutionV053Service.cs": (
        "eb7854396dc9459ce8925e768191f209803855ba", "GENERIC_EXACT_PROCESS_EXECUTION"
    ),
    "src/Matawaka.Workbench.App/BoundedRuntimeTreeMaterializationV054Service.cs": (
        "0d235e0cc22ffc12eb019c0334cdeb9e83872b28", "EXACT_RUNTIME_TREE_MATERIALIZATION"
    ),
    "KONTUR_INTEGRATION_BACKLOG.md": (
        "da7d6aeb4bc0b20e727f864c4a3eb8ce11966c3a", "PUBLIC_FUTURE_CALLER_BOUNDARY"
    ),
}

TOP_KEYS = {
    "schema", "version", "origin_main", "origin_tag", "source_bindings",
    "proven_corridor", "observed_gaps", "options", "decision",
    "required_v055_invariants", "required_v055_controls", "hygiene_dependency",
    "non_effects", "audit_digest_sha256",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def git_blob_sha1(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def canonical_digest(value: dict) -> str:
    clone = copy.deepcopy(value)
    clone.pop("audit_digest_sha256", None)
    raw = json.dumps(clone, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def load_audit() -> dict:
    return json.loads(AUDIT_PATH.read_text(encoding="utf-8"))


def validate(audit: dict, root: Path = ROOT) -> None:
    require(isinstance(audit, dict) and set(audit) == TOP_KEYS, "closed top-level audit keys")
    require(audit["schema"] == "matawaka.workbench-model-request-boundary-audit/v0.1", "audit schema")
    require(audit["version"] == "0.1", "audit version")
    require(audit["origin_main"] == EXPECTED_ORIGIN, "origin main")
    require(audit["origin_tag"] == EXPECTED_TAG, "origin tag")
    require(audit["audit_digest_sha256"] == canonical_digest(audit), "audit digest")

    bindings = audit["source_bindings"]
    require(isinstance(bindings, list) and len(bindings) == len(EXPECTED_BINDINGS), "binding count")
    observed = {}
    for item in bindings:
        require(isinstance(item, dict) and set(item) == {"path", "git_blob_sha1", "role"}, "binding shape")
        path = item["path"]
        require(path in EXPECTED_BINDINGS and path not in observed, f"unexpected/duplicate binding: {path}")
        expected_sha, expected_role = EXPECTED_BINDINGS[path]
        require(item["git_blob_sha1"] == expected_sha and item["role"] == expected_role, f"binding identity: {path}")
        full = root / path
        require(full.is_file(), f"bound source missing: {path}")
        require(git_blob_sha1(full.read_bytes()) == expected_sha, f"bound source blob drift: {path}")
        observed[path] = full.read_text(encoding="utf-8")

    require(set(observed) == set(EXPECTED_BINDINGS), "all exact sources bound")

    v052 = observed["src/Matawaka.Workbench.App/BoundedArtifactAcquisitionV052Service.cs"]
    v053 = observed["src/Matawaka.Workbench.App/BoundedRuntimeExecutionV053Service.cs"]
    v054 = observed["src/Matawaka.Workbench.App/BoundedRuntimeTreeMaterializationV054Service.cs"]
    backlog = observed["KONTUR_INTEGRATION_BACKLOG.md"]

    require("ACQUISITION_VERIFIED" in v052, "v0.52 verified acquisition state evidence")
    require("MATERIALIZED_VERIFIED" in v054, "v0.54 materialized runtime state evidence")
    require("RUNTIME_READY_OBSERVED" in v053, "v0.53 runtime readiness evidence")
    require("public const int MaxArguments = 64;" in v053, "v0.53 opaque argument bound evidence")
    require("foreach (var arg in consumedState.Arguments) psi.ArgumentList.Add(arg);" in v053, "v0.53 exact argument vector evidence")
    require("bool ModelRequestPerformed" in v053, "v0.53 model-request receipt field evidence")
    require("Runtime Ready != Model Request Authority" in v053, "v0.53 explicit model-request authority boundary")
    require("RedirectStandardOutput = true" not in v053, "v0.53 is not an output-capturing model request primitive")
    require("Runtime Started != Provider Ready != Benchmark Authority != Request Authority" in backlog, "public integration request-authority boundary")

    require(audit["proven_corridor"] == ["ACQUISITION_VERIFIED", "MATERIALIZED_VERIFIED", "RUNTIME_READY_OBSERVED"], "proven corridor")

    gaps = audit["observed_gaps"]
    require(set(gaps) == {
        "model_artifact_reverification_at_request", "separate_model_request_authority",
        "model_request_count_receipt", "stdout_model_output_capture",
        "request_payload_digest_receipt", "portable_model_result_without_local_paths",
        "v053_argument_domain_semantics",
    }, "closed gap inventory")
    for key in (
        "model_artifact_reverification_at_request", "separate_model_request_authority",
        "model_request_count_receipt", "stdout_model_output_capture",
        "request_payload_digest_receipt", "portable_model_result_without_local_paths",
    ):
        require(gaps[key] == "NOT_IMPLEMENTED", f"gap classification: {key}")
    require(gaps["v053_argument_domain_semantics"] == "STRUCTURALLY_BOUNDED_SEMANTICALLY_UNCLASSIFIED", "v0.53 argument semantic finding")

    options = {x["id"]: x for x in audit["options"]}
    require(set(options) == {
        "DIRECT_SUBPROCESS_STDIO_ONE_SHOT", "LOOPBACK_SESSION_SEPARATE_REQUEST", "REUSE_V053_AS_MODEL_REQUEST"
    }, "option set")
    require(options["DIRECT_SUBPROCESS_STDIO_ONE_SHOT"]["classification"] == "SELECT_FIRST_IMPLEMENTATION_CANDIDATE", "direct subprocess selection")
    require(options["DIRECT_SUBPROCESS_STDIO_ONE_SHOT"]["network_required"] is False and options["DIRECT_SUBPROCESS_STDIO_ONE_SHOT"]["server_required"] is False, "direct subprocess effect ceiling")
    require(options["DIRECT_SUBPROCESS_STDIO_ONE_SHOT"]["separate_model_request_authority"] is True, "direct subprocess retains separate request authority")
    require(options["LOOPBACK_SESSION_SEPARATE_REQUEST"]["classification"] == "DEFER_SEPARATE_SUCCESSOR", "loopback deferred")
    require(options["LOOPBACK_SESSION_SEPARATE_REQUEST"]["network_required"] is True and options["LOOPBACK_SESSION_SEPARATE_REQUEST"]["server_required"] is True, "loopback explicit network/server")
    require(options["REUSE_V053_AS_MODEL_REQUEST"]["classification"] == "REJECT_CURRENT_EVIDENCE", "v0.53 direct reuse rejected")
    require(options["REUSE_V053_AS_MODEL_REQUEST"]["separate_model_request_authority"] is False, "v0.53 lacks separate model-request authority")

    decision = audit["decision"]
    require(decision == {
        "next_component": "V055_BOUNDED_LOCAL_MODEL_INVOCATION_LEASE",
        "first_profile": "DIRECT_SUBPROCESS_STDIO_ONE_SHOT",
        "loopback_session": "DEFER_SEPARATE_SUCCESSOR",
        "v053_reinterpreted_as_model_request_authority": False,
        "real_model_bytes_authorized": False,
        "real_runtime_bytes_authorized": False,
        "implementation_authority_created_by_audit": False,
    }, "decision boundary")

    required_invariants = set(audit["required_v055_invariants"])
    for invariant in {
        "Runtime Ready != Model Request Authority",
        "Process Execution Authority != Model Request Authority",
        "Model Request Authority != Response Authority",
        "Model Output != Trusted Response",
        "Validated Output != Display Permit",
        "Timeout != Permission To Retry",
        "Output Capture != Content Review",
    }:
        require(invariant in required_invariants, f"missing invariant: {invariant}")

    controls = set(audit["required_v055_controls"])
    for control in {
        "exact v0.54 MATERIALIZED_VERIFIED runtime-tree evidence",
        "exact v0.52 ACQUISITION_VERIFIED model-artifact evidence",
        "runtime executable rehash immediately before process creation",
        "model artifact rehash immediately before process creation",
        "authority consumed before process creation/request release",
        "maximum one process and one model request",
        "no caller-defined arbitrary model-command argument vector",
        "independent stdout and stderr byte ceilings",
        "no server/port/network in first profile",
        "no benchmark/game/display/send/action/successor authority",
    }:
        require(control in controls, f"missing v0.55 control: {control}")

    hygiene = audit["hygiene_dependency"]
    require(hygiene == {
        "issue": 73,
        "state": "OPEN_REQUIRED_BEFORE_NEW_REMOTE_SMOKE",
        "rule": "ExpectedBytes and ExpectedSha256 derive only from immutable served bytes re-fetched after upload.",
    }, "remote smoke hygiene dependency")

    non_effects = set(audit["non_effects"])
    for item in {
        "no artifact acquisition", "no runtime-tree materialization", "no process start", "no model request",
        "no server or port bind", "no network access", "no benchmark", "no game access",
        "no display or response authority", "no Agent Execute or ActionPermit", "no publication authority",
    }:
        require(item in non_effects, f"missing non-effect: {item}")


def main() -> int:
    audit = load_audit()
    validate(audit)
    print("WORKBENCH_MODEL_REQUEST_BOUNDARY_AUDIT_V01_PASS")
    print("decision=V055_BOUNDED_LOCAL_MODEL_INVOCATION_LEASE")
    print("profile=DIRECT_SUBPROCESS_STDIO_ONE_SHOT")
    print("v053_model_request_reinterpretation=false")
    print("real_model_or_runtime_authority=false")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
