using System;
using Vidyano.Script.LanguageServer;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Guards the frozen semantic-tokens legend. Clients cache the legend once at registration, so its
/// type ordering is part of the protocol contract — these tests pin the indices and prove
/// <see cref="SemanticTokensLegendSpec.IndexOf"/> fails loudly on an unknown type rather than miscoloring.
/// </summary>
public sealed class SemanticTokensLegendTests
{
    [Fact]
    public void Legend_TokenTypeOrder_IsFrozen()
    {
        // If this order changes, every cached client legend silently miscolors — so the order is a contract.
        Assert.Equal(
            ["keyword", "type", "string", "number", "comment", "operator", "variable", "macro", "regexp"],
            ViscLanguageService.Legend.TokenTypes);
    }

    [Fact]
    public void Legend_HasNoModifiers_InV1()
    {
        Assert.Empty(ViscLanguageService.Legend.TokenModifiers);
    }

    [Fact]
    public void IndexOf_ReturnsFrozenPositions()
    {
        var legend = ViscLanguageService.Legend;
        Assert.Equal(0, legend.IndexOf("keyword"));
        Assert.Equal(1, legend.IndexOf("type"));
        Assert.Equal(2, legend.IndexOf("string"));
        Assert.Equal(3, legend.IndexOf("number"));
        Assert.Equal(4, legend.IndexOf("comment"));
        Assert.Equal(6, legend.IndexOf("variable"));
        Assert.Equal(7, legend.IndexOf("macro"));
    }

    [Fact]
    public void IndexOf_UnknownType_Throws()
    {
        Assert.Throws<ArgumentException>(() => ViscLanguageService.Legend.IndexOf("not-a-real-type"));
    }

    [Fact]
    public void Legend_IsTheSameInstanceAcrossReads()
    {
        // Static + computed once — both ends of the JSON-RPC contract must register from one instance.
        Assert.Same(ViscLanguageService.Legend, ViscLanguageService.Legend);
    }

    [Fact]
    public void Legend_EmittedIndices_AreAllWithinTheLegend()
    {
        // Every index a producer could emit must resolve to a real legend slot.
        var legend = ViscLanguageService.Legend;
        foreach (var type in legend.TokenTypes)
            Assert.InRange(legend.IndexOf(type), 0, legend.TokenTypes.Count - 1);
    }
}
