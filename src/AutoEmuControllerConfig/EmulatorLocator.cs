namespace AutoEmuControllerConfig;

internal static class EmulatorLocator
{
    public static EmulatorInstall[] Find(string? requestedDirectory, string appData, IEnumerable<string> programRoots)
    {
        if (requestedDirectory is not null)
        {
            var directory = Path.GetFullPath(requestedDirectory);
            if (File.Exists(directory)) directory = Path.GetDirectoryName(directory)!;
            return FindInDirectory(directory, appData).ToArray();
        }
        return programRoots.Where(Directory.Exists)
            .SelectMany(root => new[] { "BizHawk", "Eden", "Flycast" }.Select(name => Path.Combine(root, name)))
            .SelectMany(directory => FindInDirectory(directory, appData))
            .DistinctBy(e => e.ConfigPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<EmulatorInstall> FindInDirectory(string directory, string appData)
    {
        if (File.Exists(Path.Combine(directory, "EmuHawk.exe")))
            yield return new(EmulatorKind.BizHawk, directory, Path.Combine(directory, "config.ini"));
        if (File.Exists(Path.Combine(directory, "eden.exe")))
        {
            var portable = Path.Combine(directory, "user");
            var config = Directory.Exists(portable)
                ? Path.Combine(portable, "config", "qt-config.ini")
                : Path.Combine(appData, "eden", "config", "qt-config.ini");
            yield return new(EmulatorKind.Eden, directory, config);
        }
        if (File.Exists(Path.Combine(directory, "flycast.exe")))
            yield return new(EmulatorKind.Flycast, directory, Path.Combine(directory, "emu.cfg"));
    }
}
