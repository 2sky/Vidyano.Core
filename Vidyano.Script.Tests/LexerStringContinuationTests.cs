using System.Collections.Generic;
using System.Linq;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// A backslash before a literal newline continues a string literal onto the next source line. This must
/// behave identically on LF and CRLF files — a Windows-authored (CRLF) script previously had its CR consumed
/// as the escaped char, leaving the bare LF to end the string and report it as unterminated. See
/// <c>Lexer.ReadString</c>.
/// </summary>
public sealed class LexerStringContinuationTests
{
    private static (Token Str, IReadOnlyList<Diagnostic> Diagnostics) Lex(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.Tokenize();
        return (tokens.First(t => t.Kind == TokenKind.String), lexer.Diagnostics);
    }

    [Fact]
    public void Crlf_Continuation_DecodesLikeLf_AndIsNotUnterminated()
    {
        var (lf, lfDiags) = Lex("\"a\\\nb\"");
        var (crlf, crlfDiags) = Lex("\"a\\\r\nb\"");

        Assert.DoesNotContain(lfDiags, d => d.Kind == ErrorKind.ParseUnterminatedString);
        Assert.DoesNotContain(crlfDiags, d => d.Kind == ErrorKind.ParseUnterminatedString);
        Assert.Equal("a\nb", (string?)lf.Value);     // LF continuation already worked
        Assert.Equal(lf.Value, crlf.Value);           // CRLF must decode identically: CR dropped, single '\n'
    }

    [Fact]
    public void Crlf_Continuation_AcrossMultipleLines_JoinsWithSingleNewlines()
    {
        var (str, diags) = Lex("\"x \\\r\ny \\\r\nz\"");

        Assert.DoesNotContain(diags, d => d.Kind == ErrorKind.ParseUnterminatedString);
        Assert.Equal("x \ny \nz", (string?)str.Value);
    }

    [Fact]
    public void BareCrlf_WithoutBackslash_StillEndsString_AsUnterminated()
    {
        // The fix is scoped to `\`-preceded newlines; an unescaped CRLF still terminates the string.
        var (_, diags) = Lex("\"abc\r\nDEF");

        Assert.Contains(diags, d => d.Kind == ErrorKind.ParseUnterminatedString);
    }

    [Fact]
    public void Continuation_AdvancesLineTracking_SoTokensAfterTheStringAreOnTheRightLine()
    {
        // A token after a `\`-continued string must report its true physical line. Source spans three lines:
        //   line 1: "a\
        //   line 2: b"
        //   line 3: Z
        static int LineOfZ(string src) =>
            new Lexer(src, "<test>").Tokenize()
                .First(t => t.Kind == TokenKind.Identifier && t.Lexeme == "Z").Location.Line;

        Assert.Equal(3, LineOfZ("\"a\\\nb\"\nZ"));        // LF
        Assert.Equal(3, LineOfZ("\"a\\\r\nb\"\r\nZ"));    // CRLF parity
    }
}
