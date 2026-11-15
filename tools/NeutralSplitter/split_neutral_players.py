#!/usr/bin/env python3
"""Split a scenario's neutral player into several independent neutral players.

The original game has a single neutral player owning every monster, guard and neutral city, split only into
subraces (Greenskins, Barbarians, Dragons, ...). This tool copies a scenario under a new ID and gives each neutral
subrace type its own neutral player: separate turns, separate AI, hostile to each other. Custom-banner and plain
neutral subraces stay with the primary neutral player (the first one), which also keeps all scenario events.

Usage: split_neutral_players.py <RiseOfTheElves dir> [--source S216SC0000] [--prefix S901] [--name ...]
                                [--max-players N]
Writes into the given RiseOfTheElves directory (use a copy, e.g. a published build).
"""
import argparse
import collections
import copy
import json
import os
import sys

NEUTRAL_RACE = 'g000rr0004'
NEUTRAL_RACE_TYPE = 4
UNSET_PLAYER = 99
SUBRACE_NAMES = {
    6: 'Neutral Humans', 7: 'Neutral Elves', 8: 'Greenskins', 9: 'Dragons',
    10: 'Marsh Creatures', 11: 'Water Creatures', 12: 'Barbarians', 13: 'Wolves',
}


def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def save(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def rename(value, old, new):
    if isinstance(value, str):
        return new + value[len(old):] if value.startswith(old) else value
    if isinstance(value, list):
        return [rename(v, old, new) for v in value]
    if isinstance(value, dict):
        return {k: rename(v, old, new) for k, v in value.items()}
    return value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('root')
    ap.add_argument('--source', default='S216SC0000')
    ap.add_argument('--prefix', default='S901')
    ap.add_argument('--name', default=None)
    ap.add_argument('--max-players', type=int, default=12, help='maximum number of extra neutral players')
    a = ap.parse_args()

    root, src_id = a.root, a.source
    old, new = src_id[:4], a.prefix
    new_id = new + src_id[4:]
    scens = os.path.join(root, 'Scens')
    src_dir = os.path.join(scens, src_id)

    tables = {f: rename(load(os.path.join(src_dir, f)), old, new) for f in os.listdir(src_dir) if f.endswith('.json')}
    rows = lambda f: tables[f]['rows'] if f in tables else []

    players, subraces = rows('Splayer.json'), rows('SSubRace.json')
    stacks, forts = rows('Sstack.json'), rows('SFort.json')
    primary = next(p for p in players if p['RACE_ID'].lower() == NEUTRAL_RACE)
    primary_id = primary['PLAYER_ID']

    # Neutral subrace types that actually own something, most populous first.
    used = collections.Counter(s['SUBRACE'] for s in stacks + forts if s['OWNER'] == primary_id)
    by_type = collections.defaultdict(list)
    for s in subraces:
        if s['PLAYER_ID'] == primary_id and s['SUBRACE'] in SUBRACE_NAMES:
            by_type[s['SUBRACE']].append(s)
    counts = {t: sum(used[s['SUBRACE_ID']] for s in subs) for t, subs in by_type.items()}
    groups = [t for t in sorted(counts, key=lambda t: (-counts[t], t)) if counts[t] > 0][:a.max_players]

    def next_index(kind):
        ids = [v for f in tables.values() for r in f['rows'] for v in r.values()
               if isinstance(v, str) and len(v) >= 10 and v[4:6].upper() == kind]
        return max((int(i[6:10], 16) for i in ids if i.startswith(new)), default=-1) + 1

    next_ids = {kind: next_index(kind) for kind in ('PL', 'FG', 'PB', 'KS')}

    def new_object_id(kind):
        value = next_ids[kind]
        next_ids[kind] += 1
        return f'{new}{kind}{value:04x}'

    objmap = rows('SObjMap.json')
    what = {r['OBJ_ID'][4:6].upper(): r['WHAT'] for r in objmap}
    fog_rows = [r for r in rows('Sfog.json') if r['FOG_ID'] == primary['FOG_ID']]
    texts = rows('Tscen.json')
    created = []

    for subrace_type in groups:
        player = copy.deepcopy(primary)
        player['PLAYER_ID'] = new_object_id('PL')
        for key, kind in (('FOG_ID', 'FG'), ('BUILDS_ID', 'PB'), ('KNOWN_ID', 'KS')):
            if player.get(key):
                player[key] = new_object_id(kind)
                objmap.append({'NO_SCEN': new_id, 'OBJ_ID': player[key], 'WHAT': what.get(kind, '')})
        objmap.append({'NO_SCEN': new_id, 'OBJ_ID': player['PLAYER_ID'], 'WHAT': what['PL']})
        players.append(player)
        if 'Sfog.json' in tables:
            tables['Sfog.json']['rows'] += [{**r, 'FOG_ID': player['FOG_ID']} for r in fog_rows]

        name = SUBRACE_NAMES[subrace_type]
        texts.append({'NO_SCEN': new_id, 'ID_OBJECT': player['PLAYER_ID'], 'ID_STRING': 'NAME_TXT', 'TEXT': name, 'SEQ_NUM': 0})
        texts.append({'NO_SCEN': new_id, 'ID_OBJECT': player['PLAYER_ID'], 'ID_STRING': 'DESC_TXT', 'TEXT': f'Independent {name.lower()}', 'SEQ_NUM': 0})

        subrace_ids = {s['SUBRACE_ID'] for s in by_type[subrace_type]}
        for s in by_type[subrace_type]:
            s['PLAYER_ID'] = player['PLAYER_ID']
        for obj in stacks + forts:
            if obj['OWNER'] == primary_id and obj['SUBRACE'] in subrace_ids:
                obj['OWNER'] = player['PLAYER_ID']
        created.append((player['PLAYER_ID'], name, len(subrace_ids)))

    # A stack inside a city belongs to the city's side.
    fort_by_id = {f['CITY_ID']: f for f in forts}
    moved_inside = 0
    for s in stacks:
        fort = fort_by_id.get(s.get('INSIDE'))
        if fort and s['OWNER'] != fort['OWNER'] and fort['OWNER'] in {p for p, _, _ in created} | {primary_id} \
                and s['OWNER'] in {p for p, _, _ in created} | {primary_id}:
            s['OWNER'], s['SUBRACE'] = fort['OWNER'], fort['SUBRACE']
            moved_inside += 1

    dst_dir = os.path.join(scens, new_id)
    for f, t in tables.items():
        save(os.path.join(dst_dir, f), t)

    info_file = load(os.path.join(scens, 'Sinfo.json'))
    src_info = next(r for r in info_file['rows'] if r['NO_SCEN'] == src_id)

    def info_row(src_row):
        row = rename(copy.deepcopy(src_row), old, new)
        row['NAME'] = a.name or f'{src_row["NAME"].strip()} (split neutrals)'
        free = [i for i in range(1, 14) if row[f'PLAYER_{i}'] == UNSET_PLAYER]
        for i, _ in zip(free, created):
            row[f'PLAYER_{i}'] = NEUTRAL_RACE_TYPE
        return row

    info_file['rows'] = [r for r in info_file['rows'] if r['NO_SCEN'] != new_id] + [info_row(src_info)]
    save(os.path.join(scens, 'Sinfo.json'), info_file)

    lang_root = os.path.join(root, 'Languages')
    for lang in sorted(os.listdir(lang_root)) if os.path.isdir(lang_root) else []:
        lscens = os.path.join(lang_root, lang, 'Scens')
        lsrc = os.path.join(lscens, src_id)
        if os.path.isdir(lsrc):
            for f in os.listdir(lsrc):
                data = rename(load(os.path.join(lsrc, f)), old, new)
                if f == 'Tscen.json':
                    data['rows'] += [t for t in texts if t['ID_OBJECT'] in {p for p, _, _ in created}]
                save(os.path.join(lscens, new_id, f), data)
        linfo_path = os.path.join(lscens, 'Sinfo.json')
        if os.path.isfile(linfo_path):
            linfo = load(linfo_path)
            src_row = next((r for r in linfo['rows'] if r['NO_SCEN'] == src_id), None)
            if src_row:
                linfo['rows'] = [r for r in linfo['rows'] if r['NO_SCEN'] != new_id] + [info_row(src_row)]
                save(linfo_path, linfo)

    cscen_path = os.path.join(root, 'Campaign', 'CScen.json')
    cscen = load(cscen_path)
    src_c = next(r for r in cscen['rows'] if r['SCEN_ID'] == src_id)
    others = [r for r in cscen['rows'] if r['SCEN_ID'] != new_id]
    order = max((r['SCEN_NO'] for r in others if r['CAMP_ID'] == src_c['CAMP_ID'] and r['SCEN_NO'] is not None), default=0) + 1
    cscen['rows'] = others + [{**src_c, 'SCEN_ID': new_id, 'SCEN_NO': order}]
    save(cscen_path, cscen)

    remaining = sum(1 for s in stacks if s['OWNER'] == primary_id)
    print(f'{new_id} from {src_id}: primary neutral {primary_id} keeps {remaining} stacks; '
          f'{len(created)} new neutral players: ' + ', '.join(f'{n} ({p})' for p, n, _ in created) +
          (f'; {moved_inside} stacks inside cities moved to the city owner' if moved_inside else ''))


if __name__ == '__main__':
    sys.exit(main())
