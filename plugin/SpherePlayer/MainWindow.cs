using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SpherePlayer;

public sealed class MainWindow : Window
{
    private readonly Plugin p;
    public SongsWindow Songs { get; set; } = null!;
    public SettingsWindow Settings { get; set; } = null!;

    public MainWindow(Plugin p) : base("Sphere Player##main", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.p = p;
        var screen = ImGui.GetMainViewport().Size;
        Position = new Vector2(System.Math.Max(20, screen.X - 360), 90);
        PositionCondition = ImGuiCond.FirstUseEver;
        BgAlpha = p.Config.WindowAlpha;
    }

    private DateTime expandAskedAt = DateTime.MinValue;

    // Opens the window and un-collapses it (a collapsed window draws no video).
    public void Show()
    {
        IsOpen = true;
        Collapsed = false;
        CollapsedCondition = ImGuiCond.Always;
        expandAskedAt = DateTime.UtcNow;
        p.Guard.Grant();
    }

    public override void Update()
    {
        // The un-collapse request has done its job once the window drew; after that the user may collapse it again.
        if (Collapsed.HasValue && p.Guard.LastDrawn > expandAskedAt) { Collapsed = null; CollapsedCondition = ImGuiCond.None; }
    }

    public override void PreDraw() => BgAlpha = p.Config.WindowAlpha;

    public override void Draw()
    {
        var cfg = p.Config;
        p.Guard.Drawn();
        var host = p.Player.Installer;
        if (!host.Ready) { DrawInstall(host); return; }
        // In battle the window shrinks to the smallest size YouTube allows (video at least 200x200) instead of disappearing.
        var compact = p.Current is Situation.Battle or Situation.Boss;
        var (vw, vh) = VideoGuard.VideoSize(compact, cfg.VideoWidth);
        if (p.Controller.Now is not null && p.Video.Texture is { } tex)
        {
            var imgPos = ImGui.GetCursorScreenPos();
            ImGui.Image(tex.Handle, new Vector2(vw, vh));
            if (cfg.ClickableVideo) ForwardMouse(imgPos, vw, vh);
        }
        else
        {
            var pos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(vw, vh));
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(vw, vh), 0xFF101010);
            ImGui.GetWindowDrawList().AddText(pos + new Vector2(12, vh / 2 - 8), 0xFFAAAAAA, p.Controller.Now is null ? "No music playing" : "Video starting...");
        }
        if (p.Controller.HiddenPause)
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.3f, 1), "Paused: the video was hidden.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Resume##hidden")) p.Controller.TogglePause();
        }
        if (compact)
        {
            var t = p.Controller.Now is { } n ? (string.IsNullOrEmpty(p.Player.State.Title) ? n.Title : p.Player.State.Title) : "-";
            var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            ImGui.TextUnformatted($"{p.Current}: {Trim(t, hovered ? 34 : 44)}");
            if (hovered)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Next##mini")) p.Controller.Next(p.Current);
            }
            return;
        }
        var enabled = cfg.Enabled;
        if (ImGui.Checkbox("Music##on", ref enabled)) { cfg.Enabled = enabled; if (!enabled) p.Controller.StopNow(); p.SaveConfig(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("Next")) p.Controller.Next(p.Current);
        ImGui.SameLine();
        if (ImGui.SmallButton("Stop")) p.Controller.StopNow();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Stop: ends the song and stays quiet until you press Next or Resume.\nPause: keeps the place in the song and continues from there.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Songs...")) Songs.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings")) Settings.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton(p.Controller.Paused ? "Resume" : "Pause")) p.Controller.TogglePause();

        var gm = cfg.MuteGameMusic;
        if (ImGui.Checkbox("Mute the game's own music##gm", ref gm)) { cfg.MuteGameMusic = gm; p.SaveConfig(); }
        ImGui.SetNextItemWidth(vw - 100);
        var vol = cfg.Volume;
        if (ImGui.SliderInt("Volume", ref vol, 0, 100)) { cfg.Volume = vol; p.Player.SetVolume(vol); p.SaveConfig(); }

        var title = p.Controller.Now is { } now ? (string.IsNullOrEmpty(p.Player.State.Title) ? now.Title : p.Player.State.Title) : "-";
        ImGui.TextUnformatted($"{p.Current}: {Trim(title, 46)}");
        if (p.Controller.Why != "") ImGui.TextDisabled(Trim(p.Controller.Why, 56));
        if (p.Controller.Note != "") ImGui.TextColored(new Vector4(1, 0.6f, 0.3f, 1), Trim(p.Controller.Note, 56));

        ImGui.TextDisabled($"Mode: {cfg.Mode}. /sphere help lists the chat commands.");
    }

    private void DrawInstall(HostInstaller host)
    {
        ImGui.TextUnformatted("The music player needs a one-time download.");
        ImGui.TextDisabled("It is a small web browser (Chromium) that plays the YouTube videos.");
        ImGui.TextDisabled("About 154 MB from GitHub, checked against a fixed SHA-256 before it is used.");
        ImGui.Spacing();
        switch (host.Phase)
        {
            case HostPhase.Idle:
                if (ImGui.Button("Download now##host")) host.Start();
                break;
            case HostPhase.Failed:
                ImGui.TextColored(new Vector4(1, 0.4f, 0.3f, 1), "Download failed: " + host.Error);
                if (ImGui.Button("Try again##host")) host.Retry();
                break;
            case HostPhase.Unpacking:
                ImGui.TextUnformatted("Unpacking...");
                break;
            case HostPhase.Verifying:
                ImGui.TextUnformatted("Checking the download...");
                break;
            default:
            {
                var total = Math.Max(1, host.BytesTotal);
                var frac = Math.Clamp((float)host.BytesDone / total, 0f, 1f);
                ImGui.ProgressBar(frac, new Vector2(VideoGuard.MinWidth, 0), $"{host.BytesDone / 1048576} of {total / 1048576} MB");
                if (host.Error != "") ImGui.TextColored(new Vector4(1, 0.6f, 0.3f, 1), $"Attempt {host.Attempt} of {host.Attempts}. Last problem: {Trim(host.Error, 60)}");
                break;
            }
        }
    }

    private bool dragging;
    private int lastX = -1, lastY = -1;
    private DateTime lastMoveAt;

    // Sends mouse input over the video to the player's browser, scaled from the drawn size to the browser's size.
    // An invisible button covers the video so dragging on it (seek bar) does not move the window.
    private void ForwardMouse(Vector2 pos, float vw, float vh)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##video", new Vector2(vw, vh), ImGuiButtonFlags.MouseButtonLeft);
        var fw = p.Video.Width > 0 ? p.Video.Width : 480;
        var fh = p.Video.Height > 0 ? p.Video.Height : 270;
        var hovered = ImGui.IsItemHovered();
        var m = ImGui.GetMousePos();
        var x = (int)Math.Clamp((m.X - pos.X) / vw * fw, 0, fw - 1);
        var y = (int)Math.Clamp((m.Y - pos.Y) / vh * fh, 0, fh - 1);
        if (hovered || dragging)
        {
            if ((x != lastX || y != lastY) && DateTime.UtcNow - lastMoveAt > TimeSpan.FromMilliseconds(40))
            {
                p.Player.Mouse("move", x, y); lastX = x; lastY = y; lastMoveAt = DateTime.UtcNow;
            }
            if (hovered && ImGui.GetIO().MouseWheel != 0) p.Player.Mouse("wheel", x, y, (int)(ImGui.GetIO().MouseWheel * 120));
        }
        if (ImGui.IsItemActivated()) { p.Player.Mouse("down", x, y); dragging = true; }
        else if (dragging && !ImGui.IsItemActive()) { p.Player.Mouse("up", x, y); dragging = false; }
        if (!hovered && !dragging && lastX >= 0) { p.Player.Mouse("leave", 0, 0); lastX = lastY = -1; }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
