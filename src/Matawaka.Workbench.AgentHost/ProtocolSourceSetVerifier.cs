using System.Security.Cryptography;
using System.Text;
using Matawaka.Workbench.Catalog;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.AgentHost;

public sealed record ProtocolSourceFileVerification(
    string Path,
    string ExpectedGitBlobSha1,
    string RawWorktreeGitBlobSha1,
    string CanonicalGitBlobSha1,
    bool CheckoutLineEndingNormalizationApplied,
    bool Matched);

public sealed record ProtocolSourceSetVerification(
    string Schema,
    string Repository,
    string SourceBindingMode,
    string OriginFrontier,
    string ObservedRepositoryHead,
    bool RepositoryHeadMatchesOrigin,
    bool SourceSetMatched,
    IReadOnlyList<ProtocolSourceFileVerification> Files,
    IReadOnlyList<string> NonEffects);

/// <summary>
/// Workbench-local byte verifier for the exact UU-AAP text files consumed by
/// the compatibility adapters. Git blob SHA-1 is used only as Git object
/// identity; it is not represented as a modern cryptographic trust primitive.
///
/// Verification is intentionally independent of Git clean filters and does not
/// launch git or another helper process. Raw worktree bytes are checked first.
/// If the raw bytes do not match, v0.10.2 permits exactly one representation
/// canonicalization for the bound .js/.json text set: CRLF is mapped to LF at
/// the byte level. No other whitespace, encoding, or content transformation is
/// accepted.
/// </summary>
public static class ProtocolSourceSetVerifier
{
    public const string Schema = "matawaka.protocol-source-set-verification/v0.10.2";
    public const string BindingMode = "relevant-source-set/git-blob-identity+bounded-crlf-canonicalization/v0.10.2";

    public static ProtocolSourceSetVerification Verify(IReadOnlyList<CatalogRepository> catalog)
    {
        var repository = catalog.FirstOrDefault(item =>
            string.Equals(item.Name, "uu-aap", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Local uu-aap repository is required for relevant source-set verification.");

        var repositoryRoot = Path.GetFullPath(repository.Root);
        var rootPrefix = repositoryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? repositoryRoot
            : repositoryRoot + Path.DirectorySeparatorChar;

        var files = new List<ProtocolSourceFileVerification>();
        foreach (var binding in PclCompatibleProgress.RelevantSources)
        {
            if (!string.Equals(binding.Repository, "Matawaka/uu-aap", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected relevant-source repository binding: {binding.Repository}");

            var relative = binding.Path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                throw new InvalidDataException($"Bound UU-AAP source file is missing or escapes repository root: {binding.Path}");

            var bytes = File.ReadAllBytes(full);
            var raw = ComputeGitBlobSha1(bytes);
            var expected = binding.BlobSha.ToLowerInvariant();

            var normalizationApplied = false;
            var canonical = raw;
            if (!string.Equals(raw, expected, StringComparison.OrdinalIgnoreCase) && IsBoundTextPath(binding.Path))
            {
                var normalizedBytes = NormalizeCrLfToLf(bytes, out normalizationApplied);
                if (normalizationApplied)
                    canonical = ComputeGitBlobSha1(normalizedBytes);
            }

            var matched = string.Equals(raw, expected, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(canonical, expected, StringComparison.OrdinalIgnoreCase);

            files.Add(new ProtocolSourceFileVerification(
                binding.Path,
                expected,
                raw,
                canonical,
                normalizationApplied,
                matched));
        }

        var matchedSet = files.Count == PclCompatibleProgress.RelevantSources.Count && files.All(item => item.Matched);
        var headMatchesOrigin = string.Equals(
            repository.Head,
            PclCompatibleProgress.UuAapFrontier,
            StringComparison.OrdinalIgnoreCase);

        var receipt = new ProtocolSourceSetVerification(
            Schema,
            "Matawaka/uu-aap",
            BindingMode,
            PclCompatibleProgress.UuAapFrontier,
            repository.Head,
            headMatchesOrigin,
            matchedSet,
            files,
            new[]
            {
                "repository HEAD equality is not required when every relevant bound source blob matches",
                "raw worktree bytes are checked before any representation canonicalization",
                "only CRLF-to-LF byte canonicalization is allowed for the fixed bound .js/.json text source set",
                "no Git filters or helper processes are executed by source-set verification",
                "unrelated documentation/participation commits do not mint or revoke semantic authority",
                "Git blob SHA-1 is used only as repository object identity",
                "source-set match does not establish canonical UU-AAP conformance",
                "no git fetch",
                "no repository mutation",
                "no network access"
            });

        if (!receipt.SourceSetMatched)
        {
            var mismatches = string.Join(", ", receipt.Files.Where(item => !item.Matched).Select(item => item.Path));
            throw new InvalidDataException($"Relevant UU-AAP source-set mismatch: {mismatches}");
        }

        return receipt;
    }

    private static bool IsBoundTextPath(string path)
        => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static byte[] NormalizeCrLfToLf(byte[] bytes, out bool changed)
    {
        var crlfCount = 0;
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == 0x0d && bytes[i + 1] == 0x0a)
            {
                crlfCount++;
                i++;
            }
        }

        if (crlfCount == 0)
        {
            changed = false;
            return bytes;
        }

        var normalized = new byte[bytes.Length - crlfCount];
        var destination = 0;
        for (var source = 0; source < bytes.Length; source++)
        {
            if (source + 1 < bytes.Length && bytes[source] == 0x0d && bytes[source + 1] == 0x0a)
            {
                normalized[destination++] = 0x0a;
                source++;
            }
            else
            {
                normalized[destination++] = bytes[source];
            }
        }

        changed = true;
        return normalized;
    }

    private static string ComputeGitBlobSha1(byte[] bytes)
    {
        var prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(prefix);
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
