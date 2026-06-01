using System.Collections.Concurrent;
using Vidyano.Script.LanguageServer;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace Vidyano.Script.Tests;

/// <summary>
/// In-process <see cref="IDiagnosticSink"/> that records every publish so the document→publish
/// round-trip can be asserted with no real LSP pipe. Keeps the latest publish per uri.
/// </summary>
internal sealed class RecordingDiagnosticSink : IDiagnosticSink
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _last = new();

    public int PublishCount { get; private set; }

    public Task PublishAsync(string uri, IReadOnlyList<LspDiagnostic> diagnostics, CancellationToken ct)
    {
        _last[uri] = diagnostics;
        PublishCount++;
        return Task.CompletedTask;
    }

    /// <summary>The most recently published diagnostic set for <paramref name="uri"/>.</summary>
    public IReadOnlyList<LspDiagnostic> Last(string uri) => _last[uri];
}
