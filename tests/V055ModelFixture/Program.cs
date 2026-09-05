using System.Security.Cryptography;
using System.Text;

static string? Arg(string[] args, string name)
{
    for (var i = 0; i + 1 < args.Length; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

var modelPath = Arg(args, "--model");
var tokenText = Arg(args, "--max-output-tokens");
if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath) || !int.TryParse(tokenText, out var maxTokens) || maxTokens < 1)
    return 11;

var modelBytes = await File.ReadAllBytesAsync(modelPath);
var mode = Encoding.UTF8.GetString(modelBytes).Trim();
var stdin = Console.OpenStandardInput();
using var input = new MemoryStream();
await stdin.CopyToAsync(input);
var requestBytes = input.ToArray();
var requestText = new UTF8Encoding(false, true).GetString(requestBytes);

switch (mode)
{
    case "NORMAL":
    {
        var digest = Convert.ToHexString(SHA256.HashData(modelBytes)).ToLowerInvariant()[..12];
        Console.Out.Write($"fixture:{digest}:{requestText.Trim()}");
        return 0;
    }
    case "STDOUT_OVER":
        Console.Out.Write(new string('O', 200_000));
        return 0;
    case "STDERR_OVER":
        Console.Error.Write(new string('E', 200_000));
        Console.Out.Write("never-admit");
        return 0;
    case "SLEEP":
        await Task.Delay(TimeSpan.FromSeconds(10));
        Console.Out.Write("late");
        return 0;
    case "NONZERO":
        Console.Error.Write("fixture nonzero");
        return 7;
    case "INVALID_UTF8":
    {
        var stdout = Console.OpenStandardOutput();
        await stdout.WriteAsync(new byte[] { 0xC3, 0x28 });
        return 0;
    }
    default:
        Console.Error.Write("unknown fixture model mode");
        return 12;
}
