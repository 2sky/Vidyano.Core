using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vidyano.Script.Runtime;

/// <summary>What a VidyanoSession needs to talk to a backend: a connected HttpClient and the base URI
/// it is rooted at. The lowest real seam — Vidyano.Client only swaps transport at the HttpClient level.</summary>
public readonly record struct BackendConnection(HttpClient HttpClient, string BaseUri);

/// <summary>The backend under test, as a port. Start it to get a session transport; dispose it to tear
/// down whatever it stood up (an in-process server, an embedded store, a socket). A remote URL and an
/// in-process host are both just adapters. Implementations own the HttpClient they return unless they
/// were handed one to wrap.</summary>
public interface IBackendAdapter : IAsyncDisposable
{
    ValueTask<BackendConnection> StartAsync(CancellationToken cancellationToken = default);
}
