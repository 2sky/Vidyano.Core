using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// Boots ONE in-process Vidyano app for the whole suite — booting the Minimal API twice in a process
/// is unsafe (static source holders), and a boot costs ~2s. Tests share this server and re-seed data
/// per test via <see cref="ShopContext.Reset"/>; each script run still gets its own cookie jar.
/// </summary>
public sealed class VidyanoAppFixture : IAsyncLifetime
{
    public InProcessVidyanoBackend Backend { get; } = new();

    public async Task InitializeAsync() => await Backend.StartAsync();

    public async Task DisposeAsync() => await Backend.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class VidyanoAppCollection : ICollectionFixture<VidyanoAppFixture>
{
    public const string Name = "Vidyano in-process app";
}
