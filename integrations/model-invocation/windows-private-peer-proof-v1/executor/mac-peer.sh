#!/bin/bash
set -euo pipefail

PROOF_IP="10.77.0.1"
PEER_IP="10.77.0.2"
NETMASK="255.255.255.252"
PEER_SOURCE_SHA="b01ab34cd9923d34b72c4ca557ffb292cb9621a896be0fbbb020ddd88d8bc049"
PEER_PROJECT_SHA="75889ad8634f9c8b127e28c65619d1b876e8ca05aff42e9c10e12805af8daa82"
PEER_CONTRACT_SHA="793de6f7d0f68317d331b2e7e3eec20e02dcdd9acac5c665ceab633122aea1ca"
QUALIFIED_PEER_HEAD="efff344bc1418e9b1da946a8493f854bf8ada9af"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
SOURCE="$REPO/integrations/model-invocation/windows-private-peer-executor-v1/Program.cs"
PROJECT="$REPO/integrations/model-invocation/windows-private-peer-executor-v1/PrivatePeerListener.csproj"
CONTRACT="$REPO/integrations/model-invocation/windows-private-peer-executor-v1/PEER_CONTRACT.json"
OUT="$SCRIPT_DIR/out/mac-peer"

fail() { echo "FAIL_CLOSED: $*" >&2; exit 2; }
sha256_file() { shasum -a 256 "$1" | awk '{print tolower($1)}'; }
default_iface() { route -n get default 2>/dev/null | awk '/interface:/{print $2; exit}'; }
require_iface() {
  local iface="$1"
  [[ "$iface" =~ ^[A-Za-z0-9._-]+$ ]] || fail "invalid interface name"
  ifconfig "$iface" >/dev/null 2>&1 || fail "interface not found: $iface"
}
require_platform() {
  [[ "$(uname -s)" == "Darwin" ]] || fail "macOS required"
  [[ "$(uname -m)" == "arm64" ]] || fail "Apple-silicon arm64 required for this executor"
}
require_source_identity() {
  [[ -f "$SOURCE" && -f "$PROJECT" && -f "$CONTRACT" ]] || fail "peer source/project/contract missing"
  [[ "$(sha256_file "$SOURCE")" == "$PEER_SOURCE_SHA" ]] || fail "qualified peer source drift"
  [[ "$(sha256_file "$PROJECT")" == "$PEER_PROJECT_SHA" ]] || fail "peer project drift"
  [[ "$(sha256_file "$CONTRACT")" == "$PEER_CONTRACT_SHA" ]] || fail "qualified peer contract drift"
  git -C "$REPO" merge-base --is-ancestor "$QUALIFIED_PEER_HEAD" HEAD >/dev/null 2>&1 || fail "checkout does not descend from qualified peer head"
}
require_dotnet10() {
  command -v dotnet >/dev/null 2>&1 || fail ".NET 10 SDK is required; install the official Arm64 SDK first"
  local version major
  version="$(dotnet --version)"
  major="${version%%.*}"
  [[ "$major" == "10" ]] || fail ".NET SDK major 10 required; found $version"
}
require_peer_address() {
  local iface="$1"
  ifconfig "$iface" | grep -Eq 'inet 10\.77\.0\.2[[:space:]].*netmask 0xfffffffc' || fail "$iface is not configured as 10.77.0.2/30"
  local def
  def="$(default_iface || true)"
  [[ -z "$def" || "$def" != "$iface" ]] || fail "test interface is the default-route interface"
}

usage() {
  cat <<'EOF'
Usage:
  ./mac-peer.sh inspect
  ./mac-peer.sh configure <dedicated-interface>
  ./mac-peer.sh build <dedicated-interface>
  ./mac-peer.sh serve <dedicated-interface>

Use only a dedicated Ethernet interface. `configure` is infrastructure provisioning,
not part of the proof trial. Once `serve` is running, do not probe TCP 41060/41061.
EOF
}

mode="${1:-}"
require_platform

case "$mode" in
  inspect)
    echo "macOS=$(sw_vers -productVersion) build=$(sw_vers -buildVersion) arch=$(uname -m)"
    echo "defaultInterface=$(default_iface || true)"
    echo "interfaces:"
    ifconfig -l | tr ' ' '\n'
    echo
    echo "Choose a dedicated Ethernet interface that is NOT the default interface."
    ;;

  configure)
    iface="${2:-}"
    [[ -n "$iface" ]] || { usage; exit 64; }
    require_iface "$iface"
    def="$(default_iface || true)"
    [[ -z "$def" || "$def" != "$iface" ]] || fail "refusing to reconfigure the default-route interface $iface"
    echo "Provisioning $iface as $PEER_IP/30. This requires sudo and is separate infrastructure setup."
    sudo ifconfig "$iface" inet "$PEER_IP" netmask "$NETMASK" up
    require_peer_address "$iface"
    echo "MAC_PEER_TEST_LINK_CONFIGURED interface=$iface address=$PEER_IP/30 gateway=none-by-script"
    ;;

  build)
    iface="${2:-}"
    [[ -n "$iface" ]] || { usage; exit 64; }
    require_iface "$iface"
    require_peer_address "$iface"
    require_source_identity
    require_dotnet10
    rm -rf "$OUT"
    mkdir -p "$OUT"
    dotnet publish "$PROJECT" -c Release -r osx-arm64 --self-contained false -p:UseAppHost=false -p:DebugType=None -p:DebugSymbols=false -o "$OUT" --nologo
    [[ -f "$OUT/Workbench.PrivatePeerListener.dll" ]] || fail "peer DLL missing after publish"
    unit="$({ dotnet "$OUT/Workbench.PrivatePeerListener.dll" --unit; } | tail -n 1)"
    echo "$unit" | grep -q 'PRIVATE_PEER_PROTOCOL_V1_PURE_CONTROLS_PASS' || fail "peer pure controls did not pass"
    echo "$unit" | grep -q '"networkCalls":false' || fail "peer unit receipt unexpectedly reports network calls"
    echo "MAC_PEER_BUILD_GREEN dllSha256=$(sha256_file "$OUT/Workbench.PrivatePeerListener.dll")"
    ;;

  serve)
    iface="${2:-}"
    [[ -n "$iface" ]] || { usage; exit 64; }
    require_iface "$iface"
    require_peer_address "$iface"
    require_source_identity
    require_dotnet10
    [[ -f "$OUT/Workbench.PrivatePeerListener.dll" ]] || fail "build output absent; run './mac-peer.sh build $iface' first"

    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    evidence="$SCRIPT_DIR/evidence/mac-$stamp"
    mkdir -p "$evidence"
    repo_head="$(git -C "$REPO" rev-parse HEAD)"
    os_version="$(sw_vers -productVersion)"
    os_build="$(sw_vers -buildVersion)"
    dotnet_version="$(dotnet --version)"
    dll_sha="$(sha256_file "$OUT/Workbench.PrivatePeerListener.dll")"
    deps_sha="$(sha256_file "$OUT/Workbench.PrivatePeerListener.deps.json")"
    runtime_sha="$(sha256_file "$OUT/Workbench.PrivatePeerListener.runtimeconfig.json")"
    timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

    cat > "$evidence/mac-peer-provisioning.json" <<EOF
{
  "schema": "matawaka.private-peer-macos-provisioning/v0.1",
  "createdUtc": "$timestamp",
  "repoHead": "$repo_head",
  "qualifiedPeerHead": "$QUALIFIED_PEER_HEAD",
  "peerSourceSha256": "$PEER_SOURCE_SHA",
  "peerProjectSha256": "$PEER_PROJECT_SHA",
  "peerContractSha256": "$PEER_CONTRACT_SHA",
  "peerDllSha256": "$dll_sha",
  "peerDepsSha256": "$deps_sha",
  "peerRuntimeConfigSha256": "$runtime_sha",
  "os": "macOS",
  "osVersion": "$os_version",
  "osBuild": "$os_build",
  "architecture": "arm64",
  "dotnetSdkVersion": "$dotnet_version",
  "interface": "$iface",
  "peerAddress": "$PEER_IP",
  "proofNodeAddress": "$PROOF_IP",
  "prefixLength": 30,
  "managementPort": 41060,
  "targetPort": 41061,
  "testInterfaceIsDefaultRoute": false,
  "dnsUsedByQualification": false,
  "internetTargetUsed": false,
  "networkConfigurationMutationByProofTrial": false,
  "proofClaimed": false
}
EOF

    echo "MAC_PEER_READY evidence=$evidence"
    echo "Do not run nc/telnet/curl/port scanners against 10.77.0.2:41060 or :41061 while this listener is active."
    set +e
    dotnet "$OUT/Workbench.PrivatePeerListener.dll" --serve | tee "$evidence/peer-session.jsonl"
    rc=${PIPESTATUS[0]}
    set -e
    echo "MAC_PEER_EXIT=$rc evidence=$evidence"
    exit "$rc"
    ;;

  *)
    usage
    exit 64
    ;;
esac
