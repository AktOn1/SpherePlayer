#!/usr/bin/env python3
"""Mirror of Controller.PickOmakase for every distinct zone: which songs can omakase play there, and how big is the pool.
Writes tools/ost/pools.json; prints pool-size stats. Usage: simulate-pools.py"""
import json, collections
ROOT = __import__('pathlib').Path(__file__).resolve().parent.parent
area = json.load(open(ROOT / 'library/area.json'))
zones = json.load(open(ROOT / 'plugin/SpherePlayer/territories.json'))

def here(s, z): return z['name'].lower() in [h.lower() for h in s.get('home', [])]

def score(s, z):
    if s.get('home') and not here(s, z): return 0
    if here(s, z): return len(z['tags']) * (len(z['tags']) + 1) // 2
    return sum(len(z['tags']) - i for i, t in enumerate(z['tags']) if t in s['tags'])

def pool(z):
    kind = [s for s in area if s['kind'] == z['kind'] or here(s, z)]
    for p in (kind, area):
        sc = [(s, score(s, z)) for s in p]
        sc = [(s, v) for s, v in sc if v > 0]
        if sc and p is kind and sum(1 for _, v in sc if v >= max(v for _, v in sc) - 1) < 6:
            sc = [(s, v) for s, v in ((s, score(s, z)) for s in area) if v > 0]
        if sc:
            best = max(v for _, v in sc)
            fl = max((best + 1) // 2, min(best - 1, sorted((v for _, v in sc), reverse=True)[5] if len(sc) >= 6 else 0))
            return [(s, v) for s, v in sc if v >= fl], len(sc), p is kind
    return [], 0, False

groups = {}
for tid, z in zones.items():
    if not z.get('tags'): continue
    groups.setdefault((z['name'], z['kind'], tuple(z['tags'])), z)
out, sizes, empty, fallback = {}, [], [], 0
for (name, kind, tags), z in groups.items():
    top, n, same = pool(z)
    if not top: empty.append(name)
    if not same: fallback += 1
    sizes.append(len(top))
    out[name] = {'kind': kind, 'tags': list(tags), 'same_kind': same, 'eligible': n,
                 'top': [{'id': s['id'], 'title': s['title'], 'game': s['game'], 'score': v, 'weight': v * v * 10 * max(1, len(set(s['tags']) & set(z['tags']))) // max(1, len(s['tags'])), 'tags': s['tags']} for s, v in sorted(top, key=lambda x: -x[1])]}
json.dump(out, open(ROOT / 'tools/ost/pools.json', 'w'), indent=1, ensure_ascii=False)
sizes.sort()
print('zone groups', len(groups), 'empty', len(empty), 'cross-kind fallback', fallback)
print('top-pool size: min', sizes[0], 'median', sizes[len(sizes)//2], 'p10', sizes[len(sizes)//10], 'max', sizes[-1], 'zones with <5:', sum(1 for s in sizes if s < 5))
ft = collections.Counter(g for p in out.values() for g in [s['game'] for s in p['top']])
print(ft.most_common(8))
