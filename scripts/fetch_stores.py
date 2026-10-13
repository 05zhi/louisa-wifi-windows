#!/usr/bin/env python3
"""Adapted from madeyexz/louisa-wifi (MIT, Ian Hsiao, 2026). No third-party dependencies."""
import argparse
import datetime
import hashlib
import html
import json
import math
import os
import re
import sys
import unicodedata
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "stores.json"
URL = "https://www.louisacoffee.co/visit_result"
QUERIES = [("name", "門市"), ("address", "路"), ("address", "街"), ("address", "號"), ("name", "")]

def normalize(value):
    return re.sub(r"\s+", "", unicodedata.normalize("NFKC", value).replace("臺", "台")).upper()

def stable_id(name, address):
    return hashlib.sha256((normalize(name) + "|" + normalize(address)).encode()).hexdigest()[:20]

def passwords(phone):
    first = re.split(r"[#轉,、/]|分機|ext", phone, flags=re.I)[0]
    d = re.sub(r"[^0-9]", "", first)
    if not d:
        return []
    candidates = [d[2:], d, d[1:]] if d.startswith("02") else [d[1:], d, d[-8:]]
    return list(dict.fromkeys(p for p in candidates if 8 <= len(p) <= 63))

def coord(raw):
    raw = html.unescape(raw).strip()
    try:
        number = float(raw or 0)
        return number if math.isfinite(number) else 0
    except ValueError:
        m = re.match(r"(\d+)°\s*(\d+)'\s*([\d.]+)", raw)
        return int(m[1]) + int(m[2]) / 60 + float(m[3]) / 3600 if m else 0

def parse(page):
    for block in page.split('<div class="row"><hr></div>'):
        name = re.search(r"<h4>(.*?)</h4>", block)
        if not name:
            continue
        def field(pattern):
            m = re.search(pattern, block)
            return html.unescape(m.group(1)).strip() if m else ""
        yield {"name": html.unescape(name.group(1)).strip(), "phone": field(r"<p>電話/(.*?)</p>"),
               "address": re.sub(r"\s+", " ", field(r'rel-store-address="(.*?)"')),
               "lat": coord(field(r'rel-store-lat="(.*?)"')), "lng": coord(field(r'rel-store-lng="(.*?)"'))}

def validate(dataset, previous=None):
    if dataset.get("schemaVersion") != 1 or not dataset.get("version"):
        raise ValueError("Unsupported data schema")
    stores = dataset.get("stores", [])
    if len(stores) < 400:
        raise ValueError("Suspiciously small store dataset; preserve previous data")
    if previous and len(stores) < len(previous["stores"]) * 0.85:
        raise ValueError("More than 15% stores disappeared; preserve previous data")
    seen = set()
    for s in stores:
        if not s.get("name") or not s.get("id") or s["id"] in seen:
            raise ValueError("Missing or duplicate identity")
        seen.add(s["id"])
        if not all(isinstance(s.get(key), str) for key in ("name", "address", "phone")):
            raise ValueError("Invalid store fields")
        if not (s["lat"] == s["lng"] == 0 or 21 < s["lat"] < 26.5 and 118 < s["lng"] < 122.5):
            raise ValueError("Invalid coordinates")
        if not isinstance(s.get("passwords"), list) or any(not isinstance(p, str) or not 8 <= len(p) <= 63 or not all(32 <= ord(c) <= 126 for c in p) for p in s["passwords"]):
            raise ValueError("Invalid password candidates")
        if len(s["passwords"]) != len(set(s["passwords"])):
            raise ValueError("Duplicate candidates")

def fetch(field, value):
    body = urllib.parse.urlencode({f"data[{field}]": value}).encode()
    request = urllib.request.Request(URL, data=body, headers={"User-Agent": "louisa-wifi-windows"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read().decode("utf-8")

def refresh(previous):
    found = {}
    for field, value in QUERIES:
        for store in parse(fetch(field, value)):
            # Same store name at a different address is a different branch.
            found.setdefault((normalize(store["name"]), normalize(store["address"])), store)
        print(f"Fetched {len(found)} stores", file=sys.stderr)
    existing = previous["stores"]
    output = []
    used_ids = set()
    for s in sorted(found.values(), key=lambda s: (s["name"], s["address"])):
        if not (21 < s["lat"] < 26.5 and 118 < s["lng"] < 122.5):
            s["lat"] = s["lng"] = 0
        s["lat"] = round(s["lat"], 6); s["lng"] = round(s["lng"], 6)
        exact = [old for old in existing if normalize(old["name"]) == normalize(s["name"]) and normalize(old["address"]) == normalize(s["address"])]
        # Keep identity through a spelling change only when address + phone identify one branch.
        similar = [old for old in existing if normalize(s["address"]) and normalize(old["address"]) == normalize(s["address"]) and passwords(old["phone"]) and passwords(old["phone"]) == passwords(s["phone"])]
        match = exact if len(exact) == 1 else similar
        s["id"] = match[0]["id"] if len(match) == 1 and match[0]["id"] not in used_ids else stable_id(s["name"], s["address"])
        used_ids.add(s["id"])
        s["passwords"] = passwords(s["phone"])
        output.append(s)
    result = {"schemaVersion": 1, "version": datetime.date.today().isoformat(), "stores": output}
    validate(result, previous)
    return result

def main():
    parser = argparse.ArgumentParser(); parser.add_argument("--validate", action="store_true"); args = parser.parse_args()
    previous = json.loads(OUT.read_text(encoding="utf-8")); validate(previous)
    if args.validate:
        print(f"Validated {len(previous['stores'])} stores / {previous['version']}"); return
    result = refresh(previous)
    if result["stores"] == previous["stores"]:
        print("Store data unchanged"); return
    temporary = OUT.with_suffix(".json.tmp")
    try:
        temporary.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        os.replace(temporary, OUT)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Wrote {len(result['stores'])} validated stores")

if __name__ == "__main__":
    main()
