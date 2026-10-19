#!/usr/bin/env python3
"""Add the thinly covered tags (volcano swamp jungle snow crystal) to area tracks, judged from titles (writes tools/ost/sparse-tags.json, merged by build-ost-library.py)."""
import json, os, subprocess
ROOT = __import__('pathlib').Path(__file__).resolve().parent.parent
area = json.load(open(ROOT / 'library/area.json'))
env = {k: v for k, v in os.environ.items() if k != 'CLAUDE_CONFIG_DIR'}
TAGS = {'volcano': 'lava, fire, magma, burning, ember, forge, fiery mountain', 'swamp': 'swamp, marsh, bog, mire, fen, poisonous wetland',
        'jungle': 'jungle, tropical forest, rainforest, tribal', 'snow': 'snow, ice, glacier, frozen, winter, cold', 'crystal': 'crystal, shining light, ethereal, holy, otherworldly'}
lines = "\n".join(f"{s['id']} | {s['game']} | {s['title']}" for s in area)
prompt = ("Below are Final Fantasy area/dungeon/town tracks (id | game | title). For each requested tag list every track that musically/thematically suits a place with that tag "
          "(judge from the title and what you know of the track in its game). Be inclusive but not careless: 15-60 tracks per tag.\n"
          + "\n".join(f"- {t}: {d}" for t, d in TAGS.items()) + '\nAnswer ONLY JSON {"volcano": [ids], "swamp": [ids], ...}.\n\n' + lines)
r = subprocess.run(['claude', '-p', '--model', 'opus'], input=prompt, capture_output=True, text=True, env=env, timeout=1500)
t = r.stdout; res = json.loads(t[t.index('{'):t.rindex('}')+1])
ids = {s['id'] for s in area}
res = {k: [i for i in v if i in ids] for k, v in res.items()}
json.dump(res, open(ROOT / 'tools/ost/sparse-tags.json', 'w'), indent=1)
print({k: len(v) for k, v in res.items()})
