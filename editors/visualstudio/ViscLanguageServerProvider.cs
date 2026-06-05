// Vidyano Script (.visc) language client for Visual Studio.
namespace Vidyano.Visc.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
using Nerdbank.Streams;

#pragma warning disable VSEXTPREVIEW_LSP // LanguageServerProvider is a preview API in the VisualStudio.Extensibility SDK.

/// <summary>
/// Drives the .visc language experience inside Visual Studio by launching the <c>vidyano lsp</c> stdio
/// language server — the very server the VS Code extension uses. This client owns no language logic:
/// it locates the <c>vidyano</c> tool, spawns it, and bridges its stdio to Visual Studio's LSP plumbing.
/// Everything the user sees (diagnostics, hover) comes from the server, so the experience tracks the
/// CLI tool's version, not this extension's.
/// </summary>
[VisualStudioContribution]
internal class ViscLanguageServerProvider : LanguageServerProvider
{
    private readonly TraceSource traceSource;

    public ViscLanguageServerProvider(ExtensionCore container, VisualStudioExtensibility extensibilityObject, TraceSource traceSource)
        : base(container, extensibilityObject)
    {
        this.traceSource = traceSource;
    }

    /// <summary>The .visc document type — mirrors the VS Code grammar's <c>.visc</c> file association.</summary>
    [VisualStudioContribution]
    public static DocumentTypeConfiguration ViscDocumentType => new("visc")
    {
        FileExtensions = [".visc"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    /// <inheritdoc/>
    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "%Vidyano.Visc.LanguageServerProvider.DisplayName%",
        [DocumentFilter.FromDocumentType(ViscDocumentType)]);

    /// <inheritdoc/>
    public override Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        string? vidyano = ResolveVidyano();
        if (vidyano is null)
        {
            // Returning null leaves .visc files open without LSP features. We log the remedy rather than
            // throw; a missing tool is a user-environment issue, not an extension fault.
            this.traceSource.TraceEvent(
                TraceEventType.Error, 0,
                "Could not locate the 'vidyano' executable. Install it with 'dotnet tool install -g Vidyano.Script.Tool', " +
                "put it on PATH, or set the VIDYANO_PATH environment variable.");
            return Task.FromResult<IDuplexPipe?>(null);
        }

        ProcessStartInfo info = new()
        {
            FileName = vidyano,
            Arguments = "lsp",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

#pragma warning disable CA2000 // The process is owned by Visual Studio and disposed when it sends the stop command.
        Process process = new() { StartInfo = info };
#pragma warning restore CA2000

        if (process.Start())
        {
            return Task.FromResult<IDuplexPipe?>(new DuplexPipe(
                PipeReader.Create(process.StandardOutput.BaseStream),
                PipeWriter.Create(process.StandardInput.BaseStream)));
        }

        return Task.FromResult<IDuplexPipe?>(null);
    }

    /// <inheritdoc/>
    public override Task OnServerInitializationResultAsync(
        ServerInitializationResult serverInitializationResult,
        LanguageServerInitializationFailureInfo? initializationFailureInfo,
        CancellationToken cancellationToken)
    {
        if (serverInitializationResult == ServerInitializationResult.Failed)
        {
            this.traceSource.TraceEvent(TraceEventType.Error, 0, "The .visc language server failed to initialize.");
            this.Enabled = false;
        }

        // NOTE: The VS Code client also runs a best-effort version-skew check here, comparing initialize's
        // serverInfo.version against a minimum (it warns when the installed tool is too old). The
        // VisualStudio.Extensibility surface does not expose InitializeResult.serverInfo, so that check is
        // intentionally not ported. Revisit if a future SDK release surfaces serverInfo.
        return base.OnServerInitializationResultAsync(serverInitializationResult, initializationFailureInfo, cancellationToken);
    }

    // --- tool resolution (mirrors the VS Code extension's resolveVidyano; Windows semantics) ----------

    // An explicit VIDYANO_PATH override wins, then PATH (honoring PATHEXT), then the canonical
    // `dotnet tool install -g` directory — which the editor's inherited PATH often lags behind.
    private static string? ResolveVidyano()
    {
        string? overridePath = Environment.GetEnvironmentVariable("VIDYANO_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return FirstExisting(ExecutableCandidates(overridePath!));
        }

        string? onPath = SearchPath("vidyano");
        if (onPath is not null)
        {
            return onPath;
        }

        string toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
        return FirstExisting(ExecutableCandidates(Path.Combine(toolsDir, "vidyano")));
    }

    private static string? SearchPath(string baseName)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string? hit = FirstExisting(ExecutableCandidates(Path.Combine(dir, baseName)));
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    // On Windows a bare name needs an executable extension; mirror cmd.exe by trying each PATHEXT entry
    // unless the name already carries one.
    private static string[] ExecutableCandidates(string baseName)
    {
        if (Path.HasExtension(baseName))
        {
            return [baseName];
        }

        string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        string[] exts = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string[] candidates = new string[exts.Length];
        for (int i = 0; i < exts.Length; i++)
        {
            candidates[i] = baseName + exts[i].ToLowerInvariant();
        }

        return candidates;
    }

    private static string? FirstExisting(string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
#pragma warning restore VSEXTPREVIEW_LSP
