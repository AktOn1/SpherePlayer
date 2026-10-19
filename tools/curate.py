#!/usr/bin/env python3
"""Finds candidate YouTube links per game and category (search metadata only, nothing is downloaded)
and keeps only videos whose embedding is allowed (oEmbed). Writes library/candidates.json for review."""
import json, subprocess, sys, urllib.request, urllib.error, urllib.parse
from concurrent.futures import ThreadPoolExecutor

GAMES = ["Final Fantasy", "Final Fantasy II", "Final Fantasy III", "Final Fantasy IV", "Final Fantasy V",
         "Final Fantasy VI", "Final Fantasy VII", "Final Fantasy VIII", "Final Fantasy IX", "Final Fantasy X",
         "Final Fantasy XI", "Final Fantasy XII", "Final Fantasy XIII", "Final Fantasy XIV", "Final Fantasy XV",
         "Final Fantasy XVI"]
CATS = {"town": "town theme", "battle": "battle theme", "boss": "boss battle theme"}
KEEP = 4
MIN_S, MAX_S = {"town": (60, 600), "battle": (45, 480), "boss": (60, 600)}["town"][0], 600

def search(q):
    out = subprocess.run(["yt-dlp", f"ytsearch10:{q}", "--flat-playlist", "--dump-json", "--no-warnings"],
                         capture_output=True, text=True, timeout=120).stdout
    return [json.loads(l) for l in out.splitlines() if l.strip()]

def embeddable(vid):
    u = "https://www.youtube.com/oembed?format=json&url=" + urllib.parse.quote(f"https://www.youtube.com/watch?v={vid}")
    try:
        urllib.request.urlopen(u, timeout=20).read(); return True
    except urllib.error.HTTPError:
        return False
    except Exception:
        return False

def job(args):
    game, cat = args
    q = f"{game} {CATS[cat]}"
    if game == "Final Fantasy XIV": q = f"FFXIV {CATS[cat]} OST"
    res = []
    try: hits = search(q)
    except Exception as e: return game, cat, [], str(e)
    for h in hits:
        d = h.get("duration") or 0
        if not (45 <= d <= MAX_S): continue
        if (h.get("view_count") or 0) < 20000: continue
        if not embeddable(h["id"]): continue
        res.append({"id": h["id"], "title": h.get("title"), "channel": h.get("channel") or h.get("uploader"),
                    "seconds": d, "views": h.get("view_count")})
        if len(res) >= KEEP: break
    return game, cat, res, ""

if __name__ == "__main__":
    jobs = [(g, c) for g in GAMES for c in CATS]
    out = []
    with ThreadPoolExecutor(4) as ex:
        for game, cat, res, err in ex.map(job, jobs):
            print(game, cat, len(res), err, flush=True)
            out.append({"game": game, "category": cat, "songs": res})
    json.dump(out, open(sys.argv[1] if len(sys.argv) > 1 else "library/candidates.json", "w"), indent=1, ensure_ascii=False)
