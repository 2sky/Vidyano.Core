using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidyano.Script.Diagnostics;

/// <summary>
/// One verb's documentation: its canonical name, syntax form(s), a one-line summary, optional
/// long-form markdown (for editor hover), illustrative examples, a grouping category, and any aliases.
/// </summary>
public sealed record VerbInfo(
    string Name,
    string Syntax,
    string Summary,
    string? MarkdownDoc,
    IReadOnlyList<string> Examples,
    string Category,
    IReadOnlyList<string> Aliases);

/// <summary>
/// The single source of truth for every .visc verb. The parser's known-verb set, the Suggester's
/// candidate list, <c>vidyano help verbs</c>, and editor hover all read from here — so verb knowledge
/// can never drift between those surfaces again.
/// </summary>
/// <remarks>
/// Historically there were two diverging lists: <c>Parser.KnownVerbs</c> (which carried
/// <c>FOLLOW</c>/<c>OPEN-DETAIL</c>/<c>RELOAD</c>/<c>GOTO</c>/<c>REFRESH</c>) and the Tool's
/// syntax-form-keyed help table (which did not). This catalog reconciles them: it is keyed by bare
/// verb name and MUST contain every verb the parser recognizes. The catalog-reconciliation guard test
/// asserts that <c>Parser.KnownVerbs ⊆ VerbCatalog.Names</c>.
/// </remarks>
public static class VerbCatalog
{
    private static readonly IReadOnlyList<VerbInfo> _all =
    [
        new("SIGN-IN",
            "SIGN-IN <user> / <pwd> [LANGUAGE xx-XX]\nSIGN-IN @name = <user> / <pwd>\nSIGN-IN FROM ENV [LANGUAGE xx-XX]",
            "Authenticate against the service.",
            "Authenticate against the Vidyano service. `SIGN-IN FROM ENV` reads `VIDYANO_USER` / "
            + "`VIDYANO_PASSWORD` from the environment and loud-fails if either is unset. The `@name =` "
            + "form opens a named session.",
            ["SIGN-IN admin / admin", "SIGN-IN @admin = user / pwd", "SIGN-IN FROM ENV"],
            "session", []),

        new("SIGN-OUT",
            "SIGN-OUT",
            "End the current session.",
            null,
            ["SIGN-OUT"],
            "session", []),

        new("USE",
            "USE @name",
            "Switch to a named session (multi-session not yet implemented).",
            null,
            ["USE @admin"],
            "session", []),

        new("OPEN",
            "OPEN PersistentObject <type> <id> [AS @h]\nOPEN Query <id> [AS @h]\nOPEN MenuItem <path> [AS @h]",
            "Push a frame on the navigation stack.",
            "Pushes a Query, PersistentObject, or MenuItem frame on the navigation stack. In navigation "
            + "mode `OPEN PersistentObject`/`OPEN Query` require reachability; `OPEN MenuItem` walks the "
            + "user's menu.",
            ["OPEN MenuItem Sales/Customers", "OPEN Query Customers AS @customers", "OPEN PersistentObject \"Customer\" \"42\" AS @c"],
            "navigation", []),

        new("OPEN-ROW",
            "OPEN-ROW <index> [AS @h]\nOPEN-ROW WHERE <col> = <value> [AS @h]\nOPEN-ROW Detail \"<name>\" <index|WHERE …>",
            "Push a PO frame from a row of the current query.",
            "Opens the PersistentObject behind a row of the current query — by index, by `WHERE <col> = "
            + "<value>` (strict: 0 or >1 matches fail), or from a named detail query via `Detail \"<name>\"`.",
            ["OPEN-ROW 0 AS @row", "OPEN-ROW WHERE Name = \"Acme\"", "OPEN-ROW Detail \"OrderLines\" 0"],
            "navigation", []),

        new("GO-BACK",
            "GO-BACK",
            "Pop the top navigation frame (browser back).",
            "Pops the top navigation frame, revealing the one beneath. Refuses when the top is a PO in "
            + "edit (SAVE or CANCEL first) and when already at the root frame.",
            ["GO-BACK"],
            "navigation", []),

        new("FOLLOW",
            "FOLLOW <attr> [AS @h]",
            "Follow a reference attribute to its target PO.",
            null,
            ["FOLLOW Customer AS @c"],
            "navigation", []),

        new("OPEN-DETAIL",
            "OPEN-DETAIL \"<name>\" [AS @h]",
            "Open a detail query on the current PO as a frame.",
            null,
            ["OPEN-DETAIL \"OrderLines\""],
            "navigation", []),

        new("EDIT",
            "EDIT",
            "Enter edit mode on the current PO.",
            "Enters edit mode on the current PersistentObject. A `SET` auto-enters edit, so an explicit "
            + "`EDIT` is only needed when you want to assert edit state before changing anything.",
            ["EDIT"],
            "edit", []),

        new("CANCEL",
            "CANCEL",
            "Discard pending edits.",
            null,
            ["CANCEL"],
            "edit", []),

        new("SAVE",
            "SAVE [@initial]\nSAVE EXPECTING ERROR",
            "Persist pending edits.",
            "Saves pending edits. `SAVE EXPECTING ERROR` asserts the negative path: it passes only if the "
            + "server returns an error notification, and fails if the save unexpectedly succeeds.",
            ["SAVE", "SAVE EXPECTING ERROR"],
            "edit", []),

        new("REFRESH",
            "REFRESH",
            "Re-fetch the current PO/query from the server.",
            null,
            ["REFRESH"],
            "edit", []),

        new("RELOAD",
            "RELOAD",
            "Reload the current frame from the server.",
            null,
            ["RELOAD"],
            "edit", []),

        new("SET",
            "SET <attr> = <value>\nSET <attr> = LOOKUP \"<display>\"\nSET <attr> = ID \"<key>\"\nSET <attr> = null",
            "Change an attribute value.",
            "Writes an attribute. A bare value is a literal write (reference attrs auto-resolve via "
            + "Options/Lookup). `LOOKUP` matches `Options[].DisplayValue`; `ID` matches the raw key; "
            + "`null` clears the attribute.",
            ["SET Name = \"Acme Corp\"", "SET Status = LOOKUP \"Active\"", "SET Notes = null"],
            "edit", []),

        new("ACTION",
            "ACTION <action> [(Param=…)]\nACTION <action> = <option>\nACTION Detail \"<name>\" <action>\nACTION <action> EXPECTING ERROR",
            "Invoke an action by name.",
            "Executes an action, subject to the action-availability guard. `= <option>` chooses an "
            + "`Options[]` entry; `Detail \"<name>\"` targets a detail query; `EXPECTING ERROR` asserts "
            + "the negative path.",
            ["ACTION Export (Format=\"csv\")", "ACTION Delete = \"Yes, delete\"", "ACTION Detail \"OrderLines\" Delete"],
            "action", []),

        new("SEARCH",
            "SEARCH <text>\nSEARCH Detail \"<name>\" [text]",
            "Text-search the current query in place.",
            "Searches the current query without changing the nav stack. A leading `Detail \"<name>\"` "
            + "retargets a named detail query to load its rows; omit the text to load with an empty filter.",
            ["SEARCH \"Acme\"", "SEARCH Detail \"OrderLines\""],
            "query", []),

        new("SELECT-ROWS",
            "SELECT-ROWS <ALL | ALL EXCEPT <i|WHERE …> | NONE | <i> | WHERE <col> = <value>>\nSELECT-ROWS Detail \"<name>\" <target>",
            "Set the current query's selection.",
            "Sets the selection so a selection-gated action (e.g. Delete) can run. `ALL` is server-side "
            + "select-all (`Query.AllSelected`); `ALL EXCEPT` is inverse selection; `<i>`/`WHERE`/`NONE` "
            + "set explicit rows. Replaces the selection, never accumulates.",
            ["SELECT-ROWS ALL", "SELECT-ROWS WHERE Name = \"Acme\"", "SELECT-ROWS NONE"],
            "query", []),

        new("EXPECT",
            "EXPECT <subject> <op> <value>\nEXPECT <subject> IS [NOT] <flag>\nEXPECT <lhs> MATCHES \"<regex>\"\nEXPECT Detail \"<name>\" <query-subject>",
            "Assert on session/PO/query state.",
            "Asserts on `NavStack.*`, `TotalItems`, `Selection.*`, `IsInEdit`, `ClientOperation`, "
            + "attributes, notifications, and round-tripped metadata. `MATCHES` is a regex assertion "
            + "(1s ReDoS guard). `Detail \"<name>\"` redirects query-family subjects.",
            ["EXPECT Status = \"Approved\"", "EXPECT TotalItems >= 1", "EXPECT Code MATCHES \"^[A-Z]{2}\\d+$\""],
            "assert", []),

        new("GOTO",
            "GOTO <step>",
            "Jump to a labeled step.",
            null,
            ["GOTO cleanup"],
            "control", []),

        new("TOOL",
            "TOOL <name> [k=v, …] [-> @var]",
            "Call a registered C# delegate.",
            "Invokes a registered host delegate with named arguments. Throws become `tool-error` "
            + "diagnostics. In-process: register on `VidyanoScriptOptions.Tools`. From the CLI: load via "
            + "`--tools <pack.dll>` (implementing `IVidyanoScriptToolPack`).",
            ["TOOL fetch-user id=42 -> @user"],
            "tool", []),

        new("REQUIRES",
            "REQUIRES <expect-subject> <op> <value>\nREQUIRES TOOL <name>",
            "Precondition gate; skips the body when unmet.",
            "A precondition gate reusing the full EXPECT grammar. Holds → pass and continue. Unmet or "
            + "unevaluable → skip the rest of the body (`state-requires-unmet`, not a failure). "
            + "`REQUIRES TOOL <name>` is a capability gate.",
            ["REQUIRES TotalItems >= 1", "REQUIRES TOOL seed-db"],
            "control", []),

        new("CLEANUP",
            "CLEANUP",
            "Marker; statements after it always run.",
            "A marker statement. Anything after it runs even when the body was skipped by an unmet "
            + "`REQUIRES` — the .visc equivalent of a finally block.",
            ["CLEANUP"],
            "control", []),
    ];

    private static readonly IReadOnlySet<string> _names =
        _all.SelectMany(v => v.Aliases.Prepend(v.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, VerbInfo> _byName =
        _all.SelectMany(v => v.Aliases.Prepend(v.Name).Select(n => (Key: n, Verb: v)))
            .ToDictionary(x => x.Key, x => x.Verb, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every verb, in declaration order. Read by <c>help verbs</c> and editor hover.</summary>
    public static IReadOnlyList<VerbInfo> All => _all;

    /// <summary>Looks up a verb by name or alias, case-insensitively.</summary>
    public static bool TryGet(string lexeme, out VerbInfo info)
    {
        if (lexeme is not null && _byName.TryGetValue(lexeme, out var found))
        {
            info = found;
            return true;
        }
        info = null!;
        return false;
    }

    /// <summary>The case-insensitive set of recognized verb names (names ∪ aliases).
    /// <see cref="Parsing.Parser"/>'s known-verb gate delegates here.</summary>
    internal static IReadOnlySet<string> Names => _names;
}
