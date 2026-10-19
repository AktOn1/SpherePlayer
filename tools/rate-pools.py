#!/usr/bin/env python3
"""LLM rating of what omakase would play in each FFXIV zone group (reads tools/ost/pools.json -> tools/ost/pool-ratings.json)."""
import json, random, subprocess, os, sys, concurrent.futures as cf
ROOT = __import__('pathlib').Path(__file__).resolve().parent.parent
pools = json.load(open(ROOT / 'tools/ost/pools.json'))
random.seed(7)
names = list(pools)
batches = [names[i:i+20] for i in range(0, len(names), 20)]
env = {k: v for k, v in os.environ.items() if k != 'CLAUDE_CONFIG_DIR'}

def ask(batch):
    lines = []
    for n in batch:
        p = pools[n]; s = list({x['id']: x for x in random.choices(p['top'], weights=[max(1, x['weight']) for x in p['top']], k=10)}.values())
        lines.append(f"## {n} [{p['kind']}; tags {','.join(p['tags'])}; pool {len(p['top'])}]\n" + "\n".join(f"- {x['id']} | {x['game']} | {x['title']}" for x in s))
    prompt = ("You know Final Fantasy XIV well. For each FFXIV place below, a music player picks a random song from the listed sample (songs from all FF games). "
              "Rate 1-5 how well a typical song from that list would suit the place's atmosphere (5 = fits like its own theme, 3 = acceptable, 1 = jarring). Also list ids of songs that clearly do NOT fit (e.g. a battle theme, a sad scene piece in a cheerful city, an unrelated climate).\n"
              'Answer ONLY JSON: {"<place>": {"rating": n, "misfits": ["<id>", ...]}, ...}\n\n' + "\n\n".join(lines))
    r = subprocess.run(['claude', '-p', '--model', 'sonnet'], input=prompt, capture_output=True, text=True, env=env, timeout=900)
    t = r.stdout; return json.loads(t[t.index('{'):t.rindex('}')+1])

res = {}
with cf.ThreadPoolExecutor(4) as ex:
    for b, f in [(b, ex.submit(ask, b)) for b in batches]:
        try: res.update(f.result())
        except Exception as e: print('batch failed', e, file=sys.stderr)
json.dump(res, open(ROOT / 'tools/ost/pool-ratings.json', 'w'), indent=1, ensure_ascii=False)
rs = [v['rating'] for v in res.values()]
print('rated', len(rs), 'avg', sum(rs)/len(rs), 'hist', {i: rs.count(i) for i in range(1, 6)})
