using Vidyano.Service;
using Vidyano.Service.Repository;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// The app's <see cref="Advanced"/> (found by the server's assembly scan) — the fixture for the .visc
/// <c>INSTANT-SEARCH</c> verb. Instant search is on for <c>admin</c> only, so the <c>viewer</c> user (see
/// <see cref="InProcessVidyanoBackend"/>) exercises the feature-off path; a search returns the Products whose
/// Name contains the text, with the Name as breadcrumb.
/// </summary>
public sealed class ShopAdvanced : Advanced
{
    public override TimeSpan? GetInstantSearchDelayForUser(IUser user) =>
        user.Name == "admin" ? TimeSpan.FromMilliseconds(300) : null;

    public override void HandleInstantSearch(InstantSearchArgs args) =>
        args.AddRange($"Shop.{nameof(Product)}", ShopContext.AllProducts
            .Where(p => p.Name.Contains(args.Search, StringComparison.OrdinalIgnoreCase))
            .Select(p => new InstantResult(p.Id, p.Name))
            .ToList());
}
