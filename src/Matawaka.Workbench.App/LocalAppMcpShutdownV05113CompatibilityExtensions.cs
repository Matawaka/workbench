namespace Matawaka.Workbench.App;

/// <summary>
/// Exact-revoke receipts expose sibling revocation as a count. The v0.51.13
/// shutdown corridor admits that inherited shape only when the count is zero;
/// it never converts a positive count into acceptable shutdown evidence.
/// </summary>
public static class LocalAppMcpShutdownV05113CompatibilityExtensions
{
    public static Task<(LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)> RecordLeaseTerminalAsync(
        this LocalAppMcpShutdownTransactionV05113Service service,
        string workspaceRoot,
        string applicationId,
        string ownerSessionId,
        string leaseId,
        string exactRevokeReceiptPath,
        int siblingLeasesRevoked,
        CancellationToken cancellationToken)
        => service.RecordLeaseTerminalAsync(
            workspaceRoot,
            applicationId,
            ownerSessionId,
            leaseId,
            exactRevokeReceiptPath,
            siblingLeasesRevoked != 0,
            cancellationToken);
}
