using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vidyano.ViewModel;

// Resolve the ClientOperation name collision: Vidyano.ViewModel.ClientOperation (Vidyano.Core,
// strongly-typed wire shape) vs Vidyano.Script.Runtime.ClientOperation (script-facing wrapper
// with Type elevation). The hook plumbs the Core type through to VidyanoSession, which translates.
using CoreClientOperation = Vidyano.ViewModel.ClientOperation;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Hooks subclass installed by <see cref="VidyanoSession"/>. Pins the session to the <c>Web</c>
/// environment + <c>environmentVersion=3</c> so the server treats us like a v4 browser client:
/// default filters are applied during GetQuery (rows arrive pre-filtered and column
/// Includes/Excludes are populated on the result), and Web-only hook paths such as
/// <c>ObjectEx.UpdateSelectInPlaceAsync</c> run. The base <see cref="Hooks"/> defaults to
/// <c>Windows</c> (Native), which skips that machinery — a scripted session is closer to a
/// browser session than a desktop app, so Web is the right baseline.
///
/// Also threads <see cref="RequestedLanguage"/> (set from <c>SIGN-IN … LANGUAGE xx-XX</c>) into
/// every post so labels, messages, and notifications come back localized for the whole session,
/// not just the sign-in.
/// </summary>
internal sealed class ScriptHooks : Hooks
{
    public ScriptHooks()
        : base("Web")
    {
    }

    public string? RequestedLanguage { get; set; }

    /// <summary>Invoked for each ClientOperation the server queues in a response. Set by
    /// <see cref="VidyanoSession"/> so it can append to its <c>_allOperations</c> /
    /// <c>_lastOperations</c> buffers without exposing them here.</summary>
    public Action<CoreClientOperation>? ClientOperationObserver { get; set; }

    /// <summary>Invoked when the server raises a RetryAction mid-<c>ExecuteAction</c> (Core's
    /// <see cref="Hooks.OnRetryAction"/>). Set by <see cref="VidyanoSession"/> to park the action and
    /// surface the retry as a dialog frame; the returned task completes with the chosen option (or
    /// <c>"-1"</c> to cancel) once the script answers with <c>CONFIRM</c>. When unset, the base behaviour
    /// (auto-cancel with <c>"-1"</c>) applies — that's also the fallback for a retry raised while no park
    /// is armed, which keeps a stray server call from hanging the session.</summary>
    public Func<string, string?, string[], PersistentObject?, Task<string>>? RetryActionHandler { get; set; }

    protected override Task<string> OnRetryAction(string title, string message, string[] options, PersistentObject persistentObject)
        => RetryActionHandler?.Invoke(title, message, options, persistentObject) ?? Task.FromResult("-1");

    // Cross-assembly override of `protected internal` collapses to `protected` (the `internal`
    // portion isn't visible outside Vidyano.Core).
    protected override void OnClientOperation(CoreClientOperation operation)
    {
        ClientOperationObserver?.Invoke(operation);
    }

    protected override void OnCreateData(JObject data)
    {
        // Matches the v4 frontend (service.ts: `public environmentVersion: string = "3"`). The
        // server gates AsyncFlow.IsWeb2OrGreater on this field being non-null, which is what
        // unlocks IncludeFilters / default-filter column propagation in GetQuery responses.
        data["environmentVersion"] = "3";

        if (!string.IsNullOrEmpty(RequestedLanguage))
            data["requestedLanguage"] = RequestedLanguage;
    }
}
