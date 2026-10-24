using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SpherePlayer;

public sealed class SettingsWindow : Window
{
    private readonly Plugin p;

    public SettingsWindow(Plugin p) : base("Sphere Player: settings##settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.p = p;
        var screen = ImGui.GetMainViewport().Size;
        Position = new Vector2(Math.Max(20, screen.X - 760), 140);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    private static readonly bool IsWine = DetectWine();

    // Fetches the helper from this release's tag (not the moving main branch) and refuses to install it unless both files match the hashes built into this plugin.
    private static readonly string BridgeCommand = BuildBridgeCommand();

    private static string BuildBridgeCommand()
    {
        var asm = typeof(SettingsWindow).Assembly;
        string? Meta(string key) => asm.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(m => m.Key == key)?.Value;
        var script = Meta("BridgeSha256.sphere-player-mpris")?.ToLowerInvariant();
        var installer = Meta("BridgeSha256.install.sh")?.ToLowerInvariant();
        var v = asm.GetName().Version;
        if (script == null || installer == null || v == null) return "";
        var baseUrl = $"https://raw.githubusercontent.com/AktOn1/SpherePlayer/v{v.Major}.{v.Minor}.{v.Build}/tools/mpris-bridge/";
        return "mkdir -p ~/.local/share/sphere-player && cd ~/.local/share/sphere-player && " +
               $"curl --proto '=https' -fsSL -O {baseUrl}sphere-player-mpris -O {baseUrl}install.sh && " +
               $"printf '%s\\n' '{script}  sphere-player-mpris' '{installer}  install.sh' | sha256sum -c - && " +
               "chmod +x sphere-player-mpris install.sh && ./install.sh";
    }

    private static bool DetectWine()
    {
        try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Wine"); return k != null; }
        catch { return false; }
    }

    private void DrawLinuxKeys()
    {
        ImGui.TextDisabled("Linux: the desktop usually swallows media keys before the game sees them.");
        ImGui.TextDisabled("Optional: a small helper outside the game passes them on. Nothing is installed by this plugin.");
        var connected = p.Control?.HasClients == true;
        ImGui.TextColored(connected ? new Vector4(0.5f, 1, 0.5f, 1) : new Vector4(1, 0.8f, 0.4f, 1),
            connected ? "Helper connected: keys reach Sphere Player." : "Helper not running.");
        if (!connected && BridgeCommand.Length > 0)
        {
            if (ImGui.Button("Copy install command")) ImGui.SetClipboardText(BridgeCommand);
            ImGui.SameLine();
            ImGui.TextDisabled("Paste it into a terminal. Needs python3-dbus. Remove: systemctl --user disable --now sphere-player-mpris");
        }
    }

    public override void Draw()
    {
        var cfg = p.Config;

        var omi = cfg.Mode == PlayMode.Omakase;
        if (ImGui.Checkbox("Omakase mode (songs fit the place)", ref omi)) { cfg.Mode = omi ? PlayMode.Omakase : PlayMode.Random; p.SaveConfig(); }
        ImGui.TextDisabled("Off = any song of the right kind. On = Gridania gets forest songs, deserts get desert songs.");
        if (!p.Library.HasAreaSongs) ImGui.TextColored(new Vector4(1, 0.6f, 0.3f, 1), "This build has no area songs yet, omakase falls back to town songs.");

        var gm = cfg.MuteGameMusic;
        if (ImGui.Checkbox("Mute the game's own music while Sphere Player plays", ref gm)) { cfg.MuteGameMusic = gm; p.SaveConfig(); }
        ImGui.TextDisabled("Uses the game's own BGM mute and gives it back on Stop, Music off, or when the plugin unloads.");

        ImGui.Separator();
        var res = cfg.ResumeArea;
        if (ImGui.Checkbox("Continue the area song after a fight", ref res)) { cfg.ResumeArea = res; p.SaveConfig(); }
        ImGui.SetNextItemWidth(200);
        var fade = cfg.FadeSeconds;
        if (ImGui.SliderFloat("Fade (seconds)", ref fade, 0f, 8f, "%.1f")) { cfg.FadeSeconds = fade; p.SaveConfig(); }

        ImGui.Separator();
        ImGui.SetNextItemWidth(200);
        var vol = cfg.Volume;
        if (ImGui.SliderInt("Volume", ref vol, 0, 100)) { cfg.Volume = vol; p.Player.SetVolume(vol); p.SaveConfig(); }
        ImGui.SetNextItemWidth(200);
        var vw = cfg.VideoWidth;
        if (ImGui.SliderInt("Video width", ref vw, VideoGuard.MinWidth, 480)) { cfg.VideoWidth = vw; p.SaveConfig(); }
        var mini = cfg.AlwaysMini;
        if (ImGui.Checkbox("Always use the small window", ref mini)) { cfg.AlwaysMini = mini; p.SaveConfig(); }
        ImGui.TextDisabled("Video plus one line; the controls open while the mouse is over it. Off = small only in battle.");
        ImGui.TextDisabled("YouTube wants the video visible while music plays: in battle the window shrinks to the smallest size instead of hiding.");
        var keep = cfg.KeepPlayingHidden;
        if (ImGui.Checkbox("Keep playing when the window is collapsed or closed", ref keep)) { cfg.KeepPlayingHidden = keep; p.SaveConfig(); }
        ImGui.TextDisabled("Off = the music pauses while the video is hidden and continues when it is back (YouTube's embed rule).");
        var click = cfg.ClickableVideo;
        if (ImGui.Checkbox("Video is clickable (click to pause, seek bar, links)", ref click)) { cfg.ClickableVideo = click; p.SaveConfig(); }
        var keys = cfg.MediaKeys;
        if (ImGui.Checkbox("Hardware media keys (next, previous, play/pause)", ref keys)) { cfg.MediaKeys = keys; p.SaveConfig(); }
        if (IsWine) DrawLinuxKeys();
        ImGui.SetNextItemWidth(200);
        var a = cfg.WindowAlpha;
        if (ImGui.SliderFloat("Window opacity", ref a, 0.2f, 1f)) { cfg.WindowAlpha = a; p.SaveConfig(); }

        ImGui.Separator();
        ImGui.TextDisabled("Chat: /sphere help. Lower the game's own BGM volume in its sound settings.");
    }
}
