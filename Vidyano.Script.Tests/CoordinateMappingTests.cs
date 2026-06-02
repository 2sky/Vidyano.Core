using Vidyano.Script.Diagnostics;
using Vidyano.Script.LanguageServer;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Unit tests for the coordinate math exposed as statics on <see cref="ViscLanguageService"/> — the
/// "tokens carry no length" honesty leak. These run with no service instance and no document cache.
/// </summary>
public sealed class CoordinateMappingTests
{
    [Fact]
    public void ToLsp_OneBasedCharColumn_BecomesZeroBased()
    {
        var loc = new SourceLocation("<t>", 2, 3); // line 2, column 3 (1-based)
        var (line, ch) = ViscLanguageService.ToLsp(loc, "first line\nSET Name = 1");
        Assert.Equal(1, line);
        Assert.Equal(2, ch);
    }

    [Fact]
    public void ToLsp_AstralCharacterBeforeColumn_ShiftsToUtf16CodeUnit()
    {
        // The emoji is one Unicode char but two UTF-16 code units. The engine column counts it as one;
        // LSP expects the second column (after it) to be code-unit 2.
        var text = "\U0001F600X"; // U+1F600 then 'X'
        var loc = new SourceLocation("<t>", 1, 2); // column 2 = the 'X', 1-based char
        var (line, ch) = ViscLanguageService.ToLsp(loc, text);
        Assert.Equal(0, line);
        Assert.Equal(2, ch); // surrogate pair pushed the X to code unit 2
    }

    [Fact]
    public void ToLsp_UnknownLocation_CollapsesToOrigin()
    {
        var (line, ch) = ViscLanguageService.ToLsp(SourceLocation.Unknown, "anything");
        Assert.Equal(0, line);
        Assert.Equal(0, ch);
    }

    [Fact]
    public void RangeAt_PointInsideWord_SpansTheWholeWord()
    {
        var text = "OPEN-ROW 0";
        var loc = new SourceLocation("<t>", 1, 3); // inside "OPEN-ROW"
        var range = ViscLanguageService.RangeAt(loc, text);
        Assert.Equal(0, range.Start.Character);
        Assert.Equal(8, range.End.Character); // "OPEN-ROW" length, hyphen included
    }

    [Fact]
    public void RangeAt_PointNotOnIdentifier_IsZeroWidth()
    {
        var text = "SET Name = 1";
        var loc = new SourceLocation("<t>", 1, 4); // the space before "Name"
        var range = ViscLanguageService.RangeAt(loc, text);
        Assert.Equal(3, range.Start.Character);
        Assert.Equal(range.Start.Character, range.End.Character); // zero width
    }

    [Fact]
    public void RangeAt_PastEndOfLine_FallsBackToZeroWidthAtPoint()
    {
        var text = "SET";
        var loc = new SourceLocation("<t>", 1, 10); // far past EOL
        var range = ViscLanguageService.RangeAt(loc, text);
        Assert.Equal(range.Start.Character, range.End.Character); // EOL fallback, zero width
    }

    [Fact]
    public void RangeAt_UnknownLocation_IsZeroWidthAtOrigin()
    {
        var range = ViscLanguageService.RangeAt(SourceLocation.Unknown, "SET Name = 1");
        Assert.Equal(0, range.Start.Line);
        Assert.Equal(0, range.Start.Character);
        Assert.Equal(0, range.End.Line);
        Assert.Equal(0, range.End.Character);
    }
}
