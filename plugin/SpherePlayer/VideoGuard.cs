using System;

namespace SpherePlayer;

// YouTube's terms: the video must be visible while its audio plays. Music may only run while the music window really drew its video recently.
public sealed class VideoGuard
{
    // 16:9 video that is at least 200 pixels high and wide.
    public const int MinWidth = 356;
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Stall = TimeSpan.FromSeconds(1);

    private readonly Func<DateTime> clock;
    private DateTime lastDrawn = DateTime.MinValue;
    private DateTime graceUntil = DateTime.MinValue;
    private DateTime lastTick = DateTime.MinValue;

    public VideoGuard(Func<DateTime>? clock = null) { this.clock = clock ?? (() => DateTime.UtcNow); }

    // Called by the music window every time it draws the video area.
    public void Drawn() => lastDrawn = clock();

    // Called once per game frame. A long gap (loading screen, freeze) must not look like a hidden window.
    public void Tick()
    {
        var now = clock();
        if (lastTick != DateTime.MinValue && now - lastTick > Stall) graceUntil = Max(graceUntil, now + Fresh);
        lastTick = now;
    }

    // A command asked for the window to be shown; give it a moment to appear before music may start.
    public void Grant() => graceUntil = Max(graceUntil, clock() + Grace);

    // Size of the drawn video: the configured width, or the smallest allowed size in battle. Never below 200x200.
    public static (int W, int H) VideoSize(bool compact, int configuredWidth)
    {
        var w = compact ? MinWidth : Math.Clamp(configuredWidth, MinWidth, 480);
        return (w, w * 9 / 16);
    }

    public DateTime LastDrawn => lastDrawn;

    public bool Visible => clock() - lastDrawn < Fresh;

    // User choice: let the music play on while the window is collapsed or closed.
    public bool RequireVisible { get; set; } = true;

    public bool MayPlay => !RequireVisible || Visible || clock() < graceUntil;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
