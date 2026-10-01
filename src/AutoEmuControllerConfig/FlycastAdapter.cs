namespace AutoEmuControllerConfig;

internal static class FlycastAdapter
{
    public static string Prepare(string text, IReadOnlyList<Controller> players)
    {
        var config = new IniDocument(text);
        if (config.Get("input", "device1") is null)
            throw new InvalidDataException("Flycast config has no [input] device1 setting.");
        // SDL instance IDs restart with each process. The detector creates a fresh SDL
        // context using Flycast's backend; reject hotplug gaps instead of persisting
        // IDs from a long-running process.
        foreach (var c in players)
            if (c.InstanceId < 0 || c.InstanceId != c.SdlIndex)
                throw new InvalidOperationException("Flycast controller enumeration changed. Retry launch.");
        foreach (var key in config.Keys("input").Where(k => k.StartsWith("maple_sdl_joystick_", StringComparison.Ordinal)).ToArray())
            config.Set("input", key, "-1");
        for (var i = 0; i < 4; i++)
        {
            // Flycast's MapleDeviceType: MDT_SegaController = 0, MDT_None = 10.
            config.Set("input", $"device{i + 1}", i < players.Count ? "0" : "10");
            if (i < players.Count)
                config.Set("input", $"maple_sdl_joystick_{players[i].InstanceId}", i.ToString());
        }
        return config.ToString();
    }
}
