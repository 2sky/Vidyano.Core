﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Vidyano.ViewModel;

namespace Vidyano
{
    public sealed class ExecuteActionArgs
    {
        #region Fields

        private readonly Client client;
        private readonly string action;

        #endregion

        #region Constructors

        internal ExecuteActionArgs(Client client, string action)
        {
            this.client = client;
            this.action = action;
            Action = action.Substring(action.IndexOf('.') + 1);
        }

        #endregion

        #region Properties

        public string Action { get; set; }

        public bool IsHandled { get; set; }

        public Dictionary<string, string> Parameters { get; set; }

        public PersistentObject PersistentObject { get; internal set; }

        public Query Query { get; internal set; }

        public PersistentObject Result { get; set; }

        public QueryResultItem[] SelectedItems { get; internal set; }

        #endregion

        #region Public Methods

        public async Task<PersistentObject> ExecuteServiceRequest()
        {
            return Result = await client.ExecuteActionAsync(action, PersistentObject, Query, SelectedItems, Parameters, true).ConfigureAwait(false);
        }

        #endregion
    }
}