using System.Text;
using System.Text.Json.Nodes;
using AutoEmuControllerConfig;

// A child process used only by the PowerShell integration tests. This exercises
// the actual process launch, argument quoting, waiting and exit-code handling.
if (Environment.GetEnvironmentVariable("AEC_TEST_CHILD") == "1")
{
    if (args.Length != 2 || args[0] != "--emulator-dir" || args[1] != Environment.GetEnvironmentVariable("AEC_TEST_DIRECTORY"))
        return 77;
    Thread.Sleep(200);
    File.WriteAllText(Environment.GetEnvironmentVariable("AEC_TEST_MARKER")!, "finished");
    if (Environment.GetEnvironmentVariable("AEC_TEST_FAIL") == "1")
    {
        Console.Error.WriteLine("test preparation failure");
        return 1;
    }
    return 0;
}

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action action) => tests.Add((name, action));
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
}
void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
const string guid = "030000005e0400008e02000014017801";
Controller C(int xbox, int sdl = 0, int port = 0) => new(xbox, sdl, sdl, guid, "Xbox 360", port);
string Biz() => Baselines.Read("BizHawk.Controllers.json");
const string eden = "; keep this\r\n[Controls]\r\nplayer_0_connected=true\r\nplayer_0_connected\\default=true\r\n[Renderer]\r\nresolution_setup=2\r\n";
const string fly = "[input]\ndevice1 = 0\ndevice1.1 = 1\ndevice1.2 = 1\ndevice2 = 10\ndevice3 = 10\ndevice4 = 10\nmaple_sdl_keyboard = 0\nmaple_sdl_joystick_7 = 0\n[config]\nrend.Resolution = 1440\n";

Test("Windows slot gaps become contiguous player numbers", () =>
{
    var ordered = PlayerOrder.Sort([C(3), C(1)]);
    Equal(1, ordered[0].XInputIndex); Equal(3, ordered[1].XInputIndex);
});
Test("Duplicate Windows slots are rejected", () => Throws<InvalidOperationException>(() => PlayerOrder.Sort([C(0), C(0)])));
Test("SDL order is translated using known XInput indices", () =>
{
    var matched = ControllerMatcher.Match([C(3, 0), C(0, 1)], [0, 3]);
    Equal(1, matched[0].SdlIndex); Equal(0, matched[1].SdlIndex);
});
Test("RawInput fallback follows SDL order and remains marked best effort", () =>
{
    var matched = ControllerMatcher.Match([C(-1, 3) with { ExactMatch = false }, C(-1, 1) with { ExactMatch = false }], [1, 3]);
    Equal(1, matched[0].SdlIndex); Equal(1, matched[0].XInputIndex); True(!matched[0].ExactMatch);
});
Test("Ambiguous extra SDL devices fail before writes", () => Throws<InvalidOperationException>(() =>
    ControllerMatcher.Match([C(-1, 0) with { ExactMatch = false }, C(-1, 1) with { ExactMatch = false }], [0])));
Test("Eden removes SDL name CRC from GUID", () =>
    Equal(guid, SdlControllers.NormalizeEdenGuid("030012345e0400008e02000014017801")));

Test("Eden uses per-GUID ports, not Windows slots", () =>
{
    var output = new IniDocument(EdenAdapter.Prepare(eden, [C(1, 1, 1), C(3, 0, 0)]));
    True(output.Get("Controls", "player_0_button_a")!.Contains("port:1"));
    True(output.Get("Controls", "player_1_button_a")!.Contains("port:0"));
    True(output.Get("Controls", "player_0_button_a")!.Contains("button:1"));
    True(output.Get("Controls", "player_0_button_b")!.Contains("button:0"));
    Equal("false", output.Get("Controls", "player_0_connected\\default"));
    Equal("false", output.Get("Controls", "player_2_connected"));
    Equal("2", output.Get("Renderer", "resolution_setup"));
});
Test("Eden all 1–4 player transitions are repeatable", () =>
{
    var text = eden;
    foreach (var count in new[] { 4, 1, 3, 2, 4, 1 })
    {
        var players = Enumerable.Range(0, count).Select(i => C(i, i, i)).ToArray();
        text = EdenAdapter.Prepare(text, players);
        Equal(text, EdenAdapter.Prepare(text, players));
        var ini = new IniDocument(text);
        for (var i = 0; i < 8; i++) Equal(i < count ? "true" : "false", ini.Get("Controls", $"player_{i}_connected"));
    }
});
Test("Eden preserves supplied calibration", () =>
{
    var output = new IniDocument(EdenAdapter.Prepare(eden, [C(0)]));
    var baseline = new IniDocument(Baselines.Read("Eden.PlayerOneDefault.ini"));
    Equal(baseline.Get("Controls", "rstick"), output.Get("Controls", "player_0_rstick"));
});
Test("Eden per-game input falls back to managed global players", () =>
{
    const string input = "[Controls]\nplayer_1_profile_name=OldProfile\n[Renderer]\nvulkan_device=2\n";
    var result = new IniDocument(EdenAdapter.InheritGlobalPlayers(input));
    Equal("", result.Get("Controls", "player_1_profile_name"));
    Equal("2", result.Get("Renderer", "vulkan_device"));
});
Test("Eden rejects malformed configs", () => Throws<InvalidDataException>(() => EdenAdapter.Prepare("broken", [C(0)])));

Test("BizHawk repairs supplied swapped Genesis players", () =>
{
    var result = JsonNode.Parse(BizHawkAdapter.Prepare(Biz(), [2, 4]))!;
    Equal("X2 X", result["AllTrollers"]!["GPGX Genesis Controller"]!["P1 A"]!.GetValue<string>());
    Equal("X4 X", result["AllTrollers"]!["GPGX Genesis Controller"]!["P2 A"]!.GetValue<string>());
    Equal("X4 Y", result["AllTrollersAutoFire"]!["GPGX Genesis Controller"]!["P2 A"]!.GetValue<string>());
});
Test("BizHawk seeds additional players from supplied P1 controller layout", () =>
{
    var result = JsonNode.Parse(BizHawkAdapter.Prepare(Biz(), [1, 2, 3, 4]))!;
    Equal("X4 A", result["AllTrollers"]!["SNES Controller"]!["P4 B"]!.GetValue<string>());
    Equal("X3 A", result["AllTrollers"]!["Nintendo 64 Controller"]!["P3 A"]!.GetValue<string>());
});
Test("BizHawk analog and rumble follow the selected player", () =>
{
    var result = JsonNode.Parse(BizHawkAdapter.Prepare(Biz(), [3]))!;
    Equal("X3 LeftThumbX Axis", result["AllTrollersAnalog"]!["3DS Controller"]!["Circle Pad X"]!["Value"]!.GetValue<string>());
    Equal("X3 ", result["AllTrollersFeedbacks"]!["Nintendo 64 Controller"]!["P1 Rumble Pak"]!["GamepadPrefix"]!.GetValue<string>());
});
Test("BizHawk preserves unrelated settings and keyboard bindings", () =>
{
    var input = JsonNode.Parse(Biz())!;
    input["UserSetting"] = "unchanged";
    var result = JsonNode.Parse(BizHawkAdapter.Prepare(input.ToJsonString(), [1]))!;
    Equal("unchanged", result["UserSetting"]!.GetValue<string>());
    True(result["AllTrollers"]!["SNES Controller"]!["P1 B"]!.GetValue<string>().Contains("Z"));
});
Test("BizHawk disconnect/reconnect restores Xbox mappings", () =>
{
    var four = BizHawkAdapter.Prepare(Biz(), [1, 2, 3, 4]);
    var one = BizHawkAdapter.Prepare(four, [3]);
    var disconnected = JsonNode.Parse(one)!["AllTrollers"]!["Nintendo 64 Controller"]!["P2 A"]!.GetValue<string>();
    Equal("", disconnected);
    var two = BizHawkAdapter.Prepare(one, [1, 4]);
    Equal("X4 A", JsonNode.Parse(two)!["AllTrollers"]!["Nintendo 64 Controller"]!["P2 A"]!.GetValue<string>());
    Equal(two, BizHawkAdapter.Prepare(two, [1, 4]));
});
Test("Every nonempty Xbox slot combination remains idempotent", () =>
{
    for (var mask = 1; mask < 16; mask++)
    {
        var devices = Enumerable.Range(0, 4).Where(i => (mask & (1 << i)) != 0).Select(i => i + 1).ToArray();
        var result = BizHawkAdapter.Prepare(Biz(), devices);
        Equal(result, BizHawkAdapter.Prepare(result, devices));
    }
});
Test("BizHawk enables supplied N64 controller ports without changing accessory settings", () =>
{
    var input = JsonNode.Parse(Biz())!;
    input["CoreSyncSettings"] = JsonNode.Parse(Baselines.Read("BizHawk.Ports.json"));
    var output = JsonNode.Parse(BizHawkAdapter.Prepare(input.ToJsonString(), [1, 2, 3, 4]))!;
    var n64 = output["CoreSyncSettings"]!["BizHawk.Emulation.Cores.Nintendo.N64.N64"]!["Controllers"]!;
    True(n64[3]!["IsConnected"]!.GetValue<bool>());
    Equal(2, n64[0]!["PakType"]!.GetValue<int>());
    var ares = output["CoreSyncSettings"]!["BizHawk.Emulation.Cores.Consoles.Nintendo.Ares64.Ares64"]!;
    Equal(2, ares["P4Controller"]!.GetValue<int>());
});
Test("Flycast assigns controllers and enables only occupied console ports", () =>
{
    var text = FlycastAdapter.Prepare(fly, [C(0, 2), C(3, 0)]);
    var output = new IniDocument(text);
    Equal("0", output.Get("input", "maple_sdl_joystick_2"));
    Equal("1", output.Get("input", "maple_sdl_joystick_0"));
    Equal("-1", output.Get("input", "maple_sdl_joystick_7"));
    Equal("0", output.Get("input", "device2"));
    Equal("10", output.Get("input", "device3"));
    Equal("1", output.Get("input", "device1.1"));
    Equal("1440", output.Get("config", "rend.Resolution"));
    Equal(text, FlycastAdapter.Prepare(text, [C(0, 2), C(3, 0)]));
});
Test("Flycast rejects stale process-specific instance IDs", () => Throws<InvalidOperationException>(() =>
    FlycastAdapter.Prepare(fly, [C(0) with { InstanceId = 4 }])));
Test("INI preserves comments and CRLF", () =>
{
    var text = EdenAdapter.Prepare(eden, [C(0)]);
    True(text.StartsWith("; keep this\r\n", StringComparison.Ordinal));
    True(!text.Replace("\r\n", "").Contains('\n'));
});
Test("Duplicate target INI keys fail rather than editing an arbitrary copy", () =>
    Throws<InvalidDataException>(() => new IniDocument("[input]\ndevice1=0\ndevice1=10\n").Set("input", "device1", "0")));

var temp = Path.Combine(Path.GetTempPath(), "aec-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
Test("Discovery uses documented installed and portable locations", () =>
{
    var root = Path.Combine(temp, "programs");
    var edenDir = Path.Combine(root, "Eden");
    Directory.CreateDirectory(edenDir); File.WriteAllText(Path.Combine(edenDir, "eden.exe"), "");
    var found = EmulatorLocator.Find(null, "roaming", [root]);
    Equal(Path.Combine("roaming", "eden", "config", "qt-config.ini"), found.Single().ConfigPath);
    Directory.CreateDirectory(Path.Combine(edenDir, "user"));
    found = EmulatorLocator.Find(edenDir, "roaming", []);
    Equal(Path.Combine(edenDir, "user", "config", "qt-config.ini"), found.Single().ConfigPath);
});
Test("No-controller plans are rejected without disabling all players", () =>
    Throws<InvalidOperationException>(() => Preparation.Plan(new(EmulatorKind.Eden, temp, "unused"), [], false)));
Test("Atomic writes preserve UTF8 BOM and save an exact backup", () =>
{
    var path = Path.Combine(temp, "bom.ini");
    byte[] original = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("[input]\r\ndevice1=0\r\n")];
    File.WriteAllBytes(path, original);
    var change = FileChange.Prepare(path, t => t.Replace("device1=0", "device1=10"));
    True(change.After.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    var backup = Path.Combine(temp, "backup");
    Equal(1, FileTransaction.Apply([change], backup));
    var copy = Directory.GetFiles(backup, "*-bom.ini", SearchOption.AllDirectories).Single();
    True(File.ReadAllBytes(copy).SequenceEqual(original));
});
Test("No-op preparation does not rewrite or back up files", () =>
{
    var path = Path.Combine(temp, "noop.ini"); File.WriteAllText(path, "unchanged");
    var change = FileChange.Prepare(path, t => t);
    var backup = Path.Combine(temp, "no-backup");
    Equal(0, FileTransaction.Apply([change], backup)); True(!Directory.Exists(backup));
});
Test("Concurrent config edits are detected before replacing files", () =>
{
    var path = Path.Combine(temp, "race.ini"); File.WriteAllText(path, "before");
    var change = FileChange.Prepare(path, _ => "after");
    Throws<IOException>(() => FileTransaction.Apply([change], Path.Combine(temp, "race-backup"), () => File.WriteAllText(path, "external")));
    Equal("external", File.ReadAllText(path));
    True(!Directory.GetFiles(temp, "*.tmp").Any());
});
Test("Partial commit rolls back earlier files", () =>
{
    var a = Path.Combine(temp, "rollback-a.ini"); var b = Path.Combine(temp, "rollback-b.ini");
    File.WriteAllText(a, "A"); File.WriteAllText(b, "B");
    var ca = FileChange.Prepare(a, _ => "new A"); var cb = FileChange.Prepare(b, _ => "new B");
    Throws<IOException>(() => FileTransaction.Apply([ca, cb], Path.Combine(temp, "rollback-backup"), () => File.WriteAllText(b, "external B")));
    Equal("A", File.ReadAllText(a)); Equal("external B", File.ReadAllText(b));
});
Test("All files are staged before any changes are committed", () =>
{
    var path = Path.Combine(temp, "stage.ini"); File.WriteAllText(path, "before");
    var first = FileChange.Prepare(path, _ => "after");
    var missing = new FileChange(Path.Combine(temp, "missing", "file.ini"), [], [1]);
    Throws<DirectoryNotFoundException>(() => FileTransaction.Apply([first, missing], Path.Combine(temp, "stage-backup")));
    Equal("before", File.ReadAllText(path));
});
if (args is ["--originals", var originalDirectory])
{
    Test("Full supplied configs survive 1–4 player transitions", () =>
    {
        var bizText = File.ReadAllText(Path.Combine(originalDirectory, "BizHawk", "config.ini"));
        var originalBiz = JsonNode.Parse(bizText)!;
        var edenText = File.ReadAllText(Path.Combine(originalDirectory, "Eden", "qt-config.ini"));
        var flyText = File.ReadAllText(Path.Combine(originalDirectory, "FlyCast", "emu.cfg"));
        foreach (var count in new[] { 1, 2, 4, 3, 1, 4 })
        {
            var players = Enumerable.Range(0, count).Select(i => C(i, i, i)).ToArray();
            bizText = BizHawkAdapter.Prepare(bizText, players.Select(c => c.XInputIndex + 1).ToArray());
            edenText = EdenAdapter.Prepare(edenText, players);
            flyText = FlycastAdapter.Prepare(flyText, players);
            var bizOut = JsonNode.Parse(bizText)!;
            foreach (var field in originalBiz.AsObject().Where(p => !p.Key.StartsWith("AllTrollers", StringComparison.Ordinal) && p.Key != "CoreSyncSettings"))
                True(JsonNode.DeepEquals(field.Value, bizOut[field.Key]));
            Equal(bizText, BizHawkAdapter.Prepare(bizText, players.Select(c => c.XInputIndex + 1).ToArray()));
            Equal(edenText, EdenAdapter.Prepare(edenText, players));
            Equal(flyText, FlycastAdapter.Prepare(flyText, players));
        }
    });
}

var failed = 0;
try
{
    foreach (var test in tests)
    {
        try { test.Run(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + test.Name + "\n" + e); }
    }
}
finally { Directory.Delete(temp, recursive: true); }
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
return failed == 0 ? 0 : 1;
