#!/usr/bin/env python3
"""candidates.json -> songs.json: drops covers, assigns each video to one game/category, drops videos that name another game."""
import json, re, sys
GAMES = ["Final Fantasy", "Final Fantasy II", "Final Fantasy III", "Final Fantasy IV", "Final Fantasy V", "Final Fantasy VI",
         "Final Fantasy VII", "Final Fantasy VIII", "Final Fantasy IX", "Final Fantasy X", "Final Fantasy XI", "Final Fantasy XII",
         "Final Fantasy XIII", "Final Fantasy XIV", "Final Fantasy XV", "Final Fantasy XVI"]
ARABIC = {"Final Fantasy": "1|I", "Final Fantasy II": "2|II", "Final Fantasy III": "3|III", "Final Fantasy IV": "4|IV", "Final Fantasy V": "5|V",
          "Final Fantasy VI": "6|VI", "Final Fantasy VII": "7|VII", "Final Fantasy VIII": "8|VIII", "Final Fantasy IX": "9|IX", "Final Fantasy X": "10|X",
          "Final Fantasy XI": "11|XI", "Final Fantasy XII": "12|XII", "Final Fantasy XIII": "13|XIII", "Final Fantasy XIV": "14|XIV",
          "Final Fantasy XV": "15|XV", "Final Fantasy XVI": "16|XVI"}
def tag(g):
    n = ARABIC[g]
    return re.compile(rf"(final fantasy|ff) ?({n})(?![\w])|\bffxiv\b" if g.endswith("XIV") else rf"(final fantasy|ff) ?({n})(?![\w])", re.I)
TAGS = {g: tag(g) for g in GAMES}
BAD = re.compile(r"cover|metal|guitar|harp|remake|electo|electone|piano|orchestral|reorchestrated|lyrics|tutorial|lufia|world of final|dawntrail|endwalker|mix|medley|1 hour", re.I)
OFFICIAL = re.compile(r"square enix music|植松|topic|soundtrack", re.I)
def clean(t):
    t = re.sub(r"\[Video Soundtrack\]\s*", "", t); t = re.sub(r"\s*[\[(]?FINAL FANTASY [IVX]+[\])]?\s*$", "", t)
    t = re.sub(r"\s*\(.*Original Soundtrack\).*$", "", t); return t.strip()
c = json.load(open("library/candidates.json")); seen = set(); out = []
order = {"boss": 0, "battle": 1, "town": 2}
for blk in sorted(c, key=lambda b: order[b["category"]]):
    game = blk["game"]
    for s in blk["songs"]:
        t = s["title"]
        if s["id"] in seen or BAD.search(t) and game != "Final Fantasy XIV": continue
        if game == "Final Fantasy XIV" and re.search(r"final fantasy [iv]+\b(?!.*xiv)|FINAL FANTASY (IX|VII|V)\b", t, re.I) and "OST" not in t: pass
        others = [g for g in GAMES if g != game and TAGS[g].search(t)]
        if others and not TAGS[game].search(t): continue
        if re.search(r"remake|pixel|remaster", t, re.I) and False: continue
        if not (TAGS[game].search(t) or OFFICIAL.search((s["channel"] or "") + t)): continue
        cat = blk["category"]
        if cat == "boss" and not re.search(r"boss|decisive|final battle|force your way|fight|battle 2|tough|esper|omnis|magna|dancing|saber|blaze|price", t, re.I) and re.search(r"battle|闘|戦|バトル", t, re.I): cat = "battle"
        seen.add(s["id"]); out.append({"game": game, "category": cat, "id": s["id"], "title": clean(t)})
out.sort(key=lambda x: (GAMES.index(x["game"]), order[x["category"]]))
json.dump(out, open("library/songs.json", "w"), indent=1, ensure_ascii=False)
from collections import Counter
for (g, k), n in sorted(Counter((x["game"], x["category"]) for x in out).items(), key=lambda kv: (GAMES.index(kv[0][0]), order[kv[0][1]])): print(g, k, n)
print(len(out))
