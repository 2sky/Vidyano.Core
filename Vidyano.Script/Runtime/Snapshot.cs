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
    PoSnapshot? SessionPo = null);

/// <summary>Identifying information about the active sign-in.</summary>
public sealed record SessionSnapshot(string? User, string? Uri);

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
    IReadOnlyList<ActionSnapshot> Actions);

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

/// <summary>Compact view of the active query, including a small head of rows.</summary>
public sealed record QuerySnapshot(
    string Name,
    string? TextSearch,
    int TotalItems,
    int Count,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
