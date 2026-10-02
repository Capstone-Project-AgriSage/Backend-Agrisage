using AgriSage.Application.Common;
using AgriSage.Application.Features.Products;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Validators;
using AgriSage.Domain.Features.Products.Enums;

namespace AgriSage.UnitTests.Application.Products;

public class CatalogRulesAndValidatorTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();

    // A → B → C (A is the root), D is separate.
    private static readonly Dictionary<Guid, Guid?> Parents = new() { [A] = null, [B] = A, [C] = B, [D] = null };

    [Fact]
    public void Moving_a_category_under_its_own_descendant_creates_a_cycle()
    {
        Assert.True(CatalogRules.WouldCreateCycle(A, C, Parents));
        Assert.True(CatalogRules.WouldCreateCycle(A, B, Parents));
        Assert.True(CatalogRules.WouldCreateCycle(B, C, Parents));
        Assert.True(CatalogRules.WouldCreateCycle(A, A, Parents));
    }

    [Fact]
    public void Valid_moves_do_not_create_a_cycle()
    {
        Assert.False(CatalogRules.WouldCreateCycle(C, A, Parents));
        Assert.False(CatalogRules.WouldCreateCycle(B, D, Parents));
        Assert.False(CatalogRules.WouldCreateCycle(A, null, Parents));
        Assert.False(CatalogRules.WouldCreateCycle(D, A, Parents));
    }

    [Fact]
    public void Existing_loop_in_the_data_is_reported_instead_of_hanging()
    {
        var loop = new Dictionary<Guid, Guid?> { [A] = B, [B] = A };

        Assert.True(CatalogRules.WouldCreateCycle(D, A, loop));
    }

    [Fact]
    public void Subtree_contains_the_category_and_all_descendants()
    {
        var categories = Parents.Select(p => (p.Key, p.Value)).ToList();

        Assert.Equal(new[] { A, B, C }.Order(), CatalogRules.SubtreeIds(A, categories).Order());
        Assert.Equal(new[] { B, C }.Order(), CatalogRules.SubtreeIds(B, categories).Order());
        Assert.Equal([D], CatalogRules.SubtreeIds(D, categories));
    }

    private static CategoryResponse Category(Guid id, Guid? parent, string name, bool active = true, int order = 0) =>
        new(id, parent, name.ToUpperInvariant(), name, null, order, active);

    [Fact]
    public void Tree_is_ordered_and_nested()
    {
        var tree = CatalogRules.BuildTree(
            [Category(B, A, "Child", order: 2), Category(A, null, "Root"), Category(C, A, "Other", order: 1), Category(D, null, "Alone", order: 5)],
            activeOnly: false);

        Assert.Equal(["Root", "Alone"], tree.Select(n => n.Name));
        Assert.Equal(["Other", "Child"], tree[0].Children.Select(n => n.Name));
    }

    [Fact]
    public void Active_only_tree_hides_inactive_categories_with_their_subtree()
    {
        var tree = CatalogRules.BuildTree(
            [Category(A, null, "Root"), Category(B, A, "Hidden", active: false), Category(C, B, "Grandchild"), Category(D, A, "Shown")],
            activeOnly: true);

        var root = Assert.Single(tree);
        Assert.Equal(["Shown"], root.Children.Select(n => n.Name));
    }

    [Fact]
    public void Sale_blocker_requires_an_active_product_with_active_base_and_sale_packagings()
    {
        (bool IsBase, bool IsSale, string Status)[] good =
            [(true, false, PackagingStatus.Active), (false, true, PackagingStatus.Active)];

        Assert.Null(CatalogRules.SaleBlocker(ProductStatus.Active, good));
        Assert.NotNull(CatalogRules.SaleBlocker(ProductStatus.Inactive, good));
        Assert.NotNull(CatalogRules.SaleBlocker(ProductStatus.Discontinued, good));
        Assert.NotNull(CatalogRules.SaleBlocker(ProductStatus.Active, []));
        // No sale packaging.
        Assert.NotNull(CatalogRules.SaleBlocker(ProductStatus.Active, [(true, false, PackagingStatus.Active)]));
        // The only sale packaging is inactive.
        Assert.NotNull(CatalogRules.SaleBlocker(
            ProductStatus.Active, [(true, false, PackagingStatus.Active), (false, true, PackagingStatus.Inactive)]));
        // The base packaging is inactive.
        Assert.NotNull(CatalogRules.SaleBlocker(
            ProductStatus.Active, [(true, true, PackagingStatus.Inactive), (false, true, PackagingStatus.Active)]));
        // A base packaging that is also sold is enough.
        Assert.Null(CatalogRules.SaleBlocker(ProductStatus.Active, [(true, true, PackagingStatus.Active)]));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.jpg", true)]
    [InlineData("http://cdn.example.com/a.jpg", false)]
    [InlineData("ftp://x/a.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/a.jpg", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_absolute_https_urls_are_image_urls(string? value, bool expected) =>
        Assert.Equal(expected, Texts.IsHttpsUrl(value));

    private static PackagingRequest Pk(Guid unit, long conversion, bool isBase, string? barcode = null) =>
        new(unit, conversion, isBase, true, true, null, barcode);

    private static CreateProductRequest Product(IReadOnlyList<PackagingRequest> packagings, string sku = "SKU-1", string? image = null) =>
        new(sku, "Product", A, packagings, ImageUrl: image);

    private readonly CreateProductRequestValidator _create = new();

    [Fact]
    public void Product_needs_exactly_one_base_packaging_with_unique_units_and_barcodes()
    {
        var bottle = Guid.NewGuid();
        var box = Guid.NewGuid();

        Assert.True(_create.Validate(Product([Pk(bottle, 1, true), Pk(box, 6, false, "111")])).IsValid);
        Assert.False(_create.Validate(Product([])).IsValid);
        Assert.False(_create.Validate(Product([Pk(box, 6, false)])).IsValid);
        Assert.False(_create.Validate(Product([Pk(bottle, 1, true), Pk(box, 1, true)])).IsValid);
        Assert.False(_create.Validate(Product([Pk(bottle, 1, true), Pk(bottle, 6, false)])).IsValid);
        Assert.False(_create.Validate(Product([Pk(bottle, 1, true, "111"), Pk(box, 6, false, " 111 ")])).IsValid);
    }

    [Fact]
    public void Packaging_conversion_must_be_positive_and_one_for_the_base()
    {
        var unit = Guid.NewGuid();

        Assert.False(_create.Validate(Product([Pk(unit, 0, true)])).IsValid);
        Assert.False(_create.Validate(Product([Pk(unit, -3, true)])).IsValid);
        Assert.False(_create.Validate(Product([Pk(unit, 6, true)])).IsValid);
    }

    [Fact]
    public void Product_fields_and_image_url_are_validated()
    {
        var unit = Guid.NewGuid();
        var packagings = new[] { Pk(unit, 1, true) };

        Assert.True(_create.Validate(Product(packagings, image: "https://cdn.example.com/p.jpg")).IsValid);
        Assert.False(_create.Validate(Product(packagings, image: "http://cdn.example.com/p.jpg")).IsValid);
        Assert.False(_create.Validate(Product(packagings, sku: "")).IsValid);
        Assert.False(_create.Validate(Product(packagings, sku: new string('s', 51))).IsValid);
        Assert.False(_create.Validate(Product(packagings) with { Name = new string('n', 256) }).IsValid);
        Assert.False(_create.Validate(Product(packagings) with { CategoryId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Category_and_brand_requests_are_validated()
    {
        Assert.True(new CreateCategoryRequestValidator().Validate(new CreateCategoryRequest("PEST", "Pesticides", null, 0, null)).IsValid);
        Assert.False(new CreateCategoryRequestValidator().Validate(new CreateCategoryRequest("", "Pesticides", null, 0, null)).IsValid);
        Assert.False(new CreateCategoryRequestValidator().Validate(new CreateCategoryRequest("PEST", "x", null, -1, null)).IsValid);
        Assert.False(new UpdateCategoryRequestValidator().Validate(new UpdateCategoryRequest("", null, 0, null)).IsValid);

        var brand = new BrandRequestValidator();
        Assert.True(brand.Validate(new BrandRequest("Brand", null, null, "https://cdn.example.com/l.png")).IsValid);
        Assert.False(brand.Validate(new BrandRequest("", null, null, null)).IsValid);
        Assert.False(brand.Validate(new BrandRequest("Brand", null, null, "http://x/l.png")).IsValid);
        Assert.False(new ActiveIngredientRequestValidator().Validate(new ActiveIngredientRequest(new string('a', 201), null, null)).IsValid);
    }

    [Fact]
    public void Packaging_status_and_product_status_values_are_checked()
    {
        var packaging = new UpdatePackagingRequestValidator();
        Assert.True(packaging.Validate(new UpdatePackagingRequest(true, true, "ACTIVE")).IsValid);
        Assert.True(packaging.Validate(new UpdatePackagingRequest(true, true, "INACTIVE")).IsValid);
        Assert.False(packaging.Validate(new UpdatePackagingRequest(true, true, "DISCONTINUED")).IsValid);
        Assert.False(packaging.Validate(new UpdatePackagingRequest(true, true, "active")).IsValid);

        var status = new ChangeProductStatusRequestValidator();
        Assert.True(status.Validate(new ChangeProductStatusRequest("discontinued")).IsValid);
        Assert.False(status.Validate(new ChangeProductStatusRequest("DELETED")).IsValid);
    }

    [Fact]
    public void Ingredients_must_be_unique_and_bounded()
    {
        var id = Guid.NewGuid();
        var validator = new SetProductIngredientsRequestValidator();

        Assert.True(validator.Validate(new SetProductIngredientsRequest([])).IsValid);
        Assert.True(validator.Validate(new SetProductIngredientsRequest([new(id, "50%", null)])).IsValid);
        Assert.False(validator.Validate(new SetProductIngredientsRequest([new(id), new(id)])).IsValid);
        Assert.False(validator.Validate(new SetProductIngredientsRequest([new(id, new string('c', 101))])).IsValid);
    }

    [Fact]
    public void List_requests_validate_paging_and_status()
    {
        Assert.True(new ProductListRequestValidator().Validate(new ProductListRequest { Status = "active" }).IsValid);
        Assert.False(new ProductListRequestValidator().Validate(new ProductListRequest { Status = "BOGUS" }).IsValid);
        Assert.False(new ProductListRequestValidator().Validate(new ProductListRequest { PageSize = 0 }).IsValid);
        Assert.False(new CatalogProductListRequestValidator().Validate(new CatalogProductListRequest { PageSize = 101 }).IsValid);
        Assert.False(new CatalogProductListRequestValidator().Validate(new CatalogProductListRequest { Search = new string('a', 101) }).IsValid);
        Assert.False(new CreateStoreProductRequestValidator().Validate(new CreateStoreProductRequest(Guid.NewGuid(), null, -1)).IsValid);
        Assert.True(new CreateStoreProductRequestValidator().Validate(new CreateStoreProductRequest(Guid.NewGuid(), "S-1", 0)).IsValid);
    }
}
