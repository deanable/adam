using Adam.CatalogBrowser.ViewModels;
using FluentAssertions;
using Xunit.Abstractions;

namespace Adam.CatalogBrowser.Tests;

public class FolderTreeDiagnostics
{
    private readonly ITestOutputHelper _output;
    public FolderTreeDiagnostics(ITestOutputHelper output) => _output = output;

    [Fact]
    public void OriginalLogic_BuildsTree_WithWindowsPaths()
    {
        var paths = new HashSet<string>
        {
            @"C:\Users\Dean\Photos",
            @"C:\Users\Dean\Photos\Vacation",
            @"C:\Users\Dean\Videos"
        };

        var root = BuildTreeOriginal(paths);

        root.Name.Should().Be("All Folders");
        root.Children.Should().HaveCount(1); // C:
        
        var cDrive = root.Children.First();
        cDrive.Name.Should().Be("C:");
        cDrive.Children.Should().HaveCount(1); // Users
        
        var users = cDrive.Children.First();
        users.Name.Should().Be("Users");
        users.Children.Should().HaveCount(1); // Dean
        
        var dean = users.Children.First();
        dean.Name.Should().Be("Dean");
        dean.Children.Should().HaveCount(2); // Photos, Videos

        var photos = dean.Children.First(c => c.Name == "Photos");
        photos.Children.Should().HaveCount(1); // Vacation
    }

    [Fact]
    public void OriginalLogic_BuildsTree_WithUNCPaths()
    {
        var paths = new HashSet<string>
        {
            @"\\damserver\Assets\Photos",
            @"\\damserver\Assets\Videos",
            @"\\damserver\Assets\Videos\edit"
        };

        var root = BuildTreeOriginal(paths);

        root.Children.Should().HaveCount(1); // damserver
        var damserver = root.Children.First();
        damserver.Name.Should().Be("damserver");

        damserver.Children.Should().HaveCount(1); // Assets
        var assets = damserver.Children.First();
        assets.Name.Should().Be("Assets");
        assets.Children.Should().HaveCount(2); // Photos, Videos
    }

    [Fact]
    public void OriginalLogic_FindsNode_ByPath()
    {
        var paths = new HashSet<string>
        {
            @"C:\Users\Dean\Photos",
            @"C:\Users\Dean\Videos"
        };

        var root = BuildTreeOriginal(paths);
        var findMethod = typeof(SidebarViewModel).GetMethod("FindFolderNode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var found = findMethod!.Invoke(null, new object[] { root, @"C:\Users\Dean\Photos" }) as FolderNode;
        found.Should().NotBeNull();
        found!.Name.Should().Be("Photos");

        var notFound = findMethod!.Invoke(null, new object[] { root, @"C:\Users\Dean\Music" }) as FolderNode;
        notFound.Should().BeNull();
    }

    private static FolderNode BuildTreeOriginal(HashSet<string> paths)
    {
        var root = new FolderNode { Name = "All Folders", Path = "", IsExpanded = true };
        foreach (var dir in paths.OrderBy(p => p))
        {
            var parts = dir.Split('/', '\\');
            var current = root;
            var cumulative = "";
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                cumulative = cumulative.Length == 0 ? part : $"{cumulative}/{part}";
                var existing = current.Children.FirstOrDefault(c => c.Name == part);
                if (existing == null)
                {
                    existing = new FolderNode { Name = part, Path = cumulative };
                    current.Children.Add(existing);
                }
                current = existing;
            }
        }
        return root;
    }
}
