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
    };

    private static readonly HashSet<string> KnownOpenKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PersistentObject", "Query", "MenuItem", "Detail",
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
            "SAVE"        => new SaveStmt(null, tok.Location),
            "REFRESH"     => new RefreshStmt(null, tok.Location),
            "SET"         => ParseSet(tok.Location),
            "ACTION"      => ParseAction(tok.Location),
            "SEARCH"      => ParseSearch(tok.Location),
            "EXPECT"      => ParseExpect(tok.Location),
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
            sessionName = Advance().Lexeme;
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
            sessionName = Advance().Lexeme;
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
        var name = Advance().Lexeme;
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
        if (Peek().Kind != TokenKind.Identifier)
        {
            Error(ErrorKind.ParseExpected, "SET needs an attribute name.", Peek().Location, hint: "SET Name = \"value\"");
            return null;
        }
        var nameTok = Advance();
        if (!Match(TokenKind.Equals, out _))
        {
            Error(ErrorKind.ParseExpected, $"Expected '=' after SET {nameTok.Lexeme}.", Peek().Location);
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
        return new SetStmt(null, nameTok.Lexeme, value, hint, loc);
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
            if (Peek().Kind != TokenKind.Identifier)
            {
                Error(ErrorKind.ParseExpected, "Expected an attribute name after 'Attribute'.", Peek().Location);
                return null;
            }
            var nameTok = Advance();
            // EXPECT Attribute X LABEL = "..."
            if (Peek().Kind == TokenKind.Identifier &&
                string.Equals(Peek().Lexeme, "LABEL", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return new ExpectSubject(ExpectSubjectKind.AttributeLabel, nameTok.Lexeme, AttributeFlagKind.None, tok.Location);
            }
            return new ExpectSubject(ExpectSubjectKind.AttributeFlag, nameTok.Lexeme, AttributeFlagKind.None, tok.Location);
        }

        if (string.Equals(tok.Lexeme, "Query", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            // EXPECT Query LABEL = "..."  — operates on the current Query.
            if (Peek().Kind == TokenKind.Identifier &&
                string.Equals(Peek().Lexeme, "LABEL", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return new ExpectSubject(ExpectSubjectKind.QueryLabel, null, AttributeFlagKind.None, tok.Location);
            }
            Error(ErrorKind.ParseExpected,
                "Expected 'LABEL' after 'Query'.",
                Peek().Location,
                hint: "EXPECT Query LABEL = \"Orders\"");
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

        // Bare identifier — treat as attribute on the current PO.
        Advance();
        return new ExpectSubject(ExpectSubjectKind.Attribute, tok.Lexeme, AttributeFlagKind.None, tok.Location);
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
        return Advance().Lexeme;
    }

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
