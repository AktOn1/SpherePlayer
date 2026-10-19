#!/usr/bin/env python3
"""Second, blind opinion on every classified track (step 4 of the library build, quality check).
Re-runs the classify prompt with another model (default sonnet) WITHOUT showing the first label, then writes
tools/ost/audit/blind-<game>.json and a disagreement report tools/ost/audit/disagree.json.
usage: audit-ost.py [--only "Final Fantasy VII"]    (cached per chunk in tools/ost/audit/cache)"""
import hashlib, importlib.util, json, os, sys
from concurrent.futures import ThreadPoolExecutor
HERE = os.path.dirname(os.path.abspath(__file__)); OST = os.path.join(HERE, "ost"); AUD = os.path.join(OST, "audit")
os.makedirs(os.path.join(AUD, "cache"), exist_ok=True)
spec = importlib.util.spec_from_file_location("cls", os.path.join(HERE, "classify-ost.py")); cls = importlib.util.module_from_spec(spec); spec.loader.exec_module(cls)
MODEL = os.environ.get("AUDIT_MODEL", "sonnet")
CHUNK = 60

def job(args):
    game, items = args
    key = hashlib.md5((MODEL + game + json.dumps([i["id"] for i in items])).encode()).hexdigest()[:12]
    cf = os.path.join(AUD, "cache", key + ".json")
    if os.path.exists(cf): return json.load(open(cf))
    lines = "\n".join(f"{n} | {i['orig']} | {i['seconds']}" for n, i in enumerate(items))
    os.environ["CLS_MODEL"] = MODEL
    for _ in range(3):
        try:
            res = cls.claude(cls.PROMPT.format(game=game, tags=", ".join(cls.TAGS), lines=lines))
            by = {r["n"]: r for r in res}
            if all(n in by for n in range(len(items))): break
        except Exception as e: print("  retry", game, e, flush=True)
    else: raise RuntimeError("audit failed " + game)
    out = []
    for n, i in enumerate(items):
        r = by[n]; kind = r.get("kind") if r.get("kind") in cls.KINDS else "other"
        tags = [t for t in (r.get("tags") or []) if t in cls.TAGS][:3] if kind in ("town", "field", "dungeon") else []
        out.append({"id": i["id"], "kind": kind, "tags": tags, "en": (r.get("en") or "").strip()})
    json.dump(out, open(cf, "w"), ensure_ascii=False); return out

if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    jobs, firsts = [], {}
    for f in sorted(os.listdir(os.path.join(OST, "classified"))):
        game = f[:-5]
        if only and game != only: continue
        items = json.load(open(os.path.join(OST, "classified", f))); firsts[game] = {i["id"]: i for i in items}
        for s in range(0, len(items), CHUNK): jobs.append((game, items[s:s + CHUNK]))
    blind = {}
    with ThreadPoolExecutor(5) as ex:
        for (game, items), out in zip(jobs, ex.map(job, jobs)):
            blind.setdefault(game, []).extend(out); print(game, len(out), flush=True)
    dis = []
    for game, out in blind.items():
        json.dump(out, open(os.path.join(AUD, f"blind-{game}.json"), "w"), indent=1, ensure_ascii=False)
        for b in out:
            a = firsts[game][b["id"]]
            kind_diff = a["kind"] != b["kind"]
            area = a["kind"] in ("town", "field", "dungeon") and b["kind"] in ("town", "field", "dungeon")
            tag_diff = area and not (set(a["tags"]) & set(b["tags"]))
            if kind_diff or tag_diff:
                dis.append({"game": game, "id": a["id"], "orig": a["orig"], "title": a["title"], "seconds": a["seconds"],
                            "first": {"kind": a["kind"], "tags": a["tags"]}, "blind": {"kind": b["kind"], "tags": b["tags"]}})
    json.dump(dis, open(os.path.join(AUD, "disagree.json"), "w"), indent=1, ensure_ascii=False)
    total = sum(len(v) for v in blind.values())
    print(f"{total} tracks, {len(dis)} disagreements ({100*len(dis)/max(total,1):.0f}%)")
