using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vidyano.Script.Runtime;

/// <summary>The URL adapter: stands up the cookie-jar HttpClient a remote Vidyano service needs, or
/// wraps a caller-supplied client. Owns the clients it builds; never disposes one it was handed.</summary>
internal sealed class RemoteBackendAdapter : IBackendAdapter
{
    private readonly string _baseUri;
    private readonly HttpClient? _suppliedHttpClient;
    private readonly bool _acceptAnyServerCertificate;
    private HttpClient? _ownedHttpClient;
    private readonly List<HttpClient> _mintedHttpClients = new();

    public RemoteBackendAdapter(string baseUri, HttpClient? httpClient, bool acceptAnyServerCertificate)
    {
        _baseUri = baseUri;
        _suppliedHttpClient = httpClient;
        _acceptAnyServerCertificate = acceptAnyServerCertificate;
    }

    public ValueTask<BackendConnection> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_suppliedHttpClient is not null)
            return new ValueTask<BackendConnection>(new BackendConnection(_suppliedHttpClient, _baseUri));

        _ownedHttpClient = BuildOwnJarClient();
        return new ValueTask<BackendConnection>(new BackendConnection(_ownedHttpClient, _baseUri));
    }

    public ValueTask<BackendConnection> MintIsolatedAsync(CancellationToken cancellationToken = default)
    {
        // A named identity always needs its OWN cookie jar, even when the default slot wraps a
        // caller-supplied client — so build a fresh own-jar client here and never reuse _suppliedHttpClient.
        var client = BuildOwnJarClient();
        _mintedHttpClients.Add(client);
        return new ValueTask<BackendConnection>(new BackendConnection(client, _baseUri));
    }

    private HttpClient BuildOwnJarClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };
        if (_acceptAnyServerCertificate)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }

    public ValueTask DisposeAsync()
    {
        _ownedHttpClient?.Dispose();
        foreach (var client in _mintedHttpClients)
            client.Dispose();
        return default;
    }
}
