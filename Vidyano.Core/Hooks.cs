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

        protected internal virtual Task<int> OnRetryAction(string title, string message, string[] options, PersistentObject persistentObject)
        {
            return Task.FromResult(-1);
        }
    }
}