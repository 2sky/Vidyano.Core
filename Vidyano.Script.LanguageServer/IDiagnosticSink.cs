using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace Vidyano.Script.LanguageServer;

/// <summary>
/// Where analysis results go after a document is opened or changed. This is the one seam that lets the
/// full document→publish round-trip be asserted with no real pipe: production publishes over JSON-RPC,
/// tests record-and-assert.
/// </summary>
/// <remarks>
/// An empty <paramref name="diagnostics"/> list is meaningful — it clears the previously published set
/// for that document. The service always publishes (even on a clean document) so editors see stale
/// problems disappear.
/// </remarks>
public interface IDiagnosticSink
{
    Task PublishAsync(string uri, IReadOnlyList<LspDiagnostic> diagnostics, CancellationToken ct);
}
