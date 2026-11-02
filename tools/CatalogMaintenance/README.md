# Catalog image maintenance

This operator tool uses the project's existing configuration, image upload service
and brand service. Credentials are read in memory and never printed. No schema
changes are required.

Run from the backend repository:

```powershell
dotnet run --project tools/CatalogMaintenance -- list
$env:SHARP_MODULE = '<path to installed sharp module>'
node tools/CatalogMaintenance/prepare-logos.mjs
dotnet run --project tools/CatalogMaintenance -- import-logos tools/CatalogMaintenance/logos-prepared.json --apply
dotnet run --project tools/CatalogMaintenance -- verify
```

Preparation downloads original artwork and converts it to PNG for the existing
upload pipeline. `prepare-logos.mjs` records each source URL and source website in
the generated manifest. Hokko uses the logo region of its official CSS sprite;
Asiatic uses its sponsor artwork for Africa Singapore Business Forum 2025.

Import only fills missing logos for exact matching existing brand codes. Existing
logos are preserved unless a manifest entry explicitly specifies the exact current
`ExpectedLogoUrl`. A failed database update removes the newly uploaded file.
Verification checks all logo URLs and tests category reordering inside a transaction
that is always rolled back. Do not run verification concurrently with category edits.

## Missing product photos

```powershell
dotnet run --project tools/CatalogMaintenance -- list-products
$env:SHARP_MODULE = '<path to installed sharp module>'
node tools/CatalogMaintenance/prepare-product-images.mjs
# Inspect products-preview.png before applying.
dotnet run --project tools/CatalogMaintenance -- import-product-images tools/CatalogMaintenance/products-prepared.json --apply
dotnet run --project tools/CatalogMaintenance -- verify-product-images tools/CatalogMaintenance/products-before-<timestamp>.json
```

The preparation script records each original image URL and source page. It caches
prepared images; pass `--refresh` after changing a source. Packaging sizes in source
photos can differ from the store's sale units. The unbranded organic fertilizer uses
a representative 20 kg bag approved by the user. No generated packaging is used.
Prepared photos are exported as WebP at quality 85 to keep storage uploads small.

Import matches exact existing SKUs, fills active products only, and skips every
nonempty image URL. A pre-import snapshot is saved locally. Existing product
details are passed unchanged through the existing product service; prices, stock,
packagings and statuses are not edited. Failed product updates remove their new
uploads. Verification checks old photos remain unchanged, other product fields
of active products match the snapshot, every active product has a photo, and every
URL returns an image. Unrelated edits to inactive products are reported separately.
Avoid concurrent product edits while running this operator command.
