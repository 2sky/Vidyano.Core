using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Parsing;

/// <summary>
/// Token kinds recognized by the .visc lexer. Verbs aren't separate kinds — they come through as
/// <see cref="Identifier"/> and the parser dispatches on the lexeme. This keeps the lexer ignorant
/// of the grammar's evolution.
/// </summary>
public enum TokenKind
{
    /// <summary>End of file marker — always last.</summary>
    Eof,
    /// <summary>End of logical line. Statements are single-line in .visc.</summary>
    Newline,
    /// <summary>A <c>###</c> separator at the start of a line, optionally followed by a label.</summary>
    StepHeader,
    /// <summary>A bare identifier: variable name, attribute name, action name, menu segment.</summary>
    Identifier,
    /// <summary>A double-quoted string literal. <c>Value</c> is the decoded text.</summary>
    String,
    /// <summary>An integer literal. <c>Value</c> is the parsed long.</summary>
    Integer,
    /// <summary>A decimal literal. <c>Value</c> is the parsed decimal.</summary>
    Number,
    /// <summary><c>true</c>, <c>false</c>, or <c>null</c> (case-insensitive). <c>Value</c> is bool or null.</summary>
    Literal,
    /// <summary><c>@name</c> — session or variable handle. <c>Lexeme</c> excludes the <c>@</c>.</summary>
    At,
    /// <summary><c>{{expr}}</c> — variable interpolation. <c>Lexeme</c> is the inner expression text.</summary>
    Interp,
    /// <summary><c>=</c> or <c>==</c>.</summary>
    Equals,
    /// <summary><c>!=</c>.</summary>
    NotEquals,
    /// <summary><c>&lt;</c>.</summary>
    Less,
    /// <summary><c>&lt;=</c>.</summary>
    LessEquals,
    /// <summary><c>&gt;</c>.</summary>
    Greater,
    /// <summary><c>&gt;=</c>.</summary>
    GreaterEquals,
    /// <summary><c>/</c> — used in <c>SIGN-IN user / password</c> and menu paths.</summary>
    Slash,
    /// <summary><c>(</c></summary>
    LParen,
    /// <summary><c>)</c></summary>
    RParen,
    /// <summary><c>[</c></summary>
    LBracket,
    /// <summary><c>]</c></summary>
    RBracket,
    /// <summary><c>,</c></summary>
    Comma,
    /// <summary><c>.</c> — used in <c>Notification.Type</c> and <c>Row[0].Customer</c>.</summary>
    Dot,
    /// <summary><c>-&gt;</c> — binds a <c>TOOL</c> call's return value to a variable
    /// (<c>TOOL fetch-user id=42 -&gt; @user</c>).</summary>
    Arrow,
}

/// <summary>
/// One lexeme from a .visc source. <see cref="Lexeme"/> is the raw text as it appeared;
/// <see cref="Value"/> is the typed payload for literals (string, long, decimal, bool, null).
/// </summary>
public readonly record struct Token(
    TokenKind Kind,
    string Lexeme,
    SourceLocation Location,
    object? Value = null);
