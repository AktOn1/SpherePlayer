using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SpherePlayer;

public sealed class SongsWindow : Window
{
    private readonly Plugin p;
    private string search = "";
    private string gameFilter = "";
    private readonly System.Collections.Generic.Dictionary<Category, int> pages = new();
    private const int PageSize = 40;

    public SongsWindow(Plugin p) : base("Sphere Player: songs##songs")
    {
        this.p = p;
        var screen = ImGui.GetMainViewport().Size;
        Size = new Vector2(520, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(320, 200), MaximumSize = new Vector2(900, 800) };
        Position = new Vector2(System.Math.Max(20, screen.X - 900), 90);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var cfg = p.Config;
        if (ImGui.CollapsingHeader("Final Fantasies"))
        {
            if (ImGui.SmallButton("Tick all##gall")) { cfg.DisabledGames.Clear(); p.SaveConfig(); }
            ImGui.SameLine();
            if (ImGui.SmallButton("Untick all##gnone")) { foreach (var g in p.Library.Games) cfg.DisabledGames.Add(g); p.SaveConfig(); }
            foreach (var g in p.Library.Games)
            {
                var on = !cfg.DisabledGames.Contains(g);
                if (ImGui.Checkbox(g + "##g", ref on)) { if (on) cfg.DisabledGames.Remove(g); else cfg.DisabledGames.Add(g); p.SaveConfig(); }
            }
        }

        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo("##gf", gameFilter == "" ? "All games" : gameFilter))
        {
            if (ImGui.Selectable("All games", gameFilter == "")) { gameFilter = ""; pages.Clear(); }
            foreach (var g in p.Library.Games) if (ImGui.Selectable(g, gameFilter == g)) { gameFilter = g; pages.Clear(); }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputTextWithHint("##search", "search title or place", ref search, 64)) pages.Clear();

        foreach (var cat in new[] { Category.Town, Category.Field, Category.Dungeon, Category.Battle, Category.Boss, Category.Other })
        {
            var all = p.Library.Songs.Where(s => s.Category == cat).ToList();
            if (all.Count == 0) continue;
            var songs = all.Where(s => (gameFilter == "" || s.Game == gameFilter)
                && (search == "" || s.Title.Contains(search, System.StringComparison.OrdinalIgnoreCase) || s.Tags.Any(t => t.Contains(search, System.StringComparison.OrdinalIgnoreCase)))).ToList();
            if (!ImGui.CollapsingHeader($"{cat} ({p.Controller.Eligible(cat).Count()} on, {songs.Count} shown of {all.Count})##{cat}")) continue;
            if (songs.Count == 0) { ImGui.TextDisabled("No song matches the filter."); continue; }
            var pageCount = (songs.Count + PageSize - 1) / PageSize;
            pages.TryGetValue(cat, out var page);
            page = System.Math.Clamp(page, 0, pageCount - 1);
            if (pageCount > 1)
            {
                if (ImGui.SmallButton($"< prev##pp{cat}")) page = System.Math.Max(0, page - 1);
                ImGui.SameLine();
                ImGui.TextUnformatted($"page {page + 1} / {pageCount}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"next >##pn{cat}")) page = System.Math.Min(pageCount - 1, page + 1);
            }
            pages[cat] = page;
            if (ImGui.Button($"Play random {cat}##r{cat}")) { p.ShowMain(); p.Controller.PlayManual(p.Controller.Eligible(cat).OrderBy(_ => System.Random.Shared.Next()).FirstOrDefault() ?? songs[0], p.Current); }
            cfg.ForcedSongs.TryGetValue(cat, out var forced);
            foreach (var s in songs.Skip(page * PageSize).Take(PageSize))
            {
                var banned = cfg.BannedSongs.Contains(s.Id);
                ImGui.PushID(s.Id + cat);
                if (ImGui.SmallButton("Play")) { p.ShowMain(); p.Controller.PlayManual(s, p.Current); }
                ImGui.SameLine();
                var isForced = forced == s.Id;
                if (ImGui.SmallButton(isForced ? "Unpin" : "Pin")) { if (isForced) cfg.ForcedSongs.Remove(cat); else cfg.ForcedSongs[cat] = s.Id; p.SaveConfig(); }
                ImGui.SameLine();
                if (ImGui.SmallButton(banned ? "Unban" : "Ban")) { if (banned) cfg.BannedSongs.Remove(s.Id); else cfg.BannedSongs.Add(s.Id); p.SaveConfig(); }
                ImGui.SameLine();
                var dim = banned || cfg.DisabledGames.Contains(s.Game);
                var text = $"{(isForced ? "[pinned] " : "")}{s.Game.Replace("Final Fantasy", "FF")}: {s.Title}{(s.Tags.Length > 0 ? "  [" + string.Join(", ", s.Tags) + "]" : "")}";
                if (dim) ImGui.TextDisabled(text); else ImGui.TextUnformatted(text);
                ImGui.PopID();
            }
        }

        if (ImGui.CollapsingHeader("Game state log"))
        {
            ImGui.TextWrapped(p.Detail);
            foreach (var line in p.History.Reverse()) ImGui.TextWrapped(line);
        }
    }
}
