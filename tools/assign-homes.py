#!/usr/bin/env python3
"""Step 3b: which FFXIV places is an FFXIV area track the music of?  -> tools/ost/homes.json {video id: [territory names]}.
Tracks that are tied to a place only play there in omakase mode (the Kugane theme should not play in Limsa);
tracks that are generic (empty list) stay eligible everywhere.
Hints come from place names inside the title; Claude decides from its knowledge of the game, high confidence only."""
import json, os, re, sys, importlib.util
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
spec = importlib.util.spec_from_file_location("cls", os.path.join(HERE, "classify-ost.py")); cls = importlib.util.module_from_spec(spec); spec.loader.exec_module(cls)
names = sorted({v["name"] for v in json.load(open(os.path.join(ROOT, "plugin/SpherePlayer/territories.json"))).values()})
area = [a for a in json.load(open(os.path.join(ROOT, "library/area.json"))) if a["game"] == "Final Fantasy XIV"]
PROMPT = """These are tracks of the Final Fantasy XIV soundtrack that play in towns, fields and dungeons (track title as released, English title, and the kind we think it is).
For each track say in which in-game location(s) it is the music, using ONLY names from this list of location names:
{names}
Rules:
- Use a location only if you are confident (the title names it, or you know the zone theme, e.g. the Crystarium day/night themes belong to "The Crystarium").
- A theme of a whole region may list all its zones (e.g. a Thanalan field theme -> the five Thanalan zones).
- A generic or reused track (a chocobo theme, a track for many dungeons, a remix, anything you are unsure of) gets [].
- Never invent a name that is not in the list.
Reply with a JSON array, one object per track in order: {{"n": <number>, "home": [names]}}.

Tracks (n | released title | english title | kind):
{lines}
"""
out = {}
CH = 45
for s in range(0, len(area), CH):
    items = area[s:s + CH]
    lines = "\n".join(f"{n} | {i['track']} | {i['title']} | {i['kind']}" for n, i in enumerate(items))
    for attempt in range(3):
        try:
            res = cls.claude(PROMPT.format(names="; ".join(names), lines=lines)); by = {r["n"]: r for r in res}
            if all(n in by for n in range(len(items))): break
        except Exception as e: print("retry", e, flush=True)
    else: raise SystemExit("failed")
    for n, i in enumerate(items):
        h = [x for x in by[n].get("home", []) if x in names]
        if h: out[i["id"]] = h
    print(s, len(out), flush=True)
json.dump(out, open(os.path.join(HERE, "ost", "homes.json"), "w"), indent=1, ensure_ascii=False)
print(len(out), "of", len(area), "tracks tied to a place")
