using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace SpherePlayer;

// Appended at the end on purpose: saved settings store these by name or number.
public enum Category { Town, Battle, Boss, Field, Dungeon, Other }

public sealed record Song(string Game, Category Category, string Id, string Title, string[] Tags, string[]? HomeZones = null)
{
    // FFXIV places this track is the music of (omakase plays it only there); empty = fits anywhere its tags fit.
    public string[] Home { get; } = HomeZones ?? Array.Empty<string>();
}

public sealed record ZoneInfo(uint Id, string Name, string Kind, string[] Tags)
{
    public static readonly ZoneInfo None = new(0, "", "", Array.Empty<string>());
}

public sealed class Library
{
    public IReadOnlyList<Song> Songs { get; }
    public IReadOnlyList<string> Games { get; }
    public bool HasAreaSongs { get; }
    private readonly Dictionary<uint, ZoneInfo> zones = new();

    public Library()
    {
        var asm = Assembly.GetExecutingAssembly();
        var songs = new List<Song>();
        using (var s = asm.GetManifestResourceStream("songs.json")!)
            songs.AddRange(JsonSerializer.Deserialize<List<Raw>>(s)!.Select(r => new Song(r.game, Enum.Parse<Category>(r.category!, true), r.id, r.title, r.tags ?? Array.Empty<string>())));
        using (var s = asm.GetManifestResourceStream("area.json"))
        {
            if (s is not null)
            {
                var area = JsonSerializer.Deserialize<List<Raw>>(s)!
                    .Select(r => new Song(r.game, r.kind switch { "field" => Category.Field, "dungeon" => Category.Dungeon, _ => Category.Town }, r.id, r.title, r.tags ?? Array.Empty<string>(), r.home)).ToList();
                if (area.Count > 0)
                {
                    HasAreaSongs = true;
                    songs.RemoveAll(x => x.Category == Category.Town);
                    songs.AddRange(area);
                }
            }
        }
        Songs = songs;
        Games = Songs.Select(x => x.Game).Distinct().ToList();

        using var z = asm.GetManifestResourceStream("territories.json");
        if (z is not null)
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, RawZone>>(z)!)
                if (uint.TryParse(k, out var id)) zones[id] = new ZoneInfo(id, v.name ?? "", v.kind ?? "", v.tags ?? Array.Empty<string>());
    }

    public ZoneInfo Zone(uint territoryId) => zones.TryGetValue(territoryId, out var z) ? z : new ZoneInfo(territoryId, "", "", Array.Empty<string>());

    private sealed record Raw(string game, string? category, string? kind, string id, string title, string[]? tags, string[]? home = null);
    private sealed record RawZone(string? name, string? kind, string[]? tags);
}
