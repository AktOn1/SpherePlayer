#!/usr/bin/env python3
"""Step 1 of the full-OST library: downloads the track lists (metadata only, nothing is saved but ids/titles)
of the official SQUARE ENIX MUSIC "Video Soundtrack" playlists and of fan full-OST playlists for the games
Square Enix did not upload, checks oEmbed for embedding, writes tools/ost/raw/<game>.json.

usage: fetch-ost.py [--only "Final Fantasy VII"]    (cached: tools/ost/cache/*.json, delete to refetch)
"""
import json, os, subprocess, sys, time, urllib.request, urllib.error, urllib.parse
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "ost"); os.makedirs(os.path.join(OUT, "cache"), exist_ok=True); os.makedirs(os.path.join(OUT, "raw"), exist_ok=True)
SQEX = "UCMx60HYcw1ieiPlZZagfqXQ"

# game -> playlists. "official" = SQUARE ENIX MUSIC channel. Fan playlists only where Square Enix has none.
SOURCES = {
 "Final Fantasy":       ["PLOLBWlPQqPkvSqeFs4tcDD3OHQvH7Ou3W"],
 "Final Fantasy II":    ["PLOLBWlPQqPkvxEAkZM_2YNaycD6VG_kbt"],
 "Final Fantasy III":   ["PLOLBWlPQqPktSmRbEchGnoCvxqKvKeZ5V"],
 "Final Fantasy IV":    ["PLOLBWlPQqPkv6FnA6X5SbA00TwWkLMxbU"],
 "Final Fantasy V":     ["PLOLBWlPQqPksnWK9tgf2PTKmzsdgSgoue"],
 "Final Fantasy VI":    ["PLOLBWlPQqPkvJDeKtfbWEaCcKLP50hsAy"],
 "Final Fantasy VII":   ["PLOLBWlPQqPkvyJyjgzeMFcVxeCHD1nQ3h"],
 "Final Fantasy VIII":  ["PLOLBWlPQqPksC_WRgVMs_KKrK7Skm344U"],
 "Final Fantasy IX":    ["PLOLBWlPQqPktvUp2ke3yeXAwowekoK1Pd"],
 "Final Fantasy X":     ["PLOLBWlPQqPkt7eTc5XqXb5MQrR2XCUu6Q"],
 "Final Fantasy XI":    ["PLOLBWlPQqPksc4w4BmO7hcgFo-UaNd0uV",
                         # NightBunny8: YouTube Music auto-generated ("Topic") album playlists, one per XI album
                         "PLBO2h-GzDvIaIHHsXuATZhy4u81r8N0AQ", "PLBO2h-GzDvIaLdVj7kBvBzmEG1zFUgU1k", "PLBO2h-GzDvIYXpFf4nGHGSMCRNVNmfhs4",
                         "PLBO2h-GzDvIZ3N7BIEZCtI7Dlud5e5K2l", "PLBO2h-GzDvIZrVu0rLnSuM4pozyQa-9A5", "PLBO2h-GzDvIb63vgvbiBSNYahXBxKj0b8",
                         "PLBO2h-GzDvIYzNWWsROe9aUvacU5IULHX", "PLBO2h-GzDvIZGh1Nueqy9euexx8L7-PHb", "PLBO2h-GzDvIYYFKG6O-qGhVhQlpTRQ5O6",
                         "PLBO2h-GzDvIb93WpJiEJKshFsAOf2COUn"],
 "Final Fantasy XII":   ["PLBO2h-GzDvIY_8nTSy67oHwNgM4LFzNED"],        # original OST, Topic upload (the official Zodiac Age list has only 33)
 "Final Fantasy XIII":  ["PLBO2h-GzDvIbndGagEi6mTDBZPdbiBUHt"],        # Topic upload
 "Final Fantasy XIV":   ["PLOfnNkBu30bLj4AvIyeHjuhP4_YnmhpB0"],        # 1.0-7.0 official releases, Topic uploads
 "Final Fantasy XV":    ["PLOLBWlPQqPkvmnZLni9gg2e_iTSK7svoF"],
 "Final Fantasy XVI":   ["PLOLBWlPQqPks3QoCfi_ehxbXcqvk3fpWn", "PLOLBWlPQqPkv2_5N-zS-HenQVxN1aY9qD"],
}
EXTRA = json.load(open(os.path.join(OUT, "extra-sources.json"))) if os.path.exists(os.path.join(OUT, "extra-sources.json")) else {}
for g, ids in EXTRA.items(): SOURCES.setdefault(g, []).extend(ids)

def playlist(pid):
    cf = os.path.join(OUT, "cache", pid + ".json")
    if os.path.exists(cf): return json.load(open(cf))
    for i in range(3):
        p = subprocess.run(["yt-dlp", f"https://www.youtube.com/playlist?list={pid}", "--flat-playlist", "--dump-json", "--no-warnings"],
                           capture_output=True, text=True, timeout=300)
        rows = [json.loads(l) for l in p.stdout.splitlines() if l.strip()]
        if rows: break
        time.sleep(4)
    res = [{"id": r["id"], "title": r.get("title"), "seconds": r.get("duration"), "channel": r.get("channel") or r.get("uploader"),
            "pos": n + 1} for n, r in enumerate(rows)]
    if res: json.dump(res, open(cf, "w"), ensure_ascii=False)
    return res

def embeddable(vid):
    u = "https://www.youtube.com/oembed?format=json&url=" + urllib.parse.quote(f"https://www.youtube.com/watch?v={vid}")
    for i in range(3):
        try:
            urllib.request.urlopen(u, timeout=20).read(); return True
        except urllib.error.HTTPError as e:
            if e.code in (401, 403, 404): return False
            time.sleep(2)
        except Exception:
            time.sleep(2)
    return None

if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    for game, pids in SOURCES.items():
        if only and game != only: continue
        seen, tracks = set(), []
        for pid in pids:
            for t in playlist(pid):
                if t["id"] in seen or not t["title"] or t["title"] in ("[Deleted video]", "[Private video]"): continue
                seen.add(t["id"]); t["playlist"] = pid; tracks.append(t)
        with ThreadPoolExecutor(8) as ex:
            for t, ok in zip(tracks, ex.map(lambda t: embeddable(t["id"]), tracks)): t["embeddable"] = ok
        json.dump(tracks, open(os.path.join(OUT, "raw", game + ".json"), "w"), indent=1, ensure_ascii=False)
        print(f"{game}: {len(tracks)} tracks, embeddable {sum(1 for t in tracks if t['embeddable'])}, unknown {sum(1 for t in tracks if t['embeddable'] is None)}", flush=True)
