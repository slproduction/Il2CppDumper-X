using System.Diagnostics;

namespace Il2CppDumper.Desktop.Shell;

public interface IPathLauncher
{
    bool TryOpenDirectory(string path);
}

public sealed class ProcessPathLauncher : IPathLauncher
{
    public bool TryOpenDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                UseShellExecute = false
            };
            info.ArgumentList.Add(path);
            Process.Start(info);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
