using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workbench.PrivatePeerQualification;

internal static class Program
{
    private const string PeerAddressLiteral = "10.77.0.2";
    private const string ProofNodeAddressLiteral = "10.77.0.1";
    private const int ManagementPort = 41060;
    private const int TargetPort = 41061;
    private const int MaxManagementBytes = 4096;
    private const int MaxControlBytes = 512;
    private const string CommandSchema = "matawaka.private-peer-command/v0.1";
    private const string AckSchema = "matawaka.private-peer-ack/v0.1";
    private const string ReceiptSchema = "matawaka.private-peer-receipt/v0.1";

    private sealed record Command(string Schema, string Op, string SessionId, string NonceHex);
    private sealed record Ack(string Schema, string Op, string SessionId, bool Accepted, bool ProofClaimed);
    private sealed record PeerReceipt(
        string Schema,
        string SessionId,
        string NonceSha256,
        string PeerAddress,
        string ProofNodeAddress,
        int ManagementPort,
        int TargetPort,
        bool ParentControlAccepted,
        string? ParentControlRemoteAddress,
        int TargetAcceptsTotal,
        int ChildWindowAcceptedConnections,
        DateTimeOffset? ChildWindowOpenedUtc,
        DateTimeOffset? ChildWindowClosedUtc,
        bool ProtocolSatisfied,
        bool DnsUsed,
        bool InternetTargetUsed,
        bool ExternalServiceUsed,
        bool NetworkConfigurationMutated,
        bool ProofClaimed,
        string? FailureStage);

    private enum Phase
    {
        Empty,
        Begun,
        ControlObserved,
        Armed,
        Finalized,
        Failed
    }

    private sealed class ProtocolState
    {
        private readonly object gate = new();
        private Phase phase;
        private string sessionId = "";
        private byte[] nonceDigest = [];
        private bool parentControlAccepted;
        private string? parentControlRemoteAddress;
        private int targetAcceptsTotal;
        private int childWindowAcceptedConnections;
        private DateTimeOffset? childWindowOpenedUtc;
        private DateTimeOffset? childWindowClosedUtc;
        private string? failureStage;

        internal Phase CurrentPhase
        {
            get { lock (gate) return phase; }
        }

        internal Ack Begin(Command command)
        {
            lock (gate)
            {
                Need(phase == Phase.Empty, "BEGIN_STATE");
                ValidateCommand(command, "BEGIN");
                sessionId = command.SessionId;
                nonceDigest = SHA256.HashData(Convert.FromHexString(command.NonceHex));
                phase = Phase.Begun;
                return new Ack(AckSchema, "BEGIN", sessionId, true, false);
            }
        }

        internal void ObserveTarget(string remoteAddress, string? controlLine)
        {
            lock (gate)
            {
                targetAcceptsTotal++;
                if (phase == Phase.Begun)
                {
                    Need(remoteAddress == ProofNodeAddressLiteral, "PARENT_CONTROL_REMOTE_ADDRESS");
                    Need(controlLine is not null, "PARENT_CONTROL_LINE_ABSENT");
                    var parts = controlLine.Split('|');
                    Need(parts.Length == 3 && parts[0] == "CONTROL", "PARENT_CONTROL_LINE_SHAPE");
                    Need(parts[1] == sessionId, "PARENT_CONTROL_SESSION_MISMATCH");
                    Need(IsLowerHex(parts[2], 64), "PARENT_CONTROL_NONCE_SHAPE");
                    byte[] presented = SHA256.HashData(Convert.FromHexString(parts[2]));
                    Need(CryptographicOperations.FixedTimeEquals(presented, nonceDigest), "PARENT_CONTROL_NONCE_MISMATCH");
                    parentControlAccepted = true;
                    parentControlRemoteAddress = remoteAddress;
                    phase = Phase.ControlObserved;
                    return;
                }
                if (phase == Phase.Armed)
                {
                    childWindowAcceptedConnections++;
                    return;
                }
                Fail("TARGET_ACCEPT_OUTSIDE_ALLOWED_PHASE");
            }
        }

        internal Ack Arm(Command command)
        {
            lock (gate)
            {
                Need(phase == Phase.ControlObserved && parentControlAccepted, "ARM_STATE");
                ValidateBoundCommand(command, "ARM");
                childWindowOpenedUtc = DateTimeOffset.UtcNow;
                phase = Phase.Armed;
                return new Ack(AckSchema, "ARM", sessionId, true, false);
            }
        }

        internal PeerReceipt Finalize(Command command)
        {
            lock (gate)
            {
                Need(phase == Phase.Armed, "FINALIZE_STATE");
                ValidateBoundCommand(command, "FINALIZE");
                childWindowClosedUtc = DateTimeOffset.UtcNow;
                Need(childWindowOpenedUtc is not null && childWindowClosedUtc >= childWindowOpenedUtc, "CHILD_WINDOW_TIME_ORDER");
                Need(childWindowClosedUtc - childWindowOpenedUtc <= TimeSpan.FromSeconds(20), "CHILD_WINDOW_TOO_LONG");
                phase = Phase.Finalized;
                return Snapshot();
            }
        }

        internal PeerReceipt Snapshot()
        {
            lock (gate)
            {
                bool satisfied = phase == Phase.Finalized && parentControlAccepted && childWindowAcceptedConnections == 0 && failureStage is null;
                return new PeerReceipt(
                    ReceiptSchema,
                    sessionId,
                    nonceDigest.Length == 0 ? "" : Convert.ToHexString(nonceDigest).ToLowerInvariant(),
                    PeerAddressLiteral,
                    ProofNodeAddressLiteral,
                    ManagementPort,
                    TargetPort,
                    parentControlAccepted,
                    parentControlRemoteAddress,
                    targetAcceptsTotal,
                    childWindowAcceptedConnections,
                    childWindowOpenedUtc,
                    childWindowClosedUtc,
                    satisfied,
                    false,
                    false,
                    false,
                    false,
                    false,
                    failureStage);
            }
        }

        internal void Fail(string stage)
        {
            lock (gate)
            {
                if (failureStage is null) failureStage = stage;
                phase = Phase.Failed;
            }
        }

        private void ValidateBoundCommand(Command command, string expectedOp)
        {
            ValidateCommand(command, expectedOp);
            Need(command.SessionId == sessionId, expectedOp + "_SESSION_MISMATCH");
            byte[] presented = SHA256.HashData(Convert.FromHexString(command.NonceHex));
            Need(CryptographicOperations.FixedTimeEquals(presented, nonceDigest), expectedOp + "_NONCE_MISMATCH");
        }
    }

    private sealed class ProtocolFailure(string stage) : Exception(stage);

    private static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--unit" })) return Unit();
        if (args.SequenceEqual(new[] { "--serve" })) return await ServeAsync();
        Console.Error.WriteLine("EXPLICIT_MODE_REQUIRED");
        return 64;
    }

    private static int Unit()
    {
        int pass = 0;
        string session = "0123456789abcdef0123456789abcdef";
        string nonce = new string('a', 64);
        var begin = new Command(CommandSchema, "BEGIN", session, nonce);
        var arm = new Command(CommandSchema, "ARM", session, nonce);
        var finalize = new Command(CommandSchema, "FINALIZE", session, nonce);

        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(begin);
        var parsed = ParseCommand(encoded);
        Need(parsed == begin, "UNIT_PARSE_ROUNDTRIP"); pass++;
        foreach (byte[] hostile in new[]
        {
            [],
            new byte[MaxManagementBytes + 1],
            Encoding.UTF8.GetBytes("[]"),
            Encoding.UTF8.GetBytes("{\"Schema\":\"matawaka.private-peer-command/v0.1\",\"Op\":\"BEGIN\",\"SessionId\":\"0123456789abcdef0123456789abcdef\",\"NonceHex\":\"" + nonce + "\",\"Authority\":true}"),
            Encoding.UTF8.GetBytes("{\"Schema\":\"matawaka.private-peer-command/v0.1\",\"Op\":\"BEGIN\",\"SessionId\":\"0123456789abcdef0123456789abcdef\",\"NonceHex\":\"" + nonce + "\",\"Op\":\"ARM\"}")
        }) { MustRefuse(() => ParseCommand(hostile)); pass++; }

        var state = new ProtocolState();
        var beginAck = state.Begin(begin);
        Need(beginAck.Accepted && !beginAck.ProofClaimed && state.CurrentPhase == Phase.Begun, "UNIT_BEGIN"); pass++;
        state.ObserveTarget(ProofNodeAddressLiteral, $"CONTROL|{session}|{nonce}");
        Need(state.CurrentPhase == Phase.ControlObserved, "UNIT_CONTROL"); pass++;
        var armAck = state.Arm(arm);
        Need(armAck.Accepted && state.CurrentPhase == Phase.Armed, "UNIT_ARM"); pass++;
        var receipt = state.Finalize(finalize);
        Need(receipt.ProtocolSatisfied && receipt.ChildWindowAcceptedConnections == 0 && receipt.TargetAcceptsTotal == 1 && !receipt.ProofClaimed, "UNIT_FINALIZE_PASS"); pass++;

        var acceptedChild = new ProtocolState();
        _ = acceptedChild.Begin(begin);
        acceptedChild.ObserveTarget(ProofNodeAddressLiteral, $"CONTROL|{session}|{nonce}");
        _ = acceptedChild.Arm(arm);
        acceptedChild.ObserveTarget(ProofNodeAddressLiteral, null);
        var failedReceipt = acceptedChild.Finalize(finalize);
        Need(!failedReceipt.ProtocolSatisfied && failedReceipt.ChildWindowAcceptedConnections == 1, "UNIT_CHILD_ACCEPT_FAILS"); pass++;

        foreach (Action hostile in new Action[]
        {
            () => new ProtocolState().Arm(arm),
            () => new ProtocolState().Finalize(finalize),
            () => { var s=new ProtocolState(); _=s.Begin(begin); s.ObserveTarget("10.77.0.3", $"CONTROL|{session}|{nonce}"); },
            () => { var s=new ProtocolState(); _=s.Begin(begin); s.ObserveTarget(ProofNodeAddressLiteral, $"CONTROL|{session}|{new string('b',64)}"); },
            () => { var s=new ProtocolState(); _=s.Begin(begin); s.ObserveTarget(ProofNodeAddressLiteral, $"CONTROL|{session}|{nonce}"); _=s.Arm(new Command(CommandSchema,"ARM",new string('1',32),nonce)); },
            () => { var s=new ProtocolState(); _=s.Begin(new Command(CommandSchema,"BEGIN",session.ToUpperInvariant(),nonce)); },
            () => { var s=new ProtocolState(); _=s.Begin(new Command(CommandSchema,"BEGIN",session,new string('A',64))); },
            () => { var s=new ProtocolState(); _=s.Begin(new Command("wrong","BEGIN",session,nonce)); },
            () => { var s=new ProtocolState(); _=s.Begin(new Command(CommandSchema,"OTHER",session,nonce)); }
        }) { MustRefuse(hostile); pass++; }

        Need(IPAddress.Parse(PeerAddressLiteral).AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(IPAddress.Parse(PeerAddressLiteral)), "UNIT_PEER_LITERAL"); pass++;
        Need(IPAddress.Parse(ProofNodeAddressLiteral).AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(IPAddress.Parse(ProofNodeAddressLiteral)), "UNIT_PROOF_LITERAL"); pass++;
        Need(PeerAddressLiteral != ProofNodeAddressLiteral && ManagementPort != TargetPort, "UNIT_TOPOLOGY_DISTINCT"); pass++;
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PRIVATE_PEER_PROTOCOL_V1_PURE_CONTROLS_PASS", passed = pass, networkCalls = false }));
        return 0;
    }

    private static async Task<int> ServeAsync()
    {
        IPAddress peerAddress = IPAddress.Parse(PeerAddressLiteral);
        var management = new TcpListener(peerAddress, ManagementPort);
        var target = new TcpListener(peerAddress, TargetPort);
        var state = new ProtocolState();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            management.Start(4);
            target.Start(4);
            Task targetTask = RunTargetLoopAsync(target, state, lifetime.Token);
            while (!lifetime.IsCancellationRequested && state.CurrentPhase is not Phase.Finalized and not Phase.Failed)
            {
                using TcpClient client = await management.AcceptTcpClientAsync(lifetime.Token);
                string remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                Need(remote == ProofNodeAddressLiteral, "MANAGEMENT_REMOTE_ADDRESS");
                using NetworkStream stream = client.GetStream();
                byte[] line = await ReadBoundedLineAsync(stream, MaxManagementBytes, lifetime.Token);
                Command command = ParseCommand(line);
                if (command.Op == "BEGIN")
                {
                    Ack ack = state.Begin(command);
                    await WriteJsonLineAsync(stream, ack, lifetime.Token);
                }
                else if (command.Op == "ARM")
                {
                    Ack ack = state.Arm(command);
                    await WriteJsonLineAsync(stream, ack, lifetime.Token);
                }
                else if (command.Op == "FINALIZE")
                {
                    PeerReceipt receipt = state.Finalize(command);
                    await WriteJsonLineAsync(stream, receipt, lifetime.Token);
                    break;
                }
                else throw new ProtocolFailure("MANAGEMENT_OP");
            }
            target.Stop();
            try { await targetTask; } catch (OperationCanceledException) { }
            PeerReceipt final = state.Snapshot();
            Console.WriteLine(JsonSerializer.Serialize(final));
            return final.ProtocolSatisfied ? 0 : 3;
        }
        catch (Exception e) when (e is ProtocolFailure or IOException or SocketException or JsonException or OperationCanceledException)
        {
            state.Fail(e is ProtocolFailure ? e.Message : "PEER_MANAGED_" + e.GetType().Name);
            Console.WriteLine(JsonSerializer.Serialize(state.Snapshot()));
            return 3;
        }
        finally
        {
            management.Stop();
            target.Stop();
        }
    }

    private static async Task RunTargetLoopAsync(TcpListener listener, ProtocolState state, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && state.CurrentPhase is not Phase.Finalized and not Phase.Failed)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                string remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                string? control = null;
                if (state.CurrentPhase == Phase.Begun)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    byte[] line = await ReadBoundedLineAsync(client.GetStream(), MaxControlBytes, readTimeout.Token);
                    control = Encoding.UTF8.GetString(line);
                }
                state.ObserveTarget(remote, control);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception e) when (e is ProtocolFailure or IOException or SocketException or OperationCanceledException)
            {
                state.Fail(e is ProtocolFailure ? e.Message : "TARGET_MANAGED_" + e.GetType().Name);
                break;
            }
            finally { client?.Dispose(); }
        }
    }

    private static Command ParseCommand(byte[] bytes)
    {
        Need(bytes.Length is > 0 and <= MaxManagementBytes, "COMMAND_SIZE");
        using JsonDocument doc = JsonDocument.Parse(bytes);
        Need(doc.RootElement.ValueKind == JsonValueKind.Object, "COMMAND_OBJECT");
        string[] expected = ["Schema", "Op", "SessionId", "NonceHex"];
        string[] names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Need(names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length && names.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "COMMAND_KEYS");
        Command value = JsonSerializer.Deserialize<Command>(doc.RootElement.GetRawText()) ?? throw new ProtocolFailure("COMMAND_DESERIALIZE");
        ValidateCommand(value, value.Op);
        Need(value.Op is "BEGIN" or "ARM" or "FINALIZE", "COMMAND_OP");
        return value;
    }

    private static void ValidateCommand(Command command, string expectedOp)
    {
        Need(command.Schema == CommandSchema, expectedOp + "_SCHEMA");
        Need(command.Op == expectedOp, expectedOp + "_OP");
        Need(Guid.TryParseExact(command.SessionId, "N", out _) && command.SessionId == command.SessionId.ToLowerInvariant(), expectedOp + "_SESSION_SHAPE");
        Need(IsLowerHex(command.NonceHex, 64), expectedOp + "_NONCE_SHAPE");
    }

    private static bool IsLowerHex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static async Task<byte[]> ReadBoundedLineAsync(NetworkStream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one, cancellationToken);
            if (n == 0) throw new ProtocolFailure("LINE_EOF");
            if (one[0] == (byte)'\n') break;
            if (one[0] == 0) throw new ProtocolFailure("LINE_NUL");
            Need(buffer.Length < maxBytes, "LINE_TOO_LARGE");
            buffer.WriteByte(one[0]);
        }
        byte[] bytes = buffer.ToArray();
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];
        _ = new UTF8Encoding(false, true).GetString(bytes);
        return bytes;
    }

    private static async Task WriteJsonLineAsync<T>(NetworkStream stream, T value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        Need(payload.Length <= MaxManagementBytes, "RESPONSE_TOO_LARGE");
        await stream.WriteAsync(payload, cancellationToken);
        await stream.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void MustRefuse(Action action)
    {
        bool refused = false;
        try { action(); }
        catch (Exception e) when (e is ProtocolFailure or JsonException or FormatException) { refused = true; }
        Need(refused, "HOSTILE_CASE_ACCEPTED");
    }

    private static void Need(bool condition, string stage)
    {
        if (!condition) throw new ProtocolFailure(stage);
    }
}
