using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using OmniLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace Vidyano.Script.LanguageServer;

/// <summary>
/// The opaque composition root: the only type that touches OmniSharp, JSON-RPC, stdin, and stdout.
/// It wires a <see cref="ViscLanguageService"/> to the LSP framework's text-document and hover handlers
/// and publishes diagnostics back over the wire. There is no logic worth a unit test here — everything
/// testable lives in <see cref="ViscLanguageService"/> and <see cref="DiagnosticMapper"/>.
/// </summary>
public static class ViscLspServer
{
    private static readonly TextDocumentSelector ViscSelector =
        TextDocumentSelector.ForLanguage("visc");

    public static async Task<int> RunStdioAsync(CancellationToken ct = default)
    {
        var sink = new PublishingSink();
        var service = new ViscLanguageService(sink);

        // Advertise the tool version over LSP initialize (serverInfo.version) so the editor client can run
        // its version-skew check. Strip any "+buildmetadata" so the value is a plain major.minor.patch.
        var version = (typeof(ViscLspServer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ViscLspServer).Assembly.GetName().Version?.ToString()
            ?? "0.0.0").Split('+')[0];

        var server = await OmniLanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .WithServerInfo(new ServerInfo { Name = "vidyano-lsp", Version = version })
            .WithServices(services => services.AddSingleton(service))
            .OnDidOpenTextDocument(async (p, c) =>
                await service.DidOpenAsync(
                    p.TextDocument.Uri.ToString(), p.TextDocument.Text, c).ConfigureAwait(false),
                (_, _) => new TextDocumentSyncRegistrationOptions(TextDocumentSyncKind.Full)
                {
                    DocumentSelector = ViscSelector,
                })
            .OnDidChangeTextDocument(async (p, c) =>
                await service.DidChangeAsync(
                    p.TextDocument.Uri.ToString(), p.ContentChanges.LastOrDefault()?.Text ?? "", c).ConfigureAwait(false),
                (_, _) => new TextDocumentChangeRegistrationOptions
                {
                    DocumentSelector = ViscSelector,
                    SyncKind = TextDocumentSyncKind.Full,
                })
            .OnDidCloseTextDocument(async (p, c) =>
                await service.DidCloseAsync(p.TextDocument.Uri.ToString(), c).ConfigureAwait(false),
                (_, _) => new TextDocumentCloseRegistrationOptions
                {
                    DocumentSelector = ViscSelector,
                })
            .OnHover((p, c) => Task.FromResult(
                    service.Hover(p.TextDocument.Uri.ToString(), p.Position)),
                (_, _) => new HoverRegistrationOptions { DocumentSelector = ViscSelector }))
            .ConfigureAwait(false);

        sink.Attach(server);
        await server.WaitForExit.ConfigureAwait(false);
        return 0;
    }

    // Publishes through the running server. The server is set once after From(...) returns; before that
    // (during the handshake) no document handler can fire, so the field is always populated when used.
    private sealed class PublishingSink : IDiagnosticSink
    {
        private OmniLanguageServer? _server;

        public void Attach(OmniLanguageServer server) => _server = server;

        public Task PublishAsync(string uri, IReadOnlyList<LspDiagnostic> diagnostics, CancellationToken ct)
        {
            _server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.Parse(uri),
                Diagnostics = new Container<LspDiagnostic>(diagnostics),
            });
            return Task.CompletedTask;
        }
    }
}
