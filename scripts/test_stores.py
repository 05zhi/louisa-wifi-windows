import copy
import json
import unittest
import fetch_stores as f

class StoresTests(unittest.TestCase):
    def test_phone_formats(self):
        self.assertEqual(f.passwords("(02)2345-8613分機12"), ["23458613", "0223458613", "223458613"])
        self.assertEqual(f.passwords("03-5555555/02-23458613"), ["35555555", "035555555"])
        self.assertEqual(f.passwords(""), [])
    def test_parse_missing_fields_and_coordinates(self):
        rows = list(f.parse('<h4>測試店</h4><p>電話/02-23458613</p><a rel-store-address="臺北市 信義區" rel-store-lat="bad" rel-store-lng="NaN">'))
        self.assertEqual(rows[0]["lat"], 0); self.assertEqual(rows[0]["lng"], 0)
    def test_identity(self):
        self.assertEqual(f.stable_id("臺北 店", "信義路"), f.stable_id("台北店", "信義路"))
        self.assertNotEqual(f.stable_id("同名", "地址1"), f.stable_id("同名", "地址2"))
    def test_dataset_and_shrink_guard(self):
        data = json.loads(f.OUT.read_text(encoding="utf-8")); f.validate(data)
        broken = copy.deepcopy(data); broken["stores"] = broken["stores"][:400]
        with self.assertRaises(ValueError): f.validate(broken, data)
        broken = copy.deepcopy(data); broken["stores"][0]["lat"] = 999
        with self.assertRaises(ValueError): f.validate(broken)
        broken = copy.deepcopy(data); broken["stores"][1]["id"] = broken["stores"][0]["id"]
        with self.assertRaises(ValueError): f.validate(broken)

if __name__ == "__main__": unittest.main()
