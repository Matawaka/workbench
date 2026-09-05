# Remote smoke identity from immutable served bytes v0.1

Purpose: mechanically enforce Workbench #73 for future tiny remote smoke/control artifacts.

Invariant: `Pre-upload artifact identity != admitted remote acquisition identity`.

Required sequence:
1. publish the tiny test object at an immutable `raw.githubusercontent.com/Matawaka/workbench/<40-hex-commit>/...` path;
2. invoke `derive_remote_smoke_identity.py` for that exact immutable path;
3. the helper performs one size-bounded HTTPS read with redirects refused;
4. `ObservedBytes` and `ObservedSha256` are calculated only from bytes returned by that read;
5. only the emitted identity may be copied into a later v0.52 smoke acquisition request.

The helper accepts no caller-supplied expected byte length or expected SHA-256. It creates no acquisition, materialization, execution, model-request, game, display, or publication authority.

Default served-object ceiling: 64 MiB. The helper is only for small test/control artifacts.
