using System;
using Newtonsoft.Json.Linq;
using Vidyano.Commands;

namespace Vidyano.ViewModel
{
    public abstract class ProgramUnitItem : ViewModelBase
    {
        internal ProgramUnitItem(Client client, JObject model)
            : base(client, model)
        {
            var jGroup = model["group"];
            if (jGroup != null)
            {
                GroupId = (string)jGroup["id"];
                GroupTitle = (string)jGroup["title"];
            }
        }

        protected ProgramUnitItem(Client client, string title, string name, string groupId, string groupTitle)
            : this(client, new JObject(new JProperty("title", title), new JProperty("name", name), new JProperty("group", new JObject(new JProperty("id", groupId), new JProperty("title", groupTitle)))))
        {
        }

        public string Title => GetProperty<string>();

        internal string Name => GetProperty<string>();

        public string GroupId { get; private set; }

        public string GroupTitle { get; private set; }

        public virtual int RowSpan => 1;

        public virtual int ColumnSpan => 1;

        protected internal virtual bool IncludeInZoomedOutItemCount => true;

        public int ColumnOffset => 1;

        protected internal abstract string Template { get; }

        internal static ProgramUnitItem Create(Client client, JObject model)
        {
            if (model["query"] != null)
                return new ProgramUnitItemQuery(client, model);
            if (model["persistentObject"] != null)
                return new ProgramUnitItemPersistentObject(client, model);

            return null;
        }

        protected internal virtual void Open() { }
    }

    public class ProgramUnitItemQuery : ProgramUnitItem
    {
        internal ProgramUnitItemQuery(Client client, JObject model)
            : base(client, model)
        { }

        public string QueryName => GetProperty<string>();

        public string Query => GetProperty<string>();

        public string Count => GetProperty<string>();

        protected internal override string Template => "ProgramUnitItemTemplate.Query";

        protected internal override async void Open()
        {
            string err = null;

            try
            {
                await new Navigate().Execute(Client.GetQueryAsync(Query)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }

            if (!string.IsNullOrEmpty(err))
                await Client.Hooks.ShowNotification(err, NotificationType.Error).ConfigureAwait(false);
        }
    }

    public class ProgramUnitItemPersistentObject : ProgramUnitItem
    {
        internal ProgramUnitItemPersistentObject(Client client, JObject model)
            : base(client, model)
        { }

        public string PersistentObject => GetProperty<string>();

        public string PersistentObjectType => GetProperty<string>();

        public string ObjectId => GetProperty<string>();

        protected internal override string Template => "ProgramUnitItemTemplate.PersistentObject";

        protected internal override async void Open()
        {
            string err = null;

            try
            {
                await new Navigate().Execute(Client.GetPersistentObjectAsync(PersistentObject, ObjectId)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }

            if (!string.IsNullOrEmpty(err))
                await Client.Hooks.ShowNotification(err, NotificationType.Error).ConfigureAwait(false);
        }
    }
}