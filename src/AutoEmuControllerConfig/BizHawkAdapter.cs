using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutoEmuControllerConfig;

internal static partial class BizHawkAdapter
{
    private static readonly string[] Sections =
        ["AllTrollers", "AllTrollersAutoFire", "AllTrollersAnalog", "AllTrollersFeedbacks"];

    public static string Prepare(string text, IReadOnlyList<int> deviceNumbers)
    {
        var root = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("Empty BizHawk config.");
        if (root["AllTrollers"] is not JsonObject) throw new InvalidDataException("BizHawk config has no AllTrollers object.");
        var original = root.ToJsonString();
        var baseline = JsonNode.Parse(Baselines.Read("BizHawk.Controllers.json"))!.AsObject();
        foreach (var section in Sections)
        {
            if (root[section] is not JsonObject targetSection)
                root[section] = targetSection = new JsonObject();
            var sourceSection = baseline[section]!.AsObject();
            foreach (var groupName in sourceSection.Select(p => p.Key).Union(targetSection.Select(p => p.Key)).ToArray())
            {
                if (targetSection[groupName] is not JsonObject targetGroup)
                    targetSection[groupName] = targetGroup = new JsonObject();
                var sourceGroup = sourceSection[groupName] as JsonObject ?? new JsonObject();
                var bindings = targetGroup.DeepClone().AsObject();
                // Use the supplied layouts, including intentional P1/P2 differences.
                foreach (var (key, value) in sourceGroup)
                    if (ContainsXbox(value)) bindings[key] = value?.DeepClone();
                // Additional players inherit only the controller part of P1's layout.
                // Console topology (multitaps, expansion ports) is left to the emulator.
                foreach (var (key, value) in bindings.ToArray())
                {
                    if (!key.StartsWith("P1 ", StringComparison.Ordinal) || !ContainsXbox(value)) continue;
                    for (var player = 2; player <= 4; player++)
                    {
                        var other = $"P{player} " + key[3..];
                        if (!ContainsXbox(bindings[other])) bindings[other] = XboxOnly(value);
                    }
                }
                foreach (var (key, value) in bindings)
                {
                    if (!ContainsXbox(value)) continue;
                    var match = PlayerPattern().Match(key);
                    var player = match.Success ? int.Parse(match.Groups[1].Value) - 1 : 0;
                    if (player >= 4) continue;
                    var device = player < deviceNumbers.Count ? deviceNumbers[player] : (int?)null;
                    if (value is JsonValue)
                    {
                        // Keep existing keyboard/mouse bindings; replace only Xbox tokens.
                        var existing = targetGroup[key]?.GetValue<string>() ?? "";
                        var nonXbox = Tokens(existing).Where(t => !XboxPattern().IsMatch(t));
                        var xbox = device.HasValue
                            ? Tokens(value.GetValue<string>()).Where(t => XboxPattern().IsMatch(t))
                                .Select(t => XboxPattern().Replace(t, $"X{device} "))
                            : [];
                        targetGroup[key] = string.Join(", ", nonXbox.Concat(xbox));
                    }
                    else
                    {
                        // Analog settings and rumble prefixes have structured values.
                        targetGroup[key] = Retarget(value, device);
                    }
                }
            }
        }
        PrepareN64Ports(root, deviceNumbers.Count);
        if (root.ToJsonString() == original) return text;
        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (text.Contains("\r\n", StringComparison.Ordinal)) output = output.Replace("\n", "\r\n", StringComparison.Ordinal);
        return output + (text.EndsWith('\n') ? (text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n") : "");
    }

    private static void PrepareN64Ports(JsonObject root, int count)
    {
        // These connection fields exist in the supplied working config. Do not
        // synthesize multitaps or guess controller enums for other console cores.
        if (root["CoreSyncSettings"] is not JsonObject sync) return;
        var baseline = JsonNode.Parse(Baselines.Read("BizHawk.Ports.json"))!.AsObject();
        const string mupen = "BizHawk.Emulation.Cores.Nintendo.N64.N64";
        if (sync[mupen] is JsonObject n64 && n64["Controllers"] is JsonArray controllers)
            for (var i = 0; i < Math.Min(4, controllers.Count); i++)
                if (controllers[i] is JsonObject controller) controller["IsConnected"] = i < count;
        const string ares = "BizHawk.Emulation.Cores.Consoles.Nintendo.Ares64.Ares64";
        if (sync[ares] is JsonObject ares64)
        {
            var suppliedType = baseline[ares]!["P1Controller"]!.GetValue<int>();
            for (var i = 0; i < 4; i++)
            {
                var key = $"P{i + 1}Controller";
                var current = ares64[key]?.GetValue<int>() ?? 0;
                ares64[key] = i < count ? (current != 0 ? current : suppliedType) : 0;
            }
        }
    }

    private static string[] Tokens(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool ContainsXbox(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var text) => XboxPattern().IsMatch(text),
        JsonObject obj => obj.Any(p => ContainsXbox(p.Value)),
        _ => false
    };

    private static JsonNode? XboxOnly(JsonNode? value)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var text))
            return JsonValue.Create(string.Join(", ", Tokens(text).Where(t => XboxPattern().IsMatch(t))));
        return value?.DeepClone();
    }

    private static JsonNode? Retarget(JsonNode? value, int? device)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var text) && XboxPattern().IsMatch(text))
            return JsonValue.Create(device.HasValue ? XboxPattern().Replace(text, $"X{device} ") : "");
        if (value is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (key, child) in obj) result[key] = Retarget(child, device);
            return result;
        }
        return value?.DeepClone();
    }

    [GeneratedRegex(@"\bX\d+ ")]
    private static partial Regex XboxPattern();
    [GeneratedRegex(@"^P(\d+) ")]
    private static partial Regex PlayerPattern();
}
