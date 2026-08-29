using Pane.Abstractions;
using Pane.Plugins.Files;
using Xunit;

public class FilesPluginTests
{
    // Records how it was called and returns canned hits.
    sealed class FakeSearcher : IFileSearcher
    {
        readonly IReadOnlyList<FileHit> _hits;
        public string? LastTerms;
        public int? LastMax;
        public int Calls;
        public FakeSearcher(params FileHit[] hits) => _hits = hits;

        public IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct)
        {
            Calls++;
            LastTerms = terms;
            LastMax = max;
            return _hits;
        }
    }

    static PaneQuery Slash(string terms) => new($"/{terms}", "/", terms);

    static async Task<List<PaneResult>> Run(IPlugin plugin, PaneQuery q)
    {
        var results = new List<PaneResult>();
        await foreach (var r in plugin.QueryAsync(q, CancellationToken.None))
            results.Add(r);
        return results;
    }

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public async Task Does_not_search_without_the_keyword()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = new FilesPlugin(searcher);

        // No keyword: dispatcher passes the raw terms with Keyword == null.
        var results = await Run(plugin, new PaneQuery("report", null, "report"));

        Assert.Empty(results);
        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task Does_not_search_for_terms_shorter_than_two_chars()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "a.txt"), false));
        var plugin = new FilesPlugin(searcher);

        var results = await Run(plugin, Slash("a"));

        Assert.Empty(results);
        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task Folder_hits_get_a_folder_icon_files_get_a_file_icon()
    {
        var searcher = new FakeSearcher(
            new FileHit(Path.Combine(Home, "Projects"), true),
            new FileHit(Path.Combine(Home, "notes.md"), false));
        var plugin = new FilesPlugin(searcher);

        var results = await Run(plugin, Slash("proj"));

        Assert.Equal("📁", results[0].Icon);
        Assert.Equal("📄", results[1].Icon);
    }

    [Fact]
    public async Task Title_is_the_leaf_name()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "Docs", "notes.md"), false));
        var plugin = new FilesPlugin(searcher);

        var results = await Run(plugin, Slash("notes"));

        Assert.Equal("notes.md", results[0].Title);
    }

    [Fact]
    public async Task Subtitle_abbreviates_the_home_directory_as_tilde()
    {
        var full = Path.Combine(Home, "Docs", "notes.md");
        var searcher = new FakeSearcher(new FileHit(full, false));
        var plugin = new FilesPlugin(searcher);

        var results = await Run(plugin, Slash("notes"));

        Assert.Equal(Path.Combine("~", "Docs"), results[0].Subtitle);
    }

    [Fact]
    public async Task SearchText_is_the_full_path()
    {
        var full = Path.Combine(Home, "Docs", "notes.md");
        var searcher = new FakeSearcher(new FileHit(full, false));
        var plugin = new FilesPlugin(searcher);

        var results = await Run(plugin, Slash("notes"));

        Assert.Equal(full, results[0].SearchText);
    }

    [Fact]
    public async Task Caps_results_by_passing_a_limit_to_the_searcher()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = new FilesPlugin(searcher);

        await Run(plugin, Slash("report"));

        Assert.Equal(50, searcher.LastMax);
        Assert.Equal("report", searcher.LastTerms);
    }
}
