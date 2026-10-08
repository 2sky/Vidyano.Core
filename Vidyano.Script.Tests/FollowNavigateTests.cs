using System.Linq;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// <c>FOLLOW-NAVIGATE</c>: the parse shape, and the pure <see cref="RouteTable"/> that ports the web client's
/// routing (<c>application.ts #setRoutes</c> + <c>app.ts _convertPath</c> + the raw <c>persistent-object.</c> /
/// <c>query.</c> routes). The live round-trip runs in the integration tests.
/// </summary>
public sealed class FollowNavigateTests
{
    // The server's Routes attribute shape: route name → PO type id / query id.
    private const string RoutesJson = """
        {
          "programUnits": { "Home": "Home", "Charging": "Charging" },
          "persistentObjects": { "VestaChargePoint": "po-vcp", "Customer": "po-cust", "Inv": "po-inv" },
          "queries": { "VestaChargePoints": "q-vcp", "HTMLPages": "q-html" }
        }
        """;

    private static RouteTable Routes => RouteTable.Parse(RoutesJson);

    [Theory]
    [InlineData("VestaChargePoint", "vesta-charge-point")]
    [InlineData("Customer", "customer")]
    [InlineData("HTMLPages", "html-pages")]
    [InlineData("ProductCategory_Products", "product-category_products")]
    [InlineData("Tab2Name", "tab2name")]
    [InlineData("already-kebab", "already-kebab")]
    public void ToKebabCase_MatchesTheWebClient(string input, string expected) =>
        Assert.Equal(expected, RouteTable.ToKebabCase(input));

    [Fact]
    public void KebabRoute_WithObjectIdContainingSlash_KeepsTheWholeId()
    {
        var target = Routes.Resolve("vesta-charge-point/vestaChargePoints/9001");
        Assert.Equal(new NavigateTarget(true, "po-vcp", "vestaChargePoints/9001"), target);
    }

    [Fact]
    public void RawRouteName_Resolves() =>
        Assert.Equal(new NavigateTarget(true, "po-vcp", "1"), Routes.Resolve("VestaChargePoint/1"));

    [Fact]
    public void ProgramUnitPrefix_RawOrKebab_IsIgnored()
    {
        Assert.Equal(new NavigateTarget(true, "po-cust", "7"), Routes.Resolve("Charging/customer/7"));
        Assert.Equal(new NavigateTarget(true, "po-cust", "7"), Routes.Resolve("charging/customer/7"));
    }

    [Fact]
    public void PersistentObjectRoute_WithoutObjectId_HasNullObjectId() =>
        Assert.Equal(new NavigateTarget(true, "po-cust", null), Routes.Resolve("customer"));

    [Fact]
    public void LeadingSlashes_AreIgnored() =>
        Assert.Equal(new NavigateTarget(true, "po-cust", "7"), Routes.Resolve("//customer/7"));

    [Fact]
    public void QueryRoute_Resolves()
    {
        Assert.Equal(new NavigateTarget(false, "q-vcp", null), Routes.Resolve("vesta-charge-points"));
        Assert.Equal(new NavigateTarget(false, "q-html", null), Routes.Resolve("Home/html-pages"));
    }

    [Fact]
    public void RouteNamePrefix_DoesNotShadowALongerRoute()
    {
        // "Inv" is a prefix of nothing here, but "inv/1" must hit Inv and "invoice" must not.
        Assert.Equal(new NavigateTarget(true, "po-inv", "1"), Routes.Resolve("inv/1"));
        Assert.Null(Routes.Resolve("invoice"));
    }

    [Fact]
    public void RawPersistentObjectAndQueryRoutes_Resolve()
    {
        Assert.Equal(new NavigateTarget(true, "316b2486", "a/b"), Routes.Resolve("Management/PersistentObject.316b2486/a/b"));
        Assert.Equal(new NavigateTarget(true, "Product", null), Routes.Resolve("persistent-object.Product"));
        Assert.Equal(new NavigateTarget(false, "q-1", null), Routes.Resolve("query.q-1"));
    }

    [Fact]
    public void UnknownPath_ReturnsNull()
    {
        Assert.Null(Routes.Resolve("no-such-page/1"));
        Assert.Null(Routes.Resolve("vesta-charge-points/1")); // a query route takes no object id
    }

    [Fact]
    public void EmptyRoutes_OnlyResolveRawForms()
    {
        var empty = RouteTable.Parse("""{ "programUnits": {}, "persistentObjects": {}, "queries": {} }""");
        Assert.Null(empty.Resolve("customer/1"));
        Assert.Equal(new NavigateTarget(false, "Customers", null), empty.Resolve("query.Customers"));
    }

    [Fact]
    public void RouteNames_AreKebabCased() =>
        Assert.Contains("vesta-charge-point", Routes.RouteNames);

    [Theory]
    [InlineData("FOLLOW-NAVIGATE", null)]
    [InlineData("FOLLOW-NAVIGATE AS @cp", "cp")]
    public void Parses(string body, string? handle)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var stmt = Assert.IsType<FollowNavigateStmt>(Assert.Single(parser.Parse().Steps.SelectMany(s => s.Statements)));
        Assert.Empty(parser.Diagnostics);
        Assert.Equal(handle, stmt.AsHandle);
    }
}
