namespace Vidyano.Script.Runtime;

/// <summary>
/// How aggressively the runner enforces UI-shaped restrictions.
/// </summary>
/// <remarks>
/// Set once per script (via <c>@mode = ...</c>) or per session (via <see cref="VidyanoScriptOptions.Mode"/>).
/// Mixing modes within one scenario muddies what's being tested — the parser refuses changes after
/// the first statement runs.
/// </remarks>
public enum GuardMode
{
    /// <summary>
    /// Default. Both intrinsic validity (visibility/read-only/edit-mode/action availability/required)
    /// AND navigation reachability (you must have a UI path to the PO/Query) are enforced.
    /// </summary>
    Navigation,

    /// <summary>
    /// Intrinsic validity enforced; reachability reported as a warning but not enforced.
    /// Use to test security: a restricted user trying a direct PO open should produce both a server
    /// 'access denied' AND a 'not-reachable' warning. A regression that grants direct access loses
    /// the server error but keeps the warning.
    /// </summary>
    Audit,

    /// <summary>
    /// Intrinsic validity enforced; reachability silently skipped. Escape hatch for setup scripts
    /// that need to plant fixtures by ID. The script must declare this explicitly so reviewers see
    /// what's being bypassed.
    /// </summary>
    Direct,
}
