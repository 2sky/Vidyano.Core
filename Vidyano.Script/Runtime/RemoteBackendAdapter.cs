using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vidyano.Script.Runtime;

/// <summary>The URL adapter: stands up the cookie-jar HttpClient a remote Vidyano service needs, or
/// wraps a caller-supplied client. Owns the client it builds; never disposes one it was handed.</summary>
internal sealed class RemoteBackendAdapter : IBackendAdapter
{
    private readonly string _baseUri;
    private readonly HttpClient? _suppliedHttpClient;
    private readonly bool _acceptAnyServerCertificate;
    private HttpClient? _ownedHttpClient;

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

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };
        if (_acceptAnyServerCertificate)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _ownedHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        return new ValueTask<BackendConnection>(new BackendConnection(_ownedHttpClient, _baseUri));
    }

    public ValueTask DisposeAsync()
    {
        _ownedHttpClient?.Dispose();
        return default;
    }
}
