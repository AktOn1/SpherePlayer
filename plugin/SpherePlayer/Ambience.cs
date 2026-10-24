using System;
using System.Collections.Generic;
using System.Linq;

namespace SpherePlayer;

// Time of day (Eorzea clock) and weather of the current zone. They nudge which mood omakase prefers; they never change the place match.
public readonly record struct Ambience(bool Night, string Weather)
{
    public static readonly Ambience None = new(false, "");

    // Eorzea time runs 144/7 times faster than real time; a day is 70 real minutes.
    public static int EorzeaHour(DateTime utcNow) => (int)(new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() * 144 / 7 / 3600 % 24);
    public static bool IsNight(int eorzeaHour) => eorzeaHour >= 18 || eorzeaHour < 6;

    // Weight per mood for a zone: its own moods (3, 2) plus a small shift from night and weather.
    public Dictionary<string, int> MoodWeights(string[] zoneMoods)
    {
        var w = new Dictionary<string, int>();
        for (var i = 0; i < zoneMoods.Length && i < 2; i++) w[zoneMoods[i]] = 3 - i;
        void Add(string mood, int d) => w[mood] = Math.Clamp((w.TryGetValue(mood, out var v) ? v : 0) + d, 0, 4);
        if (Night) { Add("calm", 1); Add("mysterious", 1); Add("melancholy", 1); Add("lively", -1); Add("cheerful", -1); }
        var wx = Weather.ToLowerInvariant();
        if (wx.Contains("rain") || wx.Contains("shower") || wx.Contains("gloom")) { Add("melancholy", 1); Add("calm", 1); Add("mysterious", 1); }
        else if (wx.Contains("fog")) { Add("mysterious", 2); Add("calm", 1); }
        else if (wx.Contains("thunder") || wx.Contains("storm") || wx.Contains("gale") || wx.Contains("blizzard") || wx.Contains("umbral") || wx.Contains("tempest")) { Add("tense", 1); Add("epic", 1); Add("lively", -1); }
        else if (wx.Contains("snow")) { Add("calm", 1); Add("mysterious", 1); }
        else if (!Night && (wx.Contains("clear") || wx.Contains("fair"))) { Add("cheerful", 1); Add("calm", 1); }
        return w;
    }

    public string Label => (Night ? "night" : "day") + (Weather == "" ? "" : ", " + Weather.ToLowerInvariant());
}
