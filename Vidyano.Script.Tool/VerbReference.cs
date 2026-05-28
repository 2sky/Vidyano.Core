using Spectre.Console;

namespace Vidyano.Script.Tool;

/// <summary>
/// One place that lists every .visc verb with a one-line example. Printed by
/// <c>vidyano help verbs</c>. Keep this in sync with the parser — drift between this list and the
/// parser is the most common source of "but I tried that exact syntax" confusion.
/// </summary>
public static class VerbReference
{
    public static void Print()
    {
        var t = new Table().Border(TableBorder.Rounded).Title("[bold].visc verb reference[/]");
        t.AddColumn("Verb");
        t.AddColumn("Example");
        t.AddColumn("Notes");

        t.AddRow("@var = value",         "@app = http://localhost:5000",        "Define a script variable. Reference with {{var}}.");
        t.AddRow("@mode = ...",          "@mode = audit",                       "navigation (default) | audit | direct. Set before any verb.");
        t.AddRow("@session.<attr>",      "SET @session.CurrentYear = 2026",     "Read/write Client.Session. Also: EXPECT @session.X = … and {{@session.X}}.");
        t.AddRow("@initial.<prop>",      "EXPECT @initial.FullTypeName = \"…\"",  "Read Client.Initial (PO scalar or attribute). SAVE @initial clears the gate.");
        t.AddRow("SIGN-IN",              "SIGN-IN admin / admin",               "Or: SIGN-IN @admin = user / pwd  (named session).");
        t.AddRow("USE",                  "USE @admin",                          "Switch to a named session (multi-session not yet implemented).");
        t.AddRow("OPEN PersistentObject","OPEN PersistentObject \"Customer\" \"42\" AS @c","Opens a PO directly. In navigation mode requires reachability.");
        t.AddRow("OPEN Query",           "OPEN Query Customers AS @customers",  "Opens a query by id.");
        t.AddRow("OPEN MenuItem",        "OPEN MenuItem Sales/Customers",       "Walks the user's menu (Application.Queries[[ProgramUnits]]).");
        t.AddRow("OPEN-ROW",             "OPEN-ROW 0 AS @row",                  "Opens the PO behind a row of the current query, by index.");
        t.AddRow("OPEN-ROW WHERE",       "OPEN-ROW WHERE Name = \"Acme\"",       "Opens the PO behind the single row whose column equals the value. Strict: 0 or >1 matches fail. Value is service-string form, like SET.");
        t.AddRow("OPEN-ROW Detail",      "OPEN-ROW Detail \"OrderLines\" 0",     "Selects the row from a detail query (PO.Queries[[name]]) instead of the current query. Works with the index or WHERE form.");
        t.AddRow("GO-BACK",              "GO-BACK",                             "Pops the top nav frame (browser back). Refuses on a PO in edit, or at the root frame.");
        t.AddRow("SEARCH",               "SEARCH \"Acme\"",                      "Searches the current query.");
        t.AddRow("EDIT",                 "EDIT",                                "Enters edit mode on the current PO. SET auto-enters.");
        t.AddRow("SET",                  "SET Name = \"Acme Corp\"",             "Literal write. Reference attrs auto-resolve via Options/Lookup when no hint is given.");
        t.AddRow("SET … = LOOKUP",       "SET Status = LOOKUP \"Active\"",       "Match against attr.Options[[]].DisplayValue — works for reference attrs (Lookup query) and non-reference Options-bearing attrs (KeyValueList / Dropdown / ComboBox).");
        t.AddRow("SET … = ID",           "SET Status = ID \"active\"",           "Raw key match — Options[[]].Key for non-reference attrs, SelectedReferenceValue for reference attrs.");
        t.AddRow("SET … = null",         "SET Notes = null",                    "Clears the attribute. For reference attrs, requires CanRemoveReference.");
        t.AddRow("ACTION",               "ACTION Export (Format=\"csv\")",       "Executes an action. Subject to action-availability guard.");
        t.AddRow("ACTION X = option",    "ACTION Delete = \"Yes, delete\"",      "Choose an Options[[]] entry by label, or by `= ID <index>`. Mutually exclusive with (Param=…).");
        t.AddRow("SAVE",                 "SAVE",                                "Saves pending edits. Required attributes checked first.");
        t.AddRow("CANCEL",               "CANCEL",                              "Discards pending edits.");
        t.AddRow("EXPECT … = …",         "EXPECT Status = \"Approved\"",         "Equality (=,!=) and ordering (<,<=,>,>=) work on attribute values.");
        t.AddRow("EXPECT … IS NULL",     "EXPECT Notification IS NULL",         "Or IS NOT NULL.");
        t.AddRow("EXPECT Action … IS …", "EXPECT Action Approve IS AVAILABLE",  "IS [[NOT]] AVAILABLE | VISIBLE.");
        t.AddRow("EXPECT Attribute … IS","EXPECT Attribute Name IS REQUIRED",   "IS [[NOT]] VISIBLE | READONLY | REQUIRED | AVAILABLE (= IsVisible && !IsReadOnly).");
        t.AddRow("EXPECT IsDirty",       "EXPECT IsDirty = false",              "Same for IsInEdit.");
        t.AddRow("EXPECT TotalItems",    "EXPECT TotalItems >= 1",              "On the current query.");
        t.AddRow("EXPECT Attr TYPE",     "EXPECT Attribute X TYPE = \"String\"",  "Or TAG / TYPEHINT key for round-tripped metadata.");
        t.AddRow("EXPECT PO.<prop>",     "EXPECT PO.Type = \"Customer\"",         "Plus PO.Tag / PO.Metadata.<k> / PO.NavigationHints.<k>.");
        t.AddRow("EXPECT Query.<prop>",  "EXPECT Query.Columns[[Name]].Label = …", "Plus Query.Metadata.<k>, Query.PersistentObject.Type, etc.");
        t.AddRow("EXPECT Detail …",      "EXPECT Detail \"OrderLines\" TotalItems = 3", "Targets a detail query on the current PO. Only query subjects (TotalItems / Query.*).");
        t.AddRow("EXPECT Detail … IS",   "EXPECT Detail \"OrderLines\" IS AVAILABLE", "IS [[NOT]] AVAILABLE | VISIBLE — checks PO.Queries presence / visibility.");
        t.AddRow("TOOL",                 "TOOL fetch-user id=42 -> @user",      "Registered handler. CLI: load via --tools <pack.dll> (IVidyanoScriptToolPack).");
        t.AddRow("EXPECT … MATCHES",     "EXPECT Code MATCHES \"^[[A-Z]]{2}\\\\d+$\"", "Regex assertion (1s ReDoS-guard timeout). Null never matches.");
        t.AddRow("REQUIRES",             "REQUIRES TotalItems >= 1",            "Precondition gate (reuses EXPECT grammar). Unmet → skip rest of body.");
        t.AddRow("REQUIRES TOOL",        "REQUIRES TOOL seed-db",               "Capability gate: skip the body unless the named tool is registered.");
        t.AddRow("CLEANUP",              "CLEANUP",                             "Marker; statements after it always run, even when the body was skipped.");

        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Comments start with # — they're ignored, but `### label` starts a new step.[/]");
        AnsiConsole.MarkupLine("[grey]Strings: \"...\" with \\\" \\n \\t escapes; {{...}} interpolates inside strings (escape a literal brace as \\{).[/]");
        AnsiConsole.MarkupLine("[grey]Built-in vars: {{@today}} {{@now}} {{@uuid}} {{@random}} — evaluated per reference (capture into a var to freeze); --seed fixes the @uuid/@random sequence, --now anchors the clock.[/]");
    }
}
