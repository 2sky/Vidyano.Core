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

    /// <summary>Mints an ADDITIONAL, cookie-isolated transport over the already-started backend — one
    /// per named <c>SIGN-IN @name</c> identity, so named identities never share an auth/cookie jar. The
    /// default delegates to <see cref="StartAsync"/>, which is already correct for any adapter whose
    /// <c>StartAsync</c> yields a fresh isolated transport on each call (e.g. an in-process host minting
    /// a new cookie jar over its in-memory server). An adapter that hands back a SHARED client from
    /// <c>StartAsync</c> (e.g. a caller-supplied HttpClient) MUST override this to build a genuinely
    /// isolated transport, or two named identities would silently conflate. The adapter owns every
    /// transport it returns — from here and from <c>StartAsync</c> — and disposes them in DisposeAsync.</summary>
    ValueTask<BackendConnection> MintIsolatedAsync(CancellationToken cancellationToken = default)
        => StartAsync(cancellationToken);
}
