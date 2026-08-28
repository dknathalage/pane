namespace Pane.Core;

public interface IFilePicker
{
    Task<string?> PickFolderAsync();
}
