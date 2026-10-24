#!/usr/bin/env python3
"""Mood and finer place tags for omakase (judged by the model from titles / place names; it cannot hear the audio).
  label-moods.py songs   -> tools/ost/moods.json        {track id: {"mood": [...], "sub": [...]}}   (merged by build-ost-library.py)
  label-moods.py zones   -> tools/ost/zone-moods.json   {zone name: {"mood": [...], "sub": [...]}}  (merged by build-territories.py / --apply)
  label-moods.py --apply -> merges zone-moods.json into plugin/SpherePlayer/territories.json
Cached per chunk in tools/ost/cache/mood-*.json."""
import hashlib, json, os, re, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
ROOT = __import__('pathlib').Path(__file__).resolve().parent.parent
OST = ROOT / 'tools/ost'
MOODS = "calm cheerful lively melancholy mysterious tense epic".split()
SUB = ("harbor market palace slums village meadow steppe farmland highlands canyon wasteland battlefield coast beach lake river ocean "
       "deep-forest grove dunes oasis peaks mine tunnels sewer temple tower graveyard laboratory factory airship floating-isle ship ruined-city tundra glacier lava sanctum").split()
MOOD_HELP = ("calm = peaceful, relaxed, pastoral, quiet; cheerful = bright, happy, sunny, playful; lively = busy, energetic, bustling, festive; "
             "melancholy = sad, nostalgic, lonely, bittersweet; mysterious = eerie, strange, dreamlike, ancient, otherworldly; "
             "tense = ominous, dangerous, urgent, dark, threatening; epic = grand, heroic, majestic, sweeping")
SYSTEM = "You are an expert on Final Fantasy music and on Final Fantasy XIV's world. Follow the instructions exactly. Output only a JSON object, no prose, no code fences."

def claude(prompt):
    env = {k: v for k, v in os.environ.items() if k not in ("CLAUDE_CONFIG_DIR", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_CHILD_SESSION", "ANTHROPIC_CUSTOM_HEADERS")}
    cmd = ["claude", "-p", "--model", os.environ.get("CLS_MODEL", "opus"), "--output-format", "json", "--tools", "", "--no-session-persistence",
           "--setting-sources", "", "--strict-mcp-config", "--system-prompt", SYSTEM]
    r = subprocess.run(cmd, input=prompt, capture_output=True, text=True, env=env, cwd="/tmp", timeout=900)
    out = json.loads(r.stdout)
    if out.get("is_error"): raise RuntimeError(out.get("result", "")[:300])
    t = re.sub(r"^```(?:json)?\s*|\s*```$", "", out["result"].strip())
    return json.loads(t[t.find("{"):t.rindex("}") + 1])

SONG_PROMPT = """These are Final Fantasy area / town / field / dungeon tracks (id | game | kind | title | coarse place tags). For each, judge from the title and what you know of the track in its game:
- "mood": 1 or 2 moods, the strongest first, only from: {moods}. ({help})
- "sub": 0 to 3 finer place tags the track suits, only from: {sub}. Give none if it does not clearly suit a specific kind of place.
Be decisive, use the full range of moods (do not make everything calm).
Answer ONLY JSON: {{"<id>": {{"mood": [...], "sub": [...]}}, ...}} with every id.

{lines}
"""
ZONE_PROMPT = """These are Final Fantasy XIV places (name | kind | coarse place tags). For each, as a player would feel the place, give:
- "mood": 1 or 2 moods, the strongest first, only from: {moods}. ({help})
- "sub": 0 to 3 finer place tags, only from: {sub}. Give none if no finer tag clearly applies.
Be decisive, use the full range of moods (Limsa Lominsa is lively, a swamp or haunted ruin is mysterious or tense, a quiet village calm).
Answer ONLY JSON: {{"<name>": {{"mood": [...], "sub": [...]}}, ...}} with every name exactly as given.

{lines}
"""

def run_chunk(prefix, prompt, keys):
    h = hashlib.md5((prefix + prompt).encode()).hexdigest()[:12]
    cf = OST / 'cache' / f"mood-{h}.json"
    if cf.exists(): return json.load(open(cf))
    for attempt in range(3):
        try:
            res = claude(prompt)
            if all(k in res for k in keys): break
        except Exception as e:
            print("  retry", e, flush=True)
    else:
        raise RuntimeError("labelling failed")
    out = {k: {"mood": [m for m in res[k].get("mood", []) if m in MOODS][:2], "sub": [s for s in res[k].get("sub", []) if s in SUB][:3]} for k in keys}
    json.dump(out, open(cf, "w"), ensure_ascii=False)
    return out

def songs():
    area = json.load(open(ROOT / 'library/area.json'))
    chunks = [area[i:i + 75] for i in range(0, len(area), 75)]
    def job(c):
        lines = "\n".join(f"{s['id']} | {s['game']} | {s['kind']} | {s['title']} | {','.join(s['tags'])}" for s in c)
        return run_chunk("song", SONG_PROMPT.format(moods=", ".join(MOODS), help=MOOD_HELP, sub=", ".join(SUB), lines=lines), [s['id'] for s in c])
    res = {}
    with ThreadPoolExecutor(4) as ex:
        for out in ex.map(job, chunks): res.update(out); print(len(res), flush=True)
    json.dump(res, open(OST / 'moods.json', 'w'), indent=1, ensure_ascii=False)
    from collections import Counter
    print("moods", Counter(m for v in res.values() for m in v['mood'][:1]), "no sub", sum(1 for v in res.values() if not v['sub']))

def zones():
    z = json.load(open(ROOT / 'plugin/SpherePlayer/territories.json'))
    uniq = {}
    for v in z.values(): uniq.setdefault(v['name'], v)
    items = list(uniq.values())
    chunks = [items[i:i + 55] for i in range(0, len(items), 55)]
    def job(c):
        lines = "\n".join(f"{v['name']} | {v['kind']} | {','.join(v['tags'])}" for v in c)
        return run_chunk("zone", ZONE_PROMPT.format(moods=", ".join(MOODS), help=MOOD_HELP, sub=", ".join(SUB), lines=lines), [v['name'] for v in c])
    res = {}
    with ThreadPoolExecutor(4) as ex:
        for out in ex.map(job, chunks): res.update(out); print(len(res), flush=True)
    json.dump(res, open(OST / 'zone-moods.json', 'w'), indent=1, ensure_ascii=False)
    from collections import Counter
    print("moods", Counter(m for v in res.values() for m in v['mood'][:1]), "no sub", sum(1 for v in res.values() if not v['sub']))

def apply():
    p = ROOT / 'plugin/SpherePlayer/territories.json'
    z = json.load(open(p)); zm = json.load(open(OST / 'zone-moods.json'))
    for v in z.values():
        m = zm.get(v['name'])
        if m: v['mood'], v['sub'] = m['mood'], m['sub']
    json.dump(z, open(p, 'w'), ensure_ascii=False, indent=1); open(p, 'a').write("\n")
    print("applied", sum(1 for v in z.values() if 'mood' in v), "of", len(z))

if __name__ == "__main__":
    {"songs": songs, "zones": zones, "--apply": apply}[sys.argv[1]]()
