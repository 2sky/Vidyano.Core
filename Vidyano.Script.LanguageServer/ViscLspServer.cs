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

        // stdout is the JSON-RPC channel: bind it to the LSP transport up front, then point Console.Out at
        // stderr so any stray Console.Write (ours or a dependency's) lands on stderr instead of corrupting
        // the protocol stream.
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);

        var server = await OmniLanguageServer.From(options => options
            .WithInput(stdin)
            .WithOutput(stdout)
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

    // Publishes through the running server. The server instance is only available after From(...) returns,
    // but OmniSharp's message loop can dispatch a didOpen the moment the handshake completes — possibly
    // before this method's Attach continuation runs. Awaiting a TaskCompletionSource closes that race: an
    // early publish parks until the server is attached instead of being silently dropped.
    private sealed class PublishingSink : IDiagnosticSink
    {
        private readonly TaskCompletionSource<OmniLanguageServer> _serverTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Attach(OmniLanguageServer server) => _serverTcs.TrySetResult(server);

        public async Task PublishAsync(string uri, IReadOnlyList<LspDiagnostic> diagnostics, CancellationToken ct)
        {
            var server = await _serverTcs.Task.ConfigureAwait(false);
            server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.Parse(uri),
                Diagnostics = new Container<LspDiagnostic>(diagnostics),
            });
        }
    }
}
