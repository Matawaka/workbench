#!/usr/bin/env python3
"""Derive admitted remote-smoke identity only from immutable bytes actually served.

This helper intentionally accepts no pre-upload size/hash arguments. An identity record
can be emitted only after one exact HTTPS GET of a raw.githubusercontent.com URL whose
ref segment is a full 40-hex commit SHA.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

IMMUTABLE_RAW = re.compile(r"^/Matawaka/workbench/([0-9a-f]{40})/(.+)$")
DEFAULT_MAX_BYTES = 64 * 1024 * 1024


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: D401
        raise ValueError(f"redirect refused: HTTP {code} -> {newurl}")


def validate_immutable_url(url: str) -> tuple[str, str]:
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https":
        raise ValueError("remote smoke source must use HTTPS")
    if parsed.hostname != "raw.githubusercontent.com" or parsed.port not in (None, 443):
        raise ValueError("remote smoke source must be exact raw.githubusercontent.com HTTPS")
    if parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError("credentials/query/fragment are not allowed in immutable smoke URL")
    match = IMMUTABLE_RAW.fullmatch(parsed.path)
    if not match:
        raise ValueError("URL must bind Matawaka/workbench at an exact 40-hex commit SHA")
    relative = match.group(2)
    if not relative or any(part in ("", ".", "..") for part in relative.split("/")):
        raise ValueError("immutable smoke path is malformed")
    return match.group(1), relative


def derive_identity_from_bytes(url: str, served: bytes) -> dict:
    commit, relative = validate_immutable_url(url)
    return {
        "Schema": "matawaka.remote-smoke-served-identity/v0.1",
        "SourceUri": url,
        "Repository": "Matawaka/workbench",
        "ImmutableCommit": commit,
        "RelativePath": relative,
        "ObservedBytes": len(served),
        "ObservedSha256": hashlib.sha256(served).hexdigest(),
        "IdentitySource": "EXACT_IMMUTABLE_SERVED_BYTES",
        "PreUploadIdentityAccepted": False,
    }


def fetch_served_bytes(url: str, max_bytes: int) -> bytes:
    validate_immutable_url(url)
    if max_bytes < 1 or max_bytes > DEFAULT_MAX_BYTES:
        raise ValueError(f"max_bytes must be 1..{DEFAULT_MAX_BYTES}")
    opener = urllib.request.build_opener(NoRedirect())
    request = urllib.request.Request(
        url,
        method="GET",
        headers={
            "User-Agent": "Matawaka-Workbench-Remote-Smoke-Identity/0.1",
            "Accept": "application/octet-stream,*/*;q=0.1",
            "Accept-Encoding": "identity",
        },
    )
    with opener.open(request, timeout=30) as response:
        if getattr(response, "status", None) != 200:
            raise ValueError(f"unexpected HTTP status: {getattr(response, 'status', None)}")
        encoding = (response.headers.get("Content-Encoding") or "identity").lower()
        if encoding != "identity":
            raise ValueError(f"content encoding refused: {encoding}")
        chunks: list[bytes] = []
        total = 0
        while True:
            chunk = response.read(min(1024 * 1024, max_bytes - total + 1))
            if not chunk:
                break
            total += len(chunk)
            if total > max_bytes:
                raise ValueError(f"served object exceeds max_bytes={max_bytes}")
            chunks.append(chunk)
        return b"".join(chunks)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("url")
    parser.add_argument("output_json")
    parser.add_argument("--max-bytes", type=int, default=DEFAULT_MAX_BYTES)
    args = parser.parse_args(argv)

    served = fetch_served_bytes(args.url, args.max_bytes)
    identity = derive_identity_from_bytes(args.url, served)
    identity["ObservedAt"] = datetime.now(timezone.utc).isoformat()
    output = Path(args.output_json)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(identity, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(identity, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"REMOTE_SMOKE_IDENTITY_REFUSED: {exc}", file=sys.stderr)
        raise SystemExit(2)
