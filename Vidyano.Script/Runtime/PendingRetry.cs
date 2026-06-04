using System.Collections.Generic;
using Vidyano.ViewModel;

namespace Vidyano.Script.Runtime;

/// <summary>
/// A server-driven retry request, surfaced to the script as a modal dialog frame (the web client's
/// <c>onRetryAction</c> equivalent). The server raises one when an action handler calls
/// <c>Manager.RetryAction(...)</c> in the middle of an <c>ExecuteAction</c> call — it wants the user to
/// confirm, or to supply more information, before the action continues. Vidyano.Core's
/// <c>Client.ExecuteActionAsync</c> loop already round-trips it; this record is what the script sees
/// while it is open.
/// </summary>
/// <remarks>
/// <see cref="Options"/> are the buttons the server offered (e.g. <c>["Yes","No"]</c>); <c>CONFIRM</c>
/// answers with one of them by label or index. <see cref="Po"/> is the optional "retry persistent
/// object" — present when the server wants extra input — which the script may <c>SET</c> attributes on
/// before confirming; the edits ride back to the server via <c>retryPersistentObject</c>. <see cref="Po"/>
/// is <c>null</c> for a plain message/confirmation retry, in which case there is nothing to <c>SET</c>.
/// </remarks>
public sealed record PendingRetry(string Title, string? Message, IReadOnlyList<string> Options, PersistentObject? Po);
