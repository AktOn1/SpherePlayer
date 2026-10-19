using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace SpherePlayer;

// Picks songs for the current situation. Battle and a new zone switch at once, going back to calm music in the same zone waits so a short pause does not swap the track.
public sealed class Controller
{
    private static readonly TimeSpan BattleHold = TimeSpan.FromSeconds(0.3);
    private static readonly TimeSpan CalmHold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FirstHold = TimeSpan.FromSeconds(0.5);
    private const int MaxErrorsInRow = 6;
    private const int RecentKeep = 8;

    private readonly Library library;
    private readonly Config config;
    private readonly PlayerProcess player;
    private readonly VideoGuard guard;
    private readonly IPluginLog log;
    private readonly Random rng = new();

    private Category? wantPending;
    private DateTime wantSince;
    private Category? lockedWant;
    private int errorsInRow;
    private int seenEnds, seenErrors;
    private DateTime lastRestart;
    private DateTime lastPlayAt;
    private double lastStart;
    private int retries;
    private string lastLoggedStatus = "";
    private ZoneInfo zone = ZoneInfo.None;
    private uint playedZoneId;
    private Category lastAreaCategory = Category.Town;
    private Song? areaSong;
    private double areaTime, areaDur;
    private uint areaZoneId;
    private readonly Queue<string> recent = new();
    private readonly List<Song> history = new();
    private DateTime ourPauseAt;
    private bool hostWarmed;
    private bool menuSong;
    private bool menuDone;

    public Song? Now { get; private set; }
    public string Note { get; private set; } = "";
    public string Why { get; private set; } = "";
    public bool Paused { get; private set; }
    // Paused because the video window was collapsed, closed or hidden. Music resumes by itself when the video is visible again.
    public bool HiddenPause { get; private set; }
    // Stop was pressed: nothing starts by itself until Next, Play or a manual pick.
    public bool Stopped { get; private set; }

    public Controller(Library library, Config config, PlayerProcess player, VideoGuard guard, IPluginLog log)
    {
        this.library = library; this.config = config; this.player = player; this.guard = guard; this.log = log;
    }

    public static bool IsArea(Category c) => c is Category.Town or Category.Field or Category.Dungeon;

    public IEnumerable<Song> Eligible(Category cat) =>
        library.Songs.Where(s => s.Category == cat && IsOk(s));

    private bool IsOk(Song s) => !config.DisabledGames.Contains(s.Game) && !config.BannedSongs.Contains(s.Id);

    private int FadeMs => (int)(Math.Clamp(config.FadeSeconds, 0f, 8f) * 1000);

    public void Tick(Situation situation, bool loggedIn, ZoneInfo currentZone)
    {
        zone = currentZone;
        while (player.Events.TryDequeue(out var ev))
        {
            if (ev.Ends > seenEnds) { seenEnds = ev.Ends; errorsInRow = 0; Advance(); }
            if (ev.Errors > seenErrors)
            {
                seenErrors = ev.Errors;
                errorsInRow++;
                log.Warning("YouTube error {Code} on {Title}", ev.Error ?? -1, Now?.Title ?? "?");
                if (errorsInRow >= MaxErrorsInRow) { Note = $"Stopped after {MaxErrorsInRow} unplayable songs in a row (last YouTube error {ev.Error})."; Now = null; }
                else Advance();
            }
            if (ev.Status == "playing") { errorsInRow = 0; retries = 0; }
            // The video can be clicked: keep our pause flag in line with what the player really does.
            if (Now is not null && DateTime.UtcNow - ourPauseAt > TimeSpan.FromSeconds(2))
            {
                if (ev.Status == "paused" && !Paused) Paused = true;
                else if (ev.Status == "playing" && Paused)
                {
                    if (guard.MayPlay) { Paused = false; HiddenPause = false; }
                    else player.Pause();
                }
            }
            if (ev.Status != lastLoggedStatus) { lastLoggedStatus = ev.Status; log.Information("Player state {Status} {Title}", ev.Status, ev.Title); }
        }

        // YouTube wants the video visible while its audio plays: no window, no music.
        if (Now is not null && !Paused && !guard.MayPlay) PauseHidden();
        // Window visible again: continue by itself, no click needed.
        else if (Now is not null && Paused && HiddenPause && (guard.Visible || !guard.RequireVisible) && config.Enabled) TogglePause();

        // A play command can be lost (page not ready, YouTube slow): if nothing plays 20 s after it, send it again.
        if (Now is not null && config.Enabled && !Paused && player.Running && DateTime.UtcNow - lastPlayAt > TimeSpan.FromSeconds(20)
            && player.State.Status is not ("playing" or "buffering") && errorsInRow == 0 && retries < 3)
        {
            retries++;
            lastPlayAt = DateTime.UtcNow;
            log.Warning("Nothing playing after {Title} was requested (state {Status}), sending it again ({N}/3)", Now.Title, player.State.Status, retries);
            player.Play(Now.Id, config.Volume, lastStart, 0, 300);
        }

        if (Now is not null && config.Enabled && !Paused && !player.Running && DateTime.UtcNow - lastRestart > TimeSpan.FromSeconds(15))
        {
            lastRestart = DateTime.UtcNow;
            log.Warning("Player host is not running, restarting");
            Start(Now, 0, 0, 300);
        }

        if (!config.Enabled) Stopped = false;
        if (!config.Enabled || Stopped) return;
        if (!loggedIn)
        {
            // Main menu and character select: calm music until the game is entered.
            if (Now is { Category: Category.Battle or Category.Boss }) menuDone = false;
            if (!menuDone && !Paused && guard.MayPlay && player.HostInstalled)
            {
                menuDone = true;
                PlayRandom(Category.Town, Now is null ? 0 : 800, 800);
                menuSong = Now is not null;
            }
            return;
        }
        menuDone = false;
        if (Paused || !guard.MayPlay || !player.HostInstalled) return;

        // Start the player host right after login so the first song does not wait for Chromium to boot.
        if (!hostWarmed) { hostWarmed = true; if (!player.Running) player.Start(); }

        Category want = situation switch
        {
            Situation.Battle => Category.Battle,
            Situation.Boss => Category.Boss,
            Situation.Town => Category.Town,
            Situation.Field => Category.Field,
            Situation.Dungeon => Category.Dungeon,
            _ => Now is not null && IsArea(Now.Category) ? Now.Category : lastAreaCategory,
        };
        if (IsArea(want)) lastAreaCategory = want;

        if (lockedWant is not null && lockedWant != want) lockedWant = null;
        if (lockedWant is not null) return;

        // The song from the main menu gives way to a song for the place once the game is entered.
        if (!menuSong && Now is not null && FitsKind(Now, want) && StaysForZone(want)) { wantPending = null; return; }
        // No song of this kind exists (library without area songs): keep what is playing.
        if (IsArea(want) && !Eligible(want).Any() && !AreaFallback().Any()) return;
        // A different zone is a clear signal, so no waiting; back to calm music in the same zone waits a little.
        var newZone = IsArea(want) && zone.Id != 0 && zone.Id != playedZoneId;
        var hold = newZone ? (Now is null ? FirstHold : TimeSpan.Zero) : IsArea(want) && Now is not null ? CalmHold : BattleHold;
        if (wantPending != want) { wantPending = want; wantSince = DateTime.UtcNow; }
        if (DateTime.UtcNow - wantSince < hold) return;
        wantPending = null;
        SwitchTo(want);
    }

    // Omakase (and the empty-category fallback) picks area songs by place tags, so a Town song is fine in a dungeon zone; otherwise the 4 s calm hold would swap it again and again.
    private bool FitsKind(Song s, Category want) =>
        s.Category == want || (IsArea(want) && IsArea(s.Category) && (config.Mode == PlayMode.Omakase || !Eligible(want).Any()));

    // In omakase mode the song also has to fit the place: a new zone with different tags gets a new song.
    private bool StaysForZone(Category want)
    {
        if (config.Mode != PlayMode.Omakase || !IsArea(want) || Now is null || zone.Tags.Length == 0) return true;
        if (zone.Id == playedZoneId) return true;
        if (!HomeOk(Now)) return false;
        if (Now.Tags.Intersect(zone.Tags).Any()) { playedZoneId = zone.Id; return true; }
        return false;
    }

    // A track tied to an FFXIV place (the Kugane theme) only fits that place.
    private bool HomeOk(Song s) => s.Home.Length == 0 || zone.Name == "" || s.Home.Contains(zone.Name, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<Song> AreaFallback() => library.Songs.Where(s => IsArea(s.Category) && IsOk(s));

    private void SwitchTo(Category want)
    {
        var toCombat = want is Category.Battle or Category.Boss;
        var wasCombat = Now is { Category: Category.Battle or Category.Boss };
        if (toCombat)
        {
            if (Now is not null && IsArea(Now.Category))
            {
                areaSong = Now; areaTime = player.State.Time; areaDur = player.State.Dur; areaZoneId = zone.Id;
            }
            PlayRandom(want, 300, 300);
            return;
        }
        var fade = FadeMs;
        if (wasCombat && config.ResumeArea && areaSong is { } a && a.Category == want && areaZoneId == zone.Id && IsOk(a)
            && (areaDur <= 0 || areaTime < areaDur - 10))
        {
            areaSong = null;
            Why = "Back to the area song after the fight";
            Start(a, areaTime, fade, fade);
            return;
        }
        areaSong = null;
        PlayRandom(want, Now is null ? 0 : fade, Now is null ? 800 : fade);
    }

    public void PlayManual(Song s, Situation currentSituation)
    {
        lockedWant = currentSituation switch
        {
            Situation.Battle => Category.Battle, Situation.Boss => Category.Boss, Situation.Town => Category.Town,
            Situation.Field => Category.Field, Situation.Dungeon => Category.Dungeon, _ => Now?.Category ?? Category.Town,
        };
        errorsInRow = 0;
        Why = "Picked by you";
        Start(s, 0, 300, 300);
    }

    public void Next(Situation currentSituation)
    {
        log.Information("Next requested (button, command or media key)");
        lockedWant = null;
        errorsInRow = 0;
        var cat = Now?.Category ?? currentSituation switch
        {
            Situation.Battle => Category.Battle, Situation.Boss => Category.Boss, Situation.Field => Category.Field,
            Situation.Dungeon => Category.Dungeon, _ => Category.Town,
        };
        PlayRandom(cat, 300, 300);
    }

    public void Previous(Situation currentSituation)
    {
        if (history.Count < 2) { Next(currentSituation); return; }
        history.RemoveAt(history.Count - 1);
        var prev = history[^1];
        history.RemoveAt(history.Count - 1);
        PlayManual(prev, currentSituation);
    }

    public void PlayPause(Situation currentSituation)
    {
        if (Now is null) Next(currentSituation); else TogglePause();
    }

    public void PlayRandom(Category cat, int fadeOutMs = 0, int fadeInMs = 800)
    {
        Why = "";
        if (config.ForcedSongs.TryGetValue(cat, out var forcedId) && library.Songs.FirstOrDefault(s => s.Id == forcedId) is { } forced)
        {
            Why = "Pinned";
            Start(forced, 0, fadeOutMs, fadeInMs);
            return;
        }
        var pool = Eligible(cat).ToList();
        if (pool.Count == 0 && IsArea(cat)) pool = AreaFallback().ToList();
        if (pool.Count == 0) { Note = $"No {cat} songs enabled."; return; }

        var omakase = config.Mode == PlayMode.Omakase && IsArea(cat) && zone.Tags.Length > 0;
        Song pick;
        if (omakase && PickOmakase(pool, out var omi, out var why)) { pick = omi; Why = why; }
        else
        {
            if (omakase)
            {
                Why = $"{zone.Name}: no tagged song fits, random";
                var local = pool.Where(HomeOk).ToList();
                pool = local.Count > 0 ? local : AreaFallback().Where(HomeOk).ToList();
                if (pool.Count == 0) pool = AreaFallback().ToList();
            }
            var fresh = pool.Where(s => !recent.Contains(s.Id) && s.Id != Now?.Id).ToList();
            if (fresh.Count == 0) fresh = pool.Where(s => s.Id != Now?.Id).ToList();
            if (fresh.Count == 0) fresh = pool;
            // Games are picked evenly first, so FFXIV's 800+ tracks do not drown out the others.
            var games = fresh.Select(s => s.Game).Distinct().ToList();
            var game = games[rng.Next(games.Count)];
            var ofGame = fresh.Where(s => s.Game == game).ToList();
            pick = ofGame[rng.Next(ofGame.Count)];
        }
        Start(pick, 0, fadeOutMs, fadeInMs);
    }

    // Score = how many of the zone's tags the song has, earlier zone tags count more. Songs of the same kind are tried first, then any area song.
    private bool PickOmakase(List<Song> sameKind, out Song pick, out string why)
    {
        pick = null!; why = "";
        var all = AreaFallback().ToList();
        // The zone's own tracks count whatever kind they were filed under.
        var local = sameKind.Concat(all.Where(IsHomeHere)).Distinct().ToList();
        foreach (var pool in new[] { local, all })
        {
            var scored = pool.Select(s => (Song: s, Score: Score(s))).Where(x => x.Score > 0).ToList();
            if (scored.Count == 0) continue;
            // Too few matches would repeat the same song: widen to every area song.
            if (!ReferenceEquals(pool, all) && scored.Count(x => x.Score >= scored.Max(y => y.Score) - 1) < MinPool)
                scored = all.Select(s => (Song: s, Score: Score(s))).Where(x => x.Score > 0).ToList();
            var fresh = scored.Where(x => !recent.Contains(x.Song.Id) && x.Song.Id != Now?.Id).ToList();
            if (fresh.Count == 0) fresh = scored.Where(x => x.Song.Id != Now?.Id).ToList();
            if (fresh.Count == 0) fresh = scored;
            var best = fresh.Max(x => x.Score);
            // Within one point of the best, or lower (but at least half) until there are MinPool songs to rotate through.
            var floor = Math.Max((best + 1) / 2, Math.Min(best - 1, fresh.OrderByDescending(x => x.Score).Skip(MinPool - 1).Select(x => x.Score).DefaultIfEmpty(0).First()));
            var top = fresh.Where(x => x.Score >= floor).ToList();
            // Weight favours higher scores and "pure" songs, whose tags mostly belong to this zone.
            var weights = top.Select(x => x.Score * x.Score * 10 * Math.Max(1, x.Song.Tags.Intersect(zone.Tags).Count()) / Math.Max(1, x.Song.Tags.Length)).ToList();
            var r = rng.Next(Math.Max(1, weights.Sum()));
            Song? chosen = null;
            for (var i = 0; i < top.Count; i++) { r -= weights[i]; if (r < 0) { chosen = top[i].Song; break; } }
            pick = chosen ?? top[0].Song;
            why = $"{zone.Name}: fits {string.Join(", ", pick.Tags.Intersect(zone.Tags))}";
            return true;
        }
        return false;
    }

    private const int MinPool = 6;

    private bool IsHomeHere(Song s) => s.Home.Contains(zone.Name, StringComparer.OrdinalIgnoreCase);

    private int Score(Song s)
    {
        if (!HomeOk(s)) return 0;
        if (IsHomeHere(s)) return zone.Tags.Length * (zone.Tags.Length + 1) / 2;
        var score = 0;
        for (var i = 0; i < zone.Tags.Length; i++)
            if (s.Tags.Contains(zone.Tags[i])) score += zone.Tags.Length - i;
        return score;
    }

    private void Advance()
    {
        if (Now is null || Paused) return;
        log.Information("Song ended, picking the next one");
        PlayRandom(Now.Category, 0, 800);
    }

    private void Start(Song s, double startAt, int fadeOutMs, int fadeInMs)
    {
        if (!guard.MayPlay) { Note = "Open the music window to play: the video has to stay visible."; return; }
        if (!player.Running)
        {
            player.Start();
            if (!player.Running) { Note = player.Problem; return; }
        }
        Now = s;
        Note = "";
        Stopped = false;
        menuSong = false;
        Paused = false;
        HiddenPause = false;
        ourPauseAt = DateTime.UtcNow;
        lastPlayAt = DateTime.UtcNow;
        lastStart = startAt;
        retries = 0;
        if (IsArea(s.Category)) playedZoneId = zone.Id;
        history.Add(s);
        while (history.Count > 50) history.RemoveAt(0);
        recent.Enqueue(s.Id);
        while (recent.Count > RecentKeep) recent.Dequeue();
        log.Information("Play {Game} {Cat}: {Title} ({Id}) from {Start:0}s, fade {Out}/{In} ms. {Why}", s.Game, s.Category, s.Title, s.Id, startAt, fadeOutMs, fadeInMs, Why);
        player.Play(s.Id, config.Volume, startAt, fadeOutMs, fadeInMs);
    }

    public void TogglePause()
    {
        if (Now is null) return;
        if (Paused && !guard.MayPlay) { Note = "Open the music window to play: the video has to stay visible."; return; }
        Paused = !Paused;
        HiddenPause = false;
        ourPauseAt = DateTime.UtcNow;
        if (Paused) player.Pause();
        else if (player.Running) player.Resume();
        else Start(Now, 0, 0, 300);
    }

    private void PauseHidden()
    {
        log.Information("Music window hidden, pausing (the video has to stay visible while music plays)");
        Paused = true;
        HiddenPause = true;
        ourPauseAt = DateTime.UtcNow;
        player.Pause();
    }

    public void StopNow()
    {
        player.Stop();
        Now = null;
        Stopped = true;
        menuSong = false;
        areaSong = null;
        Paused = false;
        HiddenPause = false;
        ourPauseAt = DateTime.UtcNow;
        wantPending = null;
    }
}
