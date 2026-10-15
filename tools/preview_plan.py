#!/usr/bin/env python3
"""Offline planning tool (no PKHeX dependencies). Reads the C# catalog as source of truth.

Usage:
 python tools/preview_plan.py --output plan.csv
 python tools/preview_plan.py --games swsh,sv --langs 1,2,3 --output plan.csv
"""
from __future__ import annotations
import argparse
import csv
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[1]
MODELS = ROOT / 'LanguageDexGenerator' / 'Models.cs'


def read_catalog():
    source = MODELS.read_text(encoding='utf-8-sig')
    profiles = {}
    pattern = r'new\("(swsh|pla|sv)",\s*"[^"]+",\s*(\d+),\s*(\d+),'
    for key, lo, hi in re.findall(pattern, source):
        profiles[key] = (int(lo), int(hi))
    if len(profiles) != 3:
        raise RuntimeError('C# DexCatalog unexpectedly changed; update planner explicitly')
    return profiles


def read_languages():
    source = MODELS.read_text(encoding='utf-8-sig')
    head = source.split('public sealed record GameProfile')[0]
    return {int(i): code for i, code in re.findall(r'\(\s*(\d+),\s*"([\w-]+)",\s*"[^"]+"\s*\)', head)}


def rows(games, langs):
    profiles = read_catalog()
    ids = read_languages()
    if not games or any(g not in profiles for g in games):
        raise ValueError('Invalid game list')
    if not langs or any(language not in ids or language == 11 for language in langs):
        raise ValueError('Invalid language list; ES-LA is prohibited')
    if len(set(games)) != len(games) or len(set(langs)) != len(langs):
        raise ValueError('Duplicates not allowed')
    return [{'game': g, 'species': species, 'language_id': lang, 'language': ids[lang]}
            for g in games for lang in langs for species in range(profiles[g][0], profiles[g][1] + 1)]


def main():
    parser = argparse.ArgumentParser(description='Create offline manifest of planned Pokémon/language pairs')
    parser.add_argument('--games', default='swsh,pla,sv', help='Comma-separated game groups')
    parser.add_argument('--langs', default='1,2,3,4,5,7,8,9,10', help='Original language IDs')
    parser.add_argument('--output', default='plan.csv', help='Output CSV file')
    args = parser.parse_args()
    items = rows(args.games.split(','), [int(x) for x in args.langs.split(',')])
    with open(args.output, 'w', newline='', encoding='utf-8-sig') as f:
        w = csv.DictWriter(f, fieldnames=['game', 'species', 'language_id', 'language'])
        w.writeheader()
        w.writerows(items)
    print(f'Created {len(items)} planned pairs -> {args.output}')


if __name__ == '__main__':
    main()
