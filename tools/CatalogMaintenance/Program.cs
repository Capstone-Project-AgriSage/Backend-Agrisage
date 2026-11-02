using System.Text.Json;
using AgriSage.Application;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Interfaces;
using AgriSage.Infrastructure;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Explicit operator commands. Reads configured secrets without printing them, uses the existing
// image upload and catalog services, and fills missing images for exact matching codes/SKUs.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var apiPath = Path.Combine(root, "src/AgriSage.Api");
var secretsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Microsoft/UserSecrets/6846d3dd-c916-4f07-a77a-fc69a19797d2/secrets.json");
var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(apiPath, "appsettings.json"))
    .AddJsonFile(Path.Combine(apiPath, "appsettings.Development.json"))
    .AddJsonFile(secretsPath, optional: true).AddEnvironmentVariables()
    .AddJsonFile(Path.Combine(apiPath, "appsettings.Local.json"), optional: true).Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddApplication();
services.AddInfrastructure(config);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<IAgriSageDbContext>();
try
{
    if (args.Length == 3 && args[0] == "import-product-images" && args[2] == "--apply")
    {
        var manifestPath = Path.GetFullPath(args[1]);
        var entries = JsonSerializer.Deserialize<ProductImageEntry[]>(await File.ReadAllTextAsync(manifestPath))!;
        if (entries.Select(e => e.Sku).Distinct().Count() != entries.Length)
            throw new ArgumentException("Duplicate product SKU in manifest");
        var before = await ProductSnapshots(db);
        var snapshotPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, $"products-before-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(before, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Snapshot: {snapshotPath}");
        var images = scope.ServiceProvider.GetRequiredService<IProductImageService>();
        var catalog = scope.ServiceProvider.GetRequiredService<IProductService>();
        var saved = 0;
        foreach (var entry in entries)
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Sku == entry.Sku);
            if (product is null || product.Status != ProductStatus.Active || !string.IsNullOrWhiteSpace(product.ImageUrl))
            {
                Console.WriteLine($"SKIP {entry.Sku}: absent, inactive or already has an image");
                continue;
            }
            var path = Path.GetFullPath(entry.Source, Path.GetDirectoryName(manifestPath)!);
            await using var stream = File.OpenRead(path);
            var upload = await images.UploadAsync(stream, CancellationToken.None);
            // Read again after upload: preserve a photo or details added while downloading/uploading.
            product = await db.Products.AsNoTracking().SingleAsync(p => p.Sku == entry.Sku);
            if (product.Status != ProductStatus.Active || !string.IsNullOrWhiteSpace(product.ImageUrl))
            {
                await images.DeleteAsync(upload.StorageKey, CancellationToken.None);
                Console.WriteLine($"SKIP {entry.Sku}: product changed during upload");
                continue;
            }
            try
            {
                await catalog.UpdateAsync(product.Id, new UpdateProductRequest(product.Name, product.CategoryId,
                    product.BrandId, product.Description, product.UsageInstructions, upload.Url), CancellationToken.None);
            }
            catch
            {
                await images.DeleteAsync(upload.StorageKey, CancellationToken.None);
                throw;
            }
            saved++;
            Console.WriteLine($"SAVED {entry.Sku}: {upload.Url}");
        }
        Console.WriteLine($"Saved {saved} product images; existing images preserved.");
        return;
    }
    if (args.Length == 2 && args[0] == "verify-product-images")
    {
        var before = JsonSerializer.Deserialize<ProductSnapshot[]>(await File.ReadAllTextAsync(args[1]))!;
        var after = await ProductSnapshots(db);
        if (before.Length != after.Count) throw new InvalidOperationException("Product count changed");
        var added = 0;
        var preserved = 0;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var product in after)
        {
            var previous = before.Single(p => p.Id == product.Id);
            if (previous with { ImageUrl = product.ImageUrl } != product)
            {
                if (previous.Status == ProductStatus.Active || product.Status == ProductStatus.Active)
                    throw new InvalidOperationException($"Active product details changed: {product.Sku}");
                // This import never updates inactive products; do not overwrite unrelated operator edits.
                Console.WriteLine($"NOTICE {product.Sku}: untouched inactive product details changed outside this import");
            }
            if (!string.IsNullOrWhiteSpace(previous.ImageUrl))
            {
                if (previous.ImageUrl != product.ImageUrl) throw new InvalidOperationException($"Existing image changed: {product.Sku}");
                preserved++;
            }
            else if (!string.IsNullOrWhiteSpace(product.ImageUrl)) added++;
            if (product.Status == ProductStatus.Active && string.IsNullOrWhiteSpace(product.ImageUrl))
                throw new InvalidOperationException($"Active product has no image: {product.Sku}");
            if (string.IsNullOrWhiteSpace(product.ImageUrl)) continue;
            using var response = await http.GetAsync(product.ImageUrl);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true)
                throw new InvalidOperationException($"Not an image: {product.Sku}");
            Console.WriteLine($"VERIFIED {product.Sku}");
        }
        Console.WriteLine($"Verified: {added} images added, {preserved} existing images unchanged; active product details preserved; all active products have accessible photos.");
        return;
    }
    if (args.Length > 0 && args[0] == "list-products")
    {
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Sku, p.Name, Brand = p.Brand == null ? null : p.Brand.Name, p.ImageUrl, p.Status }).ToListAsync();
        Console.WriteLine(JsonSerializer.Serialize(products, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    var brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
    if (args.Length == 1 && args[0] == "verify")
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var brand in brands)
        {
            if (string.IsNullOrWhiteSpace(brand.LogoUrl)) throw new InvalidOperationException("A brand has no logo");
            using var response = await http.GetAsync(brand.LogoUrl);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true)
                throw new InvalidOperationException("A logo is not an image");
            Console.WriteLine($"VERIFIED {brand.Code}");
        }
        Console.WriteLine($"All {brands.Count} brand logos are accessible.");
        await using var transaction = await db.BeginTransactionAsync();
        try
        {
            var before = await db.Categories.AsNoTracking().Where(c => c.ParentId == null)
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ThenBy(c => c.Id).ToListAsync();
            if (before.Count < 2) throw new InvalidOperationException("Need two categories for verification");
            var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
            await categories.ReorderAsync(before[1].Id, 0, CancellationToken.None);
            var after = await db.Categories.AsNoTracking().Where(c => c.ParentId == null)
                .OrderBy(c => c.DisplayOrder).ToListAsync();
            if (after.Count != before.Count || after[0].Id != before[1].Id ||
                !after.Select(c => c.DisplayOrder).SequenceEqual(Enumerable.Range(0, after.Count)))
                throw new InvalidOperationException("Reorder verification failed");
            Console.WriteLine("VERIFIED category reorder and contiguous positions (rolled back).");
        }
        finally { await transaction.RollbackAsync(); }
        return;
    }
    if (args.Length == 0 || args[0] == "list")
    {
        Console.WriteLine(JsonSerializer.Serialize(brands.Select(b => new { b.Id, b.Code, b.Name, b.LogoUrl })));
        return;
    }
    if (args[0] == "import-logos" && args.Length == 3 && args[2] == "--apply")
    {
        var manifestPath = Path.GetFullPath(args[1]);
        var entries = JsonSerializer.Deserialize<LogoEntry[]>(await File.ReadAllTextAsync(manifestPath))!;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var images = scope.ServiceProvider.GetRequiredService<IProductImageService>();
        var catalog = scope.ServiceProvider.GetRequiredService<IBrandService>();
        foreach (var entry in entries)
        {
            var brand = brands.SingleOrDefault(b => b.Code == entry.Code);
            if (brand is null || (!string.IsNullOrWhiteSpace(brand.LogoUrl) && brand.LogoUrl != entry.ExpectedLogoUrl))
            {
                Console.WriteLine($"SKIP {entry.Code}: absent or already has a logo");
                continue;
            }
            var bytes = Uri.TryCreate(entry.Source, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                ? await http.GetByteArrayAsync(uri)
                : await File.ReadAllBytesAsync(Path.GetFullPath(entry.Source, Path.GetDirectoryName(manifestPath)!));
            await using var stream = new MemoryStream(bytes);
            var upload = await images.UploadAsync(stream, CancellationToken.None);
            try
            {
                await catalog.UpdateAsync(brand.Id, new BrandRequest(brand.Name, brand.Code, brand.Description, upload.Url), CancellationToken.None);
            }
            catch
            {
                await images.DeleteAsync(upload.StorageKey, CancellationToken.None);
                throw;
            }
            using var response = await http.GetAsync(upload.Url);
            response.EnsureSuccessStatusCode();
            Console.WriteLine($"SAVED {entry.Code}: {upload.Url} ({bytes.Length} bytes)");
        }
        return;
    }
    throw new ArgumentException("Use list, list-products, import-logos/import-product-images <manifest.json> --apply, or verify-product-images <snapshot.json>");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Catalog operation failed: {ex.GetType().Name}. No credentials were printed.");
    Environment.ExitCode = 1;
}

static Task<List<ProductSnapshot>> ProductSnapshots(IAgriSageDbContext context) =>
    context.Products.AsNoTracking().OrderBy(p => p.Sku).Select(p => new ProductSnapshot(
        p.Id, p.Sku, p.Name, p.CategoryId, p.BrandId, p.Description, p.UsageInstructions,
        p.RequiresLotTracking, p.RequiresExpiryDate, p.Status, p.ImageUrl)).ToListAsync();

record LogoEntry(string Code, string Source, string? ExpectedLogoUrl = null);
record ProductImageEntry(string Sku, string Source);
record ProductSnapshot(Guid Id, string Sku, string Name, Guid CategoryId, Guid? BrandId,
    string? Description, string? UsageInstructions, bool RequiresLotTracking, bool RequiresExpiryDate,
    ProductStatus Status, string? ImageUrl);
