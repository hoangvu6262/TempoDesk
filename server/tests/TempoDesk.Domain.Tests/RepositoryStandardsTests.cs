using System.Xml.Linq;

namespace TempoDesk.Domain.Tests;

public class RepositoryStandardsTests
{
    [Fact]
    public void ArchitectureSharedSettingsAndProjectReferencesMatchRepositoryConventions()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "server", "TempoDesk.sln")))
            directory = directory.Parent;

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
        var propsPath = Path.Combine(root, "server", "Directory.Build.props");
        Assert.True(File.Exists(propsPath), "Shared Directory.Build.props is required.");
        var settings = XDocument.Load(propsPath).Descendants("PropertyGroup").Single();
        Assert.Equal("enable", settings.Element("Nullable")?.Value);
        Assert.Equal("enable", settings.Element("ImplicitUsings")?.Value);
        Assert.Equal("true", settings.Element("EnableNETAnalyzers")?.Value);
        Assert.Equal("true", settings.Element("TreatWarningsAsErrors")?.Value);
        Assert.StartsWith(Path.Combine(root, "artifacts"), AppContext.BaseDirectory);

        var references = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = [],
            ["Application"] = ["Domain"],
            ["Infrastructure"] = ["Application", "Domain"],
            ["Api"] = ["Application", "Infrastructure"],
            ["DocumentProcessing"] = ["Application"],
            ["Worker"] = ["Application", "DocumentProcessing", "Infrastructure"]
        };

        foreach (var (name, expected) in references)
        {
            var project = XDocument.Load(Path.Combine(root, "server", "src",
                $"TempoDesk.{name}", $"TempoDesk.{name}.csproj"));
            var actual = project.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(
                    reference.Attribute("Include")!.Value.Replace('\\', '/')))
                .Select(reference => reference["TempoDesk.".Length..])
                .Order(StringComparer.Ordinal);
            Assert.Equal(expected, actual);

            if (name is "Domain" or "Application")
            {
                Assert.Empty(project.Descendants("PackageReference"));
                Assert.Empty(project.Descendants("FrameworkReference"));
            }

            if (name == "Api")
                foreach (var file in Directory.GetFiles(Path.Combine(root, "server", "src", "TempoDesk.Api"),
                             "*Controller.cs", SearchOption.AllDirectories))
                    Assert.DoesNotContain("TempoDeskDbContext", File.ReadAllText(file), StringComparison.Ordinal);

            var conventions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Commands"] = "Command", ["Queries"] = "Query", ["Handlers"] = "Handler",
                ["Controllers"] = "Controller", ["Jobs"] = "Job"
            };
            foreach (var (category, suffix) in conventions)
            {
                var folder = Path.Combine(root, "server", "src", $"TempoDesk.{name}", category);
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                    Assert.EndsWith(suffix, Path.GetFileNameWithoutExtension(file));
            }
        }

        Assert.Equal(6, Directory.GetFiles(Path.Combine(root, "server", "src"),
            "*.csproj", SearchOption.AllDirectories).Length);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(root, "server", "tests"),
            "*.csproj", SearchOption.AllDirectories).Length);
    }
}
