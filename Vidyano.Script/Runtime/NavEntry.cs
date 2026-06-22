using Vidyano.ViewModel;

namespace Vidyano.Script.Runtime;

/// <summary>
/// One frame in the session's navigation stack — either a <see cref="PoEntry"/> or a
/// <see cref="QueryEntry"/>. Browser-style: OPEN pushes a frame, SAVE/CANCEL pops one.
/// </summary>
/// <remarks>
/// Frames carry a <see cref="IsDialog"/> flag to model the web client's modal-dialog stack:
/// a PO with <see cref="StateBehavior.OpenAsDialog"/> opens on top of the current page rather than
/// replacing it, and nested dialogs (a New PO opening a reference, then another) keep stacking.
/// The pop/refresh rules are the same for both — the flag is exposed for assertions.
/// </remarks>
public abstract record NavEntry
{
    /// <summary>Canonical kind name used in <c>EXPECT NavStack.Top.Kind</c> ("PersistentObject" or "Query").</summary>
    public abstract string Kind { get; }

    /// <summary>Human-friendly identifier used in <c>EXPECT NavStack.Top.Name</c>
    /// (the PO's <c>Type</c> or the Query's <c>Name</c>).</summary>
    public abstract string Name { get; }

    /// <summary>Whether this frame represents a modal dialog overlaying the page below it.</summary>
    public abstract bool IsDialog { get; }
}

/// <summary>A persistent-object frame on the navigation stack.</summary>
public sealed record PoEntry(PersistentObject Po, bool IsDialog = false) : NavEntry
{
    public override string Kind => "PersistentObject";
    public override string Name => Po.Type;
    public override bool IsDialog { get; } = IsDialog;
}

/// <summary>A query frame on the navigation stack.</summary>
public sealed record QueryEntry(Query Query, bool IsDialog = false) : NavEntry
{
    public override string Kind => "Query";
    public override string Name => Query.Name;
    public override bool IsDialog { get; } = IsDialog;
}

/// <summary>A modal frame for a server-driven retry request (<see cref="PendingRetry"/>), pushed while an
/// action is paused awaiting confirmation/input and popped by <c>CONFIRM</c>. Always a dialog — it
/// overlays whatever PO/Query was current when the action ran. <see cref="Name"/> is the retry title, so
/// <c>EXPECT NavStack.Top.Kind = "RetryDialog"</c> / <c>NavStack.Top.Name</c> read it like any other frame.</summary>
public sealed record RetryEntry(PendingRetry Retry) : NavEntry
{
    public override string Kind => "RetryDialog";
    public override string Name => Retry.Title;
    public override bool IsDialog => true;
}

/// <summary>A modal frame for an open Add-Reference picker, pushed when an <c>ACTION</c>'s server result is a
/// <c>Vidyano.AddReference</c> wrapper PO and popped by <c>ADD-REFERENCE</c> (confirm) or <c>GO-BACK</c>
/// (dismiss). Always a dialog — it overlays the PO/Query the action ran on. <see cref="Picker"/> is the
/// reference picker query (reparented to <see cref="Parent"/> so its rows load against the right context);
/// query-family verbs (SEARCH / SELECT-ROWS / EXPECT TotalItems) target it while it is on top.
/// <see cref="Parent"/> is the PO the originating action ran on — the <c>parent</c> the confirming
/// <c>Query.AddReference</c> post carries — and <see cref="AddActionName"/> is that action's name, sent as the
/// <c>AddAction</c> parameter so the server routes to the right <c>OnAddReference</c> override. Kind is
/// <c>"AddReferenceDialog"</c>, so <c>EXPECT NavStack.Top.Kind = "AddReferenceDialog"</c> reads it.</summary>
public sealed record AddReferenceEntry(Query Picker, PersistentObject? Parent, string AddActionName) : NavEntry
{
    public override string Kind => "AddReferenceDialog";
    public override string Name => Picker.Name;
    public override bool IsDialog => true;
}
