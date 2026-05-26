using System.Collections.Generic;
using System.Net.Http;
using Vidyano.Script.Runtime;

namespace Vidyano.Script;

/// <summary>
/// Inputs for <see cref="VidyanoScript.RunAsync(string, VidyanoScriptOptions)"/>. None of these are
/// required to have non-default values — the script itself can set <c>@app</c>, <c>@mode</c>, and
/// credentials inline. The options object exists so library callers (LINQPad, xUnit fixtures, the CLI)
/// can override or pre-seed any of those without rewriting the script.
/// </summary>
public sealed class VidyanoScriptOptions
{
    /// <summary>
    /// Base URI of the remote Vidyano service. When set, takes precedence over the <c>@app</c> variable
    /// declared in the script. When unset, the script must declare <c>@app</c>.
    /// </summary>
    public string? RemoteUri { get; set; }

    /// <summary>
    /// Reused HttpClient. Pass <c>TestServer.CreateClient()</c> for in-process execution.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Initial guard mode. May be overridden by a <c>@mode = ...</c> directive in the script.</summary>
    public GuardMode Mode { get; set; } = GuardMode.Navigation;

    /// <summary>
    /// Pre-seeded variable bindings injected into the script's variable table. CLI <c>--var key=value</c>
    /// flags land here; xUnit fixtures supply credentials.
    /// </summary>
    public Dictionary<string, object?> Variables { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Named tool handlers callable from a script via <c>TOOL &lt;name&gt; [k=v, …] [-&gt; @var]</c>.
    /// Use these to plug external logic into a .visc script — a startup/teardown snippet, a DB
    /// lookup, an environment probe — without embedding C# in the script. The handler receives a
    /// per-call context exposing the live session, the script's variable table, and a cancellation
    /// token; throwing fails the script with a <c>tool-error</c> diagnostic. Names are
    /// case-insensitive.
    /// </summary>
    public Dictionary<string, ScriptToolHandler> Tools { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Display path used in <see cref="Diagnostics.SourceLocation"/> when the script body is supplied
    /// inline rather than read from disk. Default <c>&lt;inline&gt;</c>.
    /// </summary>
    public string SourcePath { get; set; } = "<inline>";

    /// <summary>
    /// Bypass TLS certificate validation. Use only for local development against self-signed dev certs.
    /// Ignored when <see cref="HttpClient"/> is supplied.
    /// </summary>
    public bool AcceptAnyServerCertificate { get; set; }

    /// <summary>
    /// Anchors the run clock for the built-in <c>{{@today}}</c> / <c>{{@now}}</c> variables. Each
    /// <c>{{@now}}</c> reference reads the anchor plus real elapsed time since the run started, so a
    /// pinned value fixes the origin while the clock still flows (it is not bit-reproducible — capture
    /// into a variable for an exact value). When unset, the anchor is the live system clock. Anchoring
    /// is an invocation concern — the script stays portable.
    /// </summary>
    public System.DateTimeOffset? Now { get; set; }

    /// <summary>
    /// Seeds the generators behind the built-in <c>{{@uuid}}</c> / <c>{{@random}}</c> variables.
    /// Each reference draws the next value from a seeded stream (the two streams are independent), so
    /// the same seed replays the same sequence across runs while distinct references stay distinct.
    /// Capture into a variable to freeze a value for reuse. When unset, the streams are unseeded.
    /// </summary>
    public int? Seed { get; set; }
}
