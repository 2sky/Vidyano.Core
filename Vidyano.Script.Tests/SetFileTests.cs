using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vidyano;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Coverage for <c>SET &lt;attr&gt; = FILE "&lt;path&gt;"</c>: the parser shape, the ported
/// <see cref="Vidyano.BinaryFile"/> wire-format type, the <see cref="SafePath"/> containment check, the
/// pure data-type dispatch (<c>BinaryFile</c> → <c>name|base64</c> vs <c>Image</c> → bare base64), and the
/// path-security behaviour of the FILE read driven through the interpreter (the read fails before any
/// session call, so it needs no server).
/// </summary>
public sealed class SetFileTests
{
    // --- parser shape -------------------------------------------------------------------------

    private static T SingleStatement<T>(string body) where T : Statement
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        var stmts = ast.Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<T>(stmts[0]);
    }

    [Fact]
    public void SetFile_ParsesWithFileValueKindAndNoHint()
    {
        var stmt = SingleStatement<SetStmt>("SET Photo = FILE \"fixtures/avatar.png\"");
        Assert.Equal("Photo", stmt.Attribute);
        Assert.Equal(SetValueKind.File, stmt.ValueKind);
        Assert.Null(stmt.Hint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Equal("fixtures/avatar.png", lit.Value);
    }

    [Fact]
    public void SetFile_LintsCleanOnArbitraryAttribute()
    {
        Assert.Empty(VidyanoScript.Lint("SET Whatever = FILE \"x.bin\""));
    }

    [Fact]
    public void SetId_StillRawIdHintWithValueValueKind()
    {
        // Regression: FILE is in the same if/else chain as LOOKUP/ID — the ID hint is unaffected.
        var stmt = SingleStatement<SetStmt>("SET Status = ID \"active\"");
        Assert.Equal(ReferenceHintKind.RawId, stmt.Hint);
        Assert.Equal(SetValueKind.Value, stmt.ValueKind);
    }

    [Fact]
    public void SetFile_WithNoValue_IsParseError()
    {
        var diags = VidyanoScript.Lint("SET Photo = FILE");
        Assert.NotEmpty(diags);
    }

    // --- Vidyano.BinaryFile (ported wire-format type) -----------------------------------------

    [Fact]
    public void BinaryFile_ToString_IsNamePipeBase64()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        var file = new BinaryFile("a.bin", data);
        Assert.Equal("a.bin|" + Convert.ToBase64String(data), file.ToString());
    }

    [Fact]
    public void BinaryFile_ToString_NoData_IsNamePipe()
    {
        Assert.Equal("a.bin|", new BinaryFile("a.bin").ToString());
    }

    [Fact]
    public void BinaryFile_RoundTripsThroughServiceString()
    {
        var original = new BinaryFile("report.pdf", new byte[] { 9, 8, 7, 6, 5 });
        var parsed = BinaryFile.FromServiceString(original.ToString());
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void BinaryFile_FromServiceString_NullOrEmpty_IsNull()
    {
        Assert.Null(BinaryFile.FromServiceString(null));
        Assert.Null(BinaryFile.FromServiceString(""));
    }

    [Fact]
    public void BinaryFile_FromServiceString_SplitsOnLastPipe()
    {
        // A file name may itself contain a pipe; the data is everything after the LAST one.
        var data = new byte[] { 42 };
        var parsed = BinaryFile.FromServiceString("weird|name.txt|" + Convert.ToBase64String(data));
        Assert.NotNull(parsed);
        Assert.Equal("weird|name.txt", parsed!.FileName);
        Assert.Equal(data, parsed.Data);
    }

    [Fact]
    public void BinaryFile_TryParse_MalformedBase64_IsFalse()
    {
        Assert.False(BinaryFile.TryParse("a.bin|not valid base64!!!", out var result));
        Assert.Null(result);
    }

    [Fact]
    public void BinaryFile_NullData_DoesNotThrowOnContractMethods()
    {
        // Data has a public setter; Equals/GetHashCode/ToString must stay total even if it's nulled out.
        var nulled = new BinaryFile("a.bin") { Data = null! };
        Assert.Equal("a.bin|", nulled.ToString());
        _ = nulled.GetHashCode();
        Assert.False(nulled.Equals(new BinaryFile("a.bin", new byte[] { 1 })));
        Assert.True(nulled.Equals(new BinaryFile("a.bin") { Data = null! }));
    }

    [Fact]
    public void BinaryFile_ImplicitString_IsServiceString()
    {
        string? s = new BinaryFile("a.bin", new byte[] { 1 });
        Assert.Equal("a.bin|" + Convert.ToBase64String(new byte[] { 1 }), s);
    }

    // --- SafePath containment -----------------------------------------------------------------

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "visc-safepath-root");

    [Fact]
    public void SafePath_ContainedRelativePath_Resolves()
    {
        Assert.True(SafePath.TryResolveContained(Root, "sub/file.bin", out var full));
        Assert.StartsWith(Path.GetFullPath(Root), full);
    }

    [Fact]
    public void SafePath_TraversalEscape_IsRejected()
    {
        Assert.False(SafePath.TryResolveContained(Root, "../escape.bin", out _));
    }

    [Fact]
    public void SafePath_AbsolutePath_IsRejected()
    {
        Assert.False(SafePath.TryResolveContained(Root, "/etc/passwd", out _));
    }

    [Fact]
    public void SafePath_SiblingPrefix_IsRejected()
    {
        // base ".../root" must not admit ".../rootEVIL" via the prefix check.
        Assert.False(SafePath.TryResolveContained(Root, "../visc-safepath-rootEVIL/x", out _));
    }

    // --- FormatFileValue: data-type dispatch (the Image-vs-BinaryFile distinction) ------------

    private static readonly SourceLocation Loc = new("<test>", 0, 0);

    [Fact]
    public void FormatFileValue_BinaryFile_IsNamePipeBase64()
    {
        var data = new byte[] { 1, 2, 3 };
        var res = VidyanoSession.FormatFileValue(DataTypes.BinaryFile, "File", "a.bin", data, Loc);
        Assert.True(res.Ok);
        Assert.Equal("a.bin|" + Convert.ToBase64String(data), res.Value);
    }

    [Fact]
    public void FormatFileValue_Image_IsBareBase64_NoFilenameNoPipe()
    {
        var data = new byte[] { 1, 2, 3 };
        var res = VidyanoSession.FormatFileValue(DataTypes.Image, "Avatar", "a.png", data, Loc);
        Assert.True(res.Ok);
        Assert.Equal(Convert.ToBase64String(data), res.Value);
        Assert.DoesNotContain("|", res.Value);
    }

    [Fact]
    public void FormatFileValue_NonFileType_FailsLoudly()
    {
        var res = VidyanoSession.FormatFileValue(DataTypes.String, "Name", "a.txt", new byte[] { 1 }, Loc);
        Assert.False(res.Ok);
        Assert.Equal(ErrorKind.ParseUnexpectedToken, res.Error!.Kind);
    }

    // --- FILE read path-security through the interpreter (server-free) ------------------------

    private static VidyanoSession NewSession() => new("https://127.0.0.1:1", acceptAnyServerCertificate: true);

    private static Interpreter NewInterpreter(VidyanoSession session, string? fileRoot) =>
        new(TestSessionBook.Wrap(session), initialVars: null, mode: GuardMode.Navigation, tools: null,
            cancellationToken: default, fileRoot: fileRoot);

    private static ScriptAst Parse(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    private static StatementResult RunSingle(string body, string? fileRoot)
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, fileRoot);
        var result = interp.RunAsync(Parse(body)).GetAwaiter().GetResult();
        return result.Steps.SelectMany(s => s.Statements).Single();
    }

    [Fact]
    public void FileRead_TraversalOutsideRoot_IsResolveFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "visc-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stmt = RunSingle("SET Photo = FILE \"../escape.bin\"", root);
            Assert.False(stmt.Ok);
            Assert.Contains(stmt.Diagnostics, d => d.Kind == ErrorKind.ResolveFile);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FileRead_AbsolutePath_IsResolveFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "visc-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stmt = RunSingle("SET Photo = FILE \"/etc/passwd\"", root);
            Assert.False(stmt.Ok);
            Assert.Contains(stmt.Diagnostics, d => d.Kind == ErrorKind.ResolveFile);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FileRead_MissingFile_IsResolveFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "visc-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stmt = RunSingle("SET Photo = FILE \"nope.bin\"", root);
            Assert.False(stmt.Ok);
            Assert.Contains(stmt.Diagnostics, d => d.Kind == ErrorKind.ResolveFile);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FileRead_ValidContainedFile_ReadsAndReachesSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "visc-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "ok.bin"), new byte[] { 1, 2, 3 });
            var stmt = RunSingle("SET Photo = FILE \"ok.bin\"", root);
            // The read + containment succeeded, so it proceeded to the session; with no signed-in PO that
            // surfaces as state-no-current-po — crucially NOT resolve-file.
            Assert.False(stmt.Ok);
            Assert.DoesNotContain(stmt.Diagnostics, d => d.Kind == ErrorKind.ResolveFile);
            Assert.Contains(stmt.Diagnostics, d => d.Kind == ErrorKind.StateNoCurrentPo);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
