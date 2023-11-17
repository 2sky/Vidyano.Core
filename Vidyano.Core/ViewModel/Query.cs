using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vidyano.Commands;
using Vidyano.Common;
using Vidyano.ViewModel.Actions;

namespace Vidyano.ViewModel
{
    [DebuggerDisplay("Query {Name}")]
    public class Query : ViewModelBase, IReadOnlyList<QueryResultItem>, INotifyCollectionChanged
    {
        private readonly ObservableCollection<QueryResultItem> _SelectedItems = new ObservableCollection<QueryResultItem>();
        private readonly SortedDictionary<int, QueryResultItem> items = new SortedDictionary<int, QueryResultItem>();
        private readonly List<int> queriedPages = new List<int>();
        private QueryColumn[] _Columns;
        private bool _HasSearched;
        private bool _HasSelectedItems;
        private bool _HasTextSearch;
        private int _TotalItems;
        private CancellationTokenSource searchCancellationTokenSource;
        private Task<bool> searchingTask;
        private PersistentObjectTabQuery[] semanticZoomTabs;

        internal Query(Client client, JObject model, PersistentObject parent = null, bool asLookup = false)
            : base(client, model)
        {
            Parent = parent;
            AsLookup = asLookup;

            var po = (JObject)model["persistentObject"];
            if (po != null)
                PersistentObject = client.Hooks.OnConstruct(client, po);

            if (model.TryGetValue("columns", out var columnsToken))
            {
                var columns = (JArray)columnsToken;
                Columns = columns.Select(jCol => new QueryColumn((JObject)jCol, this)).ToArray();
            }
            else
                Columns = Array.Empty<QueryColumn>();

            if (model.TryGetValue("actions", out var actionsToken))
            {
                var actions = ActionBase.GetActions(client, actionsToken, parent, this);
                CanFilter = actions.Any(a => a.Name == "Filter");

                Actions = actions.Where(a => !a.IsPinned).OfType<QueryAction>().ToArray();
                PinnedActions = actions.Where(a => a.IsPinned).OfType<QueryAction>().ToArray();
            }
            else
                PinnedActions = Actions = Array.Empty<QueryAction>();

            var newAction = Actions.OfType<New>().FirstOrDefault();
            var addAction = Actions.FirstOrDefault(a => a.Name == "AddReference");
            if (newAction != null && addAction != null)
                Actions = EnumerableEx.Return(new AddAndNewAction(newAction, addAction)).Concat(Actions).ToArray();

            var result = (JObject)model["result"];
            if (result != null)
            {
                SetResult(result);
                model.Remove("result");
            }

            if (model["isZoomedIn"] == null)
                IsZoomedIn = true;

            SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;
            HasTextSearch = !string.IsNullOrEmpty(TextSearch);
            HasNotification = !string.IsNullOrWhiteSpace(Notification);

            client.Hooks.OnConstruct(this);
        }

        public string Id => GetProperty<string>();

        public string Name => GetProperty<string>();

        public bool AutoQuery => GetProperty<bool>();

        public bool CanRead => GetProperty<bool>();

        public bool IsHidden => GetProperty<bool>();

        public string Label
        {
            get => GetProperty<string>();
            set => SetProperty(value);
        }

        public bool AsLookup { get; }

        public string Notification
        {
            get => GetProperty<string>();
            private set
            {
                if (SetProperty(value))
                    HasNotification = !string.IsNullOrWhiteSpace(value);
            }
        }

        public NotificationType NotificationType
        {
            get => (NotificationType)Enum.Parse(typeof(NotificationType), GetProperty<string>());
            private set => SetProperty(value.ToString());
        }

        public bool HasNotification
        {
            get => GetProperty<bool>();
            private set => SetProperty(value);
        }

        public int Offset => GetProperty<int>();

        public string SortOptions
        {
            get => GetProperty<string>();
            internal set => SetProperty(value);
        }

        public string TextSearch
        {
            get => GetProperty<string>();
            private set
            {
                if (SetProperty(value))
                {
                    HasTextSearch = !string.IsNullOrEmpty(value);
                    PendingSemanticZoomTabsRefresh = true;
                }
            }
        }

        public bool CanFilter { get; private set; }

        public PersistentObject Parent { get; internal set; }

        public PersistentObject PersistentObject { get; }

        internal Query SemanticZoomOwner { get; private set; }

        public bool IsSemanticZoomQuery => SemanticZoomOwner != null;

        internal int? PageSize
        {
            get => GetProperty<int?>();
            private set => SetProperty(value);
        }

        private int Skip
        {
            get => GetProperty<int>();
            set => SetProperty(value);
        }

        private int? Top
        {
            get => GetProperty<int>();
            set => SetProperty(value);
        }

        public int TotalItems
        {
            get => _TotalItems;
            private set => SetProperty(ref _TotalItems, value);
        }

        public bool HasSearched
        {
            get => _HasSearched;
            private set => SetProperty(ref _HasSearched, value);
        }

        public bool HasTextSearch
        {
            get => _HasTextSearch;
            private set => SetProperty(ref _HasTextSearch, value);
        }

        public bool HasSelectedItems
        {
            get => _HasSelectedItems;
            private set => SetProperty(ref _HasSelectedItems, value);
        }

        public QueryColumn[] Columns
        {
            get => _Columns;
            private set => SetProperty(ref _Columns, value);
        }

        public QueryAction[] Actions { get; }

        public QueryAction[] PinnedActions { get; }

        public bool HasActions
        {
            get { return Actions != null && Actions.Length > 0 && Actions.Any(a => a.IsVisible) || PinnedActions != null && PinnedActions.Length > 0 && PinnedActions.Any(a => a.IsVisible); }
        }

        public ObservableCollection<QueryResultItem> SelectedItems => _SelectedItems;

        public bool IsZoomedIn
        {
            get => GetProperty<bool>();
            set => SetProperty(value);
        }

        internal bool PendingSemanticZoomTabsRefresh { get; set; }

        #region Private Methods

        internal virtual void SelectedItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Actions.Run(a => a.Invalidate(SelectedItems.Count));
            PinnedActions.Run(a => a.Invalidate(SelectedItems.Count));
            HasSelectedItems = SelectedItems.Count > 0;
        }

        private async Task<bool> SearchAsync(bool resetItems = false)
        {
            if (searchCancellationTokenSource != null)
                searchCancellationTokenSource.Cancel();

            searchCancellationTokenSource = new CancellationTokenSource();
            var token = searchCancellationTokenSource.Token;

            try
            {
                if (resetItems)
                {
                    Skip = 0;
                    Top = PageSize;
                    items.Clear();
                    queriedPages.Clear();
                    SelectedItems.Clear();
                    CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }

                var result = await Client.ExecuteQueryAsync(this, Parent, null, AsLookup).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    SetResult(result);

                    if (resetItems)
                        CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

                    return true;
                }
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message);
            }
            finally
            {
                searchCancellationTokenSource = null;
            }

            return false;
        }

        internal virtual void SetResult(JObject result)
        {
            if (result.TryGetValue("columns", out var columnsToken))
            {
                var columns = (JArray)columnsToken;
                Columns = columns.Select(jCol =>
                {
                    var column = new QueryColumn((JObject)jCol, this);
                    if (Columns != null)
                    {
                        var sourceColumn = Columns.FirstOrDefault(c => c.Name == column.Name);
                        if (sourceColumn != null)
                        {
                            column.Includes = sourceColumn.Includes;
                            column.Excludes = sourceColumn.Excludes;
                        }
                    }
                    return column;
                }).ToArray();
            }
            else
                Columns = Array.Empty<QueryColumn>();

            TotalItems = (int?)result["totalItems"] ?? 0;
            PageSize = (int?)result["pageSize"] ?? 0;
            HasSearched = true;

            var items = Array.Empty<QueryResultItem>();
            if (result.TryGetValue("items", out var itemsToken))
                items = itemsToken.Select(jItem => new QueryResultItem((JObject)jItem, this)).ToArray();

            if (items.Length > 0)
            {
                var startIndex = (int?)result["skip"] ?? 0;
                for (var i = 0; i < items.Length; i++)
                {
                    this.items[startIndex + i] = items[i];
                    try
                    {
                        CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items[i]));
                    }
                    catch {}
                }

                if (PageSize > 0)
                    queriedPages.AddRange(Enumerable.Range(startIndex / PageSize.Value, Math.Max(1, items.Length / PageSize.Value)));
            }
        }

        #endregion

        #region Internal Methods

        internal async Task InvokeOpenItem(QueryResultItem item)
        {
            var e = new OpenQueryItemEventArgs(item);
            OpenItem(this, e);
            if (!e.Cancel)
            {
                string err = null;
                try
                {
                    await new Navigate().Execute(item.Load()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }

                if (!string.IsNullOrEmpty(err))
                    await Client.Hooks.ShowNotification(err, NotificationType.Error).ConfigureAwait(false);
            }
        }

        #endregion

        #region Public Methods

        public ActionBase GetAction(string name)
        {
            return Actions.FirstOrDefault(a => a.definition.Name == name) ?? PinnedActions.FirstOrDefault(a => a.definition.Name == name);
        }

        public async Task<QueryResultItem[]> GetItemsAsync(int skip, int top)
        {
            if (searchingTask != null)
                await searchingTask.ConfigureAwait(false);

            bool gotItems;

            if (PageSize.HasValue)
            {
                if (PageSize != 0)
                {
                    var pageSize = PageSize.Value;

                    var startPage = (int)Math.Floor((double)skip / pageSize);
                    var endPage = (int)Math.Floor(((double)skip + top - 1) / pageSize);

                    while (startPage < endPage && queriedPages.Contains(startPage))
                        startPage++;
                    while (endPage > startPage && queriedPages.Contains(endPage))
                        endPage--;

                    if (startPage != endPage || !queriedPages.Contains(startPage))
                    {
                        Skip = startPage * pageSize;
                        Top = (endPage - startPage + 1) * pageSize;

                        var st = searchingTask = SearchAsync();
                        gotItems = await st.ConfigureAwait(false);
                        searchingTask = null;
                    }
                    else
                        gotItems = true;
                }
                else
                    gotItems = true; // NOTE: PageSize 0 will always have all items
            }
            else
                gotItems = await SearchAsync().ConfigureAwait(false);

            return gotItems ? this.Skip(skip).Take(top == 0 ? TotalItems - skip : top).ToArray() : Array.Empty<QueryResultItem>();
        }

        public virtual async Task RefreshQueryAsync()
        {
            await SearchAsync(true).ConfigureAwait(false);
        }

        public virtual async Task SearchTextAsync(string text)
        {
            TextSearch = !string.IsNullOrEmpty(text) ? text : null;
            await SearchAsync(true).ConfigureAwait(false);
        }

        public void SetNotification(string notification, NotificationType notificationType = NotificationType.Error)
        {
            NotificationType = notificationType;
            Notification = notification;

            NotificationChanged(this, new NotificationChangedEventArgs(notification, notificationType));
        }

        #endregion

        #region Internal Methods

        internal async Task<PersistentObjectTabQuery[]> GetSemanticZoomTabs()
        {
            if (semanticZoomTabs == null || PendingSemanticZoomTabsRefresh)
            {
                var po = await Client.ExecuteActionAsync("QueryFilter.SemanticZoom", Parent, this).ConfigureAwait(false);
                if (po != null)
                {
                    po.Queries.Run(q =>
                    {
                        q.Value.Parent = Parent;
                        q.Value.SemanticZoomOwner = this;
                    });
                    PendingSemanticZoomTabsRefresh = false;
                    semanticZoomTabs = po.Tabs.OfType<PersistentObjectTabQuery>().ToArray();
                }
            }

            return semanticZoomTabs;
        }

        #endregion

        #region IEnumerable<QueryResultItem>

        IEnumerator<QueryResultItem> IEnumerable<QueryResultItem>.GetEnumerator()
        {
            if (!HasSearched && AutoQuery && Count == 0 && searchCancellationTokenSource == null)
                SearchAsync(true).ContinueWith(_ => this.OnPropertyChanged(string.Empty));

            var count = Count;
            for (var i = 0; i < count; i++)
            {
                yield return items.TryGetValue(i, out var item) ? item : null;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<QueryResultItem>)this).GetEnumerator();
        }

        #endregion

        #region Service Serialization

        protected override string[] GetServiceProperties()
        {
            return new[] { "id", "name", "label", "pageSize", "skip", "top", "sortOptions", "textSearch" };
        }

        internal override JObject ToServiceObject()
        {
            var jObj = base.ToServiceObject();

            if (PersistentObject != null)
                jObj["persistentObject"] = PersistentObject.ToServiceObject();

            if (Columns != null)
                jObj["columns"] = JArray.FromObject(Columns.Select(col => col.ToServiceObject()));

            return jObj;
        }

        #endregion

        public event NotifyCollectionChangedEventHandler CollectionChanged = delegate { };

        public QueryResultItem this[int index]
        {
            get
            {
                items.TryGetValue(index, out var result);

                return result;
            }
        }

        public int Count => items.Count > 0 ? items.Keys.Max() + 1 : 0;

        public event EventHandler<OpenQueryItemEventArgs> OpenItem = delegate { };
        public event EventHandler<NotificationChangedEventArgs> NotificationChanged = delegate { };
    }
}