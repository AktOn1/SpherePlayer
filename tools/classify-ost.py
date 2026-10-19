#!/usr/bin/env python3
"""Step 2: tools/ost/raw/<game>.json -> tools/ost/classified/<game>.json
Dedupes, drops jingles, asks Claude (from the track titles, nothing else) for the situation of each track
(battle / boss / town / field / dungeon / other), place tags and an English title.

usage: classify-ost.py [--only "Final Fantasy VII"]    (cached per chunk in tools/ost/cache/cls-*.json)
"""
import hashlib, json, os, re, subprocess, sys, unicodedata
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
OST = os.path.join(HERE, "ost")
os.makedirs(os.path.join(OST, "classified"), exist_ok=True)
TAGS = "city town eerie plains seaside castle industrial crystal ruins desert mountain forest cave snow jungle water sky swamp".split()
KINDS = ["battle", "boss", "town", "field", "dungeon", "other"]
CHUNK = 90
SYSTEM = "You are an expert on Final Fantasy music. Follow the instructions exactly. Output only a JSON array, no prose, no code fences."
PROMPT = """These are tracks of the original soundtrack of {game}, with the track title as uploaded (may be Japanese) and the length in seconds.
For every track decide where in the game it plays, from your knowledge of the game and the track:
- "battle": ordinary enemy / random-encounter battle theme (also open-world and FATE-style fight themes)
- "boss": boss, final boss, summon/esper/primal, rival or story-fight theme
- "town": town, city, village, home base, inn/shop, settlement theme
- "field": overworld / world map / open field / travel / airship / chocobo / vehicle theme
- "dungeon": dungeon, cave, tower, ruins, fortress, forest-dungeon, eerie exploration theme
- "other": ONLY music that does not belong to a place: opening, prelude, ending, credits, fanfare, victory, game over, cutscene / event / story / emotional-scene music, vocal songs, minigames, menus, jingles
A character theme or named theme that you know plays as the music of a specific area (a city, the world map, a dungeon) counts as that area, not "other". Use "other" only when the track is a pure cutscene piece or you really do not know the game's use of it; do not invent places.
For town / field / dungeon also give 1 to 3 tags describing the place, only from: {tags}.
Also give the English title as used in the official English release (translate Japanese titles; if unsure keep a plain literal translation).
Reply with a JSON array, one object per input track in the same order: {{"n": <number>, "kind": "...", "tags": [...], "en": "..."}}.

Tracks (n | title | seconds):
{lines}
"""

def norm(s):
    s = unicodedata.normalize("NFKC", s).lower()
    return re.sub(r"[^\w]+", "", s)

def claude(prompt):
    env = {k: v for k, v in os.environ.items() if k not in ("CLAUDE_CONFIG_DIR", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_CHILD_SESSION", "ANTHROPIC_CUSTOM_HEADERS")}
    cmd = ["claude", "-p", "--model", os.environ.get("CLS_MODEL", "opus"), "--output-format", "json", "--tools", "", "--no-session-persistence",
           "--setting-sources", "", "--strict-mcp-config", "--system-prompt", SYSTEM]
    r = subprocess.run(cmd, input=prompt, capture_output=True, text=True, env=env, cwd="/tmp", timeout=900)
    out = json.loads(r.stdout)
    if out.get("is_error"): raise RuntimeError(out.get("result", "")[:300])
    t = re.sub(r"^```(?:json)?\s*|\s*```$", "", out["result"].strip())
    return json.loads(t[t.find("["):])

def chunk_job(args):
    game, items = args
    key = hashlib.md5((game + json.dumps([(i["id"], i["title"]) for i in items])).encode()).hexdigest()[:12]
    cf = os.path.join(OST, "cache", f"cls-{key}.json")
    if os.path.exists(cf): return json.load(open(cf))
    lines = "\n".join(f"{n} | {i['title']} | {i['seconds']}" for n, i in enumerate(items))
    for attempt in range(3):
        try:
            res = claude(PROMPT.format(game=game, tags=", ".join(TAGS), lines=lines))
            by = {r["n"]: r for r in res}
            if all(n in by for n in range(len(items))): break
        except Exception as e:
            print("  retry", game, e, flush=True)
    else:
        raise RuntimeError(f"classification failed for {game}")
    out = []
    for n, i in enumerate(items):
        r = by[n]
        kind = r.get("kind") if r.get("kind") in KINDS else "other"
        tags = [t for t in (r.get("tags") or []) if t in TAGS][:3] if kind in ("town", "field", "dungeon") else []
        out.append({"id": i["id"], "orig": i["title"], "title": (r.get("en") or i["title"]).strip(), "seconds": i["seconds"], "kind": kind, "tags": tags})
    json.dump(out, open(cf, "w"), ensure_ascii=False)
    return out

if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    jobs = []
    for f in sorted(os.listdir(os.path.join(OST, "raw"))):
        game = f[:-5]
        if only and game != only: continue
        raw = [t for t in json.load(open(os.path.join(OST, "raw", f))) if t["embeddable"] and (t.get("seconds") or 0) >= 25]
        seen, items = set(), []
        for t in raw:
            k = (norm(t["title"]), round((t["seconds"] or 0) / 4))
            if k in seen: continue
            seen.add(k); items.append(t)
        for s in range(0, len(items), CHUNK): jobs.append((game, items[s:s + CHUNK]))
    results = {}
    with ThreadPoolExecutor(4) as ex:
        for (game, items), out in zip(jobs, ex.map(chunk_job, jobs)):
            results.setdefault(game, []).extend(out)
            print(game, len(out), flush=True)
    for game, out in results.items():
        json.dump(out, open(os.path.join(OST, "classified", game + ".json"), "w"), indent=1, ensure_ascii=False)
        from collections import Counter
        print(game, dict(Counter(x["kind"] for x in out)))
