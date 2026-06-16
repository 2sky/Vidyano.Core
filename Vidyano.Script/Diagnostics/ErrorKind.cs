namespace Vidyano.Script.Diagnostics;

/// <summary>
/// Stable identifiers for every kind of failure a .visc script can produce.
/// These are what agents branch on; they show up in JSON output verbatim.
/// </summary>
/// <remarks>
/// The category prefix encodes the layer that produced it:
/// <list type="bullet">
///   <item><c>parse-*</c> — the source wasn't well-formed.</item>
///   <item><c>resolve-*</c> — a name was used that doesn't exist in the current context.</item>
///   <item><c>guard-*</c> — the operation is well-formed but a real UI client wouldn't allow it.</item>
///   <item><c>state-*</c> — the session isn't in the right state for the operation.</item>
///   <item><c>assert-*</c> — an <c>EXPECT</c> didn't hold.</item>
///   <item><c>transport-*</c> — protocol/network/server failure.</item>
/// </list>
/// </remarks>
public static class ErrorKind
{
    // Parsing
    public const string ParseUnknownVerb         = "parse-unknown-verb";
    public const string ParseUnexpectedToken     = "parse-unexpected-token";
    public const string ParseUnterminatedString  = "parse-unterminated-string";
    public const string ParseExpected            = "parse-expected";
    public const string ParseInvalidValue        = "parse-invalid-value";
    public const string ParseInvalidMode         = "parse-invalid-mode";
    /// <summary>A <c>REPEAT</c> / <c>FOR-EACH</c> block reached end-of-file with no matching <c>END</c>.</summary>
    public const string ParseMissingBlockEnd     = "parse-missing-block-end";

    // Resolution — name not found
    public const string ResolveVariable          = "resolve-variable";
    public const string ResolveSession           = "resolve-session";
    public const string ResolveHandle            = "resolve-handle";
    public const string ResolveAttribute         = "resolve-attribute";
    public const string ResolveAction            = "resolve-action";
    public const string ResolveQuery             = "resolve-query";
    public const string ResolveMenuItem          = "resolve-menu-item";
    public const string ResolveEnv               = "resolve-env";
    /// <summary>A <c>SET attr = FILE "&lt;path&gt;"</c> path didn't resolve to a readable file.</summary>
    public const string ResolveFile              = "resolve-file";

    // Tier-1 guard — operation invalid for the current PO/Query
    public const string GuardAttributeHidden     = "guard-attribute-hidden";
    public const string GuardAttributeReadOnly   = "guard-attribute-read-only";
    public const string GuardActionNotAvailable  = "guard-action-not-available";
    public const string GuardActionHidden        = "guard-action-hidden";
    // Retained for API compatibility; no longer emitted. The pre-save required pre-check was removed
    // in favor of server-side validation (the server defaults some required attributes during persist).
    public const string GuardRequiredMissing     = "guard-required-missing";
    public const string GuardEditModeRequired    = "guard-edit-mode-required";
    public const string GuardNotInEdit           = "guard-not-in-edit";
    public const string GuardInEdit              = "guard-in-edit";

    // Tier-2 guard — reachability (used in `navigation` mode)
    public const string GuardNotReachable        = "guard-not-reachable";

    // Session state
    public const string StateNotConnected        = "state-not-connected";
    public const string StateNotSignedIn         = "state-not-signed-in";
    public const string StateNoCurrentPo         = "state-no-current-po";
    public const string StateNoCurrentQuery      = "state-no-current-query";
    public const string StateNoSession           = "state-no-session";
    public const string StateScopeNotImplemented = "state-scope-not-implemented";
    public const string StateInitialPending      = "state-initial-pending";
    public const string StateHandleStale         = "state-handle-stale";
    public const string StateNavStackAtRoot      = "state-nav-stack-at-root";
    /// <summary>Informational: a <c>REQUIRES</c> precondition was not met (or could not be
    /// evaluated), so the rest of the body is skipped. Not a failure.</summary>
    public const string StateRequiresUnmet       = "state-requires-unmet";
    /// <summary>A non-retry verb was attempted while a server retry dialog is open. Only
    /// <c>CONFIRM</c> / <c>SET</c> / <c>EXPECT</c> are allowed until the retry is answered.</summary>
    public const string StateRetryPending        = "state-retry-pending";
    /// <summary><c>CONFIRM</c> was issued with no retry dialog open to answer.</summary>
    public const string StateNoRetryPending      = "state-no-retry-pending";
    /// <summary>A <c>REPEAT</c> count resolved to a negative or non-integer value — the bound is
    /// unevaluable, so the loop can't run.</summary>
    public const string StateInvalidBound        = "state-invalid-bound";
    /// <summary>A loop iteration ended with a PersistentObject still in edit, so the nav stack can't be
    /// safely restored to the loop's entry depth (SAVE or CANCEL inside the body first).</summary>
    public const string StateLoopEditLeftOpen    = "state-loop-edit-left-open";
    /// <summary>A <c>FOR-EACH ROW</c> over a paged query visited only the loaded rows because the server
    /// holds more than were resident — a warning, not a failure (the loop still ran, no silent truncation).</summary>
    public const string StateForeachTruncated    = "state-foreach-truncated";

    // Assertions
    public const string AssertFailed             = "assert-failed";
    public const string AssertNotificationError  = "assert-notification-error";
    public const string AssertValidationError    = "assert-validation-error";
    /// <summary>A verb carrying the <c>EXPECTING ERROR</c> suffix completed successfully when a
    /// server error notification was the asserted outcome — the negative path didn't fire.</summary>
    public const string AssertExpectedError      = "assert-expected-error";

    // Transport / server
    public const string TransportError           = "transport-error";
    public const string ServerError              = "server-error";

    // Host-registered TOOL calls (Vidyano.Script.Runtime.ScriptToolHandler)
    public const string ToolUnknown              = "tool-unknown";
    public const string ToolError                = "tool-error";
    public const string ToolNoValue              = "tool-no-value";
}
