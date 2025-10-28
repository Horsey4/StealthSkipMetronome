using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal enum Status
{
    Disarmed = 0b000,
    Armed    = 0b001,
    Playing  = 0b010,
    Waiting  = 0b100,

    ArmedOrPlaying = 0b011,
    PlayingOrWaiting = 0b110
}

internal class Program
{
    private const int FPS = 30;
    private const int Beats = 4;

    private const int WH_KEYBOARD_LL = 13;
    private const IntPtr WM_KEYDOWN = 0x0100;
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private const string configFile = "config.ini";
    private const string defaultConfig =
        "# See: https://learn.microsoft.com/windows/inputdev/virtual-key-codes\n" +
        "ArmKeycode = 0x47 # Default: G\n" +
        "Timing = 19       # Default: 19";

    private static int armKeycode;
    private static double timing;
    private static Status status;
    private static Stopwatch timer = new();

    private static void Main()
    {
        int i;
        if ((i = LoadConfig()) != -1)
        {
            Console.WriteLine($"Config line {i + 1} malformed");
            Application.Run();
            return;
        }

        var hook = SetWindowsHookEx(WH_KEYBOARD_LL, OnKeyboardInput, 0, 0);
        Application.Run();
        UnhookWindowsHookEx(hook);
    }

    private static int LoadConfig()
    {
        if (!File.Exists(configFile)) File.WriteAllText(configFile, defaultConfig);

        var lines = File.ReadAllLines(configFile);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#')) continue;

            var args = lines[i].Split('=', 2, StringSplitOptions.TrimEntries);
            if (args.Length != 2 || args[0].Contains('#')) return i;
            else
            {
                args[1] = args[1].Split('#', 2, StringSplitOptions.TrimEntries)[0];
                switch (args[0])
                {
                    case "ArmKeycode":
                        if (args[1].StartsWith("0x"))
                        {
                            if (!int.TryParse(args[1][2..^0], NumberStyles.HexNumber, default, out armKeycode)) return i;
                        }
                        else if (!int.TryParse(args[1], out armKeycode)) return i;
                        break;
                    case "Timing":
                        if (!double.TryParse(args[1], out timing)) return i;
                        break;
                }
            }
        }

        return -1;
    }

    private static IntPtr OnKeyboardInput(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var keyCode = Marshal.ReadInt32(lParam);

        if (wParam == WM_KEYDOWN)
        {
            switch (keyCode)
            {
                default:
                    if (keyCode != armKeycode || (status & Status.ArmedOrPlaying) != 0) break;

                    new SoundPlayer("strong_beat.wav").Play();
                    status = Status.Armed;
                    Console.WriteLine("Armed");

                    break;
                case 0x5A: // Z
                case 0x0D: // Enter
                    if (status != Status.Armed) break;

                    status = Status.PlayingOrWaiting;
                    new Thread(() =>
                    {
                        timer.Restart();
                        var interval = (timing / FPS) / (Beats - 1);
                        var beat = new SoundPlayer("beat.wav");
                        var strongBeat = new SoundPlayer("strong_beat.wav");

                        beat.Play();
                        Wait(interval);
                        beat.Play();
                        Wait(interval);
                        beat.Play();
                        Wait(interval);
                        strongBeat.Play();
                        status &= ~Status.Playing;
                    }).Start();

                    break;
                case 0x43: // C
                case 0x11: // Ctrl
                    if ((status & Status.PlayingOrWaiting) == 0) break;

                    timer.Stop();
                    status = Status.Disarmed;
                    var delta = (timer.ElapsedTicks / 10_000_000.0 - (timing / FPS)) * FPS;
                    Console.WriteLine($"{delta:+0.00;-0.00;-0.00}f");

                    break;
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static void Wait(double seconds)
    {
        var ticks = (long)(seconds * 10_100_000);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedTicks < ticks);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
}