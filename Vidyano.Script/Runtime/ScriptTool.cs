using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Handler for a <c>TOOL &lt;name&gt;</c> call. Registered on
/// <see cref="VidyanoScriptOptions.Tools"/> so library callers can plug external logic
/// (startup/teardown snippets, DB lookups, environment probes) into a .visc script without
/// embedding C# in the script.
/// </summary>
/// <param name="context">Per-call context exposing the active session, the script's variable
/// table (read/write), the source location of the <c>TOOL</c> call, and a log sink that routes
/// to the same reporter as regular script output.</param>
/// <param name="args">Named arguments parsed from the <c>TOOL</c> line. Values are already
/// evaluated against the script's expression grammar (strings, numbers, bool/null,
/// <c>{{interpolations}}</c>, <c>@session.X</c> reads).</param>
/// <param name="cancellationToken">Honored when the host cancels the script run.</param>
/// <returns>A <see cref="ScriptToolResult"/>. Use <see cref="ScriptToolResult.Ok"/> when the
/// tool has no return value; use <see cref="ScriptToolResult.Value"/> to expose a value bindable
/// via <c>-&gt; @var</c>.</returns>
public delegate Task<ScriptToolResult> ScriptToolHandler(
    IScriptToolContext context,
    IReadOnlyDictionary<string, object?> args,
    CancellationToken cancellationToken);

/// <summary>
/// Per-call context handed to a <see cref="ScriptToolHandler"/>. Holds the live session, a
/// mutable view of the script's variable table (so tools can read/write <c>{{vars}}</c>), the
/// source location of the <c>TOOL</c> call (for diagnostics), and a logger that surfaces
/// messages through the active <see cref="ScriptResult"/> diagnostics channel.
/// </summary>
public interface IScriptToolContext
{
    /// <summary>The live Vidyano session backing the script. Tools may inspect it or drive it
    /// directly; doing so bypasses the engine's per-verb buffers, so prefer reading state and
    /// letting the script's own verbs make changes.</summary>
    VidyanoSession Session { get; }

    /// <summary>The script's variable table. Reads pick up <c>@var = …</c> assignments and
    /// values injected through <see cref="VidyanoScriptOptions.Variables"/>; writes are visible
    /// to subsequent <c>{{var}}</c> interpolations and EXPECT subjects. Case-insensitive.</summary>
    IDictionary<string, object?> Variables { get; }

    /// <summary>Source location of the <c>TOOL</c> line. Use this when building diagnostics so
    /// the reporter can point at the call site.</summary>
    SourceLocation Location { get; }

    /// <summary>Name of the tool as written in the script (matches the
    /// <see cref="VidyanoScriptOptions.Tools"/> key, case-insensitive).</summary>
    string ToolName { get; }
}

/// <summary>
/// Return value of a <see cref="ScriptToolHandler"/>. A bare <see cref="Ok"/> says the tool
/// completed without producing a value; <see cref="Value"/> exposes a payload that the script
/// can bind via <c>-&gt; @var</c>. Throwing from the handler becomes a script failure
/// (the engine wraps the exception in a <c>tool-error</c> diagnostic).
/// </summary>
public sealed class ScriptToolResult
{
    private ScriptToolResult(object? value, bool hasValue)
    {
        ValueOrNull = value;
        HasValue = hasValue;
    }

    /// <summary>The payload, or <c>null</c> if the tool returned <see cref="Ok"/> or
    /// <see cref="Value"/>(<c>null</c>). Inspect <see cref="HasValue"/> to disambiguate.</summary>
    public object? ValueOrNull { get; }

    /// <summary><c>true</c> when the handler produced a value (even <c>null</c>); <c>false</c>
    /// when it returned <see cref="Ok"/>. Drives whether <c>-&gt; @var</c> binding overwrites
    /// the destination variable.</summary>
    public bool HasValue { get; }

    /// <summary>Success without a return value. Equivalent to a void method — any
    /// <c>-&gt; @var</c> binding on the call site fails with <c>tool-no-value</c>.</summary>
    public static ScriptToolResult Ok { get; } = new(null, hasValue: false);

    /// <summary>Success with a return value (which may be <c>null</c>). The value is stored
    /// directly in the script's variable table when the call has a <c>-&gt; @var</c> binding.</summary>
    public static ScriptToolResult Value(object? value) => new(value, hasValue: true);
}
