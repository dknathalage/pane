using System.Text.RegularExpressions;

namespace Pane.Plugins.Apps;

public sealed class LinuxAppIndexer : IAppIndexer
{
    public IEnumerable<AppEntry> Index()
    {
        var dirs = new[]
        {
            "/usr/share/applications",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local/share/applications")
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var desktop in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.TopDirectoryOnly))
            {
                string? name = null;
                string? exec = null;

                foreach (var line in File.ReadLines(desktop))
                {
                    if (line.StartsWith("Name=", StringComparison.Ordinal) && name is null)
                        name = line["Name=".Length..].Trim();
                    else if (line.StartsWith("Exec=", StringComparison.Ordinal) && exec is null)
                        exec = line["Exec=".Length..].Trim();

                    if (name is not null && exec is not null) break;
                }

                if (name is not null && exec is not null)
                {
                    var launchTarget = Regex.Replace(exec, "%[a-zA-Z]", "").Trim();
                    yield return new AppEntry(name, launchTarget);
                }
            }
        }
    }
}
