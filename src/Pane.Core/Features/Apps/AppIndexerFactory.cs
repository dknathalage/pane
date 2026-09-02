using System.Runtime.InteropServices;

namespace Pane.Core.Features.Apps;

public static class AppIndexerFactory
{
    public static IAppIndexer Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return new MacAppIndexer();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new WindowsAppIndexer();
        return new LinuxAppIndexer();
    }
}
