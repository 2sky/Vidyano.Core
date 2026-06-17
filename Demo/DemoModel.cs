using System.ComponentModel.DataAnnotations;
using Vidyano.Service;
using Vidyano.Service.Repository;

namespace Demo;

/// <summary>
/// The tiny Vidyano app this demo boots in-process via the Minimal API — a full server (model sync,
/// real queries, real auth) defined entirely in code, no config files and no database. The data lives
/// in plain in-memory lists; <see cref="NullTargetContext"/> reads/writes them, so the demo is
/// self-contained and runs offline. A "shop" of products in categories — the same shape the .visc
/// integration tests use, minus their test-only hooks.
/// </summary>
public sealed class DemoContext : NullTargetContext
{
    private static readonly List<ProductCategory> categories =
    [
        new() { Id = "1", Name = "Tools" },
        new() { Id = "2", Name = "Electronics" },
    ];

    private static readonly List<Product> products =
    [
        new() { Id = "1", Name = "Widget", Color = "Blue", Category = "1", Title = _L("Widget", "Hulpmiddel", "Werkzeug") },
        new() { Id = "2", Name = "Gadget", Color = "Red", Category = "2", Title = _L("Gadget", "Apparaat", "Gerät") },
        new() { Id = "3", Name = "Gizmo", Color = "Green", Category = "1", Title = _L("Gizmo", "Ding", "Dingsda") },
    ];

    // The same English/Dutch/German helper the .visc shop fixtures use to author a multi-lingual value.
    private static TranslatedString _L(string en, string nl, string de) =>
        new() { ["en"] = en, ["nl"] = nl, ["de"] = de };

    public DemoContext()
    {
        Register(categories);
        Register(products);
    }

    public IQueryable<ProductCategory> ProductCategories => Query<ProductCategory>();

    public IQueryable<Product> Products => Query<Product>();
}

public sealed class Product
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Color { get; set; } = string.Empty;
    [Reference(typeof(ProductCategory))]
    public string? Category { get; set; }
    // A per-language display title — surfaced to the client as a TranslatedString attribute, so the demo
    // can exercise the multi-lingual set/read round-trip.
    public TranslatedString? Title { get; set; }
}

public sealed class ProductCategory
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}
