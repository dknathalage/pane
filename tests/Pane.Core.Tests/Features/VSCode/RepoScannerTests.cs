using Pane.Core.Features.VSCode;
using Xunit;

public class RepoScannerTests
{
    [Fact]
    public void Lists_dirs_excluding_dotfolders_sorted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "beta"));
        Directory.CreateDirectory(Path.Combine(root, "alpha"));
        Directory.CreateDirectory(Path.Combine(root, ".hidden"));
        File.WriteAllText(Path.Combine(root, "file.txt"), "x");

        var result = RepoScanner.Scan(root).ToList();

        Assert.Equal(new[] { "alpha", "beta" }, result);
    }
}
