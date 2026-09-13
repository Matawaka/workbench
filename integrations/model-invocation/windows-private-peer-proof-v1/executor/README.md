# Windows private-peer qualification executor v1

This directory prepares the physical two-node executor required by issues #106/#107. It does **not** claim the network theorem by itself and it does not authorize any model/provider use.

## Frozen qualification inputs

- qualified peer protocol head: `efff344bc1418e9b1da946a8493f854bf8ada9af`
- qualified peer source SHA-256: `b01ab34cd9923d34b72c4ca557ffb292cb9621a896be0fbbb020ddd88d8bc049`
- qualified peer contract SHA-256: `793de6f7d0f68317d331b2e7e3eec20e02dcdd9acac5c665ceab633122aea1ca`
- qualified coordinator head: `8bb6ccad8a69d1c937ea9a6e65899aeff89dc1fd`
- qualified coordinator source SHA-256: `8f59e72f2d5fa556acc39e5054abac5fcd6cf0a200e61bebb4d1b968df939288`
- qualified coordinator project SHA-256: `1e4b7b4a0ee3ed8803ad4e55273ce8e43e7ef71e0227756a68a9fb4f4ccc8b21`
- pinned `NativeBoundary.cs` Git blob: `8a8f91a13c114e9c224cf449193800f0fedfdaf2`

The only proof theorem this executor may attempt is:

`OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN`

PASS still requires the Windows diagnostic classifier to be exactly `NETISO_ERROR_TYPE_PRIVATE_NETWORK (1)` **and** the remote peer to report zero isolated-child accepts. Socket timeout/refusal is never proof.

## Required physical topology

Preferred topology:

```text
Windows Server 2025 x64 proof node             MacBook M2 synthetic peer
10.77.0.1/30                                   10.77.0.2/30
        |                                             |
        +--------- dedicated Ethernet link -----------+

management: TCP 41060
proof target: TCP 41061
subnet mask: 255.255.255.252
no gateway on the test link
no DNS on the test link
```

Use a dedicated Ethernet port/USB Ethernet adapter on each side if possible. Do not reuse the normal Internet uplink for the canonical trial.

The proof node must be Windows Server 2025 x64 for the canonical #106 closure. Running the coordinator on ordinary Windows 10/11 can be useful diagnostically, but it is not the already-qualified executor identity and must not close #106.

If the physical Windows machine is not Windows Server 2025, use a Windows Server 2025 x64 VM and attach a dedicated physical Ethernet adapter through an **external Hyper-V switch with management-OS sharing disabled**. The host should not also own the test-link address.

## Files

- `mac-peer.sh` — inspect/configure the dedicated macOS test interface, build the frozen peer source, emit provisioning receipts, and start the peer listener.
- `Prepare-WindowsProofNode.ps1` — fail-closed Windows Server 2025 preflight, build the qualified coordinator, prepare the closed `trial` directory, and emit a provisioning receipt. It performs no peer network call.
- `Run-WindowsPrivatePeerTrial.ps1` — explicit one-shot execution of the real two-node qualification. Run it only after the Mac listener is ready.
- `START_HERE_RU.md` — shorter Russian operator guide.

## Software prerequisites

### MacBook M2

- Apple-silicon macOS.
- .NET 10 SDK (`dotnet --version` major version 10). The peer is built locally from the qualified source; no downloaded opaque peer binary is trusted by this bundle.
- A dedicated Ethernet interface for the test link.

### Windows proof node

- Windows Server 2025 x64, build family `10.0.26100`.
- .NET 10 SDK.
- A dedicated Ethernet interface configured as `10.77.0.1/30`, with no default route and no DNS servers on that interface.
- The parent PowerShell elevation state is recorded in the provisioning receipt but is not itself used as proof. The child token still must independently prove AppContainer, zero capabilities, low integrity, non-elevated state and owned-Job membership.

## Order of operation

### 1. Cable the two machines

Connect the dedicated Windows Ethernet adapter directly to the Mac Ethernet adapter, or place only those two adapters on a small isolated switch.

Do not connect the test link to the household/router LAN for the canonical run.

### 2. Configure the Mac test link

From this repository checkout on the Mac:

```bash
cd integrations/model-invocation/windows-private-peer-proof-v1/executor
chmod +x mac-peer.sh
./mac-peer.sh inspect
./mac-peer.sh configure <dedicated-interface>
./mac-peer.sh build <dedicated-interface>
```

`configure` is an explicit provisioning action. It refuses to modify the current default-route interface and assigns only `10.77.0.2/30` to the selected dedicated interface.

The build step verifies the frozen peer source and contract SHA-256 values before compiling.

### 3. Configure the Windows test link

Provision the dedicated Windows interface manually as:

```text
IPv4:        10.77.0.1
prefix:      /30
subnet mask: 255.255.255.252
default GW:  none
DNS:         none
```

If Windows Server 2025 is a Hyper-V VM, attach only its test vNIC to the dedicated external switch. Keep management-OS sharing disabled for that physical adapter.

### 4. Prepare the Windows proof bundle

In PowerShell:

```powershell
cd <workbench-repository>
Set-ExecutionPolicy -Scope Process Bypass
.\integrations\model-invocation\windows-private-peer-proof-v1\executor\Prepare-WindowsProofNode.ps1
```

The script refuses the canonical preparation unless all of the following hold:

- Windows Server 2025 x64 / build family 26100;
- local exact address `10.77.0.1/30` exists;
- `10.77.0.2` is not local;
- the test interface is not a default-route interface;
- the test interface has no configured DNS server;
- the qualified coordinator/peer/NativeBoundary identities have not drifted;
- .NET 10 SDK is present.

It creates a fresh closed trial root beneath `%TEMP%\windows-isolation-qualification-*\trial`, the exact 4-file runtime manifest expected by `NativeBoundary`, synthetic sentinel files, and a provisioning receipt. It does **not** contact the Mac.

### 5. Start the Mac peer

On the Mac:

```bash
./mac-peer.sh serve <dedicated-interface>
```

Leave this terminal open. Once `--serve` is running, **do not run `nc`, `telnet`, `curl`, port scanners, `Test-NetConnection`, or any other probe against TCP 41060/41061**. The peer protocol is intentionally one-shot and unexpected connections can invalidate the session.

### 6. Run the Windows trial exactly once

The prepare script prints the exact path of its generated `executor-state.json`. Use it:

```powershell
.\integrations\model-invocation\windows-private-peer-proof-v1\executor\Run-WindowsPrivatePeerTrial.ps1 `
  -StatePath "<path-to-executor-state.json>" `
  -ExecuteQualifiedTrial
```

The explicit switch is mandatory: preparing the bundle is not authority to execute the network qualification.

This is the first canonical network interaction with the peer. The coordinator performs:

`BEGIN -> parent CONTROL -> ARM -> AppContainer child attempt -> FINALIZE`

The trial is GREEN only if, in the same session:

- normal parent control reaches `10.77.0.2:41061`;
- AppContainer is zero-capability, low-integrity, non-elevated, in the owned Job;
- Windows returns diagnostic type exactly `1` (`PRIVATE_NETWORK`);
- child socket is not connected and socket behavior is not used as proof;
- peer receipt reports `TargetAcceptsTotal=1` and `ChildWindowAcceptedConnections=0`;
- cleanup succeeds;
- no DNS/Internet/global network mutation/model/provider action occurred.

Any other classifier (`0`, `2`, `3`), missing peer receipt, unexpected connection, cleanup failure, or identity drift is FAIL_CLOSED.

## Evidence to keep after the run

Keep these files together:

1. Mac `mac-peer-provisioning.json`.
2. Mac `peer-session.jsonl` (contains the final peer receipt on completion).
3. Windows `windows-proof-provisioning.json`.
4. Windows `proof-evidence.json`.
5. Windows `coordinator-stdout.jsonl`.
6. Windows generated `manifest.json` and `executor-state.json`.

Do not reinterpret a nonzero trial exit as success because a wrapper script or CI step itself completed.

## What this does not authorize

This bundle does not install or invoke Qwen, llama, Ollama, a game, or any production model host. It does not register a production provider. Even a GREEN private-network theorem leaves `HOST_ISOLATION_PROVIDER_NOT_QUALIFIED` in place until a later, separate real-host/provider qualification succeeds.
