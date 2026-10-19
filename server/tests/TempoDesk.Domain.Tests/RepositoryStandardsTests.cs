using System.Xml.Linq;

namespace TempoDesk.Domain.Tests;

public class RepositoryStandardsTests
{
    [Fact]
    public void SharedSettingsAndProjectReferencesMatchRepositoryConventions()
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
        }

        Assert.Equal(6, Directory.GetFiles(Path.Combine(root, "server", "src"),
            "*.csproj", SearchOption.AllDirectories).Length);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(root, "server", "tests"),
            "*.csproj", SearchOption.AllDirectories).Length);
    }
}
