using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidyano.Script.Diagnostics;

/// <summary>
/// The semantic category of an identifier lexeme the lexer flattened into
/// <see cref="Parsing.TokenKind.Identifier"/>. Verbs, sub-keywords, and type-words all arrive as plain
/// identifiers, yet the grammar colors each distinctly; <see cref="KeywordCatalog.Classify"/> recovers
/// which is which.
/// </summary>
public enum SemanticCategory
{
    /// <summary>Not a reserved word — an attribute, action, menu segment, or other free identifier.</summary>
    None,

    /// <summary>A .visc verb (the statement head), authoritatively defined by <see cref="VerbCatalog"/>.</summary>
    Verb,

    /// <summary>A clause keyword inside a statement (FROM, WHERE, AS, LOOKUP, …).</summary>
    SubKeyword,

    /// <summary>A subject/type word in EXPECT/OPEN/etc. (Query, PersistentObject, TotalItems, …).</summary>
    TypeWord,
}

/// <summary>
/// The single source of truth for the .visc sub-keywords and type-words — exactly as
/// <see cref="VerbCatalog"/> is for verbs. The lexer flattens all three groups into bare identifiers, but
/// every coloring surface (the semantic-tokens producer and the editors' TextMate grammars) must agree on
/// which group a word belongs to. A drift-guard test asserts these sets equal the alternations in
/// <c>editors/vscode/syntaxes/visc.tmLanguage.json</c>, so the semantic and TextMate paths cannot diverge.
/// </summary>
/// <remarks>
/// Verb classification delegates to <see cref="VerbCatalog"/> (the verb authority); this catalog owns only
/// the sub-keyword and type-word sets. Matching is case-insensitive, mirroring how the lexer and grammar
/// treat keywords.
/// </remarks>
public static class KeywordCatalog
{
    private static readonly IReadOnlySet<string> _subKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "ENV", "LANGUAGE", "AS", "WHERE", "ALL", "EXCEPT", "NONE", "IS", "NOT", "LOOKUP", "ID",
        "MATCHES", "CONTAINS", "EXPECTING", "ERROR", "DETAIL", "VISIBLE", "READONLY", "REQUIRED", "AVAILABLE",
        "END", "ROW",
    };

    private static readonly IReadOnlySet<string> _typeWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PersistentObject", "Query", "MenuItem", "NavStack", "TotalItems", "Selection", "IsInEdit", "IsDirty",
        "ClientOperation", "NotificationType", "Notification", "Attribute", "TypeHint", "Type", "Tag", "Label", "PO",
    };

    /// <summary>Classifies an identifier lexeme. Verb wins over the local sets (no word is both), so a
    /// caller can branch on the category without consulting <see cref="VerbCatalog"/> separately.</summary>
    public static SemanticCategory Classify(string identifierLexeme)
    {
        if (identifierLexeme is null)
            return SemanticCategory.None;
        if (VerbCatalog.TryGet(identifierLexeme, out _))
            return SemanticCategory.Verb;
        if (_subKeywords.Contains(identifierLexeme))
            return SemanticCategory.SubKeyword;
        if (_typeWords.Contains(identifierLexeme))
            return SemanticCategory.TypeWord;
        return SemanticCategory.None;
    }

    /// <summary>The sub-keyword set, for the drift-guard test to compare against the grammar alternation.</summary>
    public static IReadOnlyCollection<string> SubKeywords => (IReadOnlyCollection<string>)_subKeywords;

    /// <summary>The type-word set, for the drift-guard test to compare against the grammar alternation.</summary>
    public static IReadOnlyCollection<string> TypeWords => (IReadOnlyCollection<string>)_typeWords;
}
