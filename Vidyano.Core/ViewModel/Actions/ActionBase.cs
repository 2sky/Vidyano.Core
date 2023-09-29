using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using Vidyano.Commands;
using Vidyano.Common;

namespace Vidyano.ViewModel.Actions
{
    public class ActionBase : NotifyableBase
    {
        private static readonly Dictionary<string, ConstructorInfo> actionConstructors = new Dictionary<string, ConstructorInfo>();

        protected internal readonly Definition definition;
        private bool _CanExecute, _IsVisible = true;
        protected readonly Client client;

        protected internal ActionBase(Definition definition, PersistentObject parent, Query query = null)
        {
            this.definition = definition;
            Parent = parent;
            Query = query;

            Options = definition.Options;

            client = ((ViewModelBase)query ?? parent).Client;
            Command = new ActionCommand(async obj => await client.Hooks.OnActionCommand(this, obj).ConfigureAwait(false), _ => CanExecute, this, "CanExecute");

            CanExecute = query == null;
        }

        public string DisplayName => definition.DisplayName;

        public bool IsPinned => definition.IsPinned;

        public string[] Options { get; protected set; }

        internal int Offset { get; private set; }

        internal bool IsDependent { get; private set; }

        public string Icon => "ActionIcon." + definition.Name;

        public string Name => definition.Name;

        public PersistentObject Parent { get; }

        public Query Query { get; }

        public bool IsVisible
        {
            get => _IsVisible;
            set => SetProperty(ref _IsVisible, value);
        }

        public bool CanExecute
        {
            get => _CanExecute;
            set => SetProperty(ref _CanExecute, value);
        }

        public bool HasSelectionRule => definition.SelectionRule != ExpressionParser.AlwaysTrue;

        internal virtual Definition[] DependentActions => new Definition[0];

        public ICommand Command { get; }
        internal virtual void Initialize() {}

        public virtual async Task<PersistentObject> Execute(object option)
        {
            var index = Array.IndexOf(Options, Convert.ToString(option));
            var parameters = new Dictionary<string, string> { { "MenuOption", Client.ToServiceString(index) } };
            parameters["MenuLabel"] = Client.ToServiceString(option);

            var selectedItems = Query != null && Query.Count > 0 ? Query.SelectedItems.ToArray() : new QueryResultItem[0];
            var po = await client.ExecuteActionAsync((this is QueryAction ? "Query" : "PersistentObject") + "." + definition.Name, Parent, Query, selectedItems, parameters).ConfigureAwait(false);

            if (po != null)
            {
                if (po.FullTypeName == "Vidyano.Notification")
                {
                    if (Query != null)
                        Query.SetNotification(po.Notification, po.NotificationType);
                    else if (Parent != null)
                        Parent.SetNotification(po.Notification, po.NotificationType);
                }
                else if (po.HasNotification && po.NotificationType == NotificationType.Error)
                {
                    if (Query != null)
                        Query.SetNotification(po.Notification, po.NotificationType);
                    else if (Parent != null)
                    {
                        Parent.SetNotification(po.Notification, po.NotificationType);

                        if ((po.FullTypeName == Parent.FullTypeName || po.IsNew == Parent.IsNew) && po.Id == Parent.Id && po.ObjectId == Parent.ObjectId)
                            await Parent.RefreshFromResult(po).ConfigureAwait(false);
                    }
                }
                else if (po.FullTypeName == "Vidyano.RegisteredStream")
                {
                    try
                    {
                        var stream = await client.GetStreamAsync(po).ConfigureAwait(false);
                        if (stream != null && stream.Item1 != null)
                        {
                            try
                            {
                                client.Hooks.OnStream(stream.Item2, stream.Item1);
                            }
                            finally
                            {
                                stream.Item1.Dispose();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (this is QueryAction qAction)
                            qAction.Query.SetNotification(e.Message);
                        else if (Parent != null)
                            Parent.SetNotification(e.Message);
                    }
                }
                else if (Parent == null || (po.FullTypeName != Parent.FullTypeName && po.IsNew != Parent.IsNew) || po.Id != Parent.Id || po.ObjectId != Parent.ObjectId)
                {
                    po.OwnerQuery = Query;
                    client.Hooks.OnOpen(po);
                }
                else
                {
                    Parent.SetNotification(po.Notification, po.NotificationType);
                    await Parent.RefreshFromResult(po).ConfigureAwait(false);
                }
            }

            if (definition.RefreshQueryOnCompleted && Query != null && !Query.HasNotification)
            {
                await Query.RefreshQueryAsync().ConfigureAwait(false);

                if (Query.SemanticZoomOwner != null)
                    await Query.SemanticZoomOwner.RefreshQueryAsync().ConfigureAwait(false);
            }

            return po;
        }

        internal static ActionBase[] GetActions(Client client, JToken actionsToken, PersistentObject parent, Query query = null)
        {
            var actions = new List<ActionBase>();
            var actionDefinitions = actionsToken.Select(action =>
            {
                var actionName = (string)action;
                if (actionName == "Edit" && parent != null && parent.IsNew)
                    actionName = "Save";

                client.Actions.TryGetValue(actionName, out var definition);
                return definition;
            }).Where(a => a != null).OrderBy(a => a.Offset).ToArray();

            foreach (var actionDefinition in actionDefinitions)
            {
                var action = GetAction(client, actionDefinition, parent, query);
                if (action != null)
                {
                    var parentOffset = action.Offset = actions.Count;
                    actions.Add(action);
                    foreach (var dependentActionDefinition in action.DependentActions)
                    {
                        action = GetAction(client, dependentActionDefinition, parent, query);
                        if (action != null)
                        {
                            action.Offset = parentOffset;
                            action.IsDependent = true;
                            actions.Add(action);
                        }
                    }
                }
            }

            return actions.ToArray();
        }

        internal static ActionBase GetAction(Client client, Definition definition, PersistentObject parent, Query query = null)
        {
            var constructor = actionConstructors.GetOrAdd(definition.Name + ";" + (query == null), n =>
            {
                var hooksType = client.Hooks.GetType();
                var actionType = hooksType.GetTypeInfo().Assembly.GetType("Vidyano.ViewModel.Actions." + definition.Name);
                while (actionType == null && hooksType != typeof(object))
                {
                    hooksType = hooksType.GetTypeInfo().BaseType;
                    actionType = hooksType.GetTypeInfo().Assembly.GetType("Vidyano.ViewModel.Actions." + definition.Name);
                }

                return (actionType ?? (query == null ? typeof(ActionBase) : typeof(QueryAction))).GetTypeInfo().DeclaredConstructors.First(c => !c.IsStatic);
            });

            return constructor.Invoke(new object[] { definition, parent, query }) as ActionBase;
        }

        protected internal class Definition
        {
            public string Name { get; set; }

            public string DisplayName { get; set; }

            public bool IsPinned { get; set; }

            public bool RefreshQueryOnCompleted { get; set; }

            public int Offset { get; set; }

            public string[] Options { get; set; }

            public Func<int, bool> SelectionRule { get; set; }
        }
    }
}