// Creates demo data in a RUNNING API so the front end and testers have products, prices and stock to work with.
// Every code starts with DEMO-. It goes through the public API (never straight into the database), so stock appears
// only through confirmed goods receipts and every business rule is applied. Safe to run again: what exists is kept.
//
//   dotnet run scripts/SeedDemoData.cs -- [--base-url http://localhost:5206] [--accounts-file <path>]
//
// Admin login, first match wins (the password is never printed):
//   1. environment AGRISAGE_ADMIN_IDENTIFIER + AGRISAGE_ADMIN_PASSWORD
//   2. AdminBootstrap:Email / :Password in src/AgriSage.Api/appsettings.Local.json (gitignored)
//   3. a prompt
// Passwords of the demo staff accounts are generated, written only to --accounts-file (keep it OUT of git), never printed.
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

Console.OutputEncoding = Encoding.UTF8;

var baseUrl = Arg("--base-url") ?? "http://localhost:5206";
var accountsFile = Arg("--accounts-file");
var today = DateOnly.FromDateTime(DateTime.UtcNow);

// ---- the demo catalog (VND). Conversion is in base units: 1 bao = 50 kg, 1 hộp = 20 gói, 1 chai = 100 ml ... ----
var specs = new ProductSpec[]
{
    new("DEMO-NPK-168", "Phân NPK 16-16-8 (bao 50 kg)", "DEMO-PHAN-BON", true, "DEMO-NCC-01",
        "Phân bón tổng hợp NPK 16-16-8, dùng bón lót và bón thúc cho lúa, màu, cây ăn trái.", "Bón 200-250 kg/ha tuỳ giai đoạn.",
        [new("KG", 1, true, false, true, null, 14000), new("BAG", 50, false, true, true, "Bao 50 kg", 640000)],
        [new("BAG", 40, 520000, 300, "DEMO-NPK-L1", 120), new("BAG", 60, 525000, 540, "DEMO-NPK-L2", 60), new("BAG", 100, 530000, 720, "DEMO-NPK-L3", 10)]),
    new("DEMO-URE", "Phân Ure (bao 50 kg)", "DEMO-PHAN-BON", false, "DEMO-NCC-01",
        "Phân đạm Ure hạt đục, hàm lượng đạm 46%. Hàng không ghi hạn dùng.", "Bón thúc 100-150 kg/ha.",
        [new("KG", 1, true, false, true, null, 11500), new("BAG", 50, false, true, true, "Bao 50 kg", 520000)],
        [new("BAG", 80, 440000, null, "DEMO-URE-L1", 90), new("BAG", 120, 445000, null, "DEMO-URE-L2", 20)]),
    new("DEMO-LUA-ST25", "Lúa giống ST25 (bao 25 kg)", "DEMO-GIONG", true, "DEMO-NCC-01",
        "Lúa giống xác nhận ST25, tỷ lệ nảy mầm trên 85%.", "Gieo sạ 80-100 kg/ha.",
        [new("KG", 1, true, false, true, null, 32000), new("BAG", 25, false, true, true, "Bao 25 kg", 780000)],
        [new("BAG", 30, 650000, 90, "DEMO-ST25-L1", 100), new("BAG", 50, 655000, 200, "DEMO-ST25-L2", 30)]),
    new("DEMO-HUUCO", "Phân hữu cơ vi sinh (bao 20 kg) - chưa có giá", "DEMO-PHAN-BON", false, "DEMO-NCC-01",
        "Cố tình KHÔNG có giá trong bảng giá, để thử lỗi \"thiếu giá\" khi tạo đơn.", null,
        [new("KG", 1, true, false, true, null, null), new("BAG", 20, false, true, true, "Bao 20 kg", null)],
        [new("BAG", 10, 120000, null, "DEMO-HC-L1", 15)]),
    new("DEMO-ACTARA", "Thuốc trừ sâu Actara 25WG", "DEMO-THUOC-BVTV", true, "DEMO-NCC-02",
        "Trừ rầy nâu, rầy chổng cánh, bọ trĩ trên lúa và cây ăn trái.", "Pha 1 gói cho bình 16 lít, phun khi rầy xuất hiện.",
        [new("PACK", 1, true, false, true, "Gói", 5500), new("BOX", 20, false, true, true, "Hộp 20 gói", 95000), new("CARTON", 400, false, true, false, "Thùng 20 hộp", null)],
        [new("BOX", 50, 70000, 60, "DEMO-ACT-L1", 150), new("BOX", 100, 72000, 400, "DEMO-ACT-L2", 40), new("CARTON", 2, 1350000, 500, "DEMO-ACT-L3", 20)]),
    new("DEMO-FILIA", "Thuốc trừ bệnh đạo ôn Filia 525SE (chai 100 ml)", "DEMO-THUOC-BVTV", true, "DEMO-NCC-02",
        "Phòng trừ bệnh đạo ôn (cháy lá) và lem lép hạt trên lúa.", "Pha 1 chai cho 1 ha, phun 2 lần cách nhau 7-10 ngày.",
        [new("ML", 1, true, false, false, null, null), new("BOTTLE", 100, false, true, true, "Chai 100 ml", 165000), new("CARTON", 2400, false, true, false, "Thùng 24 chai", null)],
        [new("BOTTLE", 10, 125000, 45, "DEMO-FIL-L1", 200), new("CARTON", 2, 2900000, 365, "DEMO-FIL-L2", 30)]),
    new("DEMO-SOFIT", "Thuốc trừ cỏ Sofit 300EC (chai 1 lít) - tồn thấp", "DEMO-THUOC-BVTV", true, "DEMO-NCC-02",
        "Trừ cỏ tiền nảy mầm trên ruộng lúa sạ. Chỉ nhập 5 chai để thử lỗi \"không đủ hàng\".", "Phun sau sạ 0-3 ngày.",
        [new("ML", 1, true, false, false, null, null), new("BOTTLE", 1000, false, true, true, "Chai 1 lít", 265000)],
        [new("BOTTLE", 5, 210000, 400, "DEMO-SOF-L1", 25)])
};

using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(90) };

try
{
    await Run();
}
catch (ApiException ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"FAILED: cannot reach {baseUrl}: {ex.Message}. Start the API first.");
    Environment.ExitCode = 1;
}

async Task Run()
{
    await LoginAsync();

    // ---- units (seeded reference data, mapped by code) ----
    var units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var unit in Items(await Get("api/units?pageSize=100")))
    {
        units[Text(unit, "code")] = Text(unit, "id");
    }

    string UnitId(string code) => units.TryGetValue(code, out var id)
        ? id
        : throw new InvalidOperationException($"Unit {code} is missing: run the reference seed first (--seed).");

    // ---- categories ----
    var categories = new Dictionary<string, string>();
    foreach (var (code, name, order) in new[]
             {
                 ("DEMO-PHAN-BON", "Phân bón", 1), ("DEMO-THUOC-BVTV", "Thuốc bảo vệ thực vật", 2), ("DEMO-GIONG", "Giống cây trồng", 3)
             })
    {
        var found = await Find("api/categories", code, "code", code);
        categories[code] = found is not null
            ? Text(found, "id")
            : Text(await Send(HttpMethod.Post, "api/categories", new { code, name, displayOrder = order }), "id");
        Console.WriteLine($"  category {code}: {(found is null ? "created" : "exists")}");
    }

    // ---- suppliers ----
    var suppliers = new Dictionary<string, string>();
    foreach (var (code, name, phone, province) in new[]
             {
                 ("DEMO-NCC-01", "Công ty TNHH Vật tư Nông nghiệp Miền Tây", "0292 3800 111", "Cần Thơ"),
                 ("DEMO-NCC-02", "Công ty CP Thuốc BVTV Sài Gòn", "028 3900 222", "TP. Hồ Chí Minh")
             })
    {
        var found = await Find("api/suppliers", code, "code", code);
        suppliers[code] = found is not null
            ? Text(found, "id")
            : Text(await Send(HttpMethod.Post, "api/suppliers", new { code, name, phoneNumber = phone, province }), "id");
        Console.WriteLine($"  supplier {code}: {(found is null ? "created" : "exists")}");
    }

    // ---- products, store products ----
    var catalog = new List<ProductInfo>();
    foreach (var spec in specs)
    {
        var product = await Find("api/products", spec.Sku, "sku", spec.Sku);
        var created = product is null;
        if (created)
        {
            product = await Send(HttpMethod.Post, "api/products", new
            {
                sku = spec.Sku,
                name = spec.Name,
                categoryId = categories[spec.Category],
                description = spec.Description,
                usageInstructions = spec.Usage,
                requiresLotTracking = true,
                requiresExpiryDate = spec.Expiry,
                packagings = spec.Packs.Select(p => new
                {
                    unitId = UnitId(p.Unit),
                    conversionToBase = p.Conversion,
                    isBaseUnit = p.IsBase,
                    isPurchaseUnit = p.IsPurchase,
                    isSaleUnit = p.IsSale,
                    packagingName = p.Name
                }).ToArray()
            });
        }

        var productId = Text(product!, "id");
        var detail = await Get($"api/products/{productId}");
        var packagingByUnit = detail["packagings"]!.AsArray()
            .ToDictionary(p => Text(p!, "unitCode"), p => Text(p!, "id"), StringComparer.OrdinalIgnoreCase);

        var storeProduct = await Find("api/store-products", spec.Sku, "sku", spec.Sku);
        if (storeProduct is null)
        {
            storeProduct = await Send(HttpMethod.Post, "api/store-products", new { productId });
        }

        var storeProductId = Text(storeProduct, "id");
        if (storeProduct["isSellable"]?.GetValue<bool>() != true)
        {
            await Send(HttpMethod.Post, $"api/store-products/{storeProductId}/mark-sellable");
        }

        catalog.Add(new ProductInfo(spec, storeProductId, packagingByUnit));
        Console.WriteLine($"  product {spec.Sku}: {(created ? "created" : "exists")}");
    }

    // ---- retail price list (the walk-in default); an existing ACTIVE one of someone else is never replaced ----
    var priceList = await Find("api/price-lists", "DEMO-BANGIA-LE", "code", "DEMO-BANGIA-LE");
    if (priceList is null)
    {
        priceList = await Send(HttpMethod.Post, "api/price-lists", new
        {
            code = "DEMO-BANGIA-LE",
            name = "Bảng giá khách lẻ (demo)",
            description = "Dữ liệu mẫu để thử bán hàng tại quầy",
            effectiveFrom = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            isWalkInDefault = true
        });
        Console.WriteLine("  price list DEMO-BANGIA-LE: created");
    }
    else
    {
        Console.WriteLine("  price list DEMO-BANGIA-LE: exists");
    }

    var priceListId = Text(priceList, "id");
    var priced = catalog.SelectMany(p => p.Spec.Packs.Where(x => x.Price is not null).Select(x => new
    {
        storeProductId = p.StoreProductId,
        productPackagingId = p.PackagingByUnit[x.Unit],
        sellingPrice = x.Price
    })).ToArray();
    await Send(HttpMethod.Put, $"api/price-lists/{priceListId}/items", new { items = priced });
    if (Text(priceList, "status") != "ACTIVE")
    {
        try
        {
            await Send(HttpMethod.Post, $"api/price-lists/{priceListId}/activate");
            Console.WriteLine("  price list DEMO-BANGIA-LE: activated");
        }
        catch (ApiException ex) when (ex.Status is 409 or 422)
        {
            Console.WriteLine($"  price list stays DRAFT: another walk-in price list is already ACTIVE ({ex.Status}). Left untouched.");
        }
    }

    // ---- stock: goods receipts, so lots, stock movements and the weighted-average cost are created by the real use case ----
    foreach (var (supplierCode, invoice) in new[] { ("DEMO-NCC-01", "DEMO-HD-001"), ("DEMO-NCC-02", "DEMO-HD-002") })
    {
        var supplierId = suppliers[supplierCode];
        var existing = Items(await Get($"api/goods-receipts?supplierId={supplierId}&pageSize=100"))
            .FirstOrDefault(r => Text(r, "supplierInvoiceNumber") == invoice);
        if (existing is not null && Text(existing, "status") != "DRAFT")
        {
            Console.WriteLine($"  receipt {invoice}: exists ({Text(existing, "status")})");
            continue;
        }

        var receiptId = existing is not null ? Text(existing, "id") : null;
        if (receiptId is null)
        {
            var lines = catalog.Where(p => p.Spec.Supplier == supplierCode).SelectMany(p => p.Spec.Lots.Select(lot => new
            {
                storeProductId = p.StoreProductId,
                productPackagingId = p.PackagingByUnit[lot.Unit],
                receivedQuantity = lot.Quantity,
                purchaseUnitCost = lot.Cost,
                supplierLotNumber = lot.Number,
                manufacturingDate = today.AddDays(-lot.AgeDays).ToString("yyyy-MM-dd"),
                expiryDate = lot.ExpiresInDays is { } days ? today.AddDays(days).ToString("yyyy-MM-dd") : null
            })).ToArray();
            var receipt = await Send(HttpMethod.Post, "api/goods-receipts", new
            {
                supplierId,
                supplierInvoiceNumber = invoice,
                supplierInvoiceDate = today.ToString("yyyy-MM-dd"),
                note = "Dữ liệu mẫu",
                items = lines
            });
            receiptId = Text(receipt, "id");
        }

        await Send(HttpMethod.Post, $"api/goods-receipts/{receiptId}/confirm");
        Console.WriteLine($"  receipt {invoice}: created and confirmed");
    }

    // ---- demo staff accounts ----
    var newAccounts = new List<string>();
    foreach (var (role, name, email, phone) in new[]
             {
                 ("STORE_OWNER", "Chủ cửa hàng Demo", "demo.owner@example.com", "0900000102"),
                 ("SALES_STAFF", "Nhân viên bán hàng Demo", "demo.sales@example.com", "0900000101"),
                 ("DELIVERY_STAFF", "Nhân viên giao hàng Demo", "demo.delivery@example.com", "0900000103")
             })
    {
        var found = await Find("api/staff", "demo", "email", email);
        if (found is not null)
        {
            Console.WriteLine($"  account {email}: exists (password unchanged)");
            continue;
        }

        var password = NewPassword();
        await Send(HttpMethod.Post, "api/staff", new { fullName = name, role, phoneNumber = phone, email, password });
        newAccounts.Add($"{role,-15} | {email,-26} | {password}");
        Console.WriteLine($"  account {email}: created");
    }

    if (newAccounts.Count > 0)
    {
        if (accountsFile is null)
        {
            Console.WriteLine("  (no --accounts-file given: the new passwords were NOT saved anywhere; reset them with POST /api/staff/{id}/reset-password)");
        }
        else
        {
            await File.AppendAllLinesAsync(accountsFile, newAccounts.Prepend($"# demo accounts created {DateTime.Now:yyyy-MM-dd HH:mm}"));
            Console.WriteLine($"  passwords of the {newAccounts.Count} new account(s) saved to {accountsFile}");
        }
    }

    Console.WriteLine("Done.");
}

async Task LoginAsync()
{
    var identifier = Environment.GetEnvironmentVariable("AGRISAGE_ADMIN_IDENTIFIER");
    var password = Environment.GetEnvironmentVariable("AGRISAGE_ADMIN_PASSWORD");
    if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
    {
        var local = FindUp(Path.Combine("src", "AgriSage.Api", "appsettings.Local.json"));
        if (local is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(local), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("AdminBootstrap", out var bootstrap))
            {
                identifier = bootstrap.TryGetProperty("Email", out var e) ? e.GetString() : null;
                password = bootstrap.TryGetProperty("Password", out var p) ? p.GetString() : null;
            }
        }
    }

    if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
    {
        Console.Write("Admin email or phone: ");
        identifier = Console.ReadLine();
        Console.Write("Admin password (hidden): ");
        password = ReadHidden();
    }

    var login = await Send(HttpMethod.Post, "api/auth/login", new { identifier, password });
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Text(login, "accessToken"));
    Console.WriteLine($"Logged in as {Text(login["user"]!, "role")} against {baseUrl}");
}

// ---- helpers ----
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string? FindUp(string relative)
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, relative);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static string ReadHidden()
{
    var sb = new StringBuilder();
    for (var key = Console.ReadKey(true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(true))
    {
        if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
        {
            sb.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            sb.Append(key.KeyChar);
        }
    }

    Console.WriteLine();
    return sb.ToString();
}

static string NewPassword()
{
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnpqrstuvwxyz", digits = "23456789", symbols = "@#$%&*?";
    var all = upper + lower + digits + symbols;
    var chars = new List<char>
    {
        upper[RandomNumberGenerator.GetInt32(upper.Length)], lower[RandomNumberGenerator.GetInt32(lower.Length)],
        digits[RandomNumberGenerator.GetInt32(digits.Length)], symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
    };
    while (chars.Count < 16)
    {
        chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
    }

    return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());
}

static string Text(JsonNode node, string field) => node[field]?.GetValue<string>() ?? "";

static IEnumerable<JsonNode> Items(JsonNode page) => page["items"]!.AsArray().Select(x => x!);

Task<JsonNode> Get(string path) => Send(HttpMethod.Get, path);

async Task<JsonNode?> Find(string listPath, string search, string field, string value) =>
    Items(await Get($"{listPath}?search={Uri.EscapeDataString(search)}&pageSize=100"))
        .FirstOrDefault(x => string.Equals(Text(x, field), value, StringComparison.OrdinalIgnoreCase));

async Task<JsonNode> Send(HttpMethod method, string path, object? body = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
    {
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }

    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new ApiException(method, path, (int)response.StatusCode, text);
    }

    return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!;
}

record PackSpec(string Unit, long Conversion, bool IsBase, bool IsPurchase, bool IsSale, string? Name, decimal? Price);

record LotSpec(string Unit, long Quantity, decimal Cost, int? ExpiresInDays, string Number, int AgeDays);

record ProductSpec(string Sku, string Name, string Category, bool Expiry, string Supplier, string Description, string? Usage, PackSpec[] Packs, LotSpec[] Lots);

record ProductInfo(ProductSpec Spec, string StoreProductId, Dictionary<string, string> PackagingByUnit);

class ApiException(HttpMethod method, string path, int status, string body)
    : Exception($"{method} {path} -> {status} {(body.Length > 600 ? body[..600] : body)}")
{
    public int Status { get; } = status;
}
