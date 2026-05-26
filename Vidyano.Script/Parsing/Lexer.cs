using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Parsing;

/// <summary>
/// Turns a .visc source string into a flat token stream. Single forward pass, no lookahead beyond
/// one char. Errors are collected as <see cref="Diagnostic"/>s; the lexer never throws on bad input
/// so the parser/runner can show as many problems as possible per file.
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private readonly string _path;
    private readonly List<Diagnostic> _diagnostics = new();
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public Lexer(string source, string path = "<inline>")
    {
        _source = source ?? "";
        _path = path;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        var atLineStart = true;

        while (_pos < _source.Length)
        {
            // Step header is only recognized at the start of a line.
            if (atLineStart && Match3("###"))
            {
                var loc = Here();
                _pos += 3; _col += 3;
                // Consume label until EOL — keep it as the lexeme so the parser can label the step.
                var sb = new StringBuilder();
                while (_pos < _source.Length && _source[_pos] != '\n')
                {
                    sb.Append(_source[_pos]);
                    _pos++; _col++;
                }
                tokens.Add(new Token(TokenKind.StepHeader, sb.ToString().Trim(), loc));
                atLineStart = false;
                continue;
            }

            var ch = _source[_pos];

            // Comments: # to EOL. We *don't* emit a token — comments aren't grammar.
            // (Exception: `# @mode = ...` is treated as a normal var assignment by the parser if
            // we drop the # here. We don't bother — users can write `@mode = ...` without the leading #.)
            if (ch == '#')
            {
                while (_pos < _source.Length && _source[_pos] != '\n') { _pos++; _col++; }
                continue;
            }

            // Newline — emit and reset line tracking.
            if (ch == '\n')
            {
                tokens.Add(new Token(TokenKind.Newline, "\n", Here()));
                _pos++;
                _line++;
                _col = 1;
                atLineStart = true;
                continue;
            }

            // Skip other whitespace (CR included so CRLF works).
            if (ch is ' ' or '\t' or '\r')
            {
                _pos++; _col++;
                continue;
            }

            atLineStart = false;

            // String literal
            if (ch == '"')
            {
                tokens.Add(ReadString());
                continue;
            }

            // Variable interpolation {{...}}
            if (ch == '{' && Peek(1) == '{')
            {
                tokens.Add(ReadInterp());
                continue;
            }

            // @name handle
            if (ch == '@')
            {
                tokens.Add(ReadAt());
                continue;
            }

            // Operators / punctuation
            if (TryReadOperator(out var op))
            {
                tokens.Add(op);
                continue;
            }

            // Number (possibly negative)
            if (char.IsDigit(ch) || (ch == '-' && char.IsDigit(Peek(1))))
            {
                tokens.Add(ReadNumber());
                continue;
            }

            // Identifier / keyword / literal
            if (IsIdentStart(ch))
            {
                tokens.Add(ReadIdentifier());
                continue;
            }

            _diagnostics.Add(new Diagnostic(
                ErrorKind.ParseUnexpectedToken,
                $"Unexpected character '{ch}'.",
                Here(),
                Hint: "Strings need to be in double quotes; identifiers must start with a letter or underscore."));
            _pos++; _col++;
        }

        tokens.Add(new Token(TokenKind.Eof, "", Here()));
        return tokens;
    }

    private Token ReadString()
    {
        var loc = Here();
        _pos++; _col++; // opening quote
        var sb = new StringBuilder();
        while (_pos < _source.Length && _source[_pos] != '"')
        {
            var c = _source[_pos];
            if (c == '\n')
            {
                _diagnostics.Add(new Diagnostic(
                    ErrorKind.ParseUnterminatedString,
                    "String literal is missing its closing quote.",
                    loc,
                    Hint: "Add a closing \" before the end of the line, or escape newlines as \\n."));
                return new Token(TokenKind.String, sb.ToString(), loc, sb.ToString());
            }
            if (c == '\\' && _pos + 1 < _source.Length)
            {
                _pos++; _col++;
                var esc = _source[_pos];
                sb.Append(esc switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', _ => esc });
                _pos++; _col++;
                continue;
            }
            sb.Append(c);
            _pos++; _col++;
        }
        if (_pos < _source.Length) { _pos++; _col++; } // closing quote
        var s = sb.ToString();
        return new Token(TokenKind.String, s, loc, s);
    }

    private Token ReadInterp()
    {
        var loc = Here();
        _pos += 2; _col += 2; // skip "{{"
        var sb = new StringBuilder();
        while (_pos < _source.Length)
        {
            if (_source[_pos] == '}' && Peek(1) == '}')
            {
                _pos += 2; _col += 2;
                var inner = sb.ToString().Trim();
                return new Token(TokenKind.Interp, inner, loc, inner);
            }
            if (_source[_pos] == '\n')
            {
                _diagnostics.Add(new Diagnostic(
                    ErrorKind.ParseUnexpectedToken,
                    "Variable interpolation {{...}} is missing its closing }}.",
                    loc));
                return new Token(TokenKind.Interp, sb.ToString().Trim(), loc, sb.ToString().Trim());
            }
            sb.Append(_source[_pos]);
            _pos++; _col++;
        }
        _diagnostics.Add(new Diagnostic(
            ErrorKind.ParseUnexpectedToken,
            "Variable interpolation {{...}} is missing its closing }}.",
            loc));
        return new Token(TokenKind.Interp, sb.ToString().Trim(), loc, sb.ToString().Trim());
    }

    private Token ReadAt()
    {
        var loc = Here();
        _pos++; _col++;
        var sb = new StringBuilder();
        while (_pos < _source.Length && IsIdentCont(_source[_pos]))
        {
            sb.Append(_source[_pos]);
            _pos++; _col++;
        }
        return new Token(TokenKind.At, sb.ToString(), loc);
    }

    private Token ReadNumber()
    {
        var loc = Here();
        var sb = new StringBuilder();
        if (_source[_pos] == '-') { sb.Append('-'); _pos++; _col++; }
        var isDecimal = false;
        while (_pos < _source.Length)
        {
            var c = _source[_pos];
            if (char.IsDigit(c)) { sb.Append(c); _pos++; _col++; continue; }
            if (c == '.' && !isDecimal && char.IsDigit(Peek(1))) { isDecimal = true; sb.Append(c); _pos++; _col++; continue; }
            break;
        }
        var text = sb.ToString();
        if (isDecimal && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return new Token(TokenKind.Number, text, loc, d);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return new Token(TokenKind.Integer, text, loc, i);
        _diagnostics.Add(new Diagnostic(ErrorKind.ParseInvalidValue, $"Invalid numeric literal '{text}'.", loc));
        return new Token(TokenKind.Number, text, loc, 0m);
    }

    private Token ReadIdentifier()
    {
        var loc = Here();
        var sb = new StringBuilder();
        while (_pos < _source.Length && IsIdentCont(_source[_pos]))
        {
            sb.Append(_source[_pos]);
            _pos++; _col++;
        }
        var lex = sb.ToString();
        // Match true/false/null in a case-insensitive way without conflating with identifiers proper.
        if (string.Equals(lex, "true",  StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Literal, lex, loc, true);
        if (string.Equals(lex, "false", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Literal, lex, loc, false);
        if (string.Equals(lex, "null",  StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Literal, lex, loc, null);
        return new Token(TokenKind.Identifier, lex, loc);
    }

    private bool TryReadOperator(out Token token)
    {
        var loc = Here();
        var c = _source[_pos];
        var n = Peek(1);
        Token Make(TokenKind kind, int len, string lex)
        {
            _pos += len; _col += len;
            return new Token(kind, lex, loc);
        }
        switch (c)
        {
            case '=': token = n == '=' ? Make(TokenKind.Equals, 2, "==") : Make(TokenKind.Equals, 1, "="); return true;
            case '!': if (n == '=') { token = Make(TokenKind.NotEquals, 2, "!="); return true; } break;
            // `->` binds a TOOL call's return into a variable. We special-case `-` here (rather than
            // adding a general minus operator) so the lexer's negative-number path keeps working
            // — `-42` still falls through to ReadNumber.
            case '-': if (n == '>') { token = Make(TokenKind.Arrow, 2, "->"); return true; } break;
            case '<': token = n == '=' ? Make(TokenKind.LessEquals, 2, "<=") : Make(TokenKind.Less, 1, "<"); return true;
            case '>': token = n == '=' ? Make(TokenKind.GreaterEquals, 2, ">=") : Make(TokenKind.Greater, 1, ">"); return true;
            case '/': token = Make(TokenKind.Slash, 1, "/"); return true;
            case '(': token = Make(TokenKind.LParen, 1, "("); return true;
            case ')': token = Make(TokenKind.RParen, 1, ")"); return true;
            case '[': token = Make(TokenKind.LBracket, 1, "["); return true;
            case ']': token = Make(TokenKind.RBracket, 1, "]"); return true;
            case ',': token = Make(TokenKind.Comma, 1, ","); return true;
            case '.': token = Make(TokenKind.Dot, 1, "."); return true;
        }
        token = default;
        return false;
    }

    private SourceLocation Here() => new(_path, _line, _col);
    private char Peek(int offset) => _pos + offset < _source.Length ? _source[_pos + offset] : '\0';
    private bool Match3(string s) => _pos + 2 < _source.Length && _source[_pos] == s[0] && _source[_pos + 1] == s[1] && _source[_pos + 2] == s[2];

    // Identifiers allow letters, digits, '_', '-' (so verbs like SIGN-IN are one token).
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentCont(char c)  => char.IsLetterOrDigit(c) || c == '_' || c == '-';
}
