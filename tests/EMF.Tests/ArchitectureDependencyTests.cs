namespace EMF.Tests;

public sealed class ArchitectureDependencyTests
{
    [Fact]
    public void Framework_projects_do_not_depend_on_console()
    {
        var repositoryRoot =
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../../../"));

        var projectFiles =
            Directory.GetFiles(
                Path.Combine(repositoryRoot, "src"),
                "*.csproj",
                SearchOption.AllDirectories);

        var frameworkProjects =
            projectFiles.Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}EMF.Console{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal));

        foreach (var projectFile in frameworkProjects)
        {
            var projectText =
                File.ReadAllText(projectFile);

            Assert.DoesNotContain(
                "EMF.Console",
                projectText,
                StringComparison.Ordinal);
        }
    }
}
