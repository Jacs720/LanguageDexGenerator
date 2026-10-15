import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from preview_plan import read_catalog, read_languages, rows


class LanguageDexPlanTests(unittest.TestCase):
    def test_ranges(self):
        self.assertEqual(read_catalog(), {'swsh': (810, 898), 'pla': (899, 905), 'sv': (906, 1025)})

    def test_original_languages_only(self):
        ids = read_languages()
        self.assertEqual(sorted(ids), [1, 2, 3, 4, 5, 7, 8, 9, 10])
        self.assertNotIn(11, ids)

    def test_species_count(self):
        self.assertEqual(len(rows(['swsh'], [1])), 89)
        self.assertEqual(len(rows(['pla'], [1])), 7)
        self.assertEqual(len(rows(['sv'], [1])), 120)

    def test_all_combinations_unique(self):
        data = rows(['swsh', 'pla', 'sv'], [1, 2, 3, 4, 5, 7, 8, 9, 10])
        self.assertEqual(len(data), 216 * 9)
        self.assertEqual(len({(r['game'], r['species'], r['language_id']) for r in data}), len(data))
        self.assertNotIn(11, [r['language_id'] for r in data])

    def test_partial_configuration(self):
        self.assertEqual(len(rows(['swsh', 'sv'], [1, 2])), (89 + 120) * 2)

    def test_invalid_requests(self):
        for games, langs in [(['za'], [1]), (['swsh'], [11]), (['swsh'], [6]),
                             (['swsh', 'swsh'], [1]), (['sv'], [1, 1])]:
            with self.subTest(games=games, langs=langs):
                with self.assertRaises(ValueError):
                    rows(games, langs)


if __name__ == '__main__':
    unittest.main()
