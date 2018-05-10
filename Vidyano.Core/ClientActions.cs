﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Vidyano.Common;

namespace Vidyano
{
    static class ClientActions
    {
        #region Fields

        private static readonly PersistentObjectClientActions defaultHandler = new PersistentObjectClientActions();
        private static readonly Func<PersistentObjectClientActions> returnDefaultHandler = () => defaultHandler;
        private static readonly Dictionary<string, Func<PersistentObjectClientActions>> cache = new Dictionary<string, Func<PersistentObjectClientActions>>();

        #endregion

        #region Constructors

        static ClientActions()
        {
            var client = Client.Current;
            if (client == null)
                return;

            var asm = typeof(ClientActions).GetTypeInfo().Assembly;
            var clientAssembly = client.GetType().GetTypeInfo().Assembly;
            if (!asm.Equals(clientAssembly))
                AddTypes(clientAssembly.DefinedTypes.ToArray());

            var hooksAssembly = client.Hooks.GetType().GetTypeInfo().Assembly;
            if (!asm.Equals(hooksAssembly) && !hooksAssembly.Equals(clientAssembly))
                AddTypes(hooksAssembly.DefinedTypes.ToArray());
        }

        #endregion

        #region Methods

        internal static void AddTypes(TypeInfo[] types, string prefix = null)
        {
            if (types != null && types.Length > 0)
            {
                types
                    .Where(t => t != null && !t.IsAbstract && t.Name.EndsWith("ClientActions") && typeof(PersistentObjectClientActions).GetTypeInfo().IsAssignableFrom(t))
                    .Run(t => cache[prefix + t.Name.Substring(0, t.Name.Length - 13)] = Expression.Lambda<Func<PersistentObjectClientActions>>(Expression.New(t.AsType())).Compile());
            }
        }

        public static PersistentObjectClientActions Get(string fullTypeName)
        {
            if (fullTypeName == null)
                throw new ArgumentNullException("fullTypeName");

            return cache.GetOrAdd(fullTypeName, ftn =>
            {
                if (ftn != null && !ftn.StartsWith("Vidyano."))
                {
                    var type = ftn.Substring(ftn.IndexOf('.') + 1);
                    if (cache.TryGetValue(type, out var existingActions))
                        return existingActions;
                }

                return returnDefaultHandler;
            })();
        }

        #endregion
    }
}