#!/usr/bin/env python3
"""Builds plugin/SpherePlayer/territories.json: TerritoryType RowId -> {name, kind, tags}.

Data source: public ffxiv-datamining CSVs (xivapi/ffxiv-datamining, csv/en/), read-only fetch,
cached under /tmp/ffxiv-datamining-cache.  The hand-written part is the PLACES dictionary below:
PlaceName (normalised, see norm()) -> (kind, tags).  Every TerritoryType whose PlaceName is in PLACES
is mapped (so phases/instance copies of a city or field with the same PlaceName all get the entry).
Territories linked to a ContentFinderCondition of ContentType 2 (dungeon), 4 (trial), 5 (raid) or
21 (deep dungeon) are always kind=dungeon, even if their PlaceName is a field name (e.g. Urth's Fount).

Tag vocabulary: city town forest jungle desert seaside snow mountain plains cave ruins swamp volcano
castle sky crystal industrial eerie water.

Notes on TerritoryIntendedUse seen in the CSV (this datamining revision): 0 Town, 1 Open World,
2 Inn, 3 Dungeon, 4/5 misc instanced, 6 city-state cutscene copy, 8 Raid (alliance/normal 8-man,
older 24-man), 9 Guildleve, 10 Trial, 12 Palace-like instance, 13 Residential area,
14 Housing interior, 15 duty/quest battle, 16/17 raid (8-man), 18 PvP, 23 Gold Saucer,
26 Diadem, 29 solo duty copy, 31 Deep dungeon, 41 Eureka, 48 Bozja, 54 variant, 61 Occult Crescent.
The script prints the values it actually saw for the mapped ids.

Sub-areas such as Costa del Sol or Wineport are PlaceNames inside a territory, not TerritoryTypes,
so they cannot be keyed here (they are part of Eastern La Noscea / Limsa).
Places I could not judge are skipped on purpose (see SKIPPED below); the validation lists open-world/
town territories that remain unmapped.
"""
import csv, io, json, os, sys, urllib.request, collections

BASE = "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master/csv/en/"
CACHE = "/tmp/ffxiv-datamining-cache"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "plugin", "SpherePlayer", "territories.json")

VOCAB = set("city town forest jungle desert seaside snow mountain plains cave ruins swamp volcano "
            "castle sky crystal industrial eerie water".split())

def norm(s):
    return s.replace("*", "").replace("–", "-").replace("’", "'").strip()

def fetch(name):
    os.makedirs(CACHE, exist_ok=True)
    p = os.path.join(CACHE, name)
    if not os.path.exists(p):
        with urllib.request.urlopen(BASE + name) as r, open(p, "wb") as f:
            f.write(r.read())
    return list(csv.reader(open(p, encoding="utf-8")))

# ---------------------------------------------------------------- hand-written data
# "PlaceName": "kind tag tag ..."   (first word kind, rest tags)
_P = """
# ---- cities, towns, housing ----
Limsa Lominsa Upper Decks: town city seaside
Limsa Lominsa Lower Decks: town city seaside
Limsa Lominsa: town city seaside
Ul'dah - Steps of Nald: town city desert
Ul'dah - Steps of Thal: town city desert
New Gridania: town town forest
Old Gridania: town town forest
Gridania: town town forest
Mist: town town seaside
The Lavender Beds: town town forest
The Goblet: town town desert
Shirogane: town town seaside
Empyreum: town town snow mountain
Foundation: town city snow castle
The Pillars: town city snow castle
The Firmament: town city snow castle
Idyllshire: town town mountain
Rhalgr's Reach: town town castle mountain
Kugane: town city seaside
The Doman Enclave: town town plains
The Crystarium: town city crystal
Eulmore: town city castle
Old Sharlayan: town city seaside mountain
Radz-at-Han: town city seaside desert
Tuliyollal: town city jungle seaside
Solution Nine: town city industrial crystal
The Gold Saucer: town city desert
Wolves' Den Pier: town seaside industrial

# ---- A Realm Reborn fields ----
Middle La Noscea: field plains seaside
Lower La Noscea: field seaside plains
Eastern La Noscea: field seaside plains
Western La Noscea: field seaside mountain
Upper La Noscea: field plains forest
Outer La Noscea: field plains seaside
Western Thanalan: field desert plains
Central Thanalan: field desert plains
Eastern Thanalan: field desert
Southern Thanalan: field desert ruins
Northern Thanalan: field plains desert
Central Shroud: field forest
East Shroud: field forest water
South Shroud: field forest swamp
North Shroud: field forest water
Coerthas Central Highlands: field snow mountain
Mor Dhona: field crystal ruins

# ---- Heavensward ----
Coerthas Western Highlands: field snow mountain
The Dravanian Forelands: field plains mountain
The Dravanian Hinterlands: field forest mountain
The Churning Mists: field sky mountain
The Sea of Clouds: field sky
Azys Lla: field sky ruins industrial

# ---- Stormblood ----
The Fringes: field mountain forest ruins
The Ruby Sea: field seaside water
Yanxia: field plains water
The Peaks: field mountain industrial
The Lochs: field water mountain
The Azim Steppe: field plains

# ---- Shadowbringers ----
Lakeland: field plains water
Kholusia: field seaside ruins
Amh Araeng: field desert
Il Mheg: field crystal forest
The Rak'tika Greatwood: field forest swamp
The Tempest: field water ruins

# ---- Endwalker ----
Labyrinthos: field plains ruins
Thavnair: field jungle seaside
Garlemald: field snow industrial
Mare Lamentorum: field eerie crystal industrial
Ultima Thule: field crystal eerie
Elpis: field forest crystal plains

# ---- Dawntrail ----
Urqopacha: field forest mountain
Kozama'uka: field jungle water
Yak T'el: field jungle
Shaaloani: field desert
Heritage Found: field desert ruins industrial
Living Memory: field crystal cave ruins

# ---- other open areas ----
Eureka Anemos: field plains
Eureka Pagos: field snow
Eureka Pyros: field volcano
Eureka Hydatos: field water crystal
Bozjan Southern Front: field ruins plains
Zadnor: field mountain ruins
South Horn: field plains ruins eerie
North Horn: field cave ruins eerie
The Diadem: field sky
Unnamed Island: field seaside plains

# ---- A Realm Reborn dungeons ----
The Thousand Maws of Toto-Rak: dungeon cave eerie
The Tam-Tara Deepcroft: dungeon eerie ruins cave
Copperbell Mines: dungeon cave
Sastasha: dungeon cave seaside water
Aurum Vale: dungeon cave forest
Haukke Manor: dungeon castle eerie
Halatali: dungeon cave water
Brayflox's Longstop: dungeon cave swamp
The Sunken Temple of Qarn: dungeon ruins cave desert
The Wanderer's Palace: dungeon ruins castle desert
Stone Vigil: dungeon castle snow ruins
Dzemael Darkhold: dungeon cave
Amdapor Keep: dungeon ruins castle eerie
Castrum Meridianum: dungeon industrial castle
The Praetorium: dungeon industrial castle
Pharos Sirius: dungeon seaside ruins
The Lost City of Amdapor: dungeon ruins eerie
Hullbreaker Isle: dungeon seaside ruins
Snowcloak: dungeon snow cave
The Great Gubal Library: dungeon ruins eerie
The Keeper of the Lake: dungeon cave water
Neverreap: dungeon forest ruins
The Vault: dungeon castle snow
The Fractal Continuum: dungeon ruins industrial
The Dusk Vigil: dungeon castle snow
Dusk Vigil: dungeon castle snow
Sohm Al: dungeon volcano mountain
The Aetherochemical Research Facility: dungeon industrial
Aetherochemical Research Facility: dungeon industrial
The Aery: dungeon mountain cave
Saint Mocianne's Arboretum: dungeon forest eerie
The Antitower: dungeon ruins eerie
Sohr Khai: dungeon mountain sky ruins
Xelphatol: dungeon ruins sky
Baelsar's Wall: dungeon industrial
Shisui of the Violet Tides: dungeon seaside water cave
The Temple of the Fist: dungeon ruins mountain
The Sirensong Sea: dungeon seaside water eerie
Bardam's Mettle: dungeon mountain ruins
Doma Castle: dungeon castle
Castrum Abania: dungeon industrial castle
Ala Mhigo: dungeon castle
Kugane Castle: dungeon castle seaside
The Drowned City of Skalla: dungeon water ruins
The Swallow's Compass: dungeon ruins mountain
The Ghimlyt Dark: dungeon industrial castle
Dohn Mheg: dungeon crystal forest
The Qitana Ravel: dungeon ruins jungle
Amaurot: dungeon ruins eerie crystal
The Twinning: dungeon industrial
Malikah's Well: dungeon ruins cave
Mt. Gulg: dungeon mountain ruins
Akadaemia Anyder: dungeon ruins water
Holminster Switch: dungeon eerie castle
The Grand Cosmos: dungeon castle crystal
Anamnesis Anyder: dungeon water ruins
The Heroes' Gauntlet: dungeon industrial
Matoya's Relict: dungeon cave crystal
Paglth'an: dungeon cave ruins
The Tower of Zot: dungeon ruins crystal
The Stigma Dreamscape: dungeon industrial eerie
The Tower of Babil: dungeon industrial
The Aitiascope: dungeon ruins eerie
Ktisis Hyperboreia: dungeon ruins crystal
Vanaspati: dungeon jungle ruins
The Dead Ends: dungeon ruins eerie
Smileton: dungeon industrial eerie
The Aetherfont: dungeon water seaside
The Lunar Subterrane: dungeon crystal cave
Worqor Zormor: dungeon snow ruins
Origenics: dungeon industrial
Ihuykatumu: dungeon jungle ruins
Alexandria: dungeon castle industrial
The Skydeep Cenote: dungeon cave water
Vanguard: dungeon industrial
Tender Valley: dungeon plains forest
Alzadaal's Legacy: dungeon cave ruins
The Fell Court of Troia: dungeon castle eerie
Lapis Manalis: dungeon ruins cave
The Strayborough Deadwalk: dungeon eerie
Strayborough: dungeon eerie
Yuweyawata: dungeon industrial jungle
The Clyteum: dungeon ruins castle
The Underkeep: dungeon cave ruins
The Meso Terminal: dungeon industrial
Mistwake: dungeon jungle ruins

# ---- trials ----
Bowl of Embers: dungeon volcano
The Navel: dungeon mountain cave
The Howling Eye: dungeon sky
Thornmarch: dungeon forest
Porta Decumana: dungeon industrial castle
The Whorleater: dungeon seaside water
The Weeping Saint: dungeon ruins
Hall of the Bestiarii: dungeon ruins
Griffin Crossing: dungeon industrial castle
The Striking Tree: dungeon water sky
Akh Afah Amphitheatre: dungeon snow ruins
The Dancing Plague: dungeon forest crystal
Thok ast Thok: dungeon ruins
The Limitless Blue: dungeon seaside sky water
Singularity Reactor: dungeon industrial
Containment Bay S1T7: dungeon industrial
Containment Bay P1T6: dungeon industrial
Containment Bay Z1T9: dungeon industrial
Steps of Faith: dungeon mountain sky castle
Kugane Ohashi: dungeon seaside
The Royal Airship Landing: dungeon castle sky
The Blessed Treasury: dungeon castle
Emanation: dungeon crystal
Transparency: dungeon castle
The Jade Stoa: dungeon mountain ruins
The Great Hunt: dungeon mountain plains
Castrum Fluminis: dungeon industrial castle
The Wreath of Snakes: dungeon water ruins
The Crown of the Immaculate: dungeon crystal castle
The Dying Gasp: dungeon eerie crystal
Cinder Drift: dungeon industrial volcano
Transmission Control: dungeon industrial
The Seat of Sacrifice: dungeon ruins crystal
Castrum Marinum Drydocks: dungeon industrial seaside
G-Savior Deck: dungeon industrial sky
The Mothercrystal: dungeon crystal mountain
The Final Day: dungeon crystal
The Dark Inside: dungeon crystal eerie
Storm's Crown: dungeon sky mountain
Mount Ordeals: dungeon mountain ruins
The Voidcast Dais: dungeon eerie
The Abyssal Fracture: dungeon sky crystal
Interphos: dungeon eerie
Summit of Everkeep: dungeon ruins crystal
Worqor Lar Dor: dungeon ruins
Recollection: dungeon crystal
The Windward Wilds: dungeon plains
The Ageless Necropolis: dungeon eerie ruins
Hell on Rails: dungeon eerie industrial
The Unmaking: dungeon crystal

# ---- raids ----
Labyrinth of the Ancients: dungeon ruins eerie
Upper Aetheroacoustic Exploratory Site: dungeon ruins industrial
Lower Aetheroacoustic Exploratory Site: dungeon ruins industrial
The Ragnarok: dungeon industrial
Ragnarok Drive Cylinder: dungeon industrial
Ragnarok Central Core: dungeon industrial
Dalamud's Shadow: dungeon industrial eerie
The Outer Coil: dungeon industrial
Central Decks: dungeon industrial
The Holocharts: dungeon industrial crystal
Syrcus Tower: dungeon ruins crystal
IC-06 Central Decks: dungeon industrial
IC-06 Regeneration Grid: dungeon industrial
IC-06 Main Bridge: dungeon industrial
The Burning Heart: dungeon industrial volcano
The World of Darkness: dungeon eerie crystal
The Fist of the Father: dungeon industrial
The Cuff of the Father: dungeon industrial
The Arm of the Father: dungeon industrial
The Burden of the Father: dungeon industrial
The Fist of the Son: dungeon industrial
The Cuff of the Son: dungeon industrial
The Arm of the Son: dungeon industrial
The Burden of the Son: dungeon industrial
Eyes of the Creator: dungeon industrial crystal
Breath of the Creator: dungeon industrial crystal
Heart of the Creator: dungeon industrial crystal
Soul of the Creator: dungeon industrial crystal
Void Ark: dungeon eerie sky
The Weeping City of Mhach: dungeon ruins eerie
Dun Scaith: dungeon castle eerie
Deltascape V1.0: dungeon industrial crystal
Deltascape V2.0: dungeon industrial crystal
Deltascape V3.0: dungeon industrial crystal
Deltascape V4.0: dungeon industrial crystal
Sigmascape V1.0: dungeon crystal eerie industrial
Sigmascape V2.0: dungeon crystal eerie industrial
Sigmascape V3.0: dungeon crystal eerie industrial
Sigmascape V4.0: dungeon crystal eerie industrial
Psiscape V1.0: dungeon industrial crystal
Psiscape V2.0: dungeon industrial crystal
The Interdimensional Rift: dungeon industrial crystal
The Royal City of Rabanastre: dungeon city castle
The Ridorana Lighthouse: dungeon ruins sky
The Orbonne Monastery: dungeon castle eerie
The Core: dungeon crystal
The Nereus Trench: dungeon water crystal
The Halo: dungeon crystal
Atlas Peak: dungeon mountain crystal
The Copied Factory: dungeon industrial
The Gandof Thunder Plains: dungeon industrial plains
Ashfall: dungeon volcano crystal
Great Glacier: dungeon snow crystal
The Puppets' Bunker: dungeon industrial
Laxan Loft: dungeon crystal sky
Sphere of Naught: dungeon crystal
Bygone Gaol: dungeon crystal eerie
The Garden of Nowhere: dungeon crystal
The Tower at Paradigm's Breach: dungeon crystal sky
The Sanguine Limbo: dungeon ruins eerie crystal
The Fervid Limbo: dungeon ruins eerie crystal
The Gates of Pandaemonium: dungeon ruins eerie crystal
The Gates of Pandæmonium: dungeon ruins eerie crystal
The Stagnant Limbo: dungeon ruins eerie crystal
Aglaia: dungeon castle ruins
Euphrosyne: dungeon ruins cave
Thaleia: dungeon ruins
The Caustic Purgatory: dungeon castle eerie
The Hollow Purgatory: dungeon castle eerie
The Pestilent Purgatory: dungeon castle eerie
Stygian Insenescence Cells: dungeon castle eerie
The Aetherial Slough: dungeon castle crystal
The Dæmons' Nest: dungeon castle crystal
The Chamber of Fourteen: dungeon castle crystal
Ascension: dungeon castle crystal
Scratching Ring: dungeon industrial
Lovely Lovering: dungeon industrial
Blasting Ring: dungeon industrial
The Thundering: dungeon industrial
Groovy Ring: dungeon industrial
Rebel Ring: dungeon industrial
Demolition Site: dungeon industrial
Hunter's Ring: dungeon industrial
Ring Noir: dungeon industrial
The X-Ring: dungeon industrial
The Crown: dungeon industrial
Arcadia: dungeon industrial
Jeuno: The First Walk: dungeon city castle
San d'Oria: The Second Walk: dungeon castle
Windurst: The Third Walk: dungeon forest city

# ---- deep dungeons and other instanced areas ----
The Palace of the Dead: dungeon cave eerie
Heaven-on-High: dungeon sky ruins
Eureka Orthos: dungeon ruins eerie cave
Pilgrim's Traverse: dungeon crystal eerie
The Sil'dihn Subterrane: dungeon cave ruins desert
Another Sil'dihn Subterrane: dungeon cave ruins desert
Mount Rokkon: dungeon mountain castle
Another Mount Rokkon: dungeon mountain castle
Aloalo Island: dungeon seaside jungle
Another Aloalo Island: dungeon seaside jungle
The Merchant's Tale: dungeon castle ruins
Another Merchant's Tale: dungeon castle ruins
Delubrum Reginae: dungeon castle ruins
The Aquapolis: dungeon water cave
The Lost Canals of Uznair: dungeon water cave
The Shifting Altars of Uznair: dungeon ruins cave
The Dungeons of Lyhe Ghiah: dungeon cave ruins
The Shifting Oubliettes of Lyhe Ghiah: dungeon cave
The Excitatron 6000: dungeon industrial
The Shifting Gymnasion Agonon: dungeon ruins
Cenote Ja Ja Gural: dungeon cave water
Vault Oneiron: dungeon cave eerie
"""

# Places deliberately not tagged (could not judge): Cutter's Cry, The Chrysalis, The Gilded Araya,
# Hells' Lid, Hells' Kier, The Burn, Chocobo Square, Blunderville, quest-battle-only instance names.
SKIPPED = ["Cutter's Cry", "The Chrysalis", "The Gilded Araya", "Hells' Lid", "Hells' Kier", "The Burn"]

def parse_places():
    d = {}
    for line in _P.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        name, rest = line.rsplit(": ", 1)   # names may themselves contain ': '
        parts = rest.split()
        kind, tags = parts[0], parts[1:]
        assert kind in ("town", "field", "dungeon"), line
        assert 1 <= len(tags) <= 4, line
        assert all(t in VOCAB for t in tags), line
        d[norm(name)] = (kind, tags)
    return d

def main():
    places = parse_places()
    ter_rows = fetch("TerritoryType.csv")
    pn_rows = fetch("PlaceName.csv")
    cf_rows = fetch("ContentFinderCondition.csv")
    th, ch = ter_rows[0], cf_rows[0]
    T = {k: th.index(k) for k in ("Name", "PlaceName", "TerritoryIntendedUse", "ExVersion")}
    pn = {r[0]: r[1] for r in pn_rows[1:]}
    ccol_t, ccol_type = ch.index("TerritoryType"), ch.index("ContentType")
    dungeon_types = {"2", "4", "5", "21"}
    cf_dungeon = set()
    for r in cf_rows[1:]:
        if r[ccol_type] in dungeon_types:
            cf_dungeon.add(r[ccol_t])

    out, uses, unmapped_open, unmapped_cf = {}, collections.defaultdict(collections.Counter), [], []
    for r in ter_rows[1:]:
        tid = r[0]
        if not r[T["Name"]]:
            continue
        name = norm(pn.get(r[T["PlaceName"]], ""))
        use = r[T["TerritoryIntendedUse"]]
        hit = places.get(name)
        if hit is None:
            if use in ("0", "1") and int(r[T["ExVersion"]]) <= 5 and name:
                unmapped_open.append((tid, name, use, r[T["ExVersion"]]))
            elif tid in cf_dungeon and name:
                unmapped_cf.append((tid, name))
            continue
        kind, tags = hit
        if tid in cf_dungeon:
            kind = "dungeon"
        out[tid] = {"name": name, "kind": kind, "tags": tags}
        uses[kind][use] += 1

    zm_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ost", "zone-moods.json")   # label-moods.py zones
    if os.path.exists(zm_file):
        zm = json.load(open(zm_file))
        for v in out.values():
            if v["name"] in zm: v["mood"], v["sub"] = zm[v["name"]]["mood"], zm[v["name"]]["sub"]
    os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(out.items(), key=lambda kv: int(kv[0]))), f, ensure_ascii=False, indent=1)
        f.write("\n")

    # ---- validation ----
    valid_ids = {r[0] for r in ter_rows[1:]}
    data = json.load(open(OUT, encoding="utf-8"))
    assert all(k in valid_ids for k in data), "unknown territory id"
    assert all(1 <= len(v["tags"]) <= 4 and set(v["tags"]) <= VOCAB for v in data.values()), "bad tags"
    print("territories:", len(data))
    print("per kind:", dict(collections.Counter(v["kind"] for v in data.values())))
    print("per tag:", dict(collections.Counter(t for v in data.values() for t in v["tags"]).most_common()))
    print("TerritoryIntendedUse seen per kind:", {k: dict(v) for k, v in uses.items()})
    print("unmapped open-world/town (use 0/1, ARR..Dawntrail):")
    for x in unmapped_open:
        print("  ", x)
    print("unmapped dungeon/trial/raid/deep-dungeon territories (no tag entry):")
    print("  ", sorted(set(n for _, n in unmapped_cf)))

if __name__ == "__main__":
    main()
