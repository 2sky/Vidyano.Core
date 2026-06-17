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
    /// Default. Both intrinsic validity (read-only/edit-mode/action availability/required) AND
    /// UI-reachability are enforced. Reachability covers two things a plain frontend user can't do: open
    /// a PO/Query you have no menu path to, and read or write a <em>hidden</em> attribute
    /// (<see cref="Vidyano.ViewModel.AttributeVisibility.Never"/> — the default editor never surfaces it).
    /// </summary>
    Navigation,

    /// <summary>
    /// Intrinsic validity enforced; reachability reported as a warning but not enforced.
    /// Use to test security: a restricted user trying a direct PO open should produce both a server
    /// 'access denied' AND a 'not-reachable' warning. A regression that grants direct access loses
    /// the server error but keeps the warning. Setting a hidden attribute is likewise allowed here but
    /// carries a <c>guard-attribute-hidden</c> warning (a hidden read is allowed silently — it's
    /// observational).
    /// </summary>
    Audit,

    /// <summary>
    /// Intrinsic validity enforced; reachability silently skipped. Escape hatch for setup scripts that
    /// need to plant fixtures by ID or drive a hidden attribute the way a custom web component would
    /// (Core itself never blocks a hidden-but-editable write — only read-only). The script must declare
    /// this explicitly so reviewers see what's being bypassed.
    /// </summary>
    Direct,
}
