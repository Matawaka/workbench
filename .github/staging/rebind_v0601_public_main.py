from __future__ import annotations

import subprocess
import sys
from pathlib import Path

FEATURE = "feat/workbench-v0.60.1-operator-acceptance-closure"
EXPECTED_HEAD = "45a229f66c32fad36a773e2433f1d6d5e376cef8"
OLD_CLOSURE = "aa2bfa215d3cacca015d40cd8786706aff0a9f19"
OLD_MAIN = "6541dc32182c970c8e1a6ade426a6cee7086511b"
NEW_MAIN = "ac083598711caa0c399cc0d2c385b980c083024a"
REMOTE = "https://github.com/Matawaka/workbench.git"
OLD_TOKEN = "IMPORT-EXACT-PUBLIC-6541"
NEW_TOKEN = "IMPORT-EXACT-PUBLIC-AC08"

REBIND_PATHS = [
    "src/Matawaka.Workbench.App/V0601QualifiedSourceBindings.cs",
    "src/Matawaka.Workbench.App/PublicMainImportReceiptV0601.cs",
    "src/Matawaka.Workbench.App/LocalCheckpointV0601Service.cs",
    "tooling/v0601-public-main-import/Program.cs",
    ".github/qualification/v0601-checkpoint/Program.cs",
    ".github/workflows/workbench-v0601-operator-acceptance.yml",
]

PUBLIC_100 = {
    ".github/qualification/source-bound-model/Probe.csproj": "7099afc63264a0bee684788fe50448a78272e616",
    ".github/qualification/source-bound-model/Program.cs": "065e8d339acd4d8ee9766f8433e9f5de0c598902",
    ".github/qualification/source-bound-model/translation-fixture.json": "3409b9f71c0f47f286fd191994857b8e3c862825",
    "KONTUR_INTEGRATION_BACKLOG.md": "5035aaa63702678f54533bbf98abf2f565e51a10",
    "integrations/model-invocation/QUALIFICATION.md": "606f186a6ecd960cceed18966f28f27472f6ff2f",
    "integrations/model-invocation/README.md": "b4823d0421ec663ad4bb82a49852d3022bedabc5",
    "integrations/model-invocation/source-binding.schema.json": "13b2b78cea39a870546db79c1881bc271224e721",
    "src/Matawaka.Workbench.App/SourceBoundModelInvocation.cs": "14d092d47e6a699405eededad6b577bd4bf8a4da",
}


def run(repo: Path, *args: str, check: bool = True) -> str:
    p = subprocess.run(
        args,
        cwd=repo,
        text=True,
        encoding="utf-8",
        errors="replace",
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if check and p.returncode != 0:
        raise RuntimeError(f"command failed ({p.returncode}): {' '.join(args)}\n{p.stderr}")
    return p.stdout


def read_exact(path: Path) -> str:
    return path.read_bytes().decode("utf-8")


def write_exact(path: Path, text: str) -> None:
    path.write_bytes(text.encode("utf-8"))


def replace_required(path: Path, old: str, new: str) -> None:
    text = read_exact(path)
    count = text.count(old)
    if count < 1:
        raise RuntimeError(f"required text missing in {path}: {old}")
    write_exact(path, text.replace(old, new))


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def main() -> None:
    if len(sys.argv) != 2:
        raise RuntimeError("usage: rebind_v0601_public_main.py <feature-checkout>")
    repo = Path(sys.argv[1]).resolve()
    require((repo / ".git").exists(), f"feature checkout missing: {repo}")

    head = run(repo, "git", "rev-parse", "HEAD").strip()
    require(head == EXPECTED_HEAD, f"feature head drifted before rebind: {head}")
    parents = run(repo, "git", "show", "-s", "--format=%P", "HEAD").strip()
    require(parents == f"{OLD_CLOSURE} {NEW_MAIN}", f"unexpected convergence parents: {parents}")
    remote_main_line = run(repo, "git", "ls-remote", REMOTE, "refs/heads/main").strip().splitlines()
    require(len(remote_main_line) == 1, "remote main missing/ambiguous")
    observed_main = remote_main_line[0].split("\t", 1)[0]
    require(observed_main == NEW_MAIN, f"public main drifted again: {observed_main}")
    require(run(repo, "git", "rev-parse", f"{NEW_MAIN}^1").strip() == OLD_MAIN,
            "public #100 predecessor binding changed")
    require(not run(repo, "git", "status", "--porcelain=v1", "--untracked-files=all").strip(),
            "feature checkout is not clean")

    for path, expected_blob in PUBLIC_100.items():
        public_blob = run(repo, "git", "rev-parse", f"{NEW_MAIN}:{path}").strip()
        working_blob = run(repo, "git", "hash-object", "--", path).strip()
        require(public_blob == expected_blob and working_blob == expected_blob,
                f"public #100 byte mismatch: {path}; public={public_blob}; working={working_blob}; expected={expected_blob}")

    for rel in REBIND_PATHS:
        replace_required(repo / rel, OLD_MAIN, NEW_MAIN)
    for rel in [
        "tooling/v0601-public-main-import/Program.cs",
        ".github/workflows/workbench-v0601-operator-acceptance.yml",
    ]:
        replace_required(repo / rel, OLD_TOKEN, NEW_TOKEN)

    bindings_path = repo / "src/Matawaka.Workbench.App/V0601QualifiedSourceBindings.cs"
    bindings = read_exact(bindings_path)
    require("SourceBoundModelInvocation.cs\"] = \"14d092" not in bindings,
            "public #100 source bindings already inserted unexpectedly")
    nl = "\r\n" if "\r\n" in bindings else "\n"
    needle = '            ["src/Matawaka.Workbench.App/Assets/Branding/splash-v060.b64.004"] = "e2294e891885f68e40f5fe05e88fcd9751d0d0f7"'
    require(needle in bindings, "source binding insertion marker missing")
    additions = [
        '            [".github/qualification/source-bound-model/Probe.csproj"] = "7099afc63264a0bee684788fe50448a78272e616"',
        '            [".github/qualification/source-bound-model/Program.cs"] = "065e8d339acd4d8ee9766f8433e9f5de0c598902"',
        '            [".github/qualification/source-bound-model/translation-fixture.json"] = "3409b9f71c0f47f286fd191994857b8e3c862825"',
        '            ["KONTUR_INTEGRATION_BACKLOG.md"] = "5035aaa63702678f54533bbf98abf2f565e51a10"',
        '            ["integrations/model-invocation/QUALIFICATION.md"] = "606f186a6ecd960cceed18966f28f27472f6ff2f"',
        '            ["integrations/model-invocation/README.md"] = "b4823d0421ec663ad4bb82a49852d3022bedabc5"',
        '            ["integrations/model-invocation/source-binding.schema.json"] = "13b2b78cea39a870546db79c1881bc271224e721"',
        '            ["src/Matawaka.Workbench.App/SourceBoundModelInvocation.cs"] = "14d092d47e6a699405eededad6b577bd4bf8a4da"',
    ]
    bindings = bindings.replace(needle, needle + "," + nl + ("," + nl).join(additions), 1)
    write_exact(bindings_path, bindings)

    workflow_path = repo / ".github/workflows/workbench-v0601-operator-acceptance.yml"
    workflow = read_exact(workflow_path)
    nl = "\r\n" if "\r\n" in workflow else "\n"
    allow_needle = "            '.github/qualification/v0601-checkpoint/Program.cs',"
    require(allow_needle in workflow, "workflow allowlist marker missing")
    allow_additions = [
        "            '.github/qualification/source-bound-model/Probe.csproj',",
        "            '.github/qualification/source-bound-model/Program.cs',",
        "            '.github/qualification/source-bound-model/translation-fixture.json',",
        "            'KONTUR_INTEGRATION_BACKLOG.md',",
        "            'integrations/model-invocation/QUALIFICATION.md',",
        "            'integrations/model-invocation/README.md',",
        "            'integrations/model-invocation/source-binding.schema.json',",
        "            'src/Matawaka.Workbench.App/SourceBoundModelInvocation.cs',",
    ]
    workflow = workflow.replace(allow_needle, allow_needle + nl + nl.join(allow_additions), 1)

    publish_marker = "      - name: Publish candidate and preserve neutral branding WPF smoke"
    require(publish_marker in workflow, "workflow publish marker missing")
    public_step = """      - name: Guard and qualify public number 100 source-bound model layer
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Stop'
          $public = 'ac083598711caa0c399cc0d2c385b980c083024a'
          $paths = @(
            '.github/qualification/source-bound-model/Probe.csproj',
            '.github/qualification/source-bound-model/Program.cs',
            '.github/qualification/source-bound-model/translation-fixture.json',
            'KONTUR_INTEGRATION_BACKLOG.md',
            'integrations/model-invocation/QUALIFICATION.md',
            'integrations/model-invocation/README.md',
            'integrations/model-invocation/source-binding.schema.json',
            'src/Matawaka.Workbench.App/SourceBoundModelInvocation.cs'
          )
          foreach ($path in $paths) {
            $expected = (git rev-parse \"$public`:$path\").Trim()
            $observed = (git hash-object -- $path).Trim()
            if ($expected -ne $observed) { throw \"public #100 source drift: $path\" }
          }
          dotnet run --project .\\.github\\qualification\\source-bound-model\\Probe.csproj -c Release
          Write-Host \"V0601_PUBLIC100_MODEL_LAYER_PASS paths=$($paths.Count)\"

""".replace("\n", nl)
    workflow = workflow.replace(publish_marker, public_step + publish_marker, 1)
    write_exact(workflow_path, workflow)

    for rel in REBIND_PATHS:
        text = read_exact(repo / rel)
        require(OLD_MAIN not in text, f"old public main remains in rebind file: {rel}")
    for rel in ["tooling/v0601-public-main-import/Program.cs", ".github/workflows/workbench-v0601-operator-acceptance.yml"]:
        require(OLD_TOKEN not in read_exact(repo / rel), f"old confirmation token remains in {rel}")

    # Public #100 files must remain byte-identical after all edits.
    for path, expected_blob in PUBLIC_100.items():
        observed = run(repo, "git", "hash-object", "--", path).strip()
        require(observed == expected_blob, f"public #100 bytes changed during rebind: {path}")

    diff_check = subprocess.run(["git", "diff", "--check"], cwd=repo, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    require(diff_check.returncode == 0, "git diff --check failed: " + diff_check.stdout + diff_check.stderr)
    actual = sorted(x for x in run(repo, "git", "diff", "--name-only").splitlines() if x)
    require(actual == sorted(REBIND_PATHS), f"unexpected exact rebind delta: {actual}")

    run(repo, "git", "config", "user.name", "Matawaka v0.60.1 Rebind")
    run(repo, "git", "config", "user.email", "319695216+Matawaka@users.noreply.github.com")
    run(repo, "git", "add", "--", *REBIND_PATHS)
    run(repo, "git", "commit", "-m", "Rebind v0.60.1 acceptance to public #100 frontier")
    new_head = run(repo, "git", "rev-parse", "HEAD").strip()

    remote_line = run(repo, "git", "ls-remote", "origin", f"refs/heads/{FEATURE}").strip().splitlines()
    require(len(remote_line) == 1 and remote_line[0].split("\t", 1)[0] == EXPECTED_HEAD,
            "feature branch drifted before non-force push")
    run(repo, "git", "push", "origin", f"HEAD:refs/heads/{FEATURE}")
    print(f"V0601_REBIND_PUSH_PASS newHead={new_head} force=false public={NEW_MAIN}")


if __name__ == "__main__":
    main()
