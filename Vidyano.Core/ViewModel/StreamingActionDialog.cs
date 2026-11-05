using System;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    /// <summary>
    /// A <c>dialog</c> update a streaming action (the server's <c>AsyncStreamingAction</c>) sends while it runs —
    /// its <c>StreamingDialogOptions</c>: the dialog's title, colours and size, and the notification the action ends with.
    /// </summary>
    public sealed class StreamingActionDialog
    {
        internal StreamingActionDialog(JObject raw)
        {
            Raw = raw ?? new JObject();
        }

        /// <summary>The raw dialog options, for an option the typed members don't cover.</summary>
        public JObject Raw { get; }

        public string Title => (string)Raw["title"];

        /// <summary>The notification text, or <c>null</c> when this update sets none.</summary>
        public string Notification => string.IsNullOrEmpty((string)Raw["notification"]) ? null : (string)Raw["notification"];

        /// <summary>The notification's type. Defaults to <see cref="NotificationType.Error"/> when the server sends
        /// none, as the server and the web client do.</summary>
        public NotificationType NotificationType =>
            Enum.TryParse((string)Raw["notificationType"], true, out NotificationType type) ? type : NotificationType.Error;
    }
}
