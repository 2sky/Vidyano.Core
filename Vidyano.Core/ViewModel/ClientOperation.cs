using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    /// <summary>
    /// A client-side operation queued by the server in a response's <c>operations</c> array.
    /// The server uses these to ask the client to refresh data, navigate, or execute a named
    /// helper (clipboard, message box, reload, …) — see the v4 frontend
    /// <c>client-operations.ts</c> for the catalogue.
    /// </summary>
    /// <remarks>
    /// Subclasses model the well-known shapes. Unrecognised types are surfaced as
    /// <see cref="UnknownClientOperation"/> so consumers can still inspect them without losing data.
    /// </remarks>
    public abstract class ClientOperation
    {
        private protected ClientOperation(string type, JObject raw)
        {
            Type = type;
            Raw = raw;
        }

        /// <summary>The <c>type</c> discriminator emitted by the server.</summary>
        public string Type { get; }

        /// <summary>The raw operation JSON. Use this when the strongly-typed fields don't cover a
        /// custom or newly-added operation shape.</summary>
        public JObject Raw { get; }

        internal static ClientOperation FromJson(JObject model)
        {
            var type = (string)model["type"];
            switch (type)
            {
                case "Refresh":       return new RefreshClientOperation(model);
                case "ExecuteMethod": return new ExecuteMethodClientOperation(model);
                case "Open":          return new OpenClientOperation(model);
                default:              return new UnknownClientOperation(type ?? "Unknown", model);
            }
        }
    }

    /// <summary>Tells the client to refresh a query or persistent object, optionally after a delay.</summary>
    public sealed class RefreshClientOperation : ClientOperation
    {
        internal RefreshClientOperation(JObject model) : base("Refresh", model)
        {
            Delay        = (int?)model["delay"];
            QueryId      = (string)model["queryId"];
            FullTypeName = (string)model["fullTypeName"];
            ObjectId     = (string)model["objectId"];
        }

        /// <summary>Delay in milliseconds before the client should refresh, or <c>null</c> for immediate.</summary>
        public int? Delay { get; }

        /// <summary>The Query identifier to refresh, when the refresh targets a query.</summary>
        public string QueryId { get; }

        /// <summary>The PersistentObject full type name to refresh, when the refresh targets a PO.</summary>
        public string FullTypeName { get; }

        /// <summary>The PersistentObject object id to refresh, paired with <see cref="FullTypeName"/>.</summary>
        public string ObjectId { get; }
    }

    /// <summary>Invokes one of the well-known client methods (<c>copyToClipboard</c>, <c>navigate</c>,
    /// <c>openUrl</c>, <c>reloadPage</c>, <c>showMessageBox</c>, …) with positional arguments.</summary>
    public sealed class ExecuteMethodClientOperation : ClientOperation
    {
        internal ExecuteMethodClientOperation(JObject model) : base("ExecuteMethod", model)
        {
            Name      = (string)model["name"];
            Arguments = model["arguments"] as JArray ?? new JArray();
        }

        /// <summary>The method name to invoke (see v4 frontend <c>ClientOperations</c>).</summary>
        public string Name { get; }

        /// <summary>Positional arguments passed to the method, in order.</summary>
        public JArray Arguments { get; }
    }

    /// <summary>Opens a PersistentObject in the client, optionally replacing the current frame.</summary>
    public sealed class OpenClientOperation : ClientOperation
    {
        internal OpenClientOperation(JObject model) : base("Open", model)
        {
            PersistentObject = model["persistentObject"] as JObject;
            Replace          = (bool?)model["replace"];
        }

        /// <summary>The serialised PersistentObject to open. Use <c>Hooks.OnConstruct</c> to materialise.</summary>
        public JObject PersistentObject { get; }

        /// <summary>Whether to replace the current frame (true) or push on top (false/null).</summary>
        public bool? Replace { get; }
    }

    /// <summary>Catch-all for operation types the framework does not (yet) model strongly.
    /// Consumers can inspect <see cref="ClientOperation.Raw"/> for the original payload.</summary>
    public sealed class UnknownClientOperation : ClientOperation
    {
        internal UnknownClientOperation(string type, JObject model) : base(type, model) { }
    }
}
