using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoEmuControllerConfig;

internal sealed class SdlControllers : IDisposable
{
    private readonly nint library;
    private readonly bool sdl3;
    private bool initialized;
    private bool disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlGuid { public ulong A, B; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init2(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Init3(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint JoystickList(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint OpenJoystick(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseJoystick(nint joystick);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int JoystickInt(nint joystick);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint JoystickName(nint joystick);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate SdlGuid JoystickGuid(nint joystick);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GuidString(SdlGuid guid, nint buffer, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetError();

    public string LibraryPath { get; }

    public SdlControllers(EmulatorInstall emulator)
    {
        (LibraryPath, sdl3) = FindLibrary(emulator);
        library = NativeLibrary.Load(LibraryPath);
        try
        {
            var hint = Function<SetHint>("SDL_SetHint");
            hint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (emulator.Kind == EmulatorKind.Eden)
            {
                var ini = new IniDocument(File.ReadAllText(emulator.ConfigPath));
                hint("SDL_JOYSTICK_RAWINPUT", ini.GetBoolean("Controls", "enable_raw_input", false) ? "1" : "0");
                hint("SDL_JOYSTICK_HIDAPI_XBOX", "0");
                hint("SDL_JOYSTICK_HIDAPI_JOY_CONS", ini.GetBoolean("Controls", "enable_joycon_driver", true) ? "0" : "1");
                hint("SDL_JOYSTICK_HIDAPI_SWITCH", ini.GetBoolean("Controls", "enable_procon_driver", false) ? "0" : "1");
                if (ini.GetBoolean("Controls", "disable_wgi_xinput", false))
                {
                    hint("SDL_JOYSTICK_RAWINPUT_CORRELATE_XINPUT", "0");
                    hint("SDL_JOYSTICK_WGI", "0");
                }
            }
            if (emulator.Kind == EmulatorKind.Flycast && new IniDocument(File.ReadAllText(emulator.ConfigPath)).GetBoolean("input", "DisableXInput", false))
                throw new InvalidOperationException("Flycast has DisableXInput enabled; cannot safely assign Xbox controllers.");
            // Joystick only: opening gamepads can assign artificial SDL player indices
            // to non-XInput devices. The emulator-specific GUID/port list needs neither
            // a window nor an input polling loop.
            initialized = sdl3 ? Function<Init3>("SDL_Init")(0x200) != 0 : Function<Init2>("SDL_Init")(0x200) == 0;
            if (!initialized)
                throw new InvalidOperationException("SDL initialization failed: " + Marshal.PtrToStringUTF8(Function<GetError>("SDL_GetError")()));
        }
        catch { Dispose(); throw; }
    }

    public Controller[] Read(IReadOnlyList<int> expectedSlots)
    {
        var handles = new List<nint>();
        try
        {
            var indices = Enumerate();
            var open = Function<OpenJoystick>(sdl3 ? "SDL_OpenJoystick" : "SDL_JoystickOpen");
            foreach (var index in indices)
            {
                var handle = open(index);
                if (handle == 0) throw new InvalidOperationException("A controller disappeared during SDL enumeration. Retry launch.");
                handles.Add(handle);
            }
            var getGuid = Function<JoystickGuid>(sdl3 ? "SDL_GetJoystickGUID" : "SDL_JoystickGetGUID");
            var getPlayer = Function<JoystickInt>(sdl3 ? "SDL_GetJoystickPlayerIndex" : "SDL_JoystickGetPlayerIndex");
            var getInstance = Function<JoystickInt>(sdl3 ? "SDL_GetJoystickID" : "SDL_JoystickInstanceID");
            var getName = Function<JoystickName>(sdl3 ? "SDL_GetJoystickName" : "SDL_JoystickName");
            var update = Function<VoidCall>(sdl3 ? "SDL_UpdateJoysticks" : "SDL_JoystickUpdate");
            var deadline = Stopwatch.StartNew();
            Controller[] result;
            do
            {
                update();
                var matches = new List<Controller>();
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < handles.Count; i++)
                {
                    var rawGuid = FormatGuid(getGuid(handles[i]));
                    var guid = NormalizeEdenGuid(rawGuid);
                    var port = counts.GetValueOrDefault(guid);
                    counts[guid] = port + 1;
                    var slot = getPlayer(handles[i]);
                    // 'x' = XInput, 'r' = Windows RawInput (XInput correlation).
                    // Do not mistake SDL's general player indices for XInput indices.
                    var backend = rawGuid.Substring(28, 2);
                    if (backend is not ("78" or "72")) continue;
                    matches.Add(new Controller(slot, i, getInstance(handles[i]), guid,
                        Marshal.PtrToStringUTF8(getName(handles[i])) ?? "Xbox controller", port,
                        backend == "78" && slot >= 0));
                }
                result = matches.ToArray();
                if (result.Length == expectedSlots.Count)
                    return ControllerMatcher.Match(result, expectedSlots);
                Thread.Sleep(50);
            } while (deadline.Elapsed < TimeSpan.FromSeconds(1));
            throw new InvalidOperationException($"SDL could match {result.Length} of {expectedSlots.Count} Xbox controllers using {LibraryPath}. No device order was guessed.");
        }
        finally
        {
            var close = Function<CloseJoystick>(sdl3 ? "SDL_CloseJoystick" : "SDL_JoystickClose");
            foreach (var handle in handles) close(handle);
        }
    }

    internal static string NormalizeEdenGuid(string guid) => guid[..4] + "0000" + guid[8..];

    private int[] Enumerate()
    {
        if (!sdl3) return Enumerable.Range(0, Math.Max(0, Function<IntCall>("SDL_NumJoysticks")())).ToArray();
        var pointer = Function<JoystickList>("SDL_GetJoysticks")(out var count);
        if (pointer == 0) return [];
        try
        {
            var ids = new int[count];
            Marshal.Copy(pointer, ids, 0, count);
            return ids;
        }
        finally { Function<CloseJoystick>("SDL_free")(pointer); }
    }

    private string FormatGuid(SdlGuid guid)
    {
        var buffer = Marshal.AllocHGlobal(33);
        try
        {
            Function<GuidString>(sdl3 ? "SDL_GUIDToString" : "SDL_JoystickGetGUIDString")(guid, buffer, 33);
            return Marshal.PtrToStringUTF8(buffer)!.ToLowerInvariant();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private T Function<T>(string symbol) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (initialized) { Function<VoidCall>("SDL_Quit")(); initialized = false; }
        if (library != 0) NativeLibrary.Free(library);
    }

    private static (string Path, bool Sdl3) FindLibrary(EmulatorInstall emulator)
    {
        var roots = new[] { emulator.Directory, Path.Combine(emulator.Directory, "dll"), Path.Combine(emulator.Directory, "bin") };
        foreach (var root in roots)
        {
            if (emulator.Kind == EmulatorKind.Eden && File.Exists(Path.Combine(root, "SDL3.dll")))
                return (Path.Combine(root, "SDL3.dll"), true);
            if (File.Exists(Path.Combine(root, "SDL2.dll"))) return (Path.Combine(root, "SDL2.dll"), false);
        }
        // Statically linked Eden builds expose their SDL import names as strings.
        // Identify the API rather than assuming an emulator version.
        var is3 = false;
        if (emulator.Kind == EmulatorKind.Eden)
        {
            var exe = Path.Combine(emulator.Directory, "eden.exe");
            var bytes = File.ReadAllBytes(exe);
            is3 = bytes.AsSpan().IndexOf("SDL_GetJoysticks"u8) >= 0;
            if (!is3 && bytes.AsSpan().IndexOf("SDL_NumJoysticks"u8) < 0)
                throw new InvalidOperationException("Cannot identify this Eden build's SDL version. Its SDL2.dll or SDL3.dll is required beside eden.exe.");
        }
        var bundled = Path.Combine(AppContext.BaseDirectory, is3 ? "SDL3.dll" : "SDL2.dll");
        if (!File.Exists(bundled)) throw new FileNotFoundException("Use the complete release package; the SDL runtime is missing.", bundled);
        return (bundled, is3);
    }

    public static bool BizHawkUsesSdl(string directory)
    {
        foreach (var root in new[] { directory, Path.Combine(directory, "dll") })
        {
            var dll = Path.Combine(root, "BizHawk.Bizware.Input.dll");
            if (!File.Exists(dll)) continue;
            using var file = File.OpenRead(dll);
            using var pe = new PEReader(file);
            var metadata = pe.GetMetadataReader();
            if (metadata.TypeDefinitions.Any(h => metadata.GetString(metadata.GetTypeDefinition(h).Name) == "SDL2InputAdapter"))
                return true;
        }
        var version = FileVersionInfo.GetVersionInfo(Path.Combine(directory, "EmuHawk.exe"));
        if (version.FileMajorPart == 2 && version.FileMinorPart < 10) return false;
        throw new InvalidOperationException("Cannot identify this BizHawk input backend.");
    }
}
