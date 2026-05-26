using System.Collections.Generic;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Parsing;

/// <summary>
/// A parsed .visc source. Steps are reporting-only groupings — execution is sequential across them.
/// The first step's <see cref="Step.Label"/> is empty when statements appear before any <c>###</c>.
/// </summary>
/// <remarks>
/// Named <c>ScriptAst</c> rather than <c>Script</c> to avoid colliding with the parent
/// <c>Vidyano.Script</c> namespace inside <c>Vidyano.Script.Runtime</c>.
/// </remarks>
public sealed record ScriptAst(SourceLocation Location, IReadOnlyList<Step> Steps);

/// <summary>One logical block: an optional <c>### Label</c> header and the statements under it.</summary>
public sealed record Step(string Label, SourceLocation Location, IReadOnlyList<Statement> Statements);

// --- Statements ---------------------------------------------------------------------------------

/// <summary>Base type for all .visc statements. <see cref="Location"/> points at the verb token.</summary>
public abstract record Statement(SourceLocation Location);

/// <summary><c>@name = value</c> — bind a script-scoped variable.</summary>
public sealed record VariableAssignment(string Name, Expression Value, SourceLocation Location) : Statement(Location);

/// <summary><c>@mode = navigation|audit|direct</c>. Hoisted out of variables since it changes semantics.</summary>
public sealed record ModeDirective(GuardMode Mode, SourceLocation Location) : Statement(Location);

/// <summary><c>SIGN-IN user / password [LANGUAGE xx-XX]</c>. <see cref="Language"/> is sent as
/// <c>data["requestedLanguage"]</c> via <see cref="Hooks.OnCreateData"/> so subsequent server-rendered
/// labels, messages, and notifications come back localized.</summary>
public sealed record SignInStmt(string? SessionName, Expression UserName, Expression? Password, Expression? Language, SourceLocation Location) : Statement(Location);

/// <summary><c>SIGN-OUT</c> (current session) or <c>SIGN-OUT @name</c>.</summary>
public sealed record SignOutStmt(string? SessionName, SourceLocation Location) : Statement(Location);

/// <summary><c>USE @name</c> — switch active session.</summary>
public sealed record UseSessionStmt(string SessionName, SourceLocation Location) : Statement(Location);

/// <summary><c>OPEN PersistentObject Customer 42 [AS @handle]</c>.</summary>
public sealed record OpenPersistentObjectStmt(Expression Type, Expression? ObjectId, string? AsHandle, SourceLocation Location) : Statement(Location);

/// <summary><c>OPEN Query Orders [AS @handle]</c>.</summary>
public sealed record OpenQueryStmt(Expression Id, string? AsHandle, SourceLocation Location) : Statement(Location);

/// <summary><c>OPEN MenuItem Sales/Customers [AS @handle]</c>. Path is <c>/</c>-separated.</summary>
public sealed record OpenMenuItemStmt(IReadOnlyList<Expression> PathSegments, string? AsHandle, SourceLocation Location) : Statement(Location);

/// <summary><c>OPEN-ROW 0 [AS @handle]</c> — open the PO behind a row of the current query.</summary>
public sealed record OpenRowStmt(Expression Index, string? AsHandle, SourceLocation Location) : Statement(Location);

/// <summary><c>EDIT</c> — enter edit mode on the current PO.</summary>
public sealed record EditStmt(string? Handle, SourceLocation Location) : Statement(Location);

/// <summary><c>CANCEL</c> — discard pending edits.</summary>
public sealed record CancelStmt(string? Handle, SourceLocation Location) : Statement(Location);

/// <summary><c>SAVE</c> on the current PO (top of nav stack) or <c>SAVE @initial</c> on the
/// gate PO surfaced by <see cref="Vidyano.Client.Initial"/>. <see cref="Scope"/> is <c>"initial"</c>
/// for the gate variant; <c>null</c> targets the current PO.</summary>
public sealed record SaveStmt(string? Handle, SourceLocation Location, string? Scope = null) : Statement(Location);

/// <summary><c>REFRESH</c> — refresh attributes (calls <c>PersistentObject.Refresh</c>).</summary>
public sealed record RefreshStmt(string? Handle, SourceLocation Location) : Statement(Location);

/// <summary>
/// <c>SET Name = "value"</c> — set an attribute on the current PO. For reference attributes:
/// <c>SET Name = LOOKUP "filter"</c> forces a lookup search; <c>SET Name = ID "guid"</c> sets the
/// raw <c>SelectedReferenceValue</c> without going through Options or Lookup.
/// </summary>
/// <param name="Scope">Reserved variable scope for the target PO (<c>"session"</c> for
/// <c>@session.X</c>); <c>null</c> means the top of the navigation stack.</param>
public sealed record SetStmt(string? Handle, string Attribute, Expression Value, ReferenceHintKind? Hint, SourceLocation Location, string? Scope = null) : Statement(Location);

/// <summary><c>ACTION Approve [(Param=Value, ...)]</c>.</summary>
public sealed record ActionStmt(string? Handle, string ActionName, IReadOnlyDictionary<string, Expression>? Parameters, SourceLocation Location) : Statement(Location);

/// <summary><c>SEARCH "text"</c> on the current query.</summary>
public sealed record SearchStmt(string? Handle, Expression Text, SourceLocation Location) : Statement(Location);

/// <summary><c>EXPECT &lt;subject&gt; &lt;op&gt; &lt;value&gt;</c> or <c>EXPECT &lt;subject&gt; IS [NOT] &lt;flag&gt;</c>.</summary>
public sealed record ExpectStmt(ExpectSubject Subject, ExpectOp Op, Expression? Value, SourceLocation Location) : Statement(Location);

/// <summary><c>TOOL &lt;name&gt; [k=v, …] [-&gt; @var]</c> — calls a host-registered tool with
/// named arguments and optionally binds its return value to a script variable.
/// Tools are registered on <see cref="VidyanoScriptOptions.Tools"/>.</summary>
public sealed record ToolCallStmt(string Name, IReadOnlyDictionary<string, Expression> Args, string? ResultVariable, SourceLocation Location) : Statement(Location);

// --- EXPECT subject + operator ----------------------------------------------------------------

/// <summary>Categories of things EXPECT can target.</summary>
public enum ExpectSubjectKind
{
    /// <summary>Bare name treated as an attribute on the current PO: <c>EXPECT Status = "Approved"</c>.</summary>
    Attribute,
    /// <summary>Action availability: <c>EXPECT Action Approve IS AVAILABLE</c>.</summary>
    Action,
    /// <summary>Attribute flag: <c>EXPECT Attribute Name IS VISIBLE</c> / <c>IS READONLY</c> / <c>IS REQUIRED</c>.</summary>
    AttributeFlag,
    /// <summary><c>EXPECT Notification ...</c> — value or <c>IS NULL</c>.</summary>
    Notification,
    /// <summary><c>EXPECT Notification.Type ...</c>.</summary>
    NotificationType,
    /// <summary><c>EXPECT IsDirty ...</c>.</summary>
    IsDirty,
    /// <summary><c>EXPECT IsInEdit ...</c>.</summary>
    IsInEdit,
    /// <summary><c>EXPECT TotalItems ...</c> on the current query.</summary>
    TotalItems,
    /// <summary><c>EXPECT ClientOperation Refresh "X"</c> — check operations from the previous verb's response.
    /// <see cref="ExpectSubject.Name"/> holds the operation type (Refresh / ShowNotification / Navigate / …).</summary>
    ClientOperation,
    /// <summary><c>EXPECT NavStack.Depth = N</c> — number of frames on the navigation stack.</summary>
    NavStackDepth,
    /// <summary><c>EXPECT NavStack.Top.Kind = "Query" | "PersistentObject"</c> — what kind of frame is on top
    /// (or <c>IS NULL</c> when the stack is empty).</summary>
    NavStackTopKind,
    /// <summary><c>EXPECT NavStack.Top.Name = "Orders"</c> — the friendly name of the top frame (Query.Name
    /// or PersistentObject.Type), or <c>IS NULL</c> when the stack is empty.</summary>
    NavStackTopName,
    /// <summary><c>EXPECT NavStack.Top.IsDialog = true</c> — whether the top frame is a modal dialog
    /// (PO with <see cref="Vidyano.ViewModel.StateBehavior.OpenAsDialog"/>, including nested dialogs
    /// cascading from a New PO).</summary>
    NavStackTopIsDialog,
    /// <summary><c>EXPECT Attribute X LABEL = "..."</c> — the server-localized label of an attribute.</summary>
    AttributeLabel,
    /// <summary><c>EXPECT Action X DISPLAY-NAME = "..."</c> — the server-localized display name of an action.</summary>
    ActionDisplayName,
    /// <summary><c>EXPECT Query LABEL = "..."</c> — the server-localized label of the current query.</summary>
    QueryLabel,
    /// <summary><c>EXPECT {{expr}} ...</c> — compare an interpolation result (variable or
    /// <c>Messages.X</c> lookup) against a value. Stored expression is on <see cref="ExpectSubject.Lhs"/>.</summary>
    Expression,
    /// <summary><c>EXPECT Attribute X TYPE = "String"</c>.</summary>
    AttributeType,
    /// <summary><c>EXPECT Attribute X TYPEHINT key = "value"</c> — looks up a single TypeHint entry.
    /// <see cref="ExpectSubject.MetadataKey"/> carries the typehint key.</summary>
    AttributeTypeHint,
    /// <summary><c>EXPECT Attribute X TAG = "..."</c>.</summary>
    AttributeTag,
    /// <summary><c>EXPECT PO.&lt;prop&gt;</c> for scalar properties on the current PO:
    /// Type, Tag, Breadcrumb, FullTypeName, IsNew, IsHidden.
    /// <see cref="ExpectSubject.Name"/> holds the property name.</summary>
    PoProperty,
    /// <summary><c>EXPECT PO.Metadata.&lt;key&gt; = "..."</c>. <see cref="ExpectSubject.MetadataKey"/>
    /// holds the key.</summary>
    PoMetadata,
    /// <summary><c>EXPECT PO.NavigationHints.&lt;key&gt; = "..."</c>. <see cref="ExpectSubject.MetadataKey"/>
    /// holds the key.</summary>
    PoNavigationHints,
    /// <summary><c>EXPECT Query.&lt;prop&gt;</c> for scalar properties on the current Query:
    /// Name, Label, Tag, HasSearched, TextSearch.
    /// <see cref="ExpectSubject.Name"/> holds the property name.</summary>
    QueryProperty,
    /// <summary><c>EXPECT Query.Metadata.&lt;key&gt; = "..."</c>.</summary>
    QueryMetadata,
    /// <summary><c>EXPECT Query.NavigationHints.&lt;key&gt; = "..."</c>.</summary>
    QueryNavigationHints,
    /// <summary><c>EXPECT Query.PersistentObject.&lt;prop&gt; = "..."</c> — Type / Tag.
    /// <see cref="ExpectSubject.Name"/> holds the property name.</summary>
    QueryPoProperty,
    /// <summary><c>EXPECT Query.Columns[name].&lt;prop&gt; = "..."</c> — Label / Type / Offset.
    /// <see cref="ExpectSubject.Name"/> holds the column name, <see cref="ExpectSubject.MetadataKey"/> the leaf property name.</summary>
    QueryColumn,
    /// <summary><c>EXPECT @initial IS NULL</c> — the reserved scope PO itself (no attribute).
    /// Used to assert presence/absence of the scoped PO (<see cref="Vidyano.Client.Initial"/>
    /// for <c>@initial</c>, <see cref="Vidyano.Client.Session"/> for <c>@session</c>) without
    /// reaching into an attribute. <see cref="ExpectSubject.Scope"/> identifies which scope.</summary>
    ScopedRoot,
}

/// <summary>A parsed <c>EXPECT</c> subject. <see cref="Name"/> is meaningful for Attribute/Action/AttributeFlag
/// (and for <see cref="ExpectSubjectKind.PoProperty"/>/<see cref="ExpectSubjectKind.QueryProperty"/>/<see cref="ExpectSubjectKind.QueryColumn"/>
/// where it holds the property or column name). <see cref="Lhs"/> is set for
/// <see cref="ExpectSubjectKind.Expression"/> (interpolation-as-subject). <see cref="Scope"/> is the
/// reserved variable scope (<c>"session"</c>) when the subject is <c>@session.X</c>; <c>null</c>
/// means the top of the navigation stack. <see cref="MetadataKey"/> carries the bag key for
/// metadata / navigation-hint / typehint forms, and the leaf-property name for column lookups.</summary>
public sealed record ExpectSubject(ExpectSubjectKind Kind, string? Name, AttributeFlagKind Flag, SourceLocation Location, Expression? Lhs = null, string? Scope = null, string? MetadataKey = null);

/// <summary>Which boolean attribute property an <c>EXPECT Attribute X IS ...</c> targets.</summary>
public enum AttributeFlagKind { None, Visible, ReadOnly, Required }

/// <summary>EXPECT comparison operators. <see cref="Is"/>/<see cref="IsNot"/> drive boolean assertions like IS AVAILABLE.
/// <see cref="Contains"/>/<see cref="NotContains"/> do case-insensitive substring matching against the subject's string form.</summary>
public enum ExpectOp { Eq, NotEq, Lt, LtEq, Gt, GtEq, Is, IsNot, IsNull, IsNotNull, Contains, NotContains }

// --- Expressions --------------------------------------------------------------------------------

/// <summary>Base type for expressions used as values in statements (RHS of SET, EXPECT, SEARCH, parameters).</summary>
public abstract record Expression(SourceLocation Location);

/// <summary>A parsed literal (string, long, decimal, bool, or null).</summary>
public sealed record LiteralExpr(object? Value, SourceLocation Location) : Expression(Location);

/// <summary>An identifier used as a value (rare — bare action names show up here).</summary>
public sealed record IdentifierExpr(string Name, SourceLocation Location) : Expression(Location);

/// <summary><c>{{...}}</c> — a variable interpolation. <see cref="Inner"/> is the raw text between braces.</summary>
public sealed record InterpExpr(string Inner, SourceLocation Location) : Expression(Location);

/// <summary><c>@scope.AttributeName</c> in value position — read an attribute from a reserved
/// scoped PO (<c>@session</c>). The interpreter dispatches to
/// <see cref="VidyanoSession.GetScopedAttributeValue"/>.</summary>
public sealed record VariableAttributeExpr(string Scope, string AttributeName, SourceLocation Location) : Expression(Location);
