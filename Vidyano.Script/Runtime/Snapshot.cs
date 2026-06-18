using System.Collections.Generic;

namespace Vidyano.Script.Runtime;

/// <summary>
/// A point-in-time, serializable view of the session state. The single most important contract for
/// agents — every decision they make is based on this shape. Keep it small and stable.
/// </summary>
public sealed record Snapshot(
    SessionSnapshot? Session,
    PoSnapshot? Po,
    QuerySnapshot? Query,
    IReadOnlyDictionary<string, string>? Handles,
    PoSnapshot? SessionPo = null,
    IReadOnlyList<NavFrameSnapshot>? NavStack = null);

/// <summary>Identifying information about the active sign-in.</summary>
public sealed record SessionSnapshot(string? User, string? Uri);

/// <summary>One frame of the navigation stack, oldest first. <see cref="Name"/> is the Query name or
/// PO type; <see cref="Kind"/> is "Query" or "PersistentObject". Mirrors what
/// <c>EXPECT NavStack.Top.*</c> reads, so the whole stack is visible to agents, not just the top.</summary>
public sealed record NavFrameSnapshot(string Kind, string Name, bool IsDialog);

/// <summary>Compact view of the top-of-stack PersistentObject.</summary>
public sealed record PoSnapshot(
    string Type,
    string? ObjectId,
    bool IsInEdit,
    bool IsDirty,
    bool IsNew,
    string? Notification,
    string? NotificationType,
    IReadOnlyList<AttributeSnapshot> Attributes,
    IReadOnlyList<ActionSnapshot> Actions,
    IReadOnlyList<string>? DetailQueries = null);

/// <summary>Compact view of one attribute, only the fields the UI/script can reason about.</summary>
public sealed record AttributeSnapshot(
    string Name,
    string Type,
    string? Value,
    string? DisplayValue,
    bool IsVisible,
    bool IsReadOnly,
    bool IsRequired,
    bool IsValueChanged,
    string? ValidationError);

/// <summary>Compact view of one action, only the fields the UI/script can reason about.</summary>
public sealed record ActionSnapshot(string Name, bool CanExecute, bool IsVisible);

/// <summary>Compact view of the active query, including a small head of rows. <see cref="SelectedCount"/>
/// is the literal selected-row count (<c>Query.SelectedItems.Count</c>); <see cref="AllSelected"/> is the
/// server-side select-all flag — when set, <see cref="SelectedCount"/> reports only the exclusion set, not
/// the full match, mirroring <c>EXPECT Selection.*</c>.</summary>
public sealed record QuerySnapshot(
    string Name,
    string? TextSearch,
    int TotalItems,
    int Count,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    IReadOnlyList<ActionSnapshot>? Actions = null,
    int SelectedCount = 0,
    bool AllSelected = false,
    string? Notification = null,
    string? NotificationType = null);
