using System.Diagnostics;

namespace AutoEmuControllerConfig;

internal static class Preparation
{
    public static List<FileChange> Plan(EmulatorInstall install, IReadOnlyList<Controller> players, bool bizHawkSdl)
    {
        if (players.Count is < 1 or > 4) throw new InvalidOperationException("Expected between one and four Xbox controllers.");
        var changes = new List<FileChange>();
        switch (install.Kind)
        {
            case EmulatorKind.BizHawk:
                changes.Add(FileChange.Prepare(install.ConfigPath, text => BizHawkAdapter.Prepare(text,
                    players.Select(c => (bizHawkSdl ? c.SdlIndex : c.XInputIndex) + 1).ToArray())));
                break;
            case EmulatorKind.Eden:
                var configDirectory = Path.GetDirectoryName(install.ConfigPath)!;
                changes.Add(FileChange.Prepare(install.ConfigPath, text => EdenAdapter.Prepare(text, players)));
                var sdlConfig = Path.Combine(configDirectory, "sdl2-config.ini");
                if (File.Exists(sdlConfig)) changes.Add(FileChange.Prepare(sdlConfig, text => EdenAdapter.Prepare(text, players)));
                var custom = Path.Combine(configDirectory, "custom");
                if (Directory.Exists(custom))
                    foreach (var file in Directory.EnumerateFiles(custom, "*.ini"))
                        changes.Add(FileChange.Prepare(file, EdenAdapter.InheritGlobalPlayers));
                break;
            case EmulatorKind.Flycast:
                changes.Add(FileChange.Prepare(install.ConfigPath, text => FlycastAdapter.Prepare(text, players)));
                break;
        }
        return changes;
    }

    public static void EnsureClosed(EmulatorInstall install)
    {
        string[] names = install.Kind switch
        {
            EmulatorKind.BizHawk => ["EmuHawk"],
            EmulatorKind.Eden => ["eden", "eden-cmd"],
            _ => ["flycast"]
        };
        foreach (var name in names)
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                    if (!process.HasExited)
                        throw new InvalidOperationException($"Close {install.Kind} before preparing its controllers.");
            }
    }
}
