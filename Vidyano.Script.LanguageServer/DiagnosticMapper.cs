using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Vidyano.Script.Diagnostics;
using EngineDiagnostic = Vidyano.Script.Diagnostics.Diagnostic;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace Vidyano.Script.LanguageServer;

/// <summary>
/// Pure value transform: an engine <see cref="EngineDiagnostic"/> becomes the LSP wire shape. Severity
/// is derived from the diagnostic's <c>Kind</c> string prefix, and the point location is widened into a
/// <see cref="Range"/> over the offending word.
/// </summary>
internal static class DiagnosticMapper
{
    public static LspDiagnostic ToLsp(EngineDiagnostic d, string text) => new()
    {
        Range = ViscLanguageService.RangeAt(d.Location, text),
        Severity = SeverityFor(d.Kind),
        Code = d.Kind,
        Source = "visc",
        Message = d.Hint is null ? d.Message : $"{d.Message} {d.Hint}",
    };

    // v1 never executes a script, so only lexer/parser kinds reach the editor — every one of those is an
    // error. The `lex-` arm is defensive: the lexer currently emits `parse-`-prefixed kinds, but a future
    // dedicated lexer prefix should still map to Error rather than fall through to Warning.
    private static DiagnosticSeverity SeverityFor(string kind) =>
        kind.StartsWith("parse-", StringComparison.Ordinal) || kind.StartsWith("lex-", StringComparison.Ordinal)
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;
}
