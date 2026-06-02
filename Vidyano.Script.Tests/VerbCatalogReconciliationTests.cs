using System;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// The keystone guard: proves the three historical verb-knowledge sources actually collapsed into
/// <see cref="VerbCatalog"/>. If a verb is added to the parser but not the catalog (or vice versa),
/// one of these assertions fails.
/// </summary>
public sealed class VerbCatalogReconciliationTests
{
    // The set the parser must still recognize. VerbCatalog.Names is internal to Vidyano.Script, so this
    // list stands in as the contract under test — every entry must resolve in the catalog.
    private static readonly string[] HistoricalKnownVerbs =
    [
        "SIGN-IN", "SIGN-OUT", "USE", "OPEN", "OPEN-ROW", "GO-BACK", "FOLLOW", "OPEN-DETAIL",
        "EDIT", "CANCEL", "SAVE", "REFRESH",
        "SET", "ACTION", "SEARCH", "SELECT-ROWS",
        "EXPECT",
        "TOOL",
        "REQUIRES", "CLEANUP",
    ];

    [Fact]
    public void EveryHistoricalKnownVerb_ResolvesInCatalog()
    {
        foreach (var verb in HistoricalKnownVerbs)
            Assert.True(VerbCatalog.TryGet(verb, out _), $"VerbCatalog is missing '{verb}'.");
    }

    [Fact]
    public void EveryCatalogVerb_IsRecognizedByTheParser()
    {
        // A verb in the catalog must pass the parser's known-verb gate. Some recognized verbs are
        // "not yet implemented" — they still produce a parse-unknown-verb diagnostic but with a
        // distinct message — so the reconciliation signal is the *truly-unknown* message, which only
        // fires for verbs the gate rejected.
        foreach (var v in VerbCatalog.All)
        {
            var diags = VidyanoScript.Lint(v.Name);
            Assert.DoesNotContain(diags, d => d.Message.StartsWith($"Unknown verb '{v.Name}'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryCatalogEntry_ResolvesToItselfViaTryGet()
    {
        // Spec #36: `vidyano help verbs` renders one row per VerbCatalog.All entry, so every rendered
        // row must resolve through the same TryGet that hover and the parser's known-verb gate use —
        // otherwise help could list a verb the lookup path can't find. Guards All-vs-TryGet divergence.
        foreach (var v in VerbCatalog.All)
        {
            Assert.True(VerbCatalog.TryGet(v.Name, out var found), $"VerbCatalog.All lists '{v.Name}' but TryGet can't resolve it.");
            Assert.Same(v, found);
        }
    }

    [Fact]
    public void TrulyUnknownVerb_StillRejected()
    {
        var diags = VidyanoScript.Lint("EXPEKT TotalItems = 3");
        Assert.Contains(diags, d => d.Message.StartsWith("Unknown verb 'EXPEKT'", StringComparison.Ordinal));
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        Assert.True(VerbCatalog.TryGet("sign-in", out var info));
        Assert.Equal("SIGN-IN", info.Name);
    }

    [Fact]
    public void TryGet_UnknownLexeme_ReturnsFalse()
    {
        Assert.False(VerbCatalog.TryGet("EXPEKT", out _));
    }

    [Fact]
    public void CatalogHasNoDuplicateNames()
    {
        var names = VerbCatalog.All.Select(v => v.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void EveryVerb_HasAtLeastOneExample()
    {
        Assert.All(VerbCatalog.All, v => Assert.NotEmpty(v.Examples));
    }
}
