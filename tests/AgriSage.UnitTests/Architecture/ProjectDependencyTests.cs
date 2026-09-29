using System.Xml.Linq;

namespace AgriSage.UnitTests.Architecture;

// Guards the approved 4-layer dependency direction (BACKEND_ARCHITECTURE.md §3).
// Checks declared ProjectReferences, because compiled assemblies omit references that are not yet used in code.
public class ProjectDependencyTests
{
    public static TheoryData<string, string[]> ExpectedReferences => new()
    {
        { "AgriSage.Domain", [] },
        { "AgriSage.Application", ["AgriSage.Domain"] },
        { "AgriSage.Infrastructure", ["AgriSage.Application", "AgriSage.Domain"] },
        { "AgriSage.Api", ["AgriSage.Application", "AgriSage.Infrastructure"] }
    };

    [Theory]
    [MemberData(nameof(ExpectedReferences))]
    public void Project_references_match_approved_architecture(string project, string[] expected)
    {
        var actual = GetProjectReferences(project);

        Assert.Equal(expected.Order(), actual.Order());
    }

    [Fact]
    public void Domain_has_no_package_references()
    {
        Assert.Empty(GetPackageReferences("AgriSage.Domain"));
    }

    // Application may use the EF Core abstractions package only (DbSet<T>, transactions for IAgriSageDbContext);
    // never a provider, Relational or tooling package (architecture §13).
    [Fact]
    public void Application_references_only_the_ef_core_abstractions_package()
    {
        var efPackages = GetPackageReferences("AgriSage.Application")
            .Where(package => package.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                              || package.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(["Microsoft.EntityFrameworkCore"], efPackages);
    }

    private static IEnumerable<string> GetProjectReferences(string project) =>
        GetIncludes(project, "ProjectReference").Select(Path.GetFileNameWithoutExtension)!;

    private static IEnumerable<string> GetPackageReferences(string project) =>
        GetIncludes(project, "PackageReference");

    private static IEnumerable<string> GetIncludes(string project, string itemName)
    {
        var csprojPath = Path.Combine(FindRepositoryRoot(), "src", project, $"{project}.csproj");

        return XDocument.Load(csprojPath)
            .Descendants(itemName)
            .Select(item => (string)item.Attribute("Include")!);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgriSage.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate AgriSage.sln above the test output directory.");
    }
}
