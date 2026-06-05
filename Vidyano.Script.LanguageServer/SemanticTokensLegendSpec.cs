namespace Vidyano.Script.LanguageServer;

/// <summary>
/// The single type/modifier ordering both ends of the JSON-RPC contract share. Indices are frozen for
/// the life of the protocol session (clients cache the legend from registration), so every
/// <see cref="SemanticToken.TokenTypeIndex"/> is relative to THIS instance. The server registers from the
/// same <see cref="ViscLanguageService.Legend"/> the producer encodes against, so the two ends cannot
/// disagree on what an index means.
/// </summary>
public sealed class SemanticTokensLegendSpec
{
    private readonly Dictionary<string, int> _indexByType;

    public SemanticTokensLegendSpec(IReadOnlyList<string> tokenTypes, IReadOnlyList<string> tokenModifiers)
    {
        TokenTypes = tokenTypes;
        TokenModifiers = tokenModifiers;
        _indexByType = new Dictionary<string, int>(tokenTypes.Count, StringComparer.Ordinal);
        for (var i = 0; i < tokenTypes.Count; i++)
            _indexByType[tokenTypes[i]] = i;
    }

    /// <summary>The standard LSP token-type names, in their frozen legend order.</summary>
    public IReadOnlyList<string> TokenTypes { get; }

    /// <summary>The token-modifier names; empty in v1 (no modifiers are emitted).</summary>
    public IReadOnlyList<string> TokenModifiers { get; }

    /// <summary>Resolves a token-type name to its legend index. Throws <see cref="ArgumentException"/> on an
    /// unknown type so a legend/code mismatch surfaces when the producer is built — at startup, not as a
    /// silent miscoloring at render time.</summary>
    public int IndexOf(string tokenType) =>
        _indexByType.TryGetValue(tokenType, out var i)
            ? i
            : throw new ArgumentException($"Token type '{tokenType}' is not in the semantic-tokens legend.", nameof(tokenType));
}
