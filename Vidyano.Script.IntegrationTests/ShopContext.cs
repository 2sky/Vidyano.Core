using System.ComponentModel.DataAnnotations;
using Vidyano.Service;
using Vidyano.Service.Repository;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// The in-memory Vidyano app the .visc integration tests run against. A real Vidyano server (full
/// model sync, real action execution, real notifications) booted entirely from code via the Minimal
/// API — no config files, no database. Fixtures are deterministic so assertions like
/// <c>EXPECT TotalItems = 3</c> are stable. Shaped to exercise each .visc verb family: an editable PO
/// (EDIT/SET/SAVE), a reference (SET/FOLLOW), a detail query (SEARCH/EXPECT Detail), a custom action
/// returning a notification (ACTION), a save that fails on a sentinel (SAVE EXPECTING ERROR), a
/// BinaryFile attribute (SET = FILE), an action that raises a retry dialog (CONFIRM), and a
/// multi-lingual <see cref="Product.Title"/> (SET/EXPECT … LANGUAGE; languages live in
/// <see cref="InProcessVidyanoBackend"/>).
/// </summary>
public sealed class ShopContext : NullTargetContext
{
    /// <summary>The Color value <see cref="ProductActions.OnSave"/> rejects — gives SAVE EXPECTING ERROR
    /// a deterministic server-side failure with no dependency on rule-engine semantics.</summary>
    public const string ForbiddenColor = "Forbidden";

    // NullTargetContext persists through these collections, which are process-global (the Minimal API
    // has no per-app data store). Tests share one booted app, so each test re-seeds via Reset() to stay
    // isolated; the seed is fresh instances so an edit/save in one test never leaks into the next.
    private static readonly List<ProductCategory> categories = [];
    private static readonly List<Product> products = [];
    private static readonly List<Document> documents = [];

    static ShopContext() => Reset();

    public ShopContext()
    {
        Register(categories);
        Register(products);
        Register(documents);
    }

    /// <summary>Restores the seed data. Call before each test (the collection runs serially, so this is
    /// race-free) to undo edits/saves/deletes from the previous test.</summary>
    public static void Reset()
    {
        categories.Clear();
        categories.AddRange(
        [
            new ProductCategory { Id = "1", Name = "Tools" },
            new ProductCategory { Id = "2", Name = "Electronics" },
        ]);

        products.Clear();
        products.AddRange(
        [
            new Product { Id = "1", Name = "Widget", Color = "Blue", Category = "1", Title = _L("Widget", "Hulpmiddel", "Werkzeug") },
            new Product { Id = "2", Name = "Gadget", Color = "Red", Category = "2", Title = _L("Gadget", "Apparaat", "Gerät") },
            new Product { Id = "3", Name = "Gizmo", Color = "Green", Category = "1", Title = _L("Gizmo", "Ding", "Dingsda") },
        ]);

        documents.Clear();
        documents.AddRange(
        [
            new Document { Id = "1", Name = "Spec" },
        ]);
    }

    // English / Dutch / German title — the languages must match the WithLanguage(...) set in the backend.
    // NOTE: fully qualified. This file's namespace (Vidyano.Script.IntegrationTests) makes `Vidyano` an
    // enclosing namespace, so an unqualified `TranslatedString` would bind to Core's `Vidyano.TranslatedString`
    // (enclosing-namespace types outrank `using`-imported ones) — and the SERVER's schema sync only maps a
    // property typed as the server's `Vidyano.Service.TranslatedString`, so the unqualified form silently
    // produces a plain String attribute. (The Demo, in namespace `Demo`, doesn't hit this.)
    private static global::Vidyano.Service.TranslatedString _L(string en, string nl, string de) =>
        new() { ["en"] = en, ["nl"] = nl, ["de"] = de };

    public IQueryable<ProductCategory> ProductCategories => Query<ProductCategory>();

    public IQueryable<Product> Products => Query<Product>();

    public IQueryable<Document> Documents => Query<Document>();

    public override void AddObject(PersistentObject obj, object entity)
    {
        switch (entity)
        {
            case Product product:
                product.Id ??= (products.Count + 1).ToString();
                break;
            case ProductCategory category:
                category.Id ??= (categories.Count + 1).ToString();
                break;
            case Document document:
                document.Id ??= (documents.Count + 1).ToString();
                break;
        }

        base.AddObject(obj, entity);
    }
}

public sealed class Product
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Color { get; set; } = string.Empty;
    [Reference(typeof(ProductCategory))]
    public string? Category { get; set; }

    /// <summary>A multi-lingual title — surfaced to the client as a <c>TranslatedString</c> attribute, so
    /// the .visc <c>SET</c>/<c>EXPECT … LANGUAGE</c> round-trip can be exercised. The supported languages
    /// come from the <c>WithLanguage(...)</c> set in <see cref="InProcessVidyanoBackend"/>. Fully qualified
    /// to the SERVER type (see <see cref="ShopContext"/>'s <c>_L</c> note on the enclosing-namespace pitfall).</summary>
    public global::Vidyano.Service.TranslatedString? Title { get; set; }

    // Nullable (like Category) so they're optional — a non-nullable string would be a required
    // attribute and the empty seed value would fail SAVE validation in the other Product tests.

    /// <summary>Marked <c>TriggersRefresh</c> in the model (see <see cref="InProcessVidyanoBackend"/>);
    /// setting it fires <see cref="ProductActions.OnRefresh"/>.</summary>
    public string? Trigger { get; set; }

    /// <summary>The "different attribute" the refresh writes: <see cref="ProductActions.OnRefresh"/>
    /// mirrors <see cref="Trigger"/> into it, so a SET of Trigger surfaces here after the round-trip.</summary>
    public string? Echo { get; set; }

    /// <summary>A hidden-but-editable attribute: <see cref="ProductActions.OnLoad"/> sets its
    /// <c>Visibility</c> to <see cref="AttributeVisibility.Never"/>, modelling the real-world case where a
    /// custom web component edits a field the default editor never renders. The standard UI can't set it,
    /// so the .visc SET guard tiers by mode (navigation rejects; audit warns-but-allows; direct allows).
    /// Nullable/optional so it doesn't affect SAVE validation in the other Product tests.</summary>
    public string? Secret { get; set; }

    /// <summary>A visible-but-read-only attribute: <see cref="ProductActions.OnLoad"/> sets its
    /// <c>IsReadOnly</c>. Read-only is a hard guard the mode tier never relaxes (unlike visibility), so a
    /// SET of this fails with <c>guard-attribute-read-only</c> even in <c>direct</c> mode. Visible so the
    /// read-only guard is what's exercised, not the hidden one.</summary>
    public string? Locked { get; set; }
}

public sealed class ProductCategory
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}

public sealed class Document
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    [DataType(DataTypes.BinaryFile)]
    public string? File { get; set; }
}

public sealed class ProductActions(ShopContext context)
    : PersistentObjectActions<ShopContext, Product>(context)
{
    public override void OnNew(PersistentObject obj, PersistentObject? parent, Query? query, Dictionary<string, string>? parameters)
    {
        base.OnNew(obj, parent, query, parameters);

        obj.SetAttributeValue("Name", "New Product");
        obj.SetAttributeValue("Color", "Blue");
    }

    /// <summary>Hides <see cref="Product.Secret"/> from the default editor (visibility Never) the same way a
    /// real app would for a field only a custom web component edits. The attribute still rides the wire and
    /// stays editable server-side, so a .visc SET reaches it once the mode allows a hidden write.</summary>
    public override void OnLoad(PersistentObject obj, PersistentObject? parent)
    {
        base.OnLoad(obj, parent);

        obj[nameof(Product.Secret)].Visibility = AttributeVisibility.Never;
        obj[nameof(Product.Locked)].IsReadOnly = true;
    }

    public override void OnSave(PersistentObject obj)
    {
        if ((string?)obj["Color"] == ShopContext.ForbiddenColor)
        {
            obj.AddNotification($"Color '{ShopContext.ForbiddenColor}' is not allowed.", NotificationType.Error);
            return;
        }

        base.OnSave(obj);
    }

    /// <summary>Fires when a <c>TriggersRefresh</c> attribute changes. When it's <see cref="Product.Trigger"/>,
    /// mirror the new value into the unrelated <see cref="Product.Echo"/> attribute. This is the server
    /// half of the client refresh round-trip: a SET of Trigger calls PersistentObject.Refresh, OnRefresh
    /// mutates a *different* attribute, and the refreshed Echo flows back so EXPECT can read it.</summary>
    public override void OnRefresh(RefreshArgs args)
    {
        base.OnRefresh(args);

        if (args.Attribute?.Name == nameof(Product.Trigger))
            args.PersistentObject.SetAttributeValue(nameof(Product.Echo), $"echo:{(string?)args.PersistentObject[nameof(Product.Trigger)]}");
    }

    /// <summary>Detail query: the products belonging to one category. Auto-discovered by name and
    /// wired as the <c>Products</c> detail panel on ProductCategory in <see cref="InProcessVidyanoBackend"/>.</summary>
    public IEnumerable<Product> ProductCategory_Products(CustomQueryArgs args)
    {
        args.EnsureParent(nameof(ProductCategory));

        var categoryId = args.Parent.ObjectId;
        return Context.Products.Where(p => p.Category == categoryId);
    }
}

public sealed class ProductCategoryActions(ShopContext context)
    : PersistentObjectActions<ShopContext, ProductCategory>(context)
{
    // The .visc `Detail "<name>"` resolver reads CurrentPo.Queries (the PO's `queries` payload), which
    // is populated by AddQuery — NOT by an as-detail attribute. So surface the per-category Products
    // query as a PO-level related query, keyed by its query name.
    public override void OnLoad(PersistentObject obj, PersistentObject? parent)
    {
        base.OnLoad(obj, parent);

        obj.AddQuery(nameof(ProductActions.ProductCategory_Products));
    }
}

/// <summary>PO-level custom action that sets a notification on the current PO — exercises
/// <c>ACTION</c> + <c>EXPECT Notification</c>/<c>Notification.Type</c>. It returns the acting PO (not a
/// <c>Notification(...)</c> result, which the .visc runtime drops from the nav stack), so the
/// notification rides back on the current PO where EXPECT can read it.</summary>
public sealed class HelloWorld(ShopContext context) : CustomAction<ShopContext>(context)
{
    public override PersistentObject? Execute(CustomActionArgs e)
    {
        e.Parent!.AddNotification("Hello, World!", NotificationType.OK);
        return e.Parent;
    }
}

/// <summary>Raises a server retry dialog the first time, then resumes with the chosen option and
/// reports it as a notification on the current PO — exercises <c>CONFIRM</c>, the
/// <c>EXPECT RetryDialog.*</c> subjects, and reading the post-resume notification.</summary>
public sealed class AskFirst(ShopContext context) : CustomAction<ShopContext>(context)
{
    public override PersistentObject? Execute(CustomActionArgs e)
    {
        string? chosen = null;
        if (e.Parameters == null || !e.Parameters.TryGetValue("RetryActionOption", out chosen))
            Manager.Current.RetryAction("Are you sure?", "This will proceed.", "Yes", "No");

        e.Parent!.AddNotification($"You chose: {chosen}", NotificationType.OK);
        return e.Parent;
    }
}
