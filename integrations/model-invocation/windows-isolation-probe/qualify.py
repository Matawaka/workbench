"""Create-only source/candidate reproduction and deliberate RED controls.

Builds and runs --unit only. Never invokes --native-trial, --child or a model.
Requires the already installed Windows .NET SDK; no download/install step.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile

HERE = Path(__file__).resolve().parent
SOURCES = ("NativeBoundary.cs", "Probe.cs", "prepare.py", "qualify.py")

def need(ok, message):
    if not ok:
        raise ValueError(message)

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

def run_prepare(source, output, dotnet, expected_ok):
    result = subprocess.run([sys.executable, "-I", "-B", str(source / "prepare.py"),
        "--dotnet", dotnet, "--output", str(output)], cwd=output.parent,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
    (output.parent / (output.name + ".runner.log")).write_bytes(result.stdout)
    need((result.returncode == 0) == expected_ok, "unexpected qualification outcome")
    if expected_ok:
        bundle = json.loads((output / "BUNDLE.json").read_text(encoding="utf-8"))
        need(bundle["pure_tests"]["passed"] == 79, "test-count drift requires review")
        need(bundle["native_trial_invoked"] is False and bundle["model_started"] is False, "effect claim")
        return bundle
    need((output / "unit.log").is_file(), "mutant must reach tests, not fail compilation")
    build_log = (output / "build.log").read_text(encoding="utf-8", errors="replace")
    need("error CS" not in build_log, "mutant build failure is not RED evidence")
    return {"expected_red": True, "unit_log_sha256": digest((output / "unit.log").read_bytes())}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    out = Path(args.output).absolute()
    need(os.name == "nt", "Windows-only qualification")
    need(out.name.startswith("windows-isolation-reproduction-") and not out.exists(), "new output required")
    need(out.parent.is_dir(), "parent missing")
    for parent in [out.parent, *out.parent.parents]:
        need(not (getattr(parent.lstat(), "st_file_attributes", 0) & 0x400), "reparse parent")
    raw = {name: (HERE / name).read_bytes() for name in SOURCES}
    out.mkdir()
    archive = out / "source.zip"
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as z:
        for name, data in raw.items():
            info = zipfile.ZipInfo(name, (2026, 9, 11, 20, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)
    unpacked = out / "clean-source"
    unpacked.mkdir()
    with zipfile.ZipFile(archive) as z:
        need(z.namelist() == list(SOURCES), "closed archive members")
        for name in SOURCES:
            data = z.read(name)
            need(data == raw[name], "archive source mismatch")
            (unpacked / name).write_bytes(data)
    repository = run_prepare(HERE, out / "windows-isolation-qualification-repository", args.dotnet, True)
    clean = run_prepare(unpacked, out / "windows-isolation-qualification-clean", args.dotnet, True)
    need(repository["source_sha256"] == clean["source_sha256"], "clean-source drift")
    mutations = [
        ("token-capability", "NativeBoundary.cs", "t.Capabilities == 0", "t.Capabilities >= 0"),
        ("job-process-count", "NativeBoundary.cs", "active==1", "active>=1"),
        ("receipt-boolean", "Probe.cs", 'NativeBoundary.Need(value.GetProperty(key).ValueKind==JsonValueKind.True,"CHILD_BOUNDARY_REFUSED");', 'NativeBoundary.Need(true,"CHILD_BOUNDARY_REFUSED");'),
    ]
    reds = {}
    for name, filename, original, changed in mutations:
        mutant = out / ("mutant-" + name)
        mutant.mkdir()
        for member, data in raw.items():
            text = data.decode("utf-8")
            if member == filename:
                need(text.count(original) == 1, "mutation anchor drift")
                text = text.replace(original, changed)
            (mutant / member).write_text(text, encoding="utf-8")
        reds[name] = run_prepare(mutant, out / ("windows-isolation-qualification-mutant-" + name), args.dotnet, False)
    need(all((HERE / name).read_bytes() == data for name, data in raw.items()), "repository changed during qualification")
    result = {"schema": "workbench.isolation-reproduction/v0.1", "status": "PURE_REPRODUCTION_PASS_NATIVE_NOT_INVOKED",
        "source_sha256": {name: digest(data) for name, data in raw.items()},
        "source_archive_sha256": digest(archive.read_bytes()),
        "repository": repository, "clean_unpacked": clean, "mutants": reds,
        "native_invoked": False, "model_started": False, "production_qualification": False}
    (out / "REPRODUCTION.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": result["status"], "repository_passed":79, "clean_passed":79, "mutants_red":len(reds)}))

if __name__ == "__main__":
    main()
