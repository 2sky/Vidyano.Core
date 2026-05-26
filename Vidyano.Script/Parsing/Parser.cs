using System;
using System.Collections.Generic;
using System.Linq;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Parsing;

/// <summary>
/// Parses a .visc token stream into a <see cref="Script"/> AST. The parser is intentionally
/// recoverable: when it sees something unexpected, it skips to the next newline and continues so
/// users get every error at once instead of fixing one and rediscovering the next.
/// </summary>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics;
    private int _pos;

    private static readonly HashSet<string> KnownVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "SIGN-IN", "SIGN-OUT", "USE", "OPEN", "OPEN-ROW", "FOLLOW", "OPEN-DETAIL",
        "EDIT", "CANCEL", "SAVE", "REFRESH", "RELOAD",
        "SET", "ACTION", "SEARCH", "SELECT-ROWS",
        "EXPECT", "GOTO",
        "TOOL",
    };

    private static readonly HashSet<string> KnownOpenKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PersistentObject", "Query", "MenuItem", "Detail",
    };

    /// <summary>Reserved <c>@name</c> identifiers that the engine binds to fixed scoped PersistentObjects
    /// (<c>session</c> → <c>Client.Session</c>; <c>initial</c> → <c>Client.Initial</c>;
    /// <c>user</c> / <c>application</c> reserved for future use).
    /// Assigning to any of these is a parse error — they're not script variables, so allowing
    /// <c>@session = …</c> would silently shadow the binding.</summary>
    private static readonly HashSet<string> ReservedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "session", "initial", "user", "application",
    };

    public Parser(IReadOnlyList<Token> tokens, IEnumerable<Diagnostic>? lexerDiagnostics = null)
    {
        _tokens = tokens;
        _diagnostics = lexerDiagnostics?.ToList() ?? new List<Diagnostic>();
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public ScriptAst Parse()
    {
        var path = _tokens.Count > 0 ? _tokens[0].Location.SourcePath : "<inline>";
        var scriptLoc = new SourceLocation(path, 1, 1);
        var steps = new List<Step>();

        var currentLabel = "";
        var currentLoc = scriptLoc;
        var currentStmts = new List<Statement>();

        void FlushStep()
        {
            // Always emit at least one step so callers don't have to special-case empty scripts.
            if (steps.Count == 0 || currentLabel.Length > 0 || currentStmts.Count > 0)
                steps.Add(new Step(currentLabel, currentLoc, currentStmts.ToArray()));
        }

        SkipNewlines();
        while (!IsAtEnd)
        {
            if (Peek().Kind == TokenKind.StepHeader)
            {
                FlushStep();
                currentLabel = Peek().Lexeme;
                currentLoc = Peek().Location;
                currentStmts = new List<Statement>();
                Advance();
                SkipNewlines();
                continue;
            }

            var stmt = ParseStatement();
            if (stmt != null)
                currentStmts.Add(stmt);

            // Statements end at a newline. If we're partway through a malformed line, skip to next.
            SkipToEndOfLine();
            SkipNewlines();
        }

        FlushStep();
        return new ScriptAst(scriptLoc, steps);
    }

    // --- statement dispatch -------------------------------------------------------------------

    private Statement? ParseStatement()
    {
        var tok = Peek();

        // @name = ... — variable or @mode directive
        if (tok.Kind == TokenKind.At)
        {
            return ParseVariableOrModeAssignment();
        }

        if (tok.Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseUnexpectedToken,
                $"Expected a verb (e.g. SIGN-IN, OPEN, SET, EXPECT) but got {Describe(tok)}.",
                tok.Location,
                hint: $"Lines must start with a verb or a variable assignment. {VerbHint(tok.Lexeme)}");
            return null;
        }

        var verb = tok.Lexeme;
        if (!KnownVerbs.Contains(verb))
        {
            Error(ErrorKind.ParseUnknownVerb,
                $"Unknown verb '{verb}'.",
                tok.Location,
                hint: VerbHint(verb));
            return null;
        }

        Advance();
        return verb.ToUpperInvariant() switch
        {
            "SIGN-IN"     => ParseSignIn(tok.Location),
            "SIGN-OUT"    => ParseSignOut(tok.Location),
            "USE"         => ParseUse(tok.Location),
            "OPEN"        => ParseOpen(tok.Location),
            "OPEN-ROW"    => ParseOpenRow(tok.Location),
            "EDIT"        => new EditStmt(null, tok.Location),
            "CANCEL"      => new CancelStmt(null, tok.Location),
            "SAVE"        => ParseSave(tok.Location),
            "REFRESH"     => new RefreshStmt(null, tok.Location),
            "SET"         => ParseSet(tok.Location),
            "ACTION"      => ParseAction(tok.Location),
            "SEARCH"      => ParseSearch(tok.Location),
            "EXPECT"      => ParseExpect(tok.Location),
            "TOOL"        => ParseTool(tok.Location),
            _             => UnimplementedVerb(tok),
        };
    }

    private Statement? UnimplementedVerb(Token verb)
    {
        Error(ErrorKind.ParseUnknownVerb,
            $"Verb '{verb.Lexeme}' is recognized but not yet implemented in this build.",
            verb.Location);
        return null;
    }

    // --- variable / mode ----------------------------------------------------------------------

    private Statement? ParseVariableOrModeAssignment()
    {
        var at = Consume(TokenKind.At);
        if (at.Lexeme.Length == 0)
        {
            Error(ErrorKind.ParseExpected, "Expected a name after '@'.", at.Location);
            return null;
        }

        if (ReservedScopes.Contains(at.Lexeme))
        {
            // Capitalize for the diagnostic — "Client.Session" reads better than "Client.session".
            var pascal = char.ToUpperInvariant(at.Lexeme[0]) + at.Lexeme.Substring(1).ToLowerInvariant();
            Error(ErrorKind.ParseUnexpectedToken,
                $"`@{at.Lexeme}` is reserved — bound to Client.{pascal} by the engine.",
                at.Location,
                hint: $"Read with `@{at.Lexeme}.<attr>`; write with `SET @{at.Lexeme}.<attr> = …`. Pick a different name for a script variable.");
            return null;
        }

        if (!Match(TokenKind.Equals, out _))
        {
            Error(ErrorKind.ParseExpected,
                $"Expected '=' after @{at.Lexeme}.",
                Peek().Location,
                hint: "Top-level lines starting with @ must be variable assignments: @name = value.");
            return null;
        }

        // @mode = navigation|audit|direct  (special-cased so we can validate up-front)
        if (string.Equals(at.Lexeme, "mode", StringComparison.OrdinalIgnoreCase))
        {
            var modeTok = Peek();
            if (modeTok.Kind == TokenKind.Identifier)
            {
                Advance();
                if (Enum.TryParse<GuardMode>(modeTok.Lexeme, ignoreCase: true, out var mode))
                    return new ModeDirective(mode, at.Location);
                Error(ErrorKind.ParseInvalidMode,
                    $"Unknown guard mode '{modeTok.Lexeme}'.",
                    modeTok.Location,
                    hint: "Valid modes: navigation, audit, direct.");
                return null;
            }
            Error(ErrorKind.ParseInvalidMode,
                "Expected a mode name after @mode =.",
                modeTok.Location,
                hint: "Valid modes: navigation, audit, direct.");
            return null;
        }

        var value = ParseValueExpression();
        if (value == null)
            return null;
        return new VariableAssignment(at.Lexeme, value, at.Location);
    }

    // --- SIGN-IN / USE / SIGN-OUT -------------------------------------------------------------

    private Statement? ParseSignIn(SourceLocation loc)
    {
        string? sessionName = null;
        if (Peek().Kind == TokenKind.At)
        {
            sessionName = ConsumeHandleName("SIGN-IN");
            if (sessionName is null) return null;
            if (!Match(TokenKind.Equals, out _))
            {
                Error(ErrorKind.ParseExpected, $"Expected '=' after SIGN-IN @{sessionName}.", Peek().Location);
                return null;
            }
        }

        var user = ParseValueExpression();
        if (user == null) return null;

        Expression? password = null;
        if (Match(TokenKind.Slash, out _))
        {
            password = ParseValueExpression();
            if (password == null) return null;
        }

        // Optional `LANGUAGE xx-XX` — sent to the server so labels/messages come back localized.
        Expression? language = null;
        if (Peek().Kind == TokenKind.Identifier &&
            string.Equals(Peek().Lexeme, "LANGUAGE", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            language = ParseValueExpression();
            if (language == null) return null;
        }

        return new SignInStmt(sessionName, user, password, language, loc);
    }

    private Statement? ParseSignOut(SourceLocation loc)
    {
        string? sessionName = null;
        if (Peek().Kind == TokenKind.At)
        {
            sessionName = ConsumeHandleName("SIGN-OUT");
            if (sessionName is null) return null;
        }
        return new SignOutStmt(sessionName, loc);
    }

    private Statement? ParseUse(SourceLocation loc)
    {
        if (Peek().Kind != TokenKind.At)
        {
            Error(ErrorKind.ParseExpected,
                "USE needs a session handle.",
                Peek().Location,
                hint: "USE @admin");
            return null;
        }
        var name = ConsumeHandleName("USE");
        if (name is null) return null;
        return new UseSessionStmt(name, loc);
    }

    // --- OPEN family --------------------------------------------------------------------------

    private Statement? ParseOpen(SourceLocation loc)
    {
        if (Peek().Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected,
                "OPEN needs to be followed by PersistentObject, Query, or MenuItem.",
                Peek().Location);
            return null;
        }

        var kindTok = Advance();
        if (!KnownOpenKinds.Contains(kindTok.Lexeme))
        {
            Error(ErrorKind.ParseUnexpectedToken,
                $"OPEN doesn't recognize '{kindTok.Lexeme}'.",
                kindTok.Location,
                hint: Suggester.Hint(kindTok.Lexeme, KnownOpenKinds) ?? "Use OPEN PersistentObject, OPEN Query, or OPEN MenuItem.");
            return null;
        }

        switch (kindTok.Lexeme.ToUpperInvariant())
        {
            case "PERSISTENTOBJECT":
                {
                    var type = ParseValueExpression();
                    if (type == null) return null;
                    Expression? id = null;
                    if (!IsLineTerminator(Peek()) && !IsAsKeyword(Peek()))
                        id = ParseValueExpression();
                    var asHandle = ParseOptionalAs();
                    return new OpenPersistentObjectStmt(type, id, asHandle, loc);
                }
            case "QUERY":
                {
                    var id = ParseValueExpression();
                    if (id == null) return null;
                    var asHandle = ParseOptionalAs();
                    return new OpenQueryStmt(id, asHandle, loc);
                }
            case "MENUITEM":
                {
                    // OPEN MenuItem with no segments = open the first ProgramUnit's first item.
                    // The session enforces that behaviour; we just allow the empty path here.
                    var segments = new List<Expression>();
                    if (!IsLineTerminator(Peek()) && !IsAsKeyword(Peek()))
                    {
                        var first = ParseValueExpression();
                        if (first == null) return null;
                        segments.Add(first);
                        while (Match(TokenKind.Slash, out _))
                        {
                            var seg = ParseValueExpression();
                            if (seg == null) break;
                            segments.Add(seg);
                        }
                    }
                    var asHandle = ParseOptionalAs();
                    return new OpenMenuItemStmt(segments, asHandle, loc);
                }
        }
        return null;
    }

    private Statement? ParseOpenRow(SourceLocation loc)
    {
        var index = ParseValueExpression();
        if (index == null) return null;
        var asHandle = ParseOptionalAs();
        return new OpenRowStmt(index, asHandle, loc);
    }

    // --- SET / ACTION / SEARCH ----------------------------------------------------------------

    private Statement? ParseSet(SourceLocation loc)
    {
        // Optional reserved-scope prefix: SET @session.Attr = …
        string? scope = null;
        if (Peek().Kind == TokenKind.At)
        {
            scope = TryConsumeScopePrefix();
            if (scope == null) return null;
        }

        var attrName = ParseDottedAttributeName();
        if (attrName == null) return null;
        if (!Match(TokenKind.Equals, out _))
        {
            Error(ErrorKind.ParseExpected, $"Expected '=' after SET {attrName}.", Peek().Location);
            return null;
        }

        // Optional reference-resolution hint: SET attr = LOOKUP "..." | ID "..."
        ReferenceHintKind? hint = null;
        if (Peek().Kind == TokenKind.Identifier)
        {
            if (string.Equals(Peek().Lexeme, "LOOKUP", StringComparison.OrdinalIgnoreCase)) { Advance(); hint = ReferenceHintKind.Lookup; }
            else if (string.Equals(Peek().Lexeme, "ID",   StringComparison.OrdinalIgnoreCase)) { Advance(); hint = ReferenceHintKind.RawId; }
        }

        var value = ParseValueExpression();
        if (value == null) return null;
        return new SetStmt(null, attrName, value, hint, loc, scope);
    }

    /// <summary>Reads an attribute name token sequence of the form <c>Identifier (. Identifier)*</c>
    /// and returns the dotted join. Vidyano auto-populates dotted attributes (e.g.
    /// <c>Customer.Name</c> from a <c>Customer</c> reference), so the grammar must accept them
    /// anywhere a single attribute identifier is expected. Returns null after emitting a diagnostic
    /// when the leading token isn't an identifier.</summary>
    private string? ParseDottedAttributeName()
    {
        if (Peek().Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected, "Expected an attribute name.", Peek().Location);
            return null;
        }
        var sb = new System.Text.StringBuilder(Advance().Lexeme);
        while (Peek().Kind == TokenKind.Dot && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Kind == TokenKind.Identifier)
        {
            Advance(); // '.'
            sb.Append('.').Append(Advance().Lexeme);
        }
        return sb.ToString();
    }

    private Statement? ParseAction(SourceLocation loc)
    {
        if (Peek().Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected, "ACTION needs an action name.", Peek().Location, hint: "ACTION Approve");
            return null;
        }
        var name = Advance().Lexeme;
        Dictionary<string, Expression>? parameters = null;
        if (Match(TokenKind.LParen, out _))
        {
            parameters = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            while (Peek().Kind != TokenKind.RParen && !IsAtEnd && !IsLineTerminator(Peek()))
            {
                if (Peek().Kind != TokenKind.Identifier)
                {
                    Error(ErrorKind.ParseExpected, "Expected a parameter name.", Peek().Location);
                    break;
                }
                var pname = Advance().Lexeme;
                if (!Match(TokenKind.Equals, out _))
                {
                    Error(ErrorKind.ParseExpected, $"Expected '=' after parameter '{pname}'.", Peek().Location);
                    break;
                }
                var pv = ParseValueExpression();
                if (pv == null) break;
                parameters[pname] = pv;
                if (!Match(TokenKind.Comma, out _))
                    break;
            }
            if (!Match(TokenKind.RParen, out _))
                Error(ErrorKind.ParseExpected, "Expected ')' to close ACTION parameters.", Peek().Location);
        }
        return new ActionStmt(null, name, parameters, loc);
    }

    private Statement? ParseSearch(SourceLocation loc)
    {
        var text = ParseValueExpression();
        if (text == null) return null;
        return new SearchStmt(null, text, loc);
    }

    /// <summary><c>SAVE</c> on the top of the nav stack, or <c>SAVE @initial</c> against
    /// <see cref="Vidyano.Client.Initial"/>. <c>@session</c> is intentionally rejected —
    /// the session PO auto-roundtrips on every server call, so an explicit SAVE is meaningless
    /// (and would mislead authors into thinking they need it).</summary>
    private Statement? ParseSave(SourceLocation loc)
    {
        if (Peek().Kind != TokenKind.At)
            return new SaveStmt(null, loc);

        var scope = TryConsumeScopePrefix(requireDot: false);
        if (scope == null) return null;

        if (string.Equals(scope, "session", StringComparison.OrdinalIgnoreCase))
        {
            Error(ErrorKind.ParseExpected,
                "`@session` cannot be SAVEd — it auto-roundtrips on every server call.",
                loc,
                hint: "Drop the `@session` — bare SAVE acts on the current PO; @session is mutated via SET @session.<attr> and persists implicitly.");
            return null;
        }
        if (!string.Equals(scope, "initial", StringComparison.OrdinalIgnoreCase))
        {
            Error(ErrorKind.ParseExpected,
                $"SAVE @{scope} is not yet implemented.",
                loc,
                hint: "Only `SAVE` (current PO) and `SAVE @initial` are supported in this build.");
            return null;
        }
        return new SaveStmt(null, loc, Scope: scope);
    }

    // --- TOOL ---------------------------------------------------------------------------------

    /// <summary><c>TOOL &lt;name&gt; [k=v (, k=v)*] [-&gt; @var]</c>. Args are named-only; positional
    /// would be order-sensitive and there is no overload concept. The optional <c>-&gt;</c>
    /// captures the tool's return value into a script variable.</summary>
    private Statement? ParseTool(SourceLocation loc)
    {
        if (Peek().Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected, "TOOL needs a name.", Peek().Location, hint: "TOOL seed-db");
            return null;
        }
        var name = Advance().Lexeme;

        var args = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        // Args run until end-of-line or '->'. Each is `k=v` with literal/var/interp values.
        while (!IsLineTerminator(Peek()) && Peek().Kind != TokenKind.Arrow)
        {
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected,
                    "Expected an argument name.",
                    Peek().Location,
                    hint: "TOOL accepts named args: TOOL fetch-user id=42, region=\"eu\"");
                return null;
            }
            var argTok = Advance();
            if (!Match(TokenKind.Equals, out _))
            {
                Error(ErrorKind.ParseExpected, $"Expected '=' after argument '{argTok.Lexeme}'.", Peek().Location);
                return null;
            }
            var value = ParseValueExpression();
            if (value == null) return null;
            args[argTok.Lexeme] = value;

            // Optional comma between args — accepting both forms keeps the grammar friendly
            // (`TOOL t a=1 b=2` reads as well as `TOOL t a=1, b=2`).
            Match(TokenKind.Comma, out _);
        }

        string? resultVar = null;
        if (Match(TokenKind.Arrow, out _))
        {
            if (Peek().Kind != TokenKind.At)
            {
                Error(ErrorKind.ParseExpected, "'->' needs a @variable.", Peek().Location, hint: "TOOL fetch-user id=42 -> @user");
                return null;
            }
            var atTok = Advance();
            if (atTok.Lexeme.Length == 0)
            {
                Error(ErrorKind.ParseExpected, "Expected a name after '@'.", atTok.Location);
                return null;
            }
            if (ReservedScopes.Contains(atTok.Lexeme))
            {
                Error(ErrorKind.ParseUnexpectedToken,
                    $"`@{atTok.Lexeme}` is reserved and can't be bound by TOOL.",
                    atTok.Location,
                    hint: $"Pick a different variable name; `@{atTok.Lexeme}` is bound to Client.{ToTitle(atTok.Lexeme)} by the engine.");
                return null;
            }
            resultVar = atTok.Lexeme;
        }

        return new ToolCallStmt(name, args, resultVar, loc);
    }

    // --- EXPECT -------------------------------------------------------------------------------

    private Statement? ParseExpect(SourceLocation loc)
    {
        var subject = ParseExpectSubject();
        if (subject == null)
            return null;

        // Bare form for ClientOperation: 'EXPECT ClientOperation Refresh' means 'a Refresh op fired'.
        // For other subjects we still require an explicit operator — they have no useful bare meaning.
        if (subject.Kind == ExpectSubjectKind.ClientOperation && IsLineTerminator(Peek()))
            return new ExpectStmt(subject, ExpectOp.IsNotNull, null, loc);

        // IS [NOT] form for action/attribute-flag subjects and IS [NOT] NULL on any subject
        if (Peek().Kind == TokenKind.Identifier && string.Equals(Peek().Lexeme, "IS", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            var negate = false;
            if (Peek().Kind == TokenKind.Identifier && string.Equals(Peek().Lexeme, "NOT", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                negate = true;
            }
            var word = Peek();
            if (word.Kind != TokenKind.Identifier && word.Kind != TokenKind.Literal)
            {
                Error(ErrorKind.ParseExpected, "Expected a flag after IS (NULL, AVAILABLE, VISIBLE, READONLY, REQUIRED).", word.Location);
                return null;
            }
            Advance();

            // IS [NOT] NULL
            if (string.Equals(word.Lexeme, "null", StringComparison.OrdinalIgnoreCase))
                return new ExpectStmt(subject, negate ? ExpectOp.IsNotNull : ExpectOp.IsNull, null, loc);

            // IS [NOT] <FLAG>  — only valid for Action / AttributeFlag subjects
            var op = negate ? ExpectOp.IsNot : ExpectOp.Is;
            if (subject.Kind == ExpectSubjectKind.Action)
            {
                // Allowed flags: AVAILABLE, VISIBLE
                if (!string.Equals(word.Lexeme, "AVAILABLE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(word.Lexeme, "VISIBLE", StringComparison.OrdinalIgnoreCase))
                {
                    Error(ErrorKind.ParseUnexpectedToken,
                        $"'{word.Lexeme}' isn't valid for an Action EXPECT.",
                        word.Location,
                        hint: "Use EXPECT Action Name IS [NOT] AVAILABLE or IS [NOT] VISIBLE.");
                    return null;
                }
                return new ExpectStmt(subject with { Flag = ToFlag(word.Lexeme) }, op, null, loc);
            }
            if (subject.Kind == ExpectSubjectKind.AttributeFlag)
            {
                if (!string.Equals(word.Lexeme, "VISIBLE",  StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(word.Lexeme, "READONLY", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(word.Lexeme, "REQUIRED", StringComparison.OrdinalIgnoreCase))
                {
                    Error(ErrorKind.ParseUnexpectedToken,
                        $"'{word.Lexeme}' isn't valid for an Attribute EXPECT.",
                        word.Location,
                        hint: "Use EXPECT Attribute Name IS [NOT] VISIBLE | READONLY | REQUIRED.");
                    return null;
                }
                return new ExpectStmt(subject with { Flag = ToFlag(word.Lexeme) }, op, null, loc);
            }

            Error(ErrorKind.ParseUnexpectedToken,
                "IS <flag> can only be used after Action or Attribute subjects (use a comparison operator otherwise).",
                word.Location);
            return null;
        }

        // <op> <value>
        if (!TryParseCompareOp(out var cmp))
        {
            Error(ErrorKind.ParseExpected,
                "Expected a comparison operator (=, !=, <, <=, >, >=) or IS …",
                Peek().Location);
            return null;
        }
        var rhs = ParseValueExpression();
        if (rhs == null) return null;
        return new ExpectStmt(subject, cmp, rhs, loc);
    }

    private ExpectSubject? ParseExpectSubject()
    {
        var tok = Peek();

        // EXPECT {{expr}} ... — variable or {{Messages.X}} lookup on the LHS.
        if (tok.Kind == TokenKind.Interp)
        {
            Advance();
            return new ExpectSubject(ExpectSubjectKind.Expression, null, AttributeFlagKind.None, tok.Location, new InterpExpr(tok.Lexeme, tok.Location));
        }

        // EXPECT @session.Attr ... — scoped bare-attribute subject.
        // EXPECT @initial IS NULL — scoped root (no attribute) so the gate-PO presence can be asserted.
        if (tok.Kind == TokenKind.At)
        {
            var scope = TryConsumeScopePrefix(requireDot: false);
            if (scope == null) return null;
            if (Peek().Kind != TokenKind.Dot)
                return new ExpectSubject(ExpectSubjectKind.ScopedRoot, null, AttributeFlagKind.None, tok.Location, Scope: scope);
            Advance(); // '.'
            var attrName = ParseDottedAttributeName();
            if (attrName == null) return null;
            return new ExpectSubject(ExpectSubjectKind.Attribute, attrName, AttributeFlagKind.None, tok.Location, Scope: scope);
        }

        if (tok.Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected, "EXPECT needs a subject (attribute name, IsDirty, Notification, Action, …).", tok.Location);
            return null;
        }

        // Compound subjects
        if (string.Equals(tok.Lexeme, "Notification", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            if (Match(TokenKind.Dot, out _))
            {
                if (Peek().Kind != TokenKind.Identifier)
                {
                    Error(ErrorKind.ParseExpected, "Expected a property name after 'Notification.'.", Peek().Location);
                    return null;
                }
                var propTok = Advance();
                if (!string.Equals(propTok.Lexeme, "Type", StringComparison.OrdinalIgnoreCase))
                {
                    Error(ErrorKind.ParseUnexpectedToken,
                        $"Notification has no property '{propTok.Lexeme}'.",
                        propTok.Location,
                        hint: "Only Notification.Type is supported in v1.");
                    return null;
                }
                return new ExpectSubject(ExpectSubjectKind.NotificationType, null, AttributeFlagKind.None, tok.Location);
            }
            return new ExpectSubject(ExpectSubjectKind.Notification, null, AttributeFlagKind.None, tok.Location);
        }

        if (string.Equals(tok.Lexeme, "Action", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected an action name after 'Action'.", Peek().Location);
                return null;
            }
            var nameTok = Advance();
            // EXPECT Action X DISPLAY-NAME = "..."
            if (Peek().Kind == TokenKind.Identifier &&
                string.Equals(Peek().Lexeme, "DISPLAY-NAME", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return new ExpectSubject(ExpectSubjectKind.ActionDisplayName, nameTok.Lexeme, AttributeFlagKind.None, tok.Location);
            }
            return new ExpectSubject(ExpectSubjectKind.Action, nameTok.Lexeme, AttributeFlagKind.None, tok.Location);
        }

        if (string.Equals(tok.Lexeme, "ClientOperation", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected,
                    "Expected an operation type after 'ClientOperation' (Refresh, ShowNotification, ShowDialog, Navigate, …).",
                    Peek().Location);
                return null;
            }
            var opType = Advance().Lexeme;
            return new ExpectSubject(ExpectSubjectKind.ClientOperation, opType, AttributeFlagKind.None, tok.Location);
        }

        if (string.Equals(tok.Lexeme, "Attribute", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            string? scope = null;
            if (Peek().Kind == TokenKind.At)
            {
                scope = TryConsumeScopePrefix();
                if (scope == null) return null;
            }
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected an attribute name after 'Attribute'.", Peek().Location);
                return null;
            }
            var attrName = ParseDottedAttributeName();
            if (attrName == null) return null;
            // Trailing modifier keywords: LABEL / TYPE / TAG / TYPEHINT <key>.
            if (Peek().Kind == TokenKind.Identifier)
            {
                if (string.Equals(Peek().Lexeme, "LABEL", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    return new ExpectSubject(ExpectSubjectKind.AttributeLabel, attrName, AttributeFlagKind.None, tok.Location, Scope: scope);
                }
                if (string.Equals(Peek().Lexeme, "TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    return new ExpectSubject(ExpectSubjectKind.AttributeType, attrName, AttributeFlagKind.None, tok.Location, Scope: scope);
                }
                if (string.Equals(Peek().Lexeme, "TAG", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    return new ExpectSubject(ExpectSubjectKind.AttributeTag, attrName, AttributeFlagKind.None, tok.Location, Scope: scope);
                }
                if (string.Equals(Peek().Lexeme, "TYPEHINT", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    if (Peek().Kind != TokenKind.Identifier && Peek().Kind != TokenKind.String)
                    {
                        Error(ErrorKind.ParseExpected,
                            "Expected a TypeHint key after 'TYPEHINT'.",
                            Peek().Location,
                            hint: "EXPECT Attribute Name TYPEHINT maxLength = \"50\"");
                        return null;
                    }
                    var keyTok = Advance();
                    var keyText = keyTok.Kind == TokenKind.String ? (string)keyTok.Value! : keyTok.Lexeme;
                    return new ExpectSubject(ExpectSubjectKind.AttributeTypeHint, attrName, AttributeFlagKind.None, tok.Location, Scope: scope, MetadataKey: keyText);
                }
            }
            return new ExpectSubject(ExpectSubjectKind.AttributeFlag, attrName, AttributeFlagKind.None, tok.Location, Scope: scope);
        }

        if (string.Equals(tok.Lexeme, "PO", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return ParsePoOrQueryPath(tok, forQuery: false);
        }

        if (string.Equals(tok.Lexeme, "Query", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            // Legacy form: EXPECT Query LABEL = "..."  (kept for backward compat).
            if (Peek().Kind == TokenKind.Identifier &&
                string.Equals(Peek().Lexeme, "LABEL", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return new ExpectSubject(ExpectSubjectKind.QueryLabel, null, AttributeFlagKind.None, tok.Location);
            }
            // Dotted form: EXPECT Query.<prop> | Query.Metadata.<key> | Query.NavigationHints.<key> |
            // Query.PersistentObject.<prop> | Query.Columns[<name>].<prop>
            if (Peek().Kind == TokenKind.Dot)
            {
                return ParsePoOrQueryPath(tok, forQuery: true);
            }
            Error(ErrorKind.ParseExpected,
                "Expected 'LABEL' or '.<property>' after 'Query'.",
                Peek().Location,
                hint: "EXPECT Query LABEL = \"Orders\"  •  EXPECT Query.HasSearched = true");
            return null;
        }

        if (string.Equals(tok.Lexeme, "IsDirty",    StringComparison.OrdinalIgnoreCase)) { Advance(); return new ExpectSubject(ExpectSubjectKind.IsDirty,     null, AttributeFlagKind.None, tok.Location); }
        if (string.Equals(tok.Lexeme, "IsInEdit",   StringComparison.OrdinalIgnoreCase)) { Advance(); return new ExpectSubject(ExpectSubjectKind.IsInEdit,    null, AttributeFlagKind.None, tok.Location); }
        if (string.Equals(tok.Lexeme, "TotalItems", StringComparison.OrdinalIgnoreCase)) { Advance(); return new ExpectSubject(ExpectSubjectKind.TotalItems,  null, AttributeFlagKind.None, tok.Location); }

        if (string.Equals(tok.Lexeme, "NavStack", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            if (!Match(TokenKind.Dot, out _))
            {
                Error(ErrorKind.ParseExpected,
                    "Expected '.Depth' or '.Top.Kind'/'.Top.Name' after 'NavStack'.",
                    Peek().Location,
                    hint: "EXPECT NavStack.Depth = 2  •  EXPECT NavStack.Top.Kind = \"Query\"");
                return null;
            }
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected 'Depth' or 'Top' after 'NavStack.'.", Peek().Location);
                return null;
            }
            var head = Advance();
            if (string.Equals(head.Lexeme, "Depth", StringComparison.OrdinalIgnoreCase))
                return new ExpectSubject(ExpectSubjectKind.NavStackDepth, null, AttributeFlagKind.None, tok.Location);
            if (!string.Equals(head.Lexeme, "Top", StringComparison.OrdinalIgnoreCase))
            {
                Error(ErrorKind.ParseUnexpectedToken,
                    $"NavStack has no property '{head.Lexeme}'.",
                    head.Location,
                    hint: "Use NavStack.Depth or NavStack.Top.Kind / NavStack.Top.Name.");
                return null;
            }
            if (!Match(TokenKind.Dot, out _))
            {
                Error(ErrorKind.ParseExpected,
                    "Expected '.Kind' or '.Name' after 'NavStack.Top'.",
                    Peek().Location);
                return null;
            }
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected 'Kind' or 'Name' after 'NavStack.Top.'.", Peek().Location);
                return null;
            }
            var leaf = Advance();
            if (string.Equals(leaf.Lexeme, "Kind", StringComparison.OrdinalIgnoreCase))
                return new ExpectSubject(ExpectSubjectKind.NavStackTopKind, null, AttributeFlagKind.None, tok.Location);
            if (string.Equals(leaf.Lexeme, "Name", StringComparison.OrdinalIgnoreCase))
                return new ExpectSubject(ExpectSubjectKind.NavStackTopName, null, AttributeFlagKind.None, tok.Location);
            if (string.Equals(leaf.Lexeme, "IsDialog", StringComparison.OrdinalIgnoreCase))
                return new ExpectSubject(ExpectSubjectKind.NavStackTopIsDialog, null, AttributeFlagKind.None, tok.Location);
            Error(ErrorKind.ParseUnexpectedToken,
                $"NavStack.Top has no property '{leaf.Lexeme}'.",
                leaf.Location,
                hint: "Use NavStack.Top.Kind, NavStack.Top.Name, or NavStack.Top.IsDialog.");
            return null;
        }

        // Bare identifier — treat as attribute on the current PO (dotted names allowed).
        var bareName = ParseDottedAttributeName();
        if (bareName == null) return null;
        return new ExpectSubject(ExpectSubjectKind.Attribute, bareName, AttributeFlagKind.None, tok.Location);
    }

    /// <summary>Parses the dotted/indexed remainder after a <c>PO</c> or <c>Query</c> subject:
    /// <list type="bullet">
    ///   <item><c>.&lt;Prop&gt;</c> — scalar property lookup (PoProperty / QueryProperty).</item>
    ///   <item><c>.Metadata.&lt;key&gt;</c> — bag lookup.</item>
    ///   <item><c>.NavigationHints.&lt;key&gt;</c> — bag lookup.</item>
    ///   <item><c>.PersistentObject.&lt;Prop&gt;</c> (Query only) — the query's owning PO.</item>
    ///   <item><c>.Columns[&lt;name&gt;].&lt;Prop&gt;</c> (Query only).</item>
    /// </list>
    /// Returns <c>null</c> after emitting a diagnostic on malformed paths.</summary>
    private ExpectSubject? ParsePoOrQueryPath(Token rootTok, bool forQuery)
    {
        if (!Match(TokenKind.Dot, out _))
        {
            Error(ErrorKind.ParseExpected,
                $"Expected '.<property>' after '{rootTok.Lexeme}'.",
                Peek().Location);
            return null;
        }
        if (Peek().Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected,
                $"Expected a property name after '{rootTok.Lexeme}.'.",
                Peek().Location);
            return null;
        }
        var head = Advance();
        var headName = head.Lexeme;

        // Metadata / NavigationHints — these always need a `.key` after the head.
        var isMetadata = string.Equals(headName, "Metadata", StringComparison.OrdinalIgnoreCase);
        var isNav     = string.Equals(headName, "NavigationHints", StringComparison.OrdinalIgnoreCase);
        if (isMetadata || isNav)
        {
            if (!Match(TokenKind.Dot, out _))
            {
                Error(ErrorKind.ParseExpected,
                    $"Expected '.<key>' after '{rootTok.Lexeme}.{headName}'.",
                    Peek().Location,
                    hint: $"EXPECT {rootTok.Lexeme}.{headName}.someKey = \"value\"");
                return null;
            }
            if (Peek().Kind != TokenKind.Identifier && Peek().Kind != TokenKind.String)
            {
                Error(ErrorKind.ParseExpected,
                    $"Expected a key name after '{rootTok.Lexeme}.{headName}.'.",
                    Peek().Location);
                return null;
            }
            var keyTok = Advance();
            var keyText = keyTok.Kind == TokenKind.String ? (string)keyTok.Value! : keyTok.Lexeme;
            var kind = forQuery
                ? (isMetadata ? ExpectSubjectKind.QueryMetadata : ExpectSubjectKind.QueryNavigationHints)
                : (isMetadata ? ExpectSubjectKind.PoMetadata    : ExpectSubjectKind.PoNavigationHints);
            return new ExpectSubject(kind, null, AttributeFlagKind.None, rootTok.Location, MetadataKey: keyText);
        }

        // Query-specific shapes: PersistentObject.<prop> and Columns[<name>].<prop>.
        if (forQuery && string.Equals(headName, "PersistentObject", StringComparison.OrdinalIgnoreCase))
        {
            if (!Match(TokenKind.Dot, out _))
            {
                Error(ErrorKind.ParseExpected,
                    "Expected '.<property>' after 'Query.PersistentObject'.",
                    Peek().Location,
                    hint: "EXPECT Query.PersistentObject.Type = \"Customer\"");
                return null;
            }
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected a property name after 'Query.PersistentObject.'.", Peek().Location);
                return null;
            }
            var propTok = Advance();
            return new ExpectSubject(ExpectSubjectKind.QueryPoProperty, propTok.Lexeme, AttributeFlagKind.None, rootTok.Location);
        }

        if (forQuery && string.Equals(headName, "Columns", StringComparison.OrdinalIgnoreCase))
        {
            if (!Match(TokenKind.LBracket, out _))
            {
                Error(ErrorKind.ParseExpected,
                    "Expected '[<column>]' after 'Query.Columns'.",
                    Peek().Location,
                    hint: "EXPECT Query.Columns[Name].Label = \"Customer name\"");
                return null;
            }
            if (Peek().Kind != TokenKind.Identifier && Peek().Kind != TokenKind.String)
            {
                Error(ErrorKind.ParseExpected, "Expected a column name inside '[...]'.", Peek().Location);
                return null;
            }
            var colTok = Advance();
            var colName = colTok.Kind == TokenKind.String ? (string)colTok.Value! : colTok.Lexeme;
            if (!Match(TokenKind.RBracket, out _))
            {
                Error(ErrorKind.ParseExpected, "Expected ']' after column name.", Peek().Location);
                return null;
            }
            if (!Match(TokenKind.Dot, out _))
            {
                Error(ErrorKind.ParseExpected,
                    "Expected '.<property>' after 'Query.Columns[…]'.",
                    Peek().Location,
                    hint: "EXPECT Query.Columns[Name].Label = \"Customer name\"");
                return null;
            }
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected a property name after 'Query.Columns[…].'.", Peek().Location);
                return null;
            }
            var propTok = Advance();
            return new ExpectSubject(ExpectSubjectKind.QueryColumn, colName, AttributeFlagKind.None, rootTok.Location, MetadataKey: propTok.Lexeme);
        }

        // Scalar property: PO.<prop> or Query.<prop>.
        var scalarKind = forQuery ? ExpectSubjectKind.QueryProperty : ExpectSubjectKind.PoProperty;
        return new ExpectSubject(scalarKind, headName, AttributeFlagKind.None, rootTok.Location);
    }

    /// <summary>Consumes an <c>@scope</c> prefix used in SET targets, EXPECT subjects, and value
    /// expressions. Emits a diagnostic and returns null when the name isn't a reserved scope.
    /// When <paramref name="requireDot"/> is <c>true</c> (the default) a trailing <c>.</c> is also
    /// consumed; callers that may legitimately appear without one (<c>SAVE @initial</c>,
    /// <c>EXPECT @initial IS NULL</c>) pass <c>false</c> and check what follows themselves.</summary>
    private string? TryConsumeScopePrefix(bool requireDot = true)
    {
        var atTok = Advance();
        if (atTok.Lexeme.Length == 0)
        {
            Error(ErrorKind.ParseExpected, "Expected a scope name after '@'.", atTok.Location);
            return null;
        }
        if (!ReservedScopes.Contains(atTok.Lexeme))
        {
            Error(ErrorKind.ParseUnexpectedToken,
                $"Unknown variable scope '@{atTok.Lexeme}'.",
                atTok.Location,
                hint: Suggester.Hint(atTok.Lexeme, ReservedScopes)
                      ?? "Valid scopes: @session, @initial. (@user and @application are reserved but not yet implemented.)");
            return null;
        }
        if (requireDot && !Match(TokenKind.Dot, out _))
        {
            Error(ErrorKind.ParseExpected,
                $"Expected '.<attribute>' after @{atTok.Lexeme}.",
                Peek().Location,
                hint: $"`@{atTok.Lexeme}` is a PO reference, not a value — use `@{atTok.Lexeme}.<attr>`.");
            return null;
        }
        return atTok.Lexeme;
    }

    private static AttributeFlagKind ToFlag(string lexeme) =>
        lexeme.ToUpperInvariant() switch
        {
            "VISIBLE"   => AttributeFlagKind.Visible,
            "READONLY"  => AttributeFlagKind.ReadOnly,
            "REQUIRED"  => AttributeFlagKind.Required,
            "AVAILABLE" => AttributeFlagKind.None, // captured separately by Op; flag is meaningful only for AttributeFlag
            _           => AttributeFlagKind.None,
        };

    private bool TryParseCompareOp(out ExpectOp op)
    {
        switch (Peek().Kind)
        {
            case TokenKind.Equals:        Advance(); op = ExpectOp.Eq;   return true;
            case TokenKind.NotEquals:     Advance(); op = ExpectOp.NotEq; return true;
            case TokenKind.Less:          Advance(); op = ExpectOp.Lt;   return true;
            case TokenKind.LessEquals:    Advance(); op = ExpectOp.LtEq; return true;
            case TokenKind.Greater:       Advance(); op = ExpectOp.Gt;   return true;
            case TokenKind.GreaterEquals: Advance(); op = ExpectOp.GtEq; return true;
        }

        // Word operators: CONTAINS "x" and NOT CONTAINS "x". Keyword form because '~' or '%='
        // would read worse here — partial matches are common enough in test assertions that the
        // verbosity is worth the readability.
        if (Peek().Kind == TokenKind.Identifier)
        {
            if (string.Equals(Peek().Lexeme, "CONTAINS", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                op = ExpectOp.Contains;
                return true;
            }
            if (string.Equals(Peek().Lexeme, "NOT", StringComparison.OrdinalIgnoreCase) &&
                _pos + 1 < _tokens.Count &&
                _tokens[_pos + 1].Kind == TokenKind.Identifier &&
                string.Equals(_tokens[_pos + 1].Lexeme, "CONTAINS", StringComparison.OrdinalIgnoreCase))
            {
                Advance(); // NOT
                Advance(); // CONTAINS
                op = ExpectOp.NotContains;
                return true;
            }
        }

        op = default;
        return false;
    }

    // --- value expressions --------------------------------------------------------------------

    private Expression? ParseValueExpression()
    {
        var tok = Peek();
        switch (tok.Kind)
        {
            case TokenKind.String:
                Advance();
                return new LiteralExpr(tok.Value, tok.Location);
            case TokenKind.Integer:
            case TokenKind.Number:
            case TokenKind.Literal:
                Advance();
                return new LiteralExpr(tok.Value, tok.Location);
            case TokenKind.Interp:
                Advance();
                return new InterpExpr(tok.Lexeme, tok.Location);
            case TokenKind.Identifier:
                // An identifier as a value is legal for menu segments, bare flags, etc.
                Advance();
                return new IdentifierExpr(tok.Lexeme, tok.Location);
            case TokenKind.At:
                {
                    var scope = TryConsumeScopePrefix();
                    if (scope == null) return null;
                    var attr = ParseDottedAttributeName();
                    if (attr == null) return null;
                    return new VariableAttributeExpr(scope, attr, tok.Location);
                }
        }
        Error(ErrorKind.ParseExpected, $"Expected a value but got {Describe(tok)}.", tok.Location,
            hint: "Values can be \"strings\", numbers, true/false/null, {{interpolations}}, or bare identifiers.");
        return null;
    }

    // --- token helpers ------------------------------------------------------------------------

    private bool IsAtEnd => Peek().Kind == TokenKind.Eof;
    private Token Peek() => _tokens[_pos];
    private Token Advance() => _tokens[_pos++];

    private bool Match(TokenKind kind, out Token tok)
    {
        if (Peek().Kind == kind) { tok = Advance(); return true; }
        tok = default;
        return false;
    }

    private Token Consume(TokenKind kind) => Advance();

    private bool IsLineTerminator(Token t) => t.Kind is TokenKind.Newline or TokenKind.Eof or TokenKind.StepHeader;

    private bool IsAsKeyword(Token t) =>
        t.Kind == TokenKind.Identifier && string.Equals(t.Lexeme, "AS", StringComparison.OrdinalIgnoreCase);

    private string? ParseOptionalAs()
    {
        if (!IsAsKeyword(Peek())) return null;
        Advance();
        if (Peek().Kind != TokenKind.At)
        {
            Error(ErrorKind.ParseExpected, "AS needs a @handle.", Peek().Location, hint: "OPEN Query Customers AS @customers");
            return null;
        }
        return ConsumeHandleName("AS");
    }

    /// <summary>Consumes an <c>@handle</c> token after a verb that binds a named session
    /// (SIGN-IN/SIGN-OUT/USE) or attaches a handle via AS. Rejects names that collide with
    /// reserved scopes so users can't shadow the <c>@session</c> binding with a same-named handle.</summary>
    private string? ConsumeHandleName(string verb)
    {
        var tok = Advance();
        if (ReservedScopes.Contains(tok.Lexeme))
        {
            Error(ErrorKind.ParseUnexpectedToken,
                $"'@{tok.Lexeme}' is reserved and can't be used as a {verb} handle.",
                tok.Location,
                hint: $"'@{tok.Lexeme}' is bound to Client.{ToTitle(tok.Lexeme)} by the engine. Pick a different handle name.");
            return null;
        }
        return tok.Lexeme;
    }

    private static string ToTitle(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

    private void SkipNewlines()
    {
        while (Peek().Kind == TokenKind.Newline) Advance();
    }

    private void SkipToEndOfLine()
    {
        while (!IsAtEnd && Peek().Kind != TokenKind.Newline && Peek().Kind != TokenKind.StepHeader)
            Advance();
    }

    private void Error(string kind, string message, SourceLocation loc, string? hint = null)
        => _diagnostics.Add(new Diagnostic(kind, message, loc, hint));

    private static string Describe(Token t) =>
        t.Kind switch
        {
            TokenKind.Eof       => "end of file",
            TokenKind.Newline   => "end of line",
            TokenKind.String    => $"string \"{t.Lexeme}\"",
            TokenKind.Identifier => $"'{t.Lexeme}'",
            _                   => $"'{t.Lexeme}'",
        };

    private static string? VerbHint(string verb) => Suggester.Hint(verb, KnownVerbs, "verb");
}
