using System;
using System.Runtime.InteropServices;

namespace SpherePlayer;

// Polls the hardware media keys (works while the game is in the background on Windows; under Wine only when the key reaches the game).
public sealed class MediaKeys
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private static readonly (int Vk, string Cmd)[] Keys = { (0xB0, "next"), (0xB1, "prev"), (0xB2, "stop"), (0xB3, "playpause") };
    private readonly bool[] down = new bool[Keys.Length];
    private bool broken;

    public string? Poll()
    {
        if (broken) return null;
        string? hit = null;
        try
        {
            for (var i = 0; i < Keys.Length; i++)
            {
                var d = (GetAsyncKeyState(Keys[i].Vk) & 0x8000) != 0;
                if (d && !down[i]) hit ??= Keys[i].Cmd;
                down[i] = d;
            }
        }
        catch { broken = true; }
        return hit;
    }
}
