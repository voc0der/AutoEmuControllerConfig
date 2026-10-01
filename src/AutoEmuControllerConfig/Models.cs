namespace AutoEmuControllerConfig;

internal enum EmulatorKind { BizHawk, Eden, Flycast }

internal sealed record EmulatorInstall(EmulatorKind Kind, string Directory, string ConfigPath);

// SDL identifiers belong to a particular emulator's input backend. They are never
// inferred from an XInput slot or copied from a different emulator's enumeration.
internal sealed record Controller(int XInputIndex, int SdlIndex = -1, int InstanceId = -1,
    string Guid = "", string Name = "", int GuidPort = 0, bool ExactMatch = true);

internal static class ControllerMatcher
{
    public static Controller[] Match(IEnumerable<Controller> detected, IReadOnlyList<int> slots)
    {
        var devices = detected.ToArray();
        var exact = devices.Where(c => c.ExactMatch && slots.Contains(c.XInputIndex)).ToArray();
        var remaining = slots.Except(exact.Select(c => c.XInputIndex)).Order().ToArray();
        var unmatched = devices.Where(c => !c.ExactMatch).OrderBy(c => c.SdlIndex).ToArray();
        if (unmatched.Length != remaining.Length)
            throw new InvalidOperationException("The Xbox controllers visible to SDL do not match the Windows controller count. Retry launch.");
        // RawInput has no public XInput user-index accessor. Use its natural device
        // order for the remaining devices; label this best-effort match in diagnostics.
        return PlayerOrder.Sort(exact.Concat(unmatched.Select((c, i) => c with { XInputIndex = remaining[i] })));
    }
}

internal static class PlayerOrder
{
    public static Controller[] Sort(IEnumerable<Controller> controllers)
    {
        var result = controllers.OrderBy(c => c.XInputIndex).ToArray();
        if (result.Any(c => c.XInputIndex is < 0 or > 3) ||
            result.Select(c => c.XInputIndex).Distinct().Count() != result.Length)
            throw new InvalidOperationException("Controller detection returned ambiguous XInput slots.");
        return result;
    }
}

internal static class Baselines
{
    public static string Read(string name)
    {
        using var stream = typeof(Baselines).Assembly.GetManifestResourceStream(
            $"AutoEmuControllerConfig.Baselines.{name}")
            ?? throw new InvalidOperationException($"Missing embedded baseline: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
