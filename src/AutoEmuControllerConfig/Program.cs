using System.Diagnostics;

namespace AutoEmuControllerConfig;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("AutoEmuControllerConfig: prepare Xbox players, then exit.\nUsage: AutoEmuControllerConfig [--emulator-dir <directory>] [--dry-run]\nWith no directory, detects BizHawk, Eden and Flycast in Program Files.\nExit codes: 0 prepared/skipped, 1 preparation failed, 2 unsupported platform/arguments.");
            return 0;
        }
        var timer = Stopwatch.StartNew();
        var log = new List<string>();
        void Report(string message) { Console.WriteLine(message); log.Add(message); }
        string? requested = null;
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dry-run") dryRun = true;
            else if (args[i] == "--emulator-dir" && i + 1 < args.Length) requested = args[++i];
            else { Console.Error.WriteLine("Unknown or incomplete argument. Use --help."); return 2; }
        }
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Controller detection requires Windows 10/11 x64."); return 2; }
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoEmuControllerConfig");
        using var mutex = new Mutex(false, @"Local\AutoEmuControllerConfig.Prepare");
        var locked = false;
        try
        {
            try { locked = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new InvalidOperationException("Another controller preparation is still running.");
            var installs = EmulatorLocator.Find(requested,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)]);
            if (installs.Length == 0)
            {
                Report(requested is null ? "No supported emulators found." : "This emulator is not supported; skipped.");
                return 0;
            }
            foreach (var install in installs)
            {
                Preparation.EnsureClosed(install);
                if (!File.Exists(install.ConfigPath))
                    throw new FileNotFoundException($"{install.Kind} has no existing configuration. Its working config is required.", install.ConfigPath);
            }
            var slots = XInput.WaitForStableControllers();
            Report("Connected Windows Xbox slots: " + string.Join(", ", slots.Select(i => i + 1)));
            var changes = new List<FileChange>();
            foreach (var install in installs)
            {
                var bizSdl = install.Kind == EmulatorKind.BizHawk && SdlControllers.BizHawkUsesSdl(install.Directory);
                Controller[] players;
                if (install.Kind == EmulatorKind.BizHawk && !bizSdl)
                    players = slots.Select(i => new Controller(i)).ToArray();
                else
                {
                    using var sdl = new SdlControllers(install);
                    Report($"{install.Kind}: reading controllers with {sdl.LibraryPath}");
                    players = sdl.Read(slots);
                }
                for (var i = 0; i < players.Length; i++)
                {
                    var c = players[i];
                    Report($"{install.Kind} P{i + 1}: Xbox slot {c.XInputIndex + 1}, SDL index {c.SdlIndex}, GUID {c.Guid}, port {c.GuidPort}" +
                        (c.ExactMatch ? "" : " (best effort: SDL device order)"));
                }
                changes.AddRange(Preparation.Plan(install, players, bizSdl));
            }
            void VerifyReady()
            {
                foreach (var install in installs) Preparation.EnsureClosed(install);
                if (!XInput.Connected().SequenceEqual(slots))
                    throw new InvalidOperationException("Controller connections changed during preparation. Retry launch.");
            }
            VerifyReady();
            foreach (var change in changes.Where(c => c.HasChanges)) Report((dryRun ? "Would update: " : "Updating: ") + change.Path);
            var changed = dryRun ? changes.Count(c => c.HasChanges) : FileTransaction.Apply(changes, Path.Combine(data, "backups"), VerifyReady);
            Report($"{(dryRun ? "Dry run" : "Ready")}: {changed} file(s), {timer.ElapsedMilliseconds} ms.");
            return 0;
        }
        catch (Exception e)
        {
            var message = "Preparation failed: " + e.Message;
            Console.Error.WriteLine(message);
            log.Add(message);
            return 1;
        }
        finally
        {
            if (locked) mutex.ReleaseMutex();
            try
            {
                Directory.CreateDirectory(data);
                File.WriteAllLines(Path.Combine(data, "last-run.log"), log);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
