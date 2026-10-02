using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Model/catalog/configuration checks only; never opens a database connection.
public class ReferenceSeedDataTests
{
    [Fact]
    public void Roles_match_the_five_approved_codes_and_english_names()
    {
        Assert.Equal(Enum.GetValues<RoleCode>().Order(), RoleSeeder.Entries.Select(entry => entry.Code).Order());
        Assert.Equal(["Farmer", "Store Owner", "Sales Staff", "Delivery Staff", "Admin"],
            RoleSeeder.Entries.Select(entry => entry.Name));
        using var context = ModelContext();
        var entity = context.Model.FindEntityType(typeof(Role))!;
        var code = entity.FindProperty(nameof(Role.Code))!;
        var converter = code.GetTypeMapping().Converter!;
        Assert.Equal(["FARMER", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF", "ADMIN"],
            RoleSeeder.Entries.Select(entry => (string)converter.ConvertToProvider(entry.Code)!));
        Assert.All(RoleSeeder.Entries, entry =>
        {
            Assert.True(((string)converter.ConvertToProvider(entry.Code)!).Length <= code.GetMaxLength());
            Assert.True(entry.Name.Length <= entity.FindProperty(nameof(Role.Name))!.GetMaxLength());
        });
    }

    [Fact]
    public void Units_match_all_nine_approved_code_name_symbol_triples_and_column_lengths()
    {
        Assert.Equal(
            [new("BOTTLE", "Chai", "chai"), new("BOX", "Hộp", "hộp"), new("CARTON", "Thùng", "thùng"),
                new("BAG", "Bao", "bao"), new("PACK", "Gói", "gói"), new("KG", "Kilogram", "kg"),
                new("GRAM", "Gram", "g"), new("LITER", "Lít", "l"), new UnitSeeder.Entry("ML", "Mililit", "ml")],
            UnitSeeder.Entries);
        Assert.Equal(9, UnitSeeder.Entries.Select(entry => entry.Code).Distinct().Count());
        using var context = ModelContext();
        var entity = context.Model.FindEntityType(typeof(Unit))!;
        Assert.All(UnitSeeder.Entries, entry =>
        {
            Assert.True(entry.Code.Length <= entity.FindProperty(nameof(Unit.Code))!.GetMaxLength());
            Assert.True(entry.Name.Length <= entity.FindProperty(nameof(Unit.Name))!.GetMaxLength());
            Assert.True(entry.Symbol.Length <= entity.FindProperty(nameof(Unit.Symbol))!.GetMaxLength());
        });
    }

    [Fact]
    public void Diseases_are_the_five_rice_classes_with_one_healthy_class_and_no_invented_content()
    {
        Assert.Equal(["LEAF_BLAST", "BACTERIAL_LEAF_BLIGHT", "BROWN_SPOT", "SHEATH_BLIGHT", "HEALTHY"],
            DiseaseSeeder.Entries.Select(entry => entry.Code));
        Assert.Equal(5, DiseaseSeeder.Entries.Select(entry => entry.Code).Distinct().Count());
        Assert.Equal("HEALTHY", Assert.Single(DiseaseSeeder.Entries, entry => entry.IsHealthyClass).Code);
        using var context = ModelContext();
        var entity = context.Model.FindEntityType(typeof(Disease))!;
        Assert.All(DiseaseSeeder.Entries, entry =>
        {
            Assert.True(entry.Code.Length <= entity.FindProperty(nameof(Disease.Code))!.GetMaxLength());
            Assert.True(entry.Name.Length <= entity.FindProperty(nameof(Disease.Name))!.GetMaxLength());
            var disease = new Disease(entry.Code, entry.Name, entry.IsHealthyClass);
            Assert.Equal("RICE", disease.CropType);
            Assert.Null(disease.ScientificName);
            Assert.Null(disease.Description);
            Assert.Null(disease.Symptoms);
            Assert.Null(disease.Causes);
            Assert.Null(disease.Prevention);
        });
    }

    [Theory]
    [InlineData("Code", "")]
    [InlineData("Name", " ")]
    [InlineData("AddressLine", "<ĐIỀN>")]
    [InlineData("Province", "<ĐIỀN>")]
    [InlineData("Code", "<ĐIỀN>")]
    [InlineData("Name", "<ĐIỀN>")]
    public void Store_rejects_missing_or_placeholder_required_fields(string field, string value)
    {
        var options = ValidStore();
        typeof(SeedStoreOptions).GetProperty(field)!.SetValue(options, value);
        Assert.Contains($"Seed:Store:{field}", Assert.Throws<ReferenceSeedException>(options.Validate).Message);
    }

    [Theory]
    [InlineData("Code", 30)]
    [InlineData("Name", 200)]
    [InlineData("AddressLine", 500)]
    [InlineData("Province", 150)]
    [InlineData("Ward", 150)]
    [InlineData("District", 150)]
    [InlineData("PhoneNumber", 20)]
    [InlineData("Email", 255)]
    [InlineData("TaxCode", 50)]
    public void Store_validates_exact_column_length_limits(string field, int maximumLength)
    {
        var options = ValidStore();
        typeof(SeedStoreOptions).GetProperty(field)!.SetValue(options, new string('x', maximumLength));
        options.Validate();
        typeof(SeedStoreOptions).GetProperty(field)!.SetValue(options, new string('x', maximumLength + 1));
        Assert.Contains($"Seed:Store:{field}", Assert.Throws<ReferenceSeedException>(options.Validate).Message);
    }

    [Fact]
    public void Store_accepts_required_information_with_all_optional_fields_null()
    {
        ValidStore().Validate();
    }

    [Fact]
    public async Task Invalid_store_configuration_fails_before_opening_a_connection()
    {
        await using var context = ModelContext();
        var seeder = new DatabaseSeeder(context, Options.Create(new SeedStoreOptions()));
        await Assert.ThrowsAsync<ReferenceSeedException>(() => seeder.SeedAsync(TestContext.Current.CancellationToken));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Pending_changes_cannot_be_saved_by_the_seed_command()
    {
        await using var context = ModelContext();
        context.Units.Add(new Unit("UNRELATED", "Unrelated unit"));
        var seeder = new DatabaseSeeder(context, Options.Create(ValidStore()));
        Assert.Contains("pending changes", (await Assert.ThrowsAsync<ReferenceSeedException>(
            () => seeder.SeedAsync(TestContext.Current.CancellationToken))).Message);
        Assert.True(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task A_cancelled_invocation_never_opens_a_connection_or_stages_rows()
    {
        await using var context = ModelContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DatabaseSeeder(context, Options.Create(ValidStore())).SeedAsync(cancellation.Token));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static SeedStoreOptions ValidStore() => new()
    {
        Code = "TEST-STORE", Name = "Test store", AddressLine = "Test address", Province = "Test province"
    };

    private static AgriSageDbContext ModelContext() => new(
        new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql().Options);
}
