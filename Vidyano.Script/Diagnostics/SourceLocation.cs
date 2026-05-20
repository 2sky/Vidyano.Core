namespace Vidyano.Script.Diagnostics;

/// <summary>
/// 1-based line/column position in a .visc source. Column counts characters, not bytes.
/// <see cref="SourcePath"/> may be a file path, "&lt;repl&gt;", or "&lt;inline&gt;".
/// </summary>
public readonly record struct SourceLocation(string SourcePath, int Line, int Column)
{
    public static SourceLocation Unknown { get; } = new("<unknown>", 0, 0);

    public override string ToString() => $"{SourcePath}:{Line}:{Column}";
}
