"""Checks every level JSON: original block shapes, no overlaps, and that the solved layout lets every cat
get home (BFS routing, frozen cats, keys) exactly like the game.
Usage: python Tools/check_levels.py Assets/CatPipe/Resources/Levels"""
import glob, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from catpipe_logic import validate

bad = 0
files = sorted(glob.glob(sys.argv[1] + "/level_*.json"))
for f in files:
    errors = validate(json.load(open(f, encoding="utf-8")))
    if errors:
        bad += 1
        print(os.path.basename(f), "; ".join(errors))
print(f"{len(files) - bad}/{len(files)} OK" + ("" if bad else " - ALL OK"))
