using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Catalog;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): catalog use cases against the real schema, always rolled back.
public class CatalogDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private sealed class Env : IAsyncDisposable
    {
        public Env(AgriSageDbContext context)
        {
            Context = context;
            var errors = new NpgsqlErrorClassifier();
            Categories = new CategoryService(context, errors);
            Brands = new BrandService(context, errors);
            Ingredients = new ActiveIngredientService(context);
            Products = new ProductService(context, errors);
            StoreProducts = new StoreProductService(context, errors);
            Catalog = new CatalogService(context);
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public BrandService Brands { get; }

        public ActiveIngredientService Ingredients { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public CatalogService Catalog { get; }

        public Guid Bottle { get; set; }

        public Guid Box { get; set; }

        public Guid Carton { get; set; }

        public Guid StoreId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    // Units and the active store come from the reference seed; missing ones are created inside the transaction.
    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var env = new Env(session.NewContext());
        var context = env.Context;

        async Task<Guid> UnitAsync(string code, string name)
        {
            var unit = await context.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Code == code, Token);
            if (unit is null)
            {
                unit = new Unit(code, name);
                context.Units.Add(unit);
                await context.SaveChangesAsync(Token);
            }

            return unit.Id;
        }

        env.Bottle = await UnitAsync("BOTTLE", "Bottle");
        env.Box = await UnitAsync("BOX", "Box");
        env.Carton = await UnitAsync("CARTON", "Carton");

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            await context.SaveChangesAsync(Token);
            storeId = store.Id;
        }

        env.StoreId = storeId;
        return env;
    }

    private static PackagingRequest Bottle(Env env, bool sale = true, string? barcode = null) =>
        new(env.Bottle, 1, true, false, sale, "Bottle", barcode);

    private static PackagingRequest Box(Env env, bool sale = true, string? barcode = null) =>
        new(env.Box, 6, false, true, sale, "Box of 6", barcode);

    private static async Task<CategoryResponse> NewCategoryAsync(Env env, string? tag = null, Guid? parent = null) =>
        await env.Categories.CreateAsync(
            new CreateCategoryRequest($"C-{tag ?? Tag()}", $"Category {tag}", null, 0, parent), Token);

    private static async Task<ProductResponse> NewProductAsync(
        Env env, Guid categoryId, string? tag = null, Guid? brandId = null, params PackagingRequest[] packagings) =>
        await env.Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag ?? Tag()}", $"Product {tag}", categoryId,
                packagings.Length > 0 ? packagings : [Bottle(env), Box(env)], brandId), Token);

    // ----- Categories -----

    [RealDbFact]
    public async Task Category_tree_duplicates_cycles_and_delete_rules()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var tag = Tag();
        var root = await NewCategoryAsync(env, $"{tag}A");
        var child = await NewCategoryAsync(env, $"{tag}B", root.Id);
        var grandchild = await NewCategoryAsync(env, $"{tag}C", child.Id);

        await Assert.ThrowsAsync<ConflictException>(() => env.Categories.CreateAsync(
            new CreateCategoryRequest($"c-{tag}a".ToUpperInvariant().ToLowerInvariant(), "Duplicate", null, 0, null), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => NewCategoryAsync(env, Tag(), Guid.NewGuid()));
        // A → B → C: moving A under C would close the loop.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Categories.UpdateAsync(
            root.Id, new UpdateCategoryRequest("Root", null, 0, grandchild.Id), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Categories.UpdateAsync(
            root.Id, new UpdateCategoryRequest("Root", null, 0, root.Id), Token));
        // Children block the delete.
        await Assert.ThrowsAsync<ConflictException>(() => env.Categories.DeleteAsync(root.Id, Token));

        var tree = await env.Categories.GetTreeAsync(activeOnly: false, Token);
        var node = tree.Single(n => n.Id == root.Id);
        Assert.Equal(child.Id, Assert.Single(node.Children).Id);
        Assert.Equal(grandchild.Id, Assert.Single(node.Children[0].Children).Id);

        await env.Categories.SetActiveAsync(child.Id, false, Token);
        var activeTree = await env.Categories.GetTreeAsync(activeOnly: true, Token);
        Assert.Empty(activeTree.Single(n => n.Id == root.Id).Children);

        await env.Categories.DeleteAsync(grandchild.Id, Token);
        var deleted = await env.Context.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == grandchild.Id, Token);
        Assert.NotNull(deleted.DeletedAt);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Categories.GetAsync(grandchild.Id, Token));

        var found = await env.Categories.ListAsync(new CategoryListRequest { Search = tag, PageSize = 1, Page = 1 }, Token);
        Assert.Equal(2, found.TotalCount);
        Assert.Single(found.Items);
    }

    [RealDbFact]
    public async Task Category_with_products_cannot_be_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var category = await NewCategoryAsync(env);
        var product = await NewProductAsync(env, category.Id);

        await Assert.ThrowsAsync<ConflictException>(() => env.Categories.DeleteAsync(category.Id, Token));

        await env.Products.DeleteAsync(product.Id, Token);
        // Discontinued products still reference the category.
        await Assert.ThrowsAsync<ConflictException>(() => env.Categories.DeleteAsync(category.Id, Token));
    }

    // ----- Brands and ingredients -----

    [RealDbFact]
    public async Task Brand_names_are_unique_ignoring_case_and_used_brands_cannot_be_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var name = $"Brand {Tag()}";
        var brand = await env.Brands.CreateAsync(new BrandRequest(name, "B1", null, "https://cdn.example.com/l.png"), Token);
        var other = await env.Brands.CreateAsync(new BrandRequest($"Other {Tag()}", null, null, null), Token);

        await Assert.ThrowsAsync<ConflictException>(() => env.Brands.CreateAsync(new BrandRequest(name.ToUpperInvariant(), null, null, null), Token));
        await Assert.ThrowsAsync<ConflictException>(() => env.Brands.UpdateAsync(other.Id, new BrandRequest(name, null, null, null), Token));
        // Keeping its own name is fine.
        await env.Brands.UpdateAsync(brand.Id, new BrandRequest(name, "B2", "Updated", null), Token);

        var product = await NewProductAsync(env, (await NewCategoryAsync(env)).Id, brandId: brand.Id);
        Assert.Equal(brand.Id, product.BrandId);
        await Assert.ThrowsAsync<ConflictException>(() => env.Brands.DeleteAsync(brand.Id, Token));

        await env.Brands.DeleteAsync(other.Id, Token);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Brands.GetAsync(other.Id, Token));
        await env.Brands.SetActiveAsync(brand.Id, false, Token);
        Assert.False((await env.Brands.GetAsync(brand.Id, Token)).IsActive);
    }

    [RealDbFact]
    public async Task Active_ingredient_crud_and_delete_rules()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var name = $"Ingredient {Tag()}";
        var ingredient = await env.Ingredients.CreateAsync(new ActiveIngredientRequest(name, "I1", null), Token);

        await Assert.ThrowsAsync<ConflictException>(() =>
            env.Ingredients.CreateAsync(new ActiveIngredientRequest(name.ToLowerInvariant(), null, null), Token));
        var updated = await env.Ingredients.UpdateAsync(ingredient.Id, new ActiveIngredientRequest(name, "I2", "Description"), Token);
        Assert.Equal("I2", updated.Code);

        var product = await NewProductAsync(env, (await NewCategoryAsync(env)).Id);
        await env.Products.SetIngredientsAsync(product.Id, new SetProductIngredientsRequest([new(ingredient.Id, "50%")]), Token);
        await Assert.ThrowsAsync<ConflictException>(() => env.Ingredients.DeleteAsync(ingredient.Id, Token));

        await env.Products.SetIngredientsAsync(product.Id, new SetProductIngredientsRequest([]), Token);
        // The removed link is only soft deleted, but it no longer blocks the ingredient.
        await env.Ingredients.DeleteAsync(ingredient.Id, Token);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Ingredients.GetAsync(ingredient.Id, Token));
    }

    // ----- Products and packagings -----

    [RealDbFact]
    public async Task Product_is_created_with_its_packagings_and_unique_sku_and_barcodes()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var tag = Tag();
        var category = await NewCategoryAsync(env);
        var barcode = $"BC{tag}";

        var product = await NewProductAsync(env, category.Id, tag, null, Bottle(env), Box(env, barcode: barcode));

        Assert.Equal("ACTIVE", product.Status);
        Assert.Equal(["BOTTLE", "BOX"], product.Packagings.Select(p => p.UnitCode));
        Assert.True(product.Packagings[0].IsBaseUnit);
        Assert.Equal(1, product.Packagings[0].ConversionToBase);
        Assert.Equal(6, product.Packagings[1].ConversionToBase);
        Assert.All(product.Packagings, p => Assert.Equal("ACTIVE", p.Status));
        Assert.Null(product.Store);

        await Assert.ThrowsAsync<ConflictException>(() => NewProductAsync(env, category.Id, tag.ToLowerInvariant(), null, Bottle(env)));
        await Assert.ThrowsAsync<ConflictException>(() =>
            NewProductAsync(env, category.Id, Tag(), null, Bottle(env), Box(env, barcode: barcode)));
        await Assert.ThrowsAsync<NotFoundException>(() => NewProductAsync(env, Guid.NewGuid()));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Products.CreateAsync(
            new CreateProductRequest($"SKU-{Tag()}", "P", category.Id, [new(Guid.NewGuid(), 1, true, false, true)]), Token));
    }

    [RealDbFact]
    public async Task Packaging_rules_one_base_one_per_unit_and_base_stays_active()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var product = await NewProductAsync(env, (await NewCategoryAsync(env)).Id);
        var bottle = product.Packagings.Single(p => p.IsBaseUnit);
        var box = product.Packagings.Single(p => !p.IsBaseUnit);

        await Assert.ThrowsAsync<DomainException>(() =>
            env.Products.AddPackagingAsync(product.Id, new PackagingRequest(env.Carton, 1, true, false, true), Token));
        await Assert.ThrowsAsync<DomainException>(() =>
            env.Products.AddPackagingAsync(product.Id, new PackagingRequest(env.Box, 12, false, true, true), Token));
        var withCarton = await env.Products.AddPackagingAsync(
            product.Id, new PackagingRequest(env.Carton, 24, false, true, false, "Carton 24"), Token);
        Assert.Equal(3, withCarton.Packagings.Count);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Products.UpdatePackagingAsync(
            product.Id, bottle.Id, new UpdatePackagingRequest(false, true, "INACTIVE"), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Products.DeletePackagingAsync(product.Id, bottle.Id, Token));

        var updated = await env.Products.UpdatePackagingAsync(
            product.Id, box.Id, new UpdatePackagingRequest(false, false, "INACTIVE", "Renamed", "NEWBARCODE1"), Token);
        var changed = updated.Packagings.Single(p => p.Id == box.Id);
        Assert.Equal("INACTIVE", changed.Status);
        Assert.Equal("Renamed", changed.PackagingName);
        Assert.Equal("NEWBARCODE1", changed.Barcode);
        // Conversion and base flag never change.
        Assert.Equal(6, changed.ConversionToBase);

        var removed = await env.Products.DeletePackagingAsync(product.Id, withCarton.Packagings.Single(p => p.UnitCode == "CARTON").Id, Token);
        Assert.DoesNotContain(removed.Packagings, p => p.UnitCode == "CARTON");
        await Assert.ThrowsAsync<NotFoundException>(() => env.Products.DeletePackagingAsync(product.Id, Guid.NewGuid(), Token));
    }

    [RealDbFact]
    public async Task Packaging_already_used_by_a_cart_cannot_be_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var product = await NewProductAsync(env, (await NewCategoryAsync(env)).Id);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);
        var box = product.Packagings.Single(p => !p.IsBaseUnit);

        var existingRole = await env.Context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == RoleCode.Farmer, Token);
        var role = existingRole ?? new Role(RoleCode.Farmer, "Farmer");
        var user = new User(role.Id, "Farmer", "hash", $"{Tag()}@example.test", null);
        var farmer = new FarmerProfile(user.Id);
        var cart = new Cart(env.StoreId, farmer.Id);
        cart.SetItemQuantity(
            await env.Context.StoreProducts.SingleAsync(sp => sp.Id == storeProduct.Id, Token),
            await env.Context.ProductPackagings.SingleAsync(p => p.Id == box.Id, Token),
            2);
        if (existingRole is null)
        {
            env.Context.Add(role);
        }

        env.Context.AddRange(user, farmer, cart);
        await env.Context.SaveChangesAsync(Token);

        await Assert.ThrowsAsync<ConflictException>(() => env.Products.DeletePackagingAsync(product.Id, box.Id, Token));
        var inactive = await env.Products.UpdatePackagingAsync(
            product.Id, box.Id, new UpdatePackagingRequest(true, true, "INACTIVE"), Token);
        Assert.Equal("INACTIVE", inactive.Packagings.Single(p => p.Id == box.Id).Status);
    }

    [RealDbFact]
    public async Task Update_status_and_delete_discontinue_the_product_and_take_it_off_sale()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var category = await NewCategoryAsync(env);
        var other = await NewCategoryAsync(env);
        var product = await NewProductAsync(env, category.Id);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);

        var updated = await env.Products.UpdateAsync(
            product.Id, new UpdateProductRequest("Renamed", other.Id, null, "Desc", "Use", "https://cdn.example.com/p.jpg"), Token);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(other.Id, updated.CategoryId);
        Assert.Equal("https://cdn.example.com/p.jpg", updated.ImageUrl);
        Assert.Equal(product.Sku, updated.Sku);
        Assert.Equal(storeProduct.Id, updated.Store!.StoreProductId);

        Assert.Equal("INACTIVE", (await env.Products.ChangeStatusAsync(product.Id, new ChangeProductStatusRequest("inactive"), Token)).Status);

        await env.Products.DeleteAsync(product.Id, Token);

        Assert.Equal("DISCONTINUED", (await env.Products.GetAsync(product.Id, Token)).Status);
        var store = await env.StoreProducts.GetAsync(storeProduct.Id, Token);
        Assert.False(store.IsActive);
        // The row and the SKU are kept.
        await Assert.ThrowsAsync<ConflictException>(() => NewProductAsync(env, category.Id, product.Sku["SKU-".Length..]));
    }

    [RealDbFact]
    public async Task Product_list_filters_and_pages()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var tag = Tag();
        var category = await NewCategoryAsync(env);
        var brand = await env.Brands.CreateAsync(new BrandRequest($"Brand {tag}", null, null, null), Token);
        await NewProductAsync(env, category.Id, $"{tag}A", brand.Id);
        await NewProductAsync(env, category.Id, $"{tag}B");
        var third = await NewProductAsync(env, category.Id, $"{tag}C");
        await env.Products.ChangeStatusAsync(third.Id, new ChangeProductStatusRequest("INACTIVE"), Token);

        var all = await env.Products.ListAsync(new ProductListRequest { Search = tag }, Token);
        var branded = await env.Products.ListAsync(new ProductListRequest { Search = tag, BrandId = brand.Id }, Token);
        var inactive = await env.Products.ListAsync(new ProductListRequest { Search = tag, Status = "inactive" }, Token);
        var byCategory = await env.Products.ListAsync(new ProductListRequest { CategoryId = category.Id, PageSize = 2 }, Token);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(1, branded.TotalCount);
        Assert.Equal(brand.Name(), branded.Items.Single().BrandName);
        Assert.Equal(third.Id, inactive.Items.Single().Id);
        Assert.Equal(3, byCategory.TotalCount);
        Assert.Equal(2, byCategory.Items.Count);
        Assert.Equal(category.Name, byCategory.Items[0].CategoryName);
    }

    // ----- Ingredients of a product -----

    [RealDbFact]
    public async Task Removed_ingredient_is_revived_when_added_again()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var product = await NewProductAsync(env, (await NewCategoryAsync(env)).Id);
        var first = await env.Ingredients.CreateAsync(new ActiveIngredientRequest($"A {Tag()}", null, null), Token);
        var second = await env.Ingredients.CreateAsync(new ActiveIngredientRequest($"B {Tag()}", null, null), Token);

        var both = await env.Products.SetIngredientsAsync(
            product.Id, new SetProductIngredientsRequest([new(first.Id, "10%"), new(second.Id, "20%", "note")]), Token);
        Assert.Equal(2, both.Ingredients.Count);

        var onlyFirst = await env.Products.SetIngredientsAsync(
            product.Id, new SetProductIngredientsRequest([new(first.Id, "15%")]), Token);
        Assert.Equal(first.Id, Assert.Single(onlyFirst.Ingredients).ActiveIngredientId);
        Assert.Equal("15%", onlyFirst.Ingredients[0].Concentration);

        var again = await env.Products.SetIngredientsAsync(
            product.Id, new SetProductIngredientsRequest([new(first.Id), new(second.Id, "30%")]), Token);
        Assert.Equal(2, again.Ingredients.Count);
        Assert.Equal("30%", again.Ingredients.Single(i => i.ActiveIngredientId == second.Id).Concentration);
        Assert.Null(again.Ingredients.Single(i => i.ActiveIngredientId == second.Id).Note);
        // One row per pair (the unique index also counts deleted rows) and none left deleted.
        var links = await env.Context.ProductActiveIngredients.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.ProductId == product.Id).ToListAsync(Token);
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Null(l.DeletedAt));
    }

    [RealDbFact]
    public async Task Ingredients_must_exist_and_be_active()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var product = await NewProductAsync(env, (await NewCategoryAsync(env)).Id);
        var inactive = await env.Ingredients.CreateAsync(new ActiveIngredientRequest($"X {Tag()}", null, null), Token);
        await env.Ingredients.SetActiveAsync(inactive.Id, false, Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Products.SetIngredientsAsync(
            product.Id, new SetProductIngredientsRequest([new(Guid.NewGuid())]), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Products.SetIngredientsAsync(
            product.Id, new SetProductIngredientsRequest([new(inactive.Id)]), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Products.SetIngredientsAsync(
            Guid.NewGuid(), new SetProductIngredientsRequest([]), Token));
    }

    // ----- Store products -----

    [RealDbFact]
    public async Task Only_sellable_products_can_be_added_to_the_store_and_marked_sellable()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var category = await NewCategoryAsync(env);
        var noSale = await NewProductAsync(env, category.Id, null, null, Bottle(env, sale: false));
        var product = await NewProductAsync(env, category.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.StoreProducts.CreateAsync(new CreateStoreProductRequest(noSale.Id), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.StoreProducts.CreateAsync(new CreateStoreProductRequest(Guid.NewGuid()), Token));

        var created = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id, " S-1 ", 12), Token);
        Assert.Equal("S-1", created.StoreSku);
        Assert.Equal(12, created.MinStockLevelBase);
        Assert.True(created.IsSellable);
        Assert.True(created.IsActive);
        await Assert.ThrowsAsync<ConflictException>(() => env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token));

        await env.StoreProducts.SetSellableAsync(created.Id, false, Token);
        Assert.False((await env.StoreProducts.GetAsync(created.Id, Token)).IsSellable);
        await env.Products.ChangeStatusAsync(product.Id, new ChangeProductStatusRequest("INACTIVE"), Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.StoreProducts.SetSellableAsync(created.Id, true, Token));
        await env.Products.ChangeStatusAsync(product.Id, new ChangeProductStatusRequest("ACTIVE"), Token);
        await env.StoreProducts.SetSellableAsync(created.Id, true, Token);

        var updated = await env.StoreProducts.UpdateAsync(created.Id, new UpdateStoreProductRequest("S-2", null), Token);
        Assert.Equal("S-2", updated.StoreSku);
        Assert.Null(updated.MinStockLevelBase);

        await env.StoreProducts.SetActiveAsync(created.Id, false, Token);
        var inactiveList = await env.StoreProducts.ListAsync(new StoreProductListRequest { IsActive = false, Search = product.Sku }, Token);
        Assert.Equal(created.Id, Assert.Single(inactiveList.Items).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => env.StoreProducts.GetAsync(Guid.NewGuid(), Token));
    }

    // ----- Public catalog -----

    [RealDbFact]
    public async Task Public_catalog_shows_only_active_sellable_products_without_internal_data()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var tag = Tag();
        var root = await NewCategoryAsync(env, $"{tag}R");
        var child = await NewCategoryAsync(env, $"{tag}K", root.Id);
        var brand = await env.Brands.CreateAsync(new BrandRequest($"Brand {tag}", null, null, "https://cdn.example.com/l.png"), Token);
        var ingredient = await env.Ingredients.CreateAsync(new ActiveIngredientRequest($"Ing {tag}", null, null), Token);

        var shown = await NewProductAsync(env, child.Id, $"{tag}1", brand.Id, Bottle(env, barcode: $"B{tag}"), Box(env, sale: false));
        await env.Products.AddPackagingAsync(
            shown.Id, new PackagingRequest(env.Carton, 24, false, true, true, "Carton"), Token);
        await env.Products.SetIngredientsAsync(shown.Id, new SetProductIngredientsRequest([new(ingredient.Id, "5%")]), Token);
        var hiddenNotSellable = await NewProductAsync(env, child.Id, $"{tag}2");
        var hiddenInactive = await NewProductAsync(env, child.Id, $"{tag}3");
        var hiddenNotInStore = await NewProductAsync(env, child.Id, $"{tag}4");
        var storeShown = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(shown.Id, "INTERNAL-SKU", 99), Token);
        var storeHidden1 = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(hiddenNotSellable.Id), Token);
        await env.StoreProducts.SetSellableAsync(storeHidden1.Id, false, Token);
        await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(hiddenInactive.Id), Token);
        await env.Products.ChangeStatusAsync(hiddenInactive.Id, new ChangeProductStatusRequest("INACTIVE"), Token);

        var list = await env.Catalog.GetProductsAsync(new CatalogProductListRequest { Search = tag }, Token);
        var item = Assert.Single(list.Items);
        // A product that is not in the store at all is never listed.
        Assert.DoesNotContain(list.Items, i => i.Sku == hiddenNotInStore.Sku);
        Assert.Equal(storeShown.Id, item.Id);
        Assert.Equal(shown.Sku, item.Sku);
        Assert.Equal(child.Name, item.CategoryName);
        Assert.Equal(brand.Name(), item.BrandName);
        // The category filter includes sub-categories.
        Assert.Equal(1, (await env.Catalog.GetProductsAsync(new CatalogProductListRequest { CategoryId = root.Id, Search = tag }, Token)).TotalCount);
        Assert.Equal(1, (await env.Catalog.GetProductsAsync(new CatalogProductListRequest { BrandId = brand.Id }, Token)).TotalCount);
        Assert.Equal(0, (await env.Catalog.GetProductsAsync(new CatalogProductListRequest { CategoryId = Guid.NewGuid() }, Token)).TotalCount);

        var detail = await env.Catalog.GetProductAsync(storeShown.Id, Token);
        Assert.Equal(storeShown.Id, detail.Id);
        // Base packaging plus sale packagings only (the box is not sold).
        Assert.Equal([1L, 24L], detail.Packagings.Select(p => p.ConversionToBase));
        Assert.Equal($"B{tag}", detail.Packagings[0].Barcode);
        Assert.True(detail.Packagings[0].IsBaseUnit);
        Assert.Equal(24, detail.Packagings[1].ConversionToBase);
        var ingredientEntry = Assert.Single(detail.Ingredients);
        Assert.Equal("5%", ingredientEntry.Concentration);

        // Nothing internal is part of the public contract.
        var publicFields = typeof(PublicProductResponse).GetProperties().Select(p => p.Name)
            .Concat(typeof(PublicProductListItem).GetProperties().Select(p => p.Name))
            .Concat(typeof(PublicPackaging).GetProperties().Select(p => p.Name)).ToList();
        foreach (var internalField in new[] { "StoreSku", "MinStockLevelBase", "RequiresLotTracking", "RequiresExpiryDate", "Status", "IsSellable", "IsActive", "CreatedAt", "DeletedBy" })
        {
            Assert.DoesNotContain(internalField, publicFields);
        }

        await Assert.ThrowsAsync<NotFoundException>(() => env.Catalog.GetProductAsync(storeHidden1.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Catalog.GetProductAsync(Guid.NewGuid(), Token));

        await env.StoreProducts.SetActiveAsync(storeShown.Id, false, Token);
        Assert.Equal(0, (await env.Catalog.GetProductsAsync(new CatalogProductListRequest { Search = tag }, Token)).TotalCount);
    }

    [RealDbFact]
    public async Task Public_categories_and_brands_list_only_active_entries()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var tag = Tag();
        var root = await NewCategoryAsync(env, $"{tag}R");
        var hidden = await NewCategoryAsync(env, $"{tag}H", root.Id);
        await NewCategoryAsync(env, $"{tag}G", hidden.Id);
        var activeBrand = await env.Brands.CreateAsync(new BrandRequest($"Active {tag}", null, null, null), Token);
        var inactiveBrand = await env.Brands.CreateAsync(new BrandRequest($"Inactive {tag}", null, null, null), Token);
        await env.Brands.SetActiveAsync(inactiveBrand.Id, false, Token);
        await env.Categories.SetActiveAsync(hidden.Id, false, Token);

        var tree = await env.Catalog.GetCategoriesAsync(Token);
        var brands = await env.Catalog.GetBrandsAsync(new AgriSage.Application.Common.Models.PaginationRequest { PageSize = 100 }, Token);

        Assert.Empty(tree.Single(n => n.Id == root.Id).Children);
        Assert.Contains(brands.Items, b => b.Id == activeBrand.Id);
        Assert.DoesNotContain(brands.Items, b => b.Id == inactiveBrand.Id);
    }
}

internal static class BrandResponseExtensions
{
    public static string Name(this BrandResponse brand) => brand.Name;
}
