#!/usr/bin/env python3
"""tools/area-tracklist.json -> library/area.json

For every hand-written area track, searches YouTube metadata only (yt-dlp --flat-playlist, nothing is
downloaded), filters by duration/views/title/embeddable and writes the resolved entries.

usage: resolve-area.py [--cache /tmp/area-cache.json] [--only "Final Fantasy VII"] [--out library/area.json]
Search results are cached, so the run can be interrupted and resumed in chunks.
"""
import json, os, re, subprocess, sys, time, unicodedata, urllib.request, urllib.error, urllib.parse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
GAMES = ["Final Fantasy", "Final Fantasy II", "Final Fantasy III", "Final Fantasy IV", "Final Fantasy V", "Final Fantasy VI",
         "Final Fantasy VII", "Final Fantasy VIII", "Final Fantasy IX", "Final Fantasy X", "Final Fantasy XI", "Final Fantasy XII",
         "Final Fantasy XIII", "Final Fantasy XIV", "Final Fantasy XV", "Final Fantasy XVI"]
ARABIC = {g: n for g, n in zip(GAMES, ["1|I", "2|II", "3|III", "4|IV", "5|V", "6|VI", "7|VII", "8|VIII", "9|IX", "10|X",
                                       "11|XI", "12|XII", "13|XIII", "14|XIV", "15|XV", "16|XVI"])}
SHORT = {g: ("Final Fantasy" if g == "Final Fantasy" else "FF" + g.split()[-1]) for g in GAMES}
SHORT["Final Fantasy XIV"] = "FFXIV"

def tag_re(g):
    n = ARABIC[g]
    if g == "Final Fantasy":   # game I: "Final Fantasy" not followed by another numeral / "FF1" / "FFI"
        return re.compile(r"(final fantasy(?! ?(?:[ivx]{1,5}|\d{1,2})(?![\w]))|\bff ?(1|i)(?![\w]))", re.I)
    base = rf"(final fantasy|ff) ?({n})(?![\w])"
    return re.compile(base + (r"|\bffxiv\b" if g.endswith("XIV") else ""), re.I)
TAGS = {g: tag_re(g) for g in GAMES}
OTHER_NUM = {g: re.compile(rf"(final fantasy|\bff) ?({ARABIC[g]})(?![\w])", re.I) for g in GAMES if g != "Final Fantasy"}

BAD = re.compile(r"cover|metal|guitar|harp|remake|rebirth|electone|piano|orchestra|reorchestrat|lyrics|tutorial|remix|"
                 r"medley|\bmix\b|\bhours?\b|arrange|\blive\b|concert|distant worlds|8[- ]?bit|chiptune|pixel|"
                 r"music box|jazz|ocarina|flute|violin|cello|bass|acoustic|dissidia|theatrhythm|crisis core|ever crisis|"
                 r"lufia|world of final|extended|loop|reimagin|remaster|ambience|sleep|study|relax|battle|boss|fight", re.I)
BAD_XIV_OK = re.compile(r"dawntrail|endwalker", re.I)
OFFICIAL = re.compile(r"square enix music|植松|topic|official", re.I)
STOP = {"the", "of", "a", "an", "and", "to", "in", "de", "la"}

def norm(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode().lower()
    s = re.sub(r"[’`´]", "'", s)
    s = re.sub(r"'s\b|'", "", s)   # drop possessives and apostrophes
    return re.sub(r"[^a-z0-9]+", " ", s).strip()

def words(s):
    return [w for w in norm(s).split() if w not in STOP]

def title_has_track(title, track):
    tw, nt = set(norm(title).split()), norm(title)
    need = words(track)
    return bool(need) and all(w in tw or w in nt for w in need)

def search(q, tries=3):
    for i in range(tries):
        try:
            out = subprocess.run(["yt-dlp", q, "--flat-playlist", "--dump-json", "--no-warnings"],
                                 capture_output=True, text=True, timeout=120).stdout
            res = [json.loads(l) for l in out.splitlines() if l.strip()]
            if res: return res
        except Exception:
            pass
        time.sleep(3 + 4 * i)
    return []

_emb = {}
def embeddable(vid):
    if vid in _emb: return _emb[vid]
    u = "https://www.youtube.com/oembed?format=json&url=" + urllib.parse.quote(f"https://www.youtube.com/watch?v={vid}")
    ok = False
    for i in range(3):
        try:
            urllib.request.urlopen(u, timeout=20).read(); ok = True; break
        except urllib.error.HTTPError as e:
            if e.code in (401, 403, 404): break       # not embeddable / private / gone
            time.sleep(3)
        except Exception:
            time.sleep(3)
    _emb[vid] = ok
    return ok

def candidates(game, track, hits, min_views):
    """hits (search order) -> embeddable, filtered candidates ranked: official first (search order), then views."""
    good = []
    for h in hits:
        t = h.get("title") or ""
        d = h.get("duration") or 0
        chan = h.get("channel") or h.get("uploader") or ""
        if not (45 <= d <= 720) or (h.get("view_count") or 0) < min_views: continue
        if BAD.search(t) and not (game.endswith("XIV") and BAD_XIV_OK.search(t) and not BAD.search(BAD_XIV_OK.sub("", t))): continue
        if not title_has_track(t, track): continue
        if not (TAGS[game].search(t) or OFFICIAL.search(chan)): continue
        others = [g for g in OTHER_NUM if g != game and OTHER_NUM[g].search(t)]
        if others and not TAGS[game].search(t): continue
        if others and game == "Final Fantasy": continue
        good.append(dict(id=h["id"], title=t, channel=chan, seconds=d, views=h.get("view_count") or 0,
                         official=bool(OFFICIAL.search(chan))))
    good.sort(key=lambda c: (not c["official"], -c["views"]))
    return good

def queries(game, track, relaxed):
    s = SHORT[game]
    if not relaxed:
        return [f"ytsearch8:{s} OST {track}"]
    return [f"ytsearch10:{game} {track} soundtrack", f"ytsearch10:{track} {s} original soundtrack"]

def job(item):
    game, track, relaxed, cache = item["game"], item["track"], item["relaxed"], item["cache"]
    hits = []
    for q in queries(game, track, relaxed):
        if q not in cache: cache[q] = search(q); time.sleep(1.0)
        hits += cache[q]
    seen, uniq = set(), []
    for h in hits:
        if h["id"] not in seen: seen.add(h["id"]); uniq.append(h)
    cands = candidates(game, track, uniq, 6000 if relaxed else 15000)
    out = [c for c in cands if embeddable(c["id"])][:4]
    return item, out

def run(items, relaxed, cache, workers=4):
    jobs = [dict(i, relaxed=relaxed, cache=cache) for i in items]
    with ThreadPoolExecutor(workers) as ex:
        return list(ex.map(job, jobs))

def main():
    a = sys.argv[1:]
    opt = lambda k, d=None: a[a.index(k) + 1] if k in a else d
    cache_p = opt("--cache", "/tmp/area-cache.json")
    out_p = opt("--out", os.path.join(ROOT, "library", "area.json"))
    only = opt("--only")
    cache = json.load(open(cache_p)) if os.path.exists(cache_p) else {}
    tl = json.load(open(os.path.join(HERE, "area-tracklist.json")))
    if only: tl = [t for t in tl if t["game"] == only]
    results = {}                                            # (game, track) -> candidate list
    for relaxed in (False, True):
        todo = [t for t in tl if not results.get((t["game"], t["track"]))]
        if relaxed: print(f"-- relaxed retry for {len(todo)} tracks", flush=True)
        for item, cands in run(todo, relaxed, cache):
            results[(item["game"], item["track"])] = cands
            print(("ok  " if cands else "MISS"), item["game"], "|", item["track"],
                  "->", (cands[0]["title"][:70] + " / " + cands[0]["channel"][:25]) if cands else "", flush=True)
        json.dump(cache, open(cache_p, "w"), ensure_ascii=False)
    used, entries, dbg, unresolved = set(), [], [], []
    for t in tl:
        pick = next((c for c in results.get((t["game"], t["track"]), []) if c["id"] not in used), None)
        if not pick: unresolved.append(f'{t["game"]} | {t["track"]}'); continue
        used.add(pick["id"])
        entries.append({"game": t["game"], "kind": t["kind"], "tags": t["tags"], "id": pick["id"],
                        "title": t["track"], "track": t["track"]})
        dbg.append(dict(entries[-1], yt_title=pick["title"], channel=pick["channel"], views=pick["views"]))
    entries.sort(key=lambda e: GAMES.index(e["game"]))
    json.dump(entries, open(out_p, "w"), indent=1, ensure_ascii=False)
    json.dump(dbg, open("/tmp/area-debug.json", "w"), indent=1, ensure_ascii=False)
    print(f"\nwrote {out_p}: {len(entries)} of {len(tl)} tracks")
    print("per game:", dict(sorted(Counter(e["game"] for e in entries).items(), key=lambda kv: GAMES.index(kv[0]))))
    print("per kind:", dict(Counter(e["kind"] for e in entries)))
    print("per tag :", dict(Counter(x for e in entries for x in e["tags"]).most_common()))
    print("unresolved:", *unresolved, sep="\n  ")

if __name__ == "__main__":
    main()
