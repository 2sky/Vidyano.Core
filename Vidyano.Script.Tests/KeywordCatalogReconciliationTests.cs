using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vidyano.Script.Diagnostics;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// The anti-drift keystone: proves the TextMate grammars and <see cref="KeywordCatalog"/> /
/// <see cref="VerbCatalog"/> color the SAME words the SAME way. The lexer flattens verbs, sub-keywords,
/// and type-words all into bare identifiers; two surfaces (the semantic-tokens producer and the editors'
/// grammars) must independently re-classify them. If either drifts, an editor colors a word the other
/// doesn't — these tests fail before that ships. Driven entirely through the public API
/// (<see cref="KeywordCatalog.Classify"/> + the exposed sets), no <c>InternalsVisibleTo</c> — exactly the
/// precedent <see cref="VerbCatalogReconciliationTests"/> set.
/// </summary>
public sealed class KeywordCatalogReconciliationTests
{
    // The two shipped grammars; the brief mandates they stay byte-identical.
    private static readonly string[] GrammarRelativePaths =
    [
        Path.Combine("editors", "vscode", "syntaxes", "visc.tmLanguage.json"),
        Path.Combine("editors", "visualstudio", "visc.tmLanguage.json"),
    ];

    [Fact]
    public void BothGrammars_AreByteIdentical()
    {
        var vscode = File.ReadAllBytes(GrammarPath(GrammarRelativePaths[0]));
        var vs = File.ReadAllBytes(GrammarPath(GrammarRelativePaths[1]));
        Assert.Equal(vscode, vs);
    }

    [Fact]
    public void GrammarVerbAlternation_EqualsVerbCatalog()
    {
        var catalogVerbs = VerbCatalog.All.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var grammar in GrammarRelativePaths)
        {
            var grammarVerbs = Alternation(grammar, "verb").ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.True(catalogVerbs.SetEquals(grammarVerbs),
                $"verb alternation in {grammar} diverges from VerbCatalog. " +
                $"Grammar-only: [{string.Join(", ", grammarVerbs.Except(catalogVerbs))}]; " +
                $"Catalog-only: [{string.Join(", ", catalogVerbs.Except(grammarVerbs))}]");
        }
    }

    [Fact]
    public void GrammarSubKeywordAlternation_EqualsKeywordCatalogSubKeywords()
    {
        var catalog = KeywordCatalog.SubKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var grammar in GrammarRelativePaths)
        {
            var grammarWords = Alternation(grammar, "sub-keyword").ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(catalog.SetEquals(grammarWords),
                $"sub-keyword alternation in {grammar} diverges from KeywordCatalog. " +
                $"Grammar-only: [{string.Join(", ", grammarWords.Except(catalog))}]; " +
                $"Catalog-only: [{string.Join(", ", catalog.Except(grammarWords))}]");
        }
    }

    [Fact]
    public void GrammarTypeWordAlternation_EqualsKeywordCatalogTypeWords()
    {
        var catalog = KeywordCatalog.TypeWords.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var grammar in GrammarRelativePaths)
        {
            var grammarWords = Alternation(grammar, "type-word").ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(catalog.SetEquals(grammarWords),
                $"type-word alternation in {grammar} diverges from KeywordCatalog. " +
                $"Grammar-only: [{string.Join(", ", grammarWords.Except(catalog))}]; " +
                $"Catalog-only: [{string.Join(", ", catalog.Except(grammarWords))}]");
        }
    }

    [Fact]
    public void EveryGrammarSubKeyword_ClassifiesAsSubKeyword()
    {
        foreach (var word in Alternation(GrammarRelativePaths[0], "sub-keyword"))
            Assert.Equal(SemanticCategory.SubKeyword, KeywordCatalog.Classify(word));
    }

    [Fact]
    public void EveryGrammarTypeWord_ClassifiesAsTypeWord()
    {
        foreach (var word in Alternation(GrammarRelativePaths[0], "type-word"))
            Assert.Equal(SemanticCategory.TypeWord, KeywordCatalog.Classify(word));
    }

    [Fact]
    public void EveryGrammarVerb_ClassifiesAsVerb()
    {
        foreach (var word in Alternation(GrammarRelativePaths[0], "verb"))
            Assert.Equal(SemanticCategory.Verb, KeywordCatalog.Classify(word));
    }

    [Fact]
    public void EveryCatalogSubKeyword_ClassifiesAsSubKeyword()
    {
        foreach (var word in KeywordCatalog.SubKeywords)
            Assert.Equal(SemanticCategory.SubKeyword, KeywordCatalog.Classify(word));
    }

    [Fact]
    public void EveryCatalogTypeWord_ClassifiesAsTypeWord()
    {
        foreach (var word in KeywordCatalog.TypeWords)
            Assert.Equal(SemanticCategory.TypeWord, KeywordCatalog.Classify(word));
    }

    [Fact]
    public void Classify_IsCaseInsensitive()
    {
        Assert.Equal(SemanticCategory.TypeWord, KeywordCatalog.Classify("query"));
        Assert.Equal(SemanticCategory.SubKeyword, KeywordCatalog.Classify("where"));
    }

    [Fact]
    public void Classify_FreeIdentifier_IsNone()
    {
        Assert.Equal(SemanticCategory.None, KeywordCatalog.Classify("Customers"));
    }

    [Fact]
    public void Classify_Null_IsNone()
    {
        Assert.Equal(SemanticCategory.None, KeywordCatalog.Classify(null!));
    }

    [Fact]
    public void VerbWins_OverSubKeywordAndTypeWordSets()
    {
        // No word is in two groups, but the contract is verb-first; confirm a real verb resolves to Verb.
        Assert.Equal(SemanticCategory.Verb, KeywordCatalog.Classify("CONFIRM"));
    }

    // Reads the named repository entry's "match" alternation `(?:A|B|C)` from the grammar JSON and returns
    // the literal words. Parses the JSON properly (so the regex only operates on the match string), then
    // pulls the alternatives out of the non-capturing group.
    private static IEnumerable<string> Alternation(string grammarRelativePath, string repositoryKey)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath(grammarRelativePath)));
        var match = doc.RootElement
            .GetProperty("repository")
            .GetProperty(repositoryKey)
            .GetProperty("match")
            .GetString()!;

        // The alternation lives inside the single non-capturing group `(?:...)`.
        var group = Regex.Match(match, @"\(\?:(?<body>[^)]*)\)");
        Assert.True(group.Success, $"No (?:...) alternation found in '{repositoryKey}' match: {match}");
        return group.Groups["body"].Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GrammarPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vidyano.Core.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(path), $"Grammar not found at {path}");
        return path;
    }
}
