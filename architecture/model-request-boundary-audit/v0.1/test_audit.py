#!/usr/bin/env python3
from __future__ import annotations

import copy
from pathlib import Path

from validate import load_audit, validate

ROOT = Path(__file__).resolve().parent.parents[2]


def expect_refused(name: str, mutate) -> None:
    value = copy.deepcopy(load_audit())
    mutate(value)
    try:
        validate(value, ROOT)
    except ValueError:
        print(f"PASS refused {name}")
        return
    raise AssertionError(f"hostile mutation unexpectedly accepted: {name}")


def main() -> int:
    validate(load_audit(), ROOT)
    print("PASS baseline")

    expect_refused("origin drift", lambda x: x.__setitem__("origin_main", "0" * 40))
    expect_refused("source blob substitution", lambda x: x["source_bindings"][1].__setitem__("git_blob_sha1", "0" * 40))
    expect_refused("reuse v053 promoted", lambda x: x["options"][2].__setitem__("classification", "SELECT_FIRST_IMPLEMENTATION_CANDIDATE"))
    expect_refused("v053 reinterpreted as model request authority", lambda x: x["decision"].__setitem__("v053_reinterpreted_as_model_request_authority", True))
    expect_refused("loopback silently selected", lambda x: x["decision"].__setitem__("first_profile", "LOOPBACK_SESSION_SEPARATE_REQUEST"))
    expect_refused("direct profile network widened", lambda x: x["options"][0].__setitem__("network_required", True))
    expect_refused("direct profile server widened", lambda x: x["options"][0].__setitem__("server_required", True))
    expect_refused("separate request authority removed", lambda x: x["options"][0].__setitem__("separate_model_request_authority", False))
    expect_refused("audit creates implementation authority", lambda x: x["decision"].__setitem__("implementation_authority_created_by_audit", True))
    expect_refused("real model bytes authorized", lambda x: x["decision"].__setitem__("real_model_bytes_authorized", True))
    expect_refused("real runtime bytes authorized", lambda x: x["decision"].__setitem__("real_runtime_bytes_authorized", True))
    expect_refused("arbitrary model args control deleted", lambda x: x["required_v055_controls"].remove("no caller-defined arbitrary model-command argument vector"))
    expect_refused("model rehash control deleted", lambda x: x["required_v055_controls"].remove("model artifact rehash immediately before process creation"))
    expect_refused("output ceilings deleted", lambda x: x["required_v055_controls"].remove("independent stdout and stderr byte ceilings"))
    expect_refused("response authority collapse", lambda x: x["required_v055_invariants"].remove("Model Request Authority != Response Authority"))
    expect_refused("timeout implies retry", lambda x: x["required_v055_invariants"].remove("Timeout != Permission To Retry"))
    expect_refused("remote smoke hygiene bypass", lambda x: x["hygiene_dependency"].__setitem__("state", "IGNORED"))
    expect_refused("model request non-effect deleted", lambda x: x["non_effects"].remove("no model request"))

    print("WORKBENCH_MODEL_REQUEST_BOUNDARY_AUDIT_V01_HOSTILE_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
