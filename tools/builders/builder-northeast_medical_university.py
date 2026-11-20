"""builder-northeast - b1-education-medical-university: Medical University on the University Hill north strip.

Medical Drive: tree-lined Medium Road from the east end of the z=4250 campus road (-1000,4250) north and west
along the strip at z~4502 (clear of the University01 and TechnicalUniversity01 lots). MedicalUniversity01
(496 x 144 m) fronts it on the north side, lot x -1598..-1102, z 4515..4659.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_medical_university.json"
DRIVE = [[-1000, 4250], [-975, 4390], [-1060, 4488], [-1300, 4502], [-1700, 4502]]
UNI = {"prefab": "MedicalUniversity01", "x": -1350, "z": 4587, "angle": 180}
out = {}
with paused("builder-northeast"):
    out["drive"] = post("/road", {"prefab": "Medium Road", "points": DRIVE, "curve": "smooth"})
    print("drive", short(out["drive"]))
    d = post("/building", {**UNI, "dryRun": True})
    print("uni dry", short(d))
    if d.get("errors") == 0 and not d.get("wouldDemolish"):
        out["uni"] = post("/building", UNI)
        print("uni", short(out["uni"]))
    out["trees"] = post("/roadupgrade", {"polyline": DRIVE, "tolerance": 15, "prefabFilter": "Medium Road",
                                         "upgrade": "Trees", "side": "both"})
    print("trees", short(out["trees"]))
json.dump(out, open(LOG, "w"), indent=1)
