#!/usr/bin/env python3
import copy
import hashlib
import importlib.util
from pathlib import Path

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("remote_identity", HERE / "derive_remote_smoke_identity.py")
mod = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(mod)

COMMIT = "a" * 40
URL = f"https://raw.githubusercontent.com/Matawaka/workbench/{COMMIT}/artifacts/smoke/tiny.bin"

commit, relative = mod.validate_immutable_url(URL)
assert commit == COMMIT
assert relative == "artifacts/smoke/tiny.bin"

served = b"exact-served-bytes\x00\xff"
identity = mod.derive_identity_from_bytes(URL, served)
assert identity["ObservedBytes"] == len(served)
assert identity["ObservedSha256"] == hashlib.sha256(served).hexdigest()
assert identity["IdentitySource"] == "EXACT_IMMUTABLE_SERVED_BYTES"
assert identity["PreUploadIdentityAccepted"] is False

bad = [
    URL.replace("https://", "http://"),
    URL.replace("raw.githubusercontent.com", "github.com"),
    URL.replace(COMMIT, "main"),
    URL + "?download=1",
    URL + "#fragment",
    f"https://raw.githubusercontent.com/Matawaka/workbench/{COMMIT}/../escape",
]
for candidate in bad:
    try:
        mod.validate_immutable_url(candidate)
    except ValueError:
        pass
    else:
        raise AssertionError(f"hostile URL accepted: {candidate}")

# API discipline: admitted identity derives bytes/hash internally and accepts no expected size/hash.
assert "expected" not in mod.derive_identity_from_bytes.__code__.co_varnames
assert mod.DEFAULT_MAX_BYTES == 64 * 1024 * 1024

print("remote smoke identity helper: PASS")
