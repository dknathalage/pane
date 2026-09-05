using Pane.Core.Updates;
using Xunit;

public class UpdateStateTests
{
    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pane-state-{Guid.NewGuid():N}.json");

    [Fact]
    public void A_missing_file_means_never_checked()
    {
        Assert.Null(new UpdateState(TempPath()).LoadLastCheck());
    }

    [Fact]
    public void The_last_check_time_round_trips()
    {
        var path = TempPath();
        var when = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        new UpdateState(path).SaveLastCheck(when);

        Assert.Equal(when, new UpdateState(path).LoadLastCheck());
        File.Delete(path);
    }

    [Fact]
    public void A_corrupt_file_means_never_checked_rather_than_throwing()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ not json");

        Assert.Null(new UpdateState(path).LoadLastCheck());
        File.Delete(path);
    }

    [Fact]
    public void Saving_creates_the_directory_when_it_is_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pane-state-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "update-state.json");

        new UpdateState(path).SaveLastCheck(DateTimeOffset.UtcNow);

        Assert.True(File.Exists(path));
        Directory.Delete(dir, recursive: true);
    }
}
