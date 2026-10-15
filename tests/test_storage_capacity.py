import unittest

class StorageCapacityTests(unittest.TestCase):
    def test_eight_missing_languages_fit_all_games(self):
        for species in (89, 7, 120):
            pokemon = species * 8
            boxes_needed = (pokemon + 29) // 30
            self.assertLessEqual(boxes_needed, 32)
        self.assertEqual(89 * 8, 712)
        self.assertEqual(7 * 8, 56)
        self.assertEqual(120 * 8, 960)

    def test_nine_languages_exceed_sv_capacity(self):
        self.assertEqual(120 * 9, 1080)
        self.assertGreater(120 * 9, 960)

    def test_storage_file_payload_bytes(self):
        self.assertEqual(712 * 328 + 56 * 360 + 960 * 328, 568576)

if __name__ == '__main__':
    unittest.main()
