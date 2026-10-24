#!/usr/bin/env python3
"""Step 3 (+ overrides.json, homes.json): tools/ost/classified/*.json -> library/songs.json (battle, boss) and library/area.json (town, field, dungeon).
Tracks classified "other" (cutscene, fanfare, character scene music ...) go into songs.json as category "other":
the plugin never plays them by itself, they can be browsed and played by hand in the song window."""
import json, os, re
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
GAMES = ["Final Fantasy", "Final Fantasy II", "Final Fantasy III", "Final Fantasy IV", "Final Fantasy V", "Final Fantasy VI",
         "Final Fantasy VII", "Final Fantasy VIII", "Final Fantasy IX", "Final Fantasy X", "Final Fantasy XI", "Final Fantasy XII",
         "Final Fantasy XIII", "Final Fantasy XIV", "Final Fantasy XV", "Final Fantasy XVI"]
CLEAN = re.compile(r"\s*[\[(【]\s*(video soundtrack|audio)\s*[\])】]\s*", re.I)
def load(name):
    f = os.path.join(HERE, "ost", name)
    return json.load(open(f)) if os.path.exists(f) else {}
overrides, homes = load("overrides.json"), load("homes.json")   # audit corrections (arbitrate-ost.py) and FFXIV place ties (assign-homes.py)
songs, area, other = [], [], []
extra = load("extra-tracks.json") or []   # hand-picked tracks the playlists lack (MusicBrainz gap check); classified by hand, not by classify-ost.py
for g in GAMES:
    p = os.path.join(HERE, "ost", "classified", g + ".json")
    if not os.path.exists(p): continue
    have = json.load(open(p))
    known = {t["id"] for t in have}
    for t in have + [{"id": e["id"], "orig": e["orig"], "title": e["title"], "seconds": e["seconds"], "kind": e["kind"], "tags": e["tags"]} for e in extra if e["game"] == g and e["id"] not in known]:
        title = CLEAN.sub(" ", t["title"]).strip()
        if t["id"] in overrides: t = {**t, "kind": overrides[t["id"]]["kind"], "tags": overrides[t["id"]]["tags"]}
        if t["kind"] in ("battle", "boss"): songs.append({"game": g, "category": t["kind"], "id": t["id"], "title": title})
        elif t["kind"] in ("town", "field", "dungeon"): area.append({"game": g, "kind": t["kind"], "tags": t["tags"], "id": t["id"], "title": title, "track": t["orig"], **({"home": homes[t["id"]]} if t["id"] in homes and t["kind"] in ("town", "field", "dungeon") else {})})
        else: songs.append({"game": g, "category": "other", "id": t["id"], "title": title}); other.append(1)
for tag, ids in load("sparse-tags.json").items():   # retag-sparse.py: thinly covered tags
    for a in area:
        if a["id"] in ids and tag not in a["tags"]: a["tags"] = a["tags"] + [tag]
moods = load("moods.json")
for a in area:   # label-moods.py songs: mood and finer place tags
    m = moods.get(a["id"])
    if m: a["mood"], a["sub"] = m["mood"], m["sub"]
json.dump(songs, open(os.path.join(ROOT, "library", "songs.json"), "w"), indent=1, ensure_ascii=False)
json.dump(area, open(os.path.join(ROOT, "library", "area.json"), "w"), indent=1, ensure_ascii=False)
from collections import Counter
print("battle/boss", Counter(s["category"] for s in songs), "area", Counter(a["kind"] for a in area), "other", len(other))
