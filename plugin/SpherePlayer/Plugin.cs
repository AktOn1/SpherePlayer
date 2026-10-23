using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace SpherePlayer;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private IDalamudPluginInterface PluginInterface { get; init; } = null!;
    [PluginService] private ICondition Condition { get; init; } = null!;
    [PluginService] private IClientState ClientState { get; init; } = null!;
    [PluginService] private IDataManager Data { get; init; } = null!;
    [PluginService] private ITargetManager Targets { get; init; } = null!;
    [PluginService] private IFramework Framework { get; init; } = null!;
    [PluginService] private ICommandManager Commands { get; init; } = null!;
    [PluginService] private IPluginLog Log { get; init; } = null!;
    [PluginService] private IChatGui Chat { get; init; } = null!;
    [PluginService] private ITextureProvider Textures { get; init; } = null!;
    [PluginService] private IGameConfig GameConfig { get; init; } = null!;

    private readonly WindowSystem windows = new("SpherePlayer");
    private readonly MainWindow window;
    private readonly SongsWindow songs;
    private readonly SettingsWindow settings;
    private Situation current = Situation.Idle;
    private string lastLogged = "";
    private bool bossFight;
    public Queue<string> History { get; } = new();

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Config ?? new Config();
        if (Config.Version < 2) { Config.Mode = PlayMode.Omakase; Config.Version = 2; PluginInterface.SavePluginConfig(Config); }
        Library = new Library();
        var configDir = PluginInterface.GetPluginConfigDirectory();
        Installer = new HostInstaller(System.IO.Path.Combine(configDir, "host"), HostManifest.LoadEmbedded(), Log);
        Player = new PlayerProcess(Installer, System.IO.Path.Combine(configDir, "cef-cache"), Log);
        Controller = new Controller(Library, Config, Player, Guard, Log);
        Video = new VideoFeed(Textures, Log);
        try { Control = new ControlServer(System.IO.Path.Combine(configDir, "control.port"), Log); }
        catch (Exception ex) { Log.Warning(ex, "Control port could not start"); }
        window = new MainWindow(this);
        songs = new SongsWindow(this);
        settings = new SettingsWindow(this);
        window.Songs = songs;
        window.Settings = settings;
        windows.AddWindow(window);
        windows.AddWindow(songs);
        windows.AddWindow(settings);
        // The video is part of the music, so the window always starts open, whatever it was at the last session.
        window.Show();
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ShowMain;
        PluginInterface.UiBuilder.OpenConfigUi += ShowMain;
        foreach (var name in new[] { CommandName, "/sphereplayer" })
            Commands.AddHandler(name, new CommandInfo(OnCommand) { HelpMessage = "Music: /sphere help lists everything (next, stop, on, off, vol, mode, settings, songs ...)" });
        Framework.Update += OnUpdate;
    }

    private const string CommandName = "/sphere";

    public VideoGuard Guard { get; } = new();
    public HostInstaller Installer { get; }

    // Anything that can start music opens and un-collapses the window first: the video has to be visible while it plays.
    public void ShowMain() => window.Show();

    private void Say(string text) => Chat.Print(text, "Sphere Player");

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0] : "";
        var arg = parts.Length > 1 ? parts[1] : "";
        switch (verb)
        {
            case "": case "toggle": case "window": window.Toggle(); break;
            case "settings": case "config": case "options": settings.Toggle(); break;
            case "songs": case "library": songs.Toggle(); break;
            case "next": case "skip": ShowMain(); Controller.Next(current); break;
            case "prev": case "previous": case "back": ShowMain(); Controller.Previous(current); break;
            case "playpause": case "toggle-pause": ShowMain(); Controller.PlayPause(current); break;
            case "stop": Controller.StopNow(); break;
            case "on": Config.Enabled = true; SaveConfig(); Say("Music on."); break;
            case "off": Config.Enabled = false; Controller.StopNow(); SaveConfig(); Say("Music off."); break;
            case "pause": if (!Controller.Paused) Controller.TogglePause(); break;
            case "resume": case "play": ShowMain(); if (Controller.Paused) Controller.TogglePause(); else if (Controller.Now is null) Controller.Next(current); break;
            case "vol": case "volume":
                if (arg.Length > 0 && int.TryParse(arg, out var v)) SetVolume(arg[0] is '+' or '-' ? Config.Volume + v : v);
                else Say($"Volume {Config.Volume}. Use /sphere vol 40, vol +10 or vol -10.");
                break;
            case "mute": if (Config.Volume > 0) { muteBackup = Config.Volume; SetVolume(0); } else SetVolume(muteBackup > 0 ? muteBackup : 30); break;
            case "gamemusic":
                Config.MuteGameMusic = arg is "off" or "unmute" ? false : arg is "on" or "mute" ? true : !Config.MuteGameMusic;
                SaveConfig();
                Say(Config.MuteGameMusic ? "The game's own music is muted while Sphere Player plays." : "The game's own music is left alone.");
                break;
            case "mode":
                if (arg is "omakase") SetMode(PlayMode.Omakase);
                else if (arg is "random") SetMode(PlayMode.Random);
                else SetMode(Config.Mode == PlayMode.Omakase ? PlayMode.Random : PlayMode.Omakase);
                break;
            case "omakase": SetMode(PlayMode.Omakase); break;
            case "random": SetMode(PlayMode.Random); break;
            case "fade":
                if (arg.Length > 0 && float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { Config.FadeSeconds = Math.Clamp(f, 0f, 8f); SaveConfig(); }
                Say($"Fade between area and battle music: {Config.FadeSeconds:0.#} s.");
                break;
            case "ban":
                if (Controller.Now is { } bn) { Config.BannedSongs.Add(bn.Id); SaveConfig(); Say($"Banned: {bn.Title}"); ShowMain(); Controller.Next(current); }
                break;
            case "pin":
                if (Controller.Now is { } pn) { Config.ForcedSongs[pn.Category] = pn.Id; SaveConfig(); Say($"Pinned for {pn.Category}: {pn.Title}"); }
                break;
            case "unpin":
                if (Controller.Now is { } un) { Config.ForcedSongs.Remove(un.Category); SaveConfig(); Say($"Unpinned {un.Category}."); }
                break;
            case "now": case "song":
                Say(Controller.Now is { } now ? $"{now.Game.Replace("Final Fantasy", "FF")}: {now.Title} ({now.Category}). {Controller.Why}" : "Nothing is playing.");
                break;
            default:
                Say("/sphere commands: (none) window | settings | songs | next | prev | playpause | stop | on | off | pause | resume | vol N (or +N, -N) | mute | gamemusic [on|off] | mode [omakase|random] | fade SECONDS | ban | pin | unpin | now. Alias: /ffm. Usable in macros.");
                break;
        }
    }

    private int muteBackup;
    private readonly MediaKeys mediaKeys = new();
    private readonly Dictionary<string, DateTime> mediaAt = new();
    private DateTime nextStatus;
    private string lastStatusKey = "";
    public ControlServer? Control { get; private set; }

    // Same command from two sources within a moment (key seen by the game and by the bridge) counts once.
    public void Media(string cmd)
    {
        var now = DateTime.UtcNow;
        if (mediaAt.TryGetValue(cmd, out var t) && now - t < TimeSpan.FromMilliseconds(400)) return;
        mediaAt[cmd] = now;
        if (cmd is "next" or "prev" or "playpause" or "play") ShowMain();
        switch (cmd)
        {
            case "next": Controller.Next(current); break;
            case "prev": Controller.Previous(current); break;
            case "playpause": Controller.PlayPause(current); break;
            case "play": if (Controller.Paused) Controller.TogglePause(); else if (Controller.Now is null) Controller.Next(current); break;
            case "pause": if (!Controller.Paused && Controller.Now is not null) Controller.TogglePause(); break;
            case "stop": Controller.StopNow(); break;
        }
    }

    private void PumpMedia()
    {
        if (Config.MediaKeys && mediaKeys.Poll() is { } key) Media(key);
        if (Control is null) return;
        while (Control.Commands.TryDequeue(out var c)) Media(c);
        if (!Control.HasClients) return;
        var cn = Controller.Now;
        var st = Player.State;
        var key2 = $"{cn?.Id}|{Controller.Paused}|{st.Status}|{st.Title}|{Config.Volume}";
        if (key2 == lastStatusKey && DateTime.UtcNow < nextStatus) return;
        lastStatusKey = key2;
        nextStatus = DateTime.UtcNow.AddSeconds(4);
        Control.Publish(System.Text.Json.JsonSerializer.Serialize(new
        {
            running = true,
            hasSong = cn is not null,
            playing = cn is not null && !Controller.Paused && st.Status is "playing" or "buffering",
            paused = Controller.Paused,
            id = cn?.Id ?? "",
            title = cn is null ? "" : (string.IsNullOrEmpty(st.Title) ? cn.Title : st.Title),
            game = cn?.Game ?? "",
            category = cn?.Category.ToString() ?? "",
            pos = st.Time,
            dur = st.Dur,
            volume = Config.Volume,
        }));
    }

    private void SetVolume(int v)
    {
        Config.Volume = Math.Clamp(v, 0, 100);
        Player.SetVolume(Config.Volume);
        SaveConfig();
        Say($"Volume {Config.Volume}.");
    }

    private void SetMode(PlayMode m)
    {
        Config.Mode = m;
        SaveConfig();
        Say(m == PlayMode.Omakase ? "Omakase mode: songs are matched to the place you are in." : "Random mode: any song of the right kind.");
    }

    public Config Config { get; }
    public Library Library { get; }
    public PlayerProcess Player { get; }
    public Controller Controller { get; }
    public VideoFeed Video { get; }
    public ZoneInfo Zone { get; private set; } = ZoneInfo.None;
    public void SaveConfig() => PluginInterface.SavePluginConfig(Config);
    public Situation Current => current;
    public string Detail { get; private set; } = "";

    private uint? gameMuteBackup;
    private bool gameMuteBroken;

    // The game's own music is muted only while one of our songs is loaded; Stop or Music off gives it back.
    private void ApplyGameMute()
    {
        var want = Config.MuteGameMusic && Config.Enabled && Controller.Now is not null;
        if (gameMuteBroken) return;
        try
        {
            if (want && gameMuteBackup is null)
            {
                if (GameConfig.System.TryGet("IsSndBgm", out uint cur)) { gameMuteBackup = cur; GameConfig.System.Set("IsSndBgm", 1u); }
            }
            else if (!want && gameMuteBackup is { } b)
            {
                gameMuteBackup = null;
                GameConfig.System.Set("IsSndBgm", b);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Could not change the game music mute"); gameMuteBroken = true; }
    }

    private void OnUpdate(IFramework _)
    {
        var next = Detect(out var detail);
        Detail = detail;
        if (detail != lastLogged)
        {
            lastLogged = detail;
            Log.Information("Raw {Situation}: {Detail}", next, detail);
            History.Enqueue($"{DateTime.Now:HH:mm:ss} {next}: {detail}");
            while (History.Count > 12) History.Dequeue();
        }
        Guard.RequireVisible = !Config.KeepPlayingHidden;
        Guard.Tick();
        Controller.Tick(next, ClientState.IsLoggedIn, Zone);
        ApplyGameMute();
        PumpMedia();
        var videoWanted = Guard.Visible;
        if (Player.Running) { Player.SetVideo(videoWanted); if (videoWanted) Video.Update(Player.Generation); }
        if (next == current) return;
        Log.Information("Situation {Old} -> {New}", current, next);
        current = next;
    }

    private Situation Detect(out string detail)
    {
        detail = "";
        if (!ClientState.IsLoggedIn) { Zone = ZoneInfo.None; return Situation.Idle; }
        var row = Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(ClientState.TerritoryType);
        var use = row?.TerritoryIntendedUse.RowId ?? 0;
        var inDuty = Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BoundByDuty56] || Condition[ConditionFlag.BoundByDuty95];
        var inCombat = Condition[ConditionFlag.InCombat];
        var contentType = row?.ContentFinderCondition.ValueNullable?.ContentType.RowId ?? 0;
        var t = Targets.Target;
        var targetInfo = "none";
        var bossTarget = false;
        if (t is not null)
        {
            var bc = t as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
            var rank = Data.GetExcelSheet<BNpcBase>().GetRowOrDefault(t.BaseId)?.Rank ?? 255;
            bossTarget = inDuty && t.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc && (rank == 2 || (bc?.MaxHp ?? 0) >= 30_000_000);
            targetInfo = $"{t.ObjectKind} name=\"{t.Name}\" baseId={t.BaseId} rank={rank} level={bc?.Level} maxHp={bc?.MaxHp}";
        }
        var place = row?.PlaceName.ValueNullable?.Name.ToString() ?? "?";
        Zone = Library.Zone(ClientState.TerritoryType);
        if (Zone.Name == "") Zone = Zone with { Name = place };
        detail = $"territory={ClientState.TerritoryType} place=\"{place}\" use={use} contentType={contentType} duty={inDuty} combat={inCombat} loading={Condition[ConditionFlag.BetweenAreas]} zone={Zone.Kind}/{string.Join(",", Zone.Tags)} target={targetInfo}";
        // The target flickers to none during a fight, so a boss target keeps the boss music until combat ends.
        if (!inCombat) bossFight = false;
        else if (bossTarget) bossFight = true;
        if (inCombat) return bossFight ? Situation.Boss : Situation.Battle;
        return Zone.Kind switch
        {
            "town" => Situation.Town,
            "field" => Situation.Field,
            "dungeon" => Situation.Dungeon,
            _ => use == 0 ? Situation.Town : use == 1 ? Situation.Field : inDuty ? Situation.Dungeon : Situation.Idle,
        };
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        if (gameMuteBackup is { } b) { try { GameConfig.System.Set("IsSndBgm", b); } catch { } gameMuteBackup = null; }
        Commands.RemoveHandler(CommandName);
        Commands.RemoveHandler("/sphereplayer");
        Control?.Dispose();
        Player.Dispose();
        Installer.Dispose();
        Video.Dispose();
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        windows.RemoveAllWindows();
    }
}
