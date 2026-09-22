namespace Matawaka.Workbench.JevLab;

/// <summary>Stable display-safe rejection code, without local paths or input fragments.</summary>
public sealed class JevInputException : Exception
{
    public string Code { get; }
    internal JevInputException(string code) : base(code) => Code = code;
}
