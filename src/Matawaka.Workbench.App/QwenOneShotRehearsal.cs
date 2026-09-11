using System.IO;
using System.Security.Cryptography;
using static Matawaka.Workbench.App.QwenIsolatedOneShotAdapter;

namespace Matawaka.Workbench.App;

// Internal pure event machine, deliberately NOT a production lease or process API.
// Test evidence cannot escape into RequireLiveAdmission, which always refuses.
internal sealed record QwenSyntheticHostEvidence(string EvidenceClass, string ReviewDigest,
    string ExecutableDigest, string ModelDigest, bool NetworkDenied, bool SingleOwnedProcess,
    bool AllModelLayersOnGpu, bool CpuFallback, bool ToolsEnabled);
internal sealed record QwenSyntheticCandidate(string Status, string ReviewDigest, string EnvelopeDigest,
    string Text, string TextSha256, bool ProcessNetworkIsolationProven = false,
    bool ModelRequestPerformed = false, bool ContentReviewComplete = false, bool DisplayPermitCreated = false);

internal sealed class QwenOneShotRehearsal : IDisposable
{
    internal sealed class SyntheticTicket { }
    private readonly object gate = new();
    private readonly SyntheticTicket ticket = new();
    private readonly QwenCodeOnlyPlan plan;
    private readonly long issued, expires;
    private long observed;
    private string state = "PREPARED_SYNTHETIC_ONLY";
    private readonly MemoryStream stdout = new();
    private int stderrBytes;
    private bool disposed;

    internal QwenOneShotRehearsal(QwenCodeOnlyPlan plan, long syntheticIssuedMilliseconds)
    {
        Need(plan is not null && syntheticIssuedMilliseconds >= 0 &&
            syntheticIssuedMilliseconds <= long.MaxValue - plan!.EffectiveBudgetMilliseconds, "TEMPORAL_ORIGIN");
        this.plan = plan!;
        issued = observed = syntheticIssuedMilliseconds;
        expires = issued + plan!.EffectiveBudgetMilliseconds;
    }
    internal SyntheticTicket Ticket => ticket;
    internal string State { get { lock (gate) return state; } }

    private void CheckTime(long now)
    {
        Need(now >= issued && now >= observed && now < expires, "TEMPORAL_REFUSAL");
        observed = now;
    }
    private void CheckHost(QwenSyntheticHostEvidence host)
    {
        Need(host is not null && host.EvidenceClass == "SYNTHETIC_TEST_ONLY" &&
            host.ReviewDigest == plan.ReviewDigest && host.ExecutableDigest == plan.ExecutableDigest &&
            host.ModelDigest == plan.ModelDigest, "SYNTHETIC_BINDING");
        Need(host!.NetworkDenied && host.SingleOwnedProcess && host.AllModelLayersOnGpu &&
            !host.CpuFallback && !host.ToolsEnabled, "SYNTHETIC_ISOLATION_REFUSAL");
    }
    private void ClearOutput()
    {
        if (stdout.TryGetBuffer(out var buffer) && buffer.Array is not null)
            CryptographicOperations.ZeroMemory(buffer.Array);
        stdout.SetLength(0);
    }
    private void Refuse() { state = "REFUSED_SYNTHETIC_ONLY"; ClearOutput(); }

    internal void Begin(SyntheticTicket supplied, long now, QwenSyntheticHostEvidence host,
        bool cancelled = false, bool superseded = false)
    {
        lock (gate)
        {
            Need(!disposed && ReferenceEquals(supplied, ticket) && state == "PREPARED_SYNTHETIC_ONLY", "TICKET_UNAVAILABLE");
            // Consume before all attempt-specific checks. Clock rollback cannot revive refusal.
            Refuse();
            CheckTime(now);
            Need(!cancelled && !superseded, "REQUEST_CANCELLED_OR_SUPERSEDED");
            CheckHost(host);
            state = "RUNNING_SYNTHETIC_ONLY";
        }
    }

    internal void ObserveStream(long now, ReadOnlySpan<byte> bytes, bool isStderr = false)
    {
        lock (gate)
        {
            Need(!disposed && state == "RUNNING_SYNTHETIC_ONLY", "NOT_RUNNING");
            try
            {
                CheckTime(now);
                if (isStderr)
                {
                    Need(bytes.Length <= plan.MaxStderrBytes - stderrBytes, "STDERR_LIMIT");
                    stderrBytes += bytes.Length; // Count only; raw stderr is never retained.
                }
                else
                {
                    Need(bytes.Length <= plan.MaxStdoutBytes - stdout.Length, "STDOUT_LIMIT");
                    stdout.Write(bytes);
                }
            }
            catch { Refuse(); throw; }
        }
    }

    internal QwenSyntheticCandidate Finish(long now, int exitCode, bool allOwnedProcessesExited,
        int observedTokens, QwenSyntheticHostEvidence host)
    {
        lock (gate)
        {
            Need(!disposed && state == "RUNNING_SYNTHETIC_ONLY", "NOT_RUNNING");
            try
            {
                CheckTime(now); CheckHost(host);
                Need(exitCode == 0 && allOwnedProcessesExited, "TERMINAL_PROCESS_REFUSAL");
                Need(observedTokens > 0 && observedTokens <= plan.MaxOutputTokens, "TOKEN_LIMIT");
                var bytes = stdout.ToArray();
                string text;
                try { text = Text(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                Need(text.Length > 0 && text.Length <= plan.MaxOutputChars && text == text.Trim() &&
                    !text.Any(c => char.IsControl(c) && c is not '\n' and not '\t'), "OUTPUT_TEXT_REFUSAL");
                state = "COMPLETED_SYNTHETIC_ONLY";
                ClearOutput();
                return new("SYNTHETIC_UNTRUSTED_CANDIDATE_NOT_MODEL_OUTPUT", plan.ReviewDigest,
                    plan.EnvelopeDigest, text, TextDigest(text));
            }
            catch { Refuse(); throw; }
        }
    }

    internal void Cancel()
    {
        lock (gate)
        {
            if (!disposed && state is "PREPARED_SYNTHETIC_ONLY" or "RUNNING_SYNTHETIC_ONLY") Refuse();
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (state != "COMPLETED_SYNTHETIC_ONLY") Refuse();
            ClearOutput(); stdout.Dispose(); disposed = true;
        }
    }
}
