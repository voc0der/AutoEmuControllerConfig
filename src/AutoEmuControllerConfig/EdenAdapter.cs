using System.Text.RegularExpressions;

namespace AutoEmuControllerConfig;

internal static partial class EdenAdapter
{
    public static string Prepare(string text, IReadOnlyList<Controller> players)
    {
        var config = new IniDocument(text);
        if (!config.Keys("Controls").Any()) throw new InvalidDataException("Eden config has no [Controls] settings.");
        var first = new IniDocument(Baselines.Read("Eden.PlayerOneDefault.ini"));
        var second = new IniDocument(Baselines.Read("Eden.PlayerTwoDefault.ini"));
        for (var player = 0; player < 8; player++)
        {
            var prefix = $"player_{player}_";
            config.SetExplicit("Controls", prefix + "connected", player < players.Count ? "true" : "false");
            if (player >= players.Count) continue;
            var controller = players[player];
            if (controller.Guid.Length != 32 || controller.GuidPort < 0)
                throw new InvalidDataException("Eden needs a detected SDL GUID and per-GUID port.");
            var baseline = player == 1 ? second : first;
            // The supplied profiles define the layout. Clone P1 for additional players.
            foreach (var key in baseline.Keys("Controls").Where(k => !k.EndsWith("\\default", StringComparison.Ordinal)))
            {
                if (!(key.StartsWith("button_", StringComparison.Ordinal) || key is "lstick" or "rstick" or "motionleft" or "motionright")) continue;
                var value = baseline.Get("Controls", key)!;
                if (value.Contains("engine:sdl", StringComparison.Ordinal))
                {
                    value = GuidPattern().Replace(value, "${1}" + controller.Guid);
                    value = PortPattern().Replace(value, "${1}" + controller.GuidPort);
                }
                config.SetExplicit("Controls", prefix + key, value);
            }
            config.SetExplicit("Controls", prefix + "type", baseline.Get("Controls", "type") ?? "0");
            config.SetExplicit("Controls", prefix + "profile_name", "");
        }
        return config.ToString();
    }

    // Per-game profiles override the global assignments. Restore inheritance while
    // keeping all per-game graphics, CPU, and other settings.
    public static string InheritGlobalPlayers(string text)
    {
        var config = new IniDocument(text);
        for (var i = 0; i < 8; i++)
        {
            var key = $"player_{i}_profile_name";
            if (config.Get("Controls", key) is not null)
                config.SetExplicit("Controls", key, "");
        }
        return config.ToString();
    }

    [GeneratedRegex(@"(\bguid:)[0-9a-fA-F]{32}")]
    private static partial Regex GuidPattern();
    [GeneratedRegex(@"(\bport:)\d+")]
    private static partial Regex PortPattern();
}
