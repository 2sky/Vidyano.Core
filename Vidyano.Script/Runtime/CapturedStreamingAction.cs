using System.Collections.Generic;
using Vidyano.ViewModel;

namespace Vidyano.Script.Runtime;

/// <summary>
/// What a streaming action (the server's <c>AsyncStreamingAction</c>) sent while the previous <c>ACTION</c> ran it:
/// its progress messages in order and the notification of its dialog — what the web client shows in its streaming
/// action dialog. Its client operations land in <see cref="VidyanoSession.LastOperations"/> like any other.
/// </summary>
public sealed class CapturedStreamingAction
{
    private readonly List<string> messages = new();

    internal CapturedStreamingAction(string action) => Action = action;

    public string Action { get; }

    public IReadOnlyList<string> Messages => messages;

    /// <summary>The latest notification a dialog update set (an update without one keeps it, as the web dialog does),
    /// or <c>null</c>.</summary>
    public string? Notification { get; private set; }

    public NotificationType? NotificationType { get; private set; }

    internal void AddMessage(string? message) => messages.Add(message ?? "");

    internal void ApplyDialog(StreamingActionDialog dialog)
    {
        if (dialog.Notification is null)
            return;

        Notification = dialog.Notification;
        NotificationType = dialog.NotificationType;
    }
}
