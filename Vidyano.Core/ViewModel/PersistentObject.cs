﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vidyano.Common;
using Vidyano.ViewModel.Actions;

namespace Vidyano.ViewModel
{
    [DebuggerDisplay("PersistentObject {Type}")]
    public class PersistentObject : ViewModelBase
    {
        internal PersistentObject(Client client, JObject model)
            : base(client, model)
        {
            if (model.TryGetValue("attributes", out var attributesToken))
            {
                var attributes = (JArray)attributesToken;
                Attributes = attributes.Select(jAttr =>
                {
                    if (jAttr["lookup"] != null)
                        return new PersistentObjectAttributeWithReference(client, (JObject)jAttr, this);

                    if (jAttr["details"] != null)
                        return new PersistentObjectAttributeAsDetail(client, (JObject)jAttr, this);

                    return new PersistentObjectAttribute(client, (JObject)jAttr, this);
                }).ToArray();
            }
            else
                Attributes = Array.Empty<PersistentObjectAttribute>();

            if (model.TryGetValue("queries", out var queriesToken))
            {
                var queries = (JArray)queriesToken;
                Queries = new KeyValueList<string, Query>(queries.Select(jQuery /* :-) */ => client.Hooks.OnConstruct(client, (JObject)jQuery, this, false)).ToDictionary(q => q.Name, q => q));
            }
            else
                Queries = new KeyValueList<string, Query>(new Dictionary<string, Query>());

            var parent = (JObject)model["parent"];
            if (parent != null)
                Parent = client.Hooks.OnConstruct(client, parent);

            // Initialize Tabs and Groups
            var tabIndex = 0;
            var attributeTabs = !IsHidden ? Attributes.OrderBy(attr => attr.Offset).GroupBy(attr => attr.Tab).Select(tab =>
            {
                var groups = tab.OrderBy(attr => attr.Offset).GroupBy(attr => attr.GroupName).Select(group => new PersistentObjectAttributeGroup(client, group.Key, group.ToArray())).ToArray();
                if (groups.Length == 1)
                    groups[0].IsNameVisible = false;

                var t = (PersistentObjectTab)CreateAttributesTab(groups.SelectMany(g => g.Attributes).ToArray(), string.IsNullOrEmpty(tab.Key) ? Label : tab.Key, this);
                t.Index = tabIndex++;
                return t;
            }) : Array.Empty<PersistentObjectTabAttributes>();

            Tabs = attributeTabs.Concat(Queries.OrderBy(q => q.Value.Offset).Select(q => CreateQueryTab(q.Value))).ToList();

            if (!IsHidden)
            {
                // Initialize Action
                if (model.TryGetValue("actions", out var actionsToken))
                {
                    var actions = ActionBase.GetActions(client, actionsToken, this);

                    Actions = actions.Where(a => !a.IsPinned).ToArray();
                    PinnedActions = actions.Where(a => a.IsPinned).ToArray();

                    Actions.Run(a => a.Initialize());
                    PinnedActions.Run(a => a.Initialize());
                }
                else
                    Actions = PinnedActions = Array.Empty<ActionBase>();
            }
            else
                Actions = PinnedActions = Array.Empty<ActionBase>();

            // Also check IsInEdit (Object could have been reconstructed after suspend/resume)
            IsInEdit = IsInEdit || IsNew || StateBehavior.HasFlag(StateBehavior.OpenInEdit) || StateBehavior.HasFlag(StateBehavior.StayInEdit);
            IsDirty = IsDirty; // Also triggers reconstructed changes

            // Specials
            HasNotification = !string.IsNullOrWhiteSpace(Notification);

            client.Hooks.OnConstruct(this);

            Tabs.Select((tab, n) => tab.Index = n).Run();
        }

        public string Id
        {
            get { return GetProperty<string>(); }
        }

        public string ObjectId
        {
            get { return GetProperty<string>(); }
            private set { SetProperty(value); }
        }

        public string Breadcrumb
        {
            get { return GetProperty<string>(); }
            set { SetProperty(value); }
        }

        public bool IsHidden
        {
            get { return GetProperty<bool>(); }
        }

        public bool IsNew
        {
            get { return GetProperty<bool>(); }
            private set { SetProperty(value); }
        }

        public string Label
        {
            get { return GetProperty<string>(); }
            set { SetProperty(value); }
        }

        public string NewOptions
        {
            get { return GetProperty<string>(); }
        }

        public string Notification
        {
            get { return GetProperty<string>(); }
            private set
            {
                if (SetProperty(value))
                    HasNotification = !string.IsNullOrWhiteSpace(value);
            }
        }

        public NotificationType NotificationType
        {
            get { return (NotificationType)Enum.Parse(typeof(NotificationType), GetProperty<string>()); }
            private set { SetProperty(value.ToString()); }
        }

        public bool HasNotification
        {
            get { return GetProperty<bool>(); }
            private set { SetProperty(value); }
        }

        public StateBehavior StateBehavior
        {
            get { return (StateBehavior)Enum.Parse(typeof(StateBehavior), GetProperty<string>()); }
        }

        public string Type
        {
            get { return GetProperty<string>(); }
        }

        public string FullTypeName
        {
            get { return GetProperty<string>(); }
        }

        public PersistentObjectAttribute[] Attributes { get; }

        public IReadOnlyDictionary<string, Query> Queries { get; }

        public PersistentObject Parent { get; internal set; }

        public Query OwnerQuery { get; internal set; }

        public PersistentObjectAttributeWithReference OwnerAttributeWithReference { get; internal set; }

        public PersistentObjectAttributeAsDetail OwnerDetailAttribute { get; internal set; }

        public PersistentObjectAttribute this[string name]
        {
            get { return GetAttribute(name); }
        }

        public List<PersistentObjectTab> Tabs { get; }

        public ActionBase[] Actions { get; }

        public ActionBase[] PinnedActions { get; }

        public bool HasActions
        {
            get { return Actions != null && Actions.Length > 0 && Actions.Any(a => a.IsVisible) || PinnedActions != null && PinnedActions.Length > 0 && PinnedActions.Any(a => a.IsVisible); }
        }

        public virtual bool IsInEdit
        {
            get { return GetProperty<bool>(); }
            internal set
            {
                if (SetProperty(value) && value)
                {
                    // Back-up
                    BackupSecurityToken = SecurityToken;
                    Attributes.Run(a => a.BackupBeforeEdit());
                }

                var cancelEdit = GetAction("CancelEdit");
                if (cancelEdit != null)
                    cancelEdit.CanExecute = value;

                var endEdit = GetAction("EndEdit");
                if (endEdit != null)
                    endEdit.IsVisible = value;

                var edit = GetAction("Edit");
                if (edit != null)
                    edit.IsVisible = !value;
            }
        }

        internal string[] QueriesToRefresh
        {
            get { return GetProperty<string[]>(); }
        }

        public bool IsDirty
        {
            get { return GetProperty<bool>(); }
            internal set
            {
                value &= IsInEdit;
                SetProperty(value);

                var endEdit = GetAction("EndEdit");
                if (endEdit != null)
                    endEdit.CanExecute = value;
            }
        }

        internal string SecurityToken
        {
            get { return GetProperty<string>(); }
            set { SetProperty(value); }
        }

        private string BackupSecurityToken
        {
            get { return GetProperty<string>(); }
            set { SetProperty(value); }
        }

        public event EventHandler<NotificationChangedEventArgs> NotificationChanged = delegate { };

        protected virtual PersistentObjectTabAttributes CreateAttributesTab(PersistentObjectAttribute[] attributes, string title, PersistentObject parent)
        {
            return new PersistentObjectTabAttributes(attributes, title, parent);
        }

        protected virtual PersistentObjectTabQuery CreateQueryTab(Query query)
        {
            return new PersistentObjectTabQuery(query);
        }

        public void CancelEdit()
        {
            if (!IsInEdit)
                return;

            SecurityToken = BackupSecurityToken;
            Attributes.Run(a => a.RestoreEditBackup());
            IsDirty = false;

            if (!StateBehavior.HasFlag(StateBehavior.StayInEdit))
                IsInEdit = false;

            Notification = null;
        }

        public void Edit()
        {
            IsInEdit = true;
        }

        public async Task Save()
        {
            try
            {
                var result = await Client.ExecuteActionAsync("PersistentObject.Save", this).ConfigureAwait(false);
                if (result == null)
                    return;

                await RefreshFromResult(result).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(Notification) || NotificationType != NotificationType.Error)
                {
                    IsDirty = false;
                    IsInEdit = StateBehavior.HasFlag(StateBehavior.StayInEdit);

                    if (OwnerAttributeWithReference != null)
                    {
                        if (OwnerAttributeWithReference.ObjectId != ObjectId)
                        {
                            OwnerAttributeWithReference.Parent.Edit();

                            var fakeSelectedItem = new JObject(new JProperty("id", ObjectId), new JProperty("values", new JArray()));
                            await OwnerAttributeWithReference.ChangeReference(new QueryResultItem(fakeSelectedItem, OwnerAttributeWithReference.Lookup)).ConfigureAwait(false);
                        }
                    }
                    else if (OwnerQuery != null)
                    {
                        await OwnerQuery.RefreshQueryAsync().ConfigureAwait(false);

                        if (OwnerQuery.SemanticZoomOwner != null)
                            await OwnerQuery.SemanticZoomOwner.RefreshQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message);
            }
        }

        public async Task RefreshFromResult(PersistentObject result)
        {
            SetNotification(result.Notification, result.NotificationType);

            if (Attributes != null && result.Attributes != null)
            {
                foreach (var attr in Attributes)
                {
                    var serviceAttribute = result.Attributes.FirstOrDefault(a => a.Id == attr.Id);
                    if (serviceAttribute != null)
                    {
                        attr.OptionsDirect = serviceAttribute.OptionsDirect;
                        attr.IsReadOnly = serviceAttribute.IsReadOnly;
                        attr.IsRequired = serviceAttribute.IsRequired;

                        if (attr.IsVisible != serviceAttribute.IsVisible)
                            attr.Visibility = serviceAttribute.Visibility;

                        attr.ValueDirect = serviceAttribute.ValueDirect;
                        var attrWithRef = attr as PersistentObjectAttributeWithReference;
                        var serviceAttrWithRef = serviceAttribute as PersistentObjectAttributeWithReference;
                        if (attrWithRef != null && serviceAttrWithRef != null)
                            attrWithRef.ObjectId = serviceAttrWithRef.ObjectId;

                        var attrAsDetail = attr as PersistentObjectAttributeAsDetail;
                        var serviceAttrAsDetail = serviceAttribute as PersistentObjectAttributeAsDetail;
                        if (attrAsDetail != null && serviceAttrAsDetail != null)
                        {
                            attrAsDetail.Objects = serviceAttrAsDetail.Objects != null
                                ? serviceAttrAsDetail.Objects.Select(obj =>
                                {
                                    obj.Parent = this;
                                    obj.OwnerDetailAttribute = attrAsDetail;
                                    obj.IsInEdit = IsInEdit;
                                    return obj;
                                }).ToArray()
                                : Array.Empty<PersistentObject>();
                        }

                        attr.TriggersRefresh = serviceAttribute.TriggersRefresh;
                        attr.IsValueChanged = serviceAttribute.IsValueChanged;
                        attr.ValidationError = serviceAttribute.ValidationError;
                    }
                }

                if (IsNew)
                {
                    ObjectId = result.ObjectId;
                    IsNew = result.IsNew;
                }

                SecurityToken = result.SecurityToken;
                IsDirty = Attributes.Any(a => a.IsValueChanged);

                if (result.Breadcrumb != null)
                    Breadcrumb = result.Breadcrumb;

                if (result.QueriesToRefresh != null && Queries != null)
                {
                    foreach (var id in result.QueriesToRefresh)
                    {
                        Query query = null;

                        if (Guid.TryParse(id, out var guid))
                            query = Queries.FirstOrDefault(q => q.Value.Id == id).Value;

                        if (query == null)
                            query = Queries[id];

                        if (query != null && query.HasSearched)
                            await query.RefreshQueryAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task RefreshAttributesAsync(PersistentObjectAttribute attribute = null)
        {
            var parameters = attribute != null ? new Dictionary<string, string> { { "RefreshedPersistentObjectAttributeId", Client.ToServiceString(attribute.Id) } } : null;
            try
            {
                var result = await Client.ExecuteActionAsync("PersistentObject.Refresh", this, null, null, parameters).ConfigureAwait(false);

                SetNotification(result.Notification, result.NotificationType);

                if (!HasNotification || NotificationType != NotificationType.Error)
                    await RefreshFromResult(result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message);
            }
        }

        public void SetNotification(string notification, NotificationType notificationType = NotificationType.Error)
        {
            NotificationType = notificationType;
            Notification = notification;

            NotificationChanged(this, new NotificationChangedEventArgs(notification, notificationType));
        }

        public ActionBase GetAction(string actionName)
        {
            return Actions.FirstOrDefault(a => a.Name == actionName) ?? PinnedActions.FirstOrDefault(a => a.Name == actionName);
        }

        public PersistentObjectAttribute GetAttribute(string attributeName)
        {
            return Attributes.FirstOrDefault(a => a.Name == attributeName);
        }

        public object GetAttributeValue(string attributeName)
        {
            var attr = GetAttribute(attributeName);
            return attr?.Value;
        }

        public Query GetQuery(string queryName)
        {
            return Queries[queryName];
        }

        public void SetAttributeValue(string attributeName, object value)
        {
            var attr = GetAttribute(attributeName);
            if (attr != null)
                attr.Value = value;
        }

        #region Service Serialization

        protected override string[] GetServiceProperties()
        {
            return new[] { "id", "type", "objectId", "isNew", "isHidden", "bulkObjectIds", "securityToken" };
        }

        internal override JObject ToServiceObject()
        {
            var jObj = base.ToServiceObject();

            if (Parent != null)
                jObj["parent"] = Parent.ToServiceObject();

            jObj["attributes"] = JArray.FromObject(Attributes.Select(attr => attr.ToServiceObject()));

            return jObj;
        }

        #endregion
    }
}