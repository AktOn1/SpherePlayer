#!/usr/bin/env python3
"""Step 4b: for tracks where the first classification and the blind audit disagree (tools/ost/audit/disagree.json),
a third pass (opus) sees both opinions in random order and decides.  Writes tools/ost/overrides.json
{video id: {"kind":..., "tags":[...], "was": {...}}} for every track whose final label differs from the first one.
build-ost-library.py applies the overrides."""
import hashlib, importlib.util, json, os, random
from concurrent.futures import ThreadPoolExecutor
HERE = os.path.dirname(os.path.abspath(__file__)); OST = os.path.join(HERE, "ost")
spec = importlib.util.spec_from_file_location("cls", os.path.join(HERE, "classify-ost.py")); cls = importlib.util.module_from_spec(spec); spec.loader.exec_module(cls)
PROMPT = """Final Fantasy music tracks where two reviewers disagreed about where the track plays. Decide the correct label for each, from your own knowledge of the game and the track.
Kinds:
- "battle": ordinary enemy / random-encounter battle theme (also open-world and FATE-style fight themes)
- "boss": boss, final boss, summon/esper/primal, rival or story-fight theme
- "town": town, city, village, home base, inn/shop, settlement theme
- "field": overworld / world map / open field / travel / airship / chocobo / vehicle theme
- "dungeon": dungeon, cave, tower, ruins, fortress, eerie exploration theme
- "other": ONLY music that does not belong to a place: opening, prelude, ending, credits, fanfare, victory, game over, cutscene / event / story / emotional-scene music, vocal songs, minigames, menus, jingles
A named theme that plays as the music of a specific area counts as that area. Do not invent places.
For town / field / dungeon also give 1 to 3 tags describing the place, only from: {tags}.
You may pick either opinion or something else. Reply with a JSON array, one object per track in order: {{"n": <number>, "kind": "...", "tags": [...]}}.

Tracks (n | game | released title | english title | opinion 1 | opinion 2):
{lines}
"""
def fmt(o): return o["kind"] + (" " + "/".join(o["tags"]) if o["tags"] else "")
def job(args):
    items = args
    key = hashlib.md5(json.dumps([i["id"] for i in items]).encode()).hexdigest()[:12]
    cf = os.path.join(OST, "audit", "cache", "arb-" + key + ".json")
    if os.path.exists(cf): return json.load(open(cf))
    lines = []
    for n, i in enumerate(items):
        ops = [i["first"], i["blind"]]; random.Random(i["id"]).shuffle(ops)
        lines.append(f"{n} | {i['game']} | {i['orig']} | {i['title']} | {fmt(ops[0])} | {fmt(ops[1])}")
    os.environ["CLS_MODEL"] = "opus"
    for _ in range(3):
        try:
            res = cls.claude(PROMPT.format(tags=", ".join(cls.TAGS), lines="\n".join(lines))); by = {r["n"]: r for r in res}
            if all(n in by for n in range(len(items))): break
        except Exception as e: print("retry", e, flush=True)
    else: raise SystemExit("arbitration failed")
    out = []
    for n, i in enumerate(items):
        r = by[n]; kind = r.get("kind") if r.get("kind") in cls.KINDS else i["first"]["kind"]
        tags = [t for t in (r.get("tags") or []) if t in cls.TAGS][:3] if kind in ("town", "field", "dungeon") else []
        out.append({"id": i["id"], "kind": kind, "tags": tags})
    json.dump(out, open(cf, "w")); return out
if __name__ == "__main__":
    dis = json.load(open(os.path.join(OST, "audit", "disagree.json")))
    chunks = [dis[s:s + 50] for s in range(0, len(dis), 50)]
    ov, kept = {}, 0
    with ThreadPoolExecutor(5) as ex:
        for items, out in zip(chunks, ex.map(job, chunks)):
            for i, o in zip(items, out):
                f = i["first"]
                if o["kind"] != f["kind"] or (o["kind"] in ("town", "field", "dungeon") and set(o["tags"]) != set(f["tags"])):
                    ov[i["id"]] = {"kind": o["kind"], "tags": o["tags"], "was": f, "title": i["title"], "game": i["game"]}
                else: kept += 1
            print(len(ov), kept, flush=True)
    json.dump(ov, open(os.path.join(OST, "overrides.json"), "w"), indent=1, ensure_ascii=False)
    from collections import Counter
    print(len(ov), "changed,", kept, "first label confirmed;", Counter((v["was"]["kind"], v["kind"]) for v in ov.values()).most_common(12))
