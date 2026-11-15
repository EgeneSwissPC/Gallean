#!/usr/bin/env python3
"""Build a large Gallean test scenario from an existing one.

The source scenario is placed unchanged (players, cities, stacks, events) in the middle of a SIZE x SIZE map.
The rest of the map repeats the source terrain as neutral land, together with copies of its mountains,
landmarks, roads and resource crystals (objects without cross references), so the whole map has content.

Usage: generate_big_map.py <RiseOfTheElves dir> [--source S216SC0000] [--prefix S900] [--size 512] [--name ...]
Writes the new scenario into the given RiseOfTheElves directory (use a copy, e.g. a published build).
"""
import argparse
import copy
import json
import os
import sys

TILE_BLOCK_W, TILE_BLOCK_H = 8, 4
NEUTRAL = 5
RACE_TERRAINS = {1, 2, 3, 4, 6}


def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def save(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def sixbit_decode(text):
    out, bits, count = bytearray(), 0, 0
    for ch in text:
        bits = (bits << 6) | (ord(ch) - 33)
        count += 6
        if count >= 8:
            count -= 8
            out.append((bits >> count) & 0xFF)
    return bytes(out)


def sixbit_encode(data, length):
    bits, count, out = 0, 0, []
    for byte in data:
        bits = (bits << 8) | byte
        count += 8
        while count >= 6:
            count -= 6
            out.append(chr(33 + ((bits >> count) & 0x3F)))
    if count:
        out.append(chr(33 + ((bits << (6 - count)) & 0x3F)))
    while len(out) < length:
        out.append('!')
    return ''.join(out)


def rename(value, old, new):
    """Replace the scenario prefix in every string ID (S216... -> S900...)."""
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
    ap.add_argument('--prefix', default='S900')
    ap.add_argument('--size', type=int, default=512)
    ap.add_argument('--name', default=None)
    a = ap.parse_args()

    root, src_id, size = a.root, a.source, a.size
    old, new = src_id[:4], a.prefix
    new_id = new + src_id[4:]
    scens = os.path.join(root, 'Scens')
    src_dir = os.path.join(scens, src_id)
    dst_dir = os.path.join(scens, new_id)
    assert size % TILE_BLOCK_W == 0 and size % TILE_BLOCK_H == 0 and size <= 4096

    info_file = load(os.path.join(scens, 'Sinfo.json'))
    src_info = next(r for r in info_file['rows'] if r['NO_SCEN'] == src_id)
    n = src_info['MAP_SIZE']
    off = (size - n) // 2

    tables = {f: rename(load(os.path.join(src_dir, f)), old, new) for f in os.listdir(src_dir) if f.endswith('.json')}
    rows = lambda f: tables[f]['rows'] if f in tables else []

    def in_center(x, y):
        return off <= x < off + n and off <= y < off + n

    # Terrain: decode the source map, tile it across the new map (race land outside the centre becomes neutral).
    src_tiles = [[0] * n for _ in range(n)]
    block_len = None
    for b in rows('SMapBlck.json'):
        block_len = len(b['BLOCKDATA'])
        y0, x0 = int(b['BLOCKID'][6:8], 16), int(b['BLOCKID'][8:10], 16)
        raw = sixbit_decode(b['BLOCKDATA'])
        for i in range(TILE_BLOCK_W * TILE_BLOCK_H):
            x, y = x0 + i % TILE_BLOCK_W, y0 + i // TILE_BLOCK_W
            if x < n and y < n:
                src_tiles[y][x] = int.from_bytes(raw[i * 4:i * 4 + 4], 'little')
    # Round-trip check of the encoder against the original data.
    b0 = rows('SMapBlck.json')[0]
    assert sixbit_encode(sixbit_decode(b0['BLOCKDATA'])[:128], block_len) == b0['BLOCKDATA'], 'sixbit encoder mismatch'

    blocks = []
    for by in range(0, size, TILE_BLOCK_H):
        for bx in range(0, size, TILE_BLOCK_W):
            data = bytearray()
            for i in range(TILE_BLOCK_W * TILE_BLOCK_H):
                x, y = bx + i % TILE_BLOCK_W, by + i // TILE_BLOCK_W
                v = src_tiles[(y - off) % n][(x - off) % n]
                if not in_center(x, y) and (v & 7) in RACE_TERRAINS:
                    v = (v & ~7) | NEUTRAL
                data += v.to_bytes(4, 'little')
            blocks.append({'NO_SCEN': new_id, 'BLOCKID': f'{new}MB{by:03x}{bx:03x}', 'BLOCKDATA': sixbit_encode(data, block_len)})
    tables['SMapBlck.json']['rows'] = blocks

    # Centre: move every positioned object of the source scenario by the offset.
    for f, t in tables.items():
        for r in t['rows']:
            if 'POS_X' in r and 'POS_Y' in r and r['POS_X'] is not None and r['POS_Y'] is not None:
                r['POS_X'] += off
                r['POS_Y'] += off

    # Copies of standalone objects for every other tile of the repeated terrain.
    counters = {}

    def next_id(kind, existing):
        if kind not in counters:
            counters[kind] = max((int(i[6:10], 16) for i in existing), default=-1) + 1
        value = counters[kind]
        counters[kind] += 1
        assert value <= 0xFFFF, f'out of {kind} IDs'
        return f'{new}{kind}{value:04x}'

    mapel = rows('SMapEl.json')
    mapel_by_element = {}
    for r in mapel:
        mapel_by_element.setdefault(r['ELEMENT'], []).append(r)

    crystals, landmarks, roads, mounts = rows('SCrys.json'), rows('SLmark.json'), rows('SRoad.json'), rows('SMounts.json')
    objmap = rows('SObjMap.json')
    what = {r['OBJ_ID'][4:6]: r['WHAT'] for r in objmap}
    objmap = [r for r in objmap if r['OBJ_ID'][4:6] != 'MB']
    objmap += [{'NO_SCEN': new_id, 'OBJ_ID': b['BLOCKID'], 'WHAT': what['MB']} for b in blocks]
    new_mapel, new_objs = [], []
    next_mount = max(m['ID_MOUNT'] for m in mounts) + 1 if mounts else 1
    src_crystals, src_landmarks, src_roads, src_mounts = list(crystals), list(landmarks), list(roads), list(mounts)
    all_crys_ids = [c['CRYSTAL_ID'] for c in crystals]
    all_lm_ids = [l['LMARK_ID'] for l in landmarks]
    all_road_ids = [r['ROAD_ID'] for r in roads]

    k_range = range(-(off // n) - 1, (size - off) // n + 1)
    copies = 0
    for ky in k_range:
        for kx in k_range:
            if kx == 0 and ky == 0:
                continue
            dx, dy = kx * n, ky * n
            fits = lambda x, y: 0 <= x < size and 0 <= y < size and not in_center(x, y)
            placed = False

            def copy_object(src, id_key, kind, existing_ids, target):
                cells = mapel_by_element.get(src[id_key], [])
                points = [(src['POS_X'] + dx, src['POS_Y'] + dy)] + [(c['POS_X'] + dx, c['POS_Y'] + dy) for c in cells]
                if not all(fits(x, y) for x, y in points):
                    return False
                obj = copy.deepcopy(src)
                obj[id_key] = next_id(kind, existing_ids)
                obj['POS_X'] += dx
                obj['POS_Y'] += dy
                target.append(obj)
                for c in cells:
                    new_mapel.append({**c, 'POS_X': c['POS_X'] + dx, 'POS_Y': c['POS_Y'] + dy, 'ELEMENT': obj[id_key]})
                new_objs.append({'NO_SCEN': new_id, 'OBJ_ID': obj[id_key], 'WHAT': what[kind]})
                return True

            for c in src_crystals:
                placed |= copy_object(c, 'CRYSTAL_ID', 'CR', all_crys_ids, crystals)
            for l in src_landmarks:
                placed |= copy_object(l, 'LMARK_ID', 'MM', all_lm_ids, landmarks)
            for r in src_roads:
                placed |= copy_object(r, 'ROAD_ID', 'RA', all_road_ids, roads)
            for m in src_mounts:
                x, y = m['POS_X'] + dx, m['POS_Y'] + dy
                if fits(x, y) and fits(x + m['SIZE_X'] - 1, y + m['SIZE_Y'] - 1) and \
                        not any(in_center(xx, yy) for xx in range(x, x + m['SIZE_X']) for yy in range(y, y + m['SIZE_Y'])):
                    mounts.append({**m, 'ID_MOUNT': next_mount, 'POS_X': x, 'POS_Y': y})
                    next_mount += 1
                    placed = True
            copies += placed

    tables['SMapEl.json']['rows'] = mapel + new_mapel
    tables['SObjMap.json']['rows'] = objmap + new_objs

    # Fog rows are sized to the map width (6 bits per character); the loader starts with fresh fog anyway.
    if 'Sfog.json' in tables:
        fog_ids = sorted({r['FOG_ID'] for r in rows('Sfog.json')})
        width = (size + 5) // 6
        tables['Sfog.json']['rows'] = [{'NO_SCEN': new_id, 'FOG_ID': f, 'POS_Y': y, 'FOG': '!' * width} for f in fog_ids for y in range(size)]

    for f, t in tables.items():
        save(os.path.join(dst_dir, f), t)

    name = a.name or f'{src_info["NAME"].strip()} {size}x{size}'

    def info_row(src_row):
        row = rename(copy.deepcopy(src_row), old, new)
        row['MAP_SIZE'] = size
        row['NAME'] = name
        return row

    info_file['rows'] = [r for r in info_file['rows'] if r['NO_SCEN'] != new_id] + [info_row(src_info)]
    save(os.path.join(scens, 'Sinfo.json'), info_file)

    # Localized scenario texts and names, where the source has them.
    lang_root = os.path.join(root, 'Languages')
    for lang in sorted(os.listdir(lang_root)) if os.path.isdir(lang_root) else []:
        lscens = os.path.join(lang_root, lang, 'Scens')
        lsrc = os.path.join(lscens, src_id)
        if os.path.isdir(lsrc):
            for f in os.listdir(lsrc):
                save(os.path.join(lscens, new_id, f), rename(load(os.path.join(lsrc, f)), old, new))
        linfo_path = os.path.join(lscens, 'Sinfo.json')
        if os.path.isfile(linfo_path):
            linfo = load(linfo_path)
            src_row = next((r for r in linfo['rows'] if r['NO_SCEN'] == src_id), None)
            if src_row:
                linfo['rows'] = [r for r in linfo['rows'] if r['NO_SCEN'] != new_id] + [info_row(src_row)]
                save(linfo_path, linfo)

    # Campaign scenario list (used for ordering and scenario metadata).
    cscen_path = os.path.join(root, 'Campaign', 'CScen.json')
    cscen = load(cscen_path)
    src_c = next(r for r in cscen['rows'] if r['SCEN_ID'] == src_id)
    others = [r for r in cscen['rows'] if r['SCEN_ID'] != new_id]
    # Every listed scenario needs an order number within its campaign, or the quest list fails to load.
    order = max((r['SCEN_NO'] for r in others if r['CAMP_ID'] == src_c['CAMP_ID'] and r['SCEN_NO'] is not None), default=0) + 1
    cscen['rows'] = others + [{**src_c, 'SCEN_ID': new_id, 'SCEN_NO': order}]
    save(cscen_path, cscen)

    print(f'{new_id}: {size}x{size}, source {src_id} ({n}x{n}) at offset {off}; {len(blocks)} map blocks, '
          f'{copies} terrain copies, {len(crystals)} crystals, {len(landmarks)} landmarks, {len(roads)} roads, '
          f'{len(mounts)} mountains, {len(tables["SMapEl.json"]["rows"])} map elements')


if __name__ == '__main__':
    sys.exit(main())
