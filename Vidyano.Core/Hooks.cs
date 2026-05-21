using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Vidyano.ViewModel;
using Vidyano.ViewModel.Actions;

namespace Vidyano
{
    public class Hooks
    {
        public Hooks()
            : this("Windows")
        {
        }

        public Hooks(string environment)
        {
            Environment = environment;
        }

        protected internal string Environment { get; set; }

        internal virtual Task<object> UserPictureFromUrl(string url)
        {
            return Task.FromResult(default(object));
        }

        public virtual Task ShowNotification(string notification, NotificationType notificationType)
        {
            return Task.CompletedTask;
        }

        protected internal virtual Task OnAction(ExecuteActionArgs e)
        {
            return Task.CompletedTask;
        }

        protected internal virtual void OnSessionUpdated(PersistentObject session)
        {
        }

        internal virtual PersistentObject OnConstruct(Client client, JObject model)
        {
            return new PersistentObject(client, model);
        }

        internal virtual Query OnConstruct(Client client, JObject model, PersistentObject parent, bool asLookup)
        {
            return new Query(client, model, parent, asLookup);
        }

        protected internal virtual void OnConstruct(PersistentObject po)
        {
        }

        protected internal virtual void OnConstruct(Query query)
        {
        }

        protected internal virtual void OnStream(string name, Stream stream)
        {
        }

        internal virtual void OnOpen(PersistentObject po)
        {
        }

        internal virtual void OnOpen(Query query)
        {
        }

        protected internal virtual Task OnLoadProgramUnitItems(IList<ProgramUnitItem> items)
        {
            return Task.CompletedTask;

        }

        /// <summary>
        /// Called once per <see cref="ClientOperation"/> dequeued from a server response. Override to
        /// react to server-driven refresh / navigate / message-box requests, mirroring the v4
        /// frontend's <c>onClientOperation</c>. The default implementation is a no-op — like the v4
        /// frontend, operations aren't retained by the client; record them here if you need history.
        /// </summary>
        protected internal virtual void OnClientOperation(ClientOperation operation)
        {
        }

        /// <summary>
        /// Called by <see cref="Client.EnsureInitialSatisfiedAsync"/> when the server returned an
        /// Initial <see cref="PersistentObject"/> that gates the application (license terms, forced
        /// two-factor enrolment, forced password reset, …). Override to render the PO and drive it
        /// to completion — typically by executing its <c>Save</c> action. Return a <see cref="Task"/>
        /// that completes once the gate has been resolved; <see cref="Client.EnsureInitialSatisfiedAsync"/>
        /// then waits for the server's <c>reloadPage</c> ClientOperation and re-establishes the
        /// session. The default implementation is a no-op, which leaves <see cref="Client.Initial"/>
        /// untouched and causes <see cref="Client.EnsureInitialSatisfiedAsync"/> to hang on the
        /// <c>reloadPage</c> wait — override it whenever you opt into the gate flow.
        /// </summary>
        protected internal virtual Task OnInitialRequired(PersistentObject initial)
        {
            return Task.CompletedTask;
        }

        internal virtual object ByteArrayToImageSource(MemoryStream memoryStream)
        {
            return null;
        }

        internal virtual Task SignOut()
        {
            return Task.CompletedTask;

        }

        internal virtual async Task OnActionCommand(ActionBase action, object obj)
        {
            await action.Execute(null).ConfigureAwait(false);
        }

        internal string UniqueId { get; set; }
        internal virtual string GetSignedTimeStamp()
        {
            return null;
        }

        protected internal virtual Task OnInitialized()
        {
            return Task.CompletedTask;

        }

        protected internal virtual void OnCreateData(JObject data)
        {
        }

        protected internal virtual Task<string> OnRetryAction(string title, string message, string[] options, PersistentObject persistentObject)
        {
            return Task.FromResult("-1");
        }
    }
}