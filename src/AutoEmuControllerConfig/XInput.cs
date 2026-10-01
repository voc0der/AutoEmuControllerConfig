using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoEmuControllerConfig;

internal static class XInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint index, out State state);

    public static int[] Connected() => Enumerable.Range(0, 4)
        .Where(i => XInputGetState((uint)i, out _) == 0).ToArray();

    public static int[] WaitForStableControllers()
    {
        var timer = Stopwatch.StartNew();
        var stableSince = TimeSpan.Zero;
        var previous = Connected();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(75);
            var current = Connected();
            if (!current.SequenceEqual(previous)) { previous = current; stableSince = timer.Elapsed; }
            if (current.Length > 0 && timer.Elapsed - stableSince >= TimeSpan.FromMilliseconds(300)) return current;
        }
        throw new InvalidOperationException(previous.Length == 0
            ? "No Xbox/XInput controller appeared within 5 seconds. Connect a controller and retry launch. No settings were changed."
            : "Controllers kept changing during preparation. Retry launch once they are connected.");
    }
}
