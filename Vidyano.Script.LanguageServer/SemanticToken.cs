namespace Vidyano.Script.LanguageServer;

/// <summary>
/// One classified span, already legend-relative and in 0-based UTF-16 LSP coordinates.
/// <see cref="TokenTypeIndex"/> indexes <see cref="SemanticTokensLegendSpec.TokenTypes"/> on the shared
/// <see cref="ViscLanguageService.Legend"/>; <see cref="ModifierBits"/> is the bit set over that legend's
/// modifiers (always 0 in v1). Spans are emitted non-overlapping in ascending <c>(Line, StartChar)</c>
/// order — exactly what an LSP <c>SemanticTokensBuilder.Push</c> consumes, so the server-side encoder is
/// a one-line foreach with no sorting of its own.
/// </summary>
public readonly record struct SemanticToken(
    int Line,
    int StartChar,
    int Length,
    int TokenTypeIndex,
    int ModifierBits);
