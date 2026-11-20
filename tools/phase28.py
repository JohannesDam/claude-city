"""Phase 28 - South health & office quarter (milestone 14 'Grand City'). The empty strip between the office
park and the old industry: hospital + medical university + health research institute + central
investigation bureau along z=-424, and a new office street (office demand 60, unemployment penalty)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import fit, get, paused, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, r):
    print(f"{label:<28} {short(r)}", flush=True)
    return r


UPPER = (-2320, -1140, -440, -410)      # the z=-424 road
MID = (-2320, -1140, -720, -680)        # the z=-700 road
EAST = (-1170, -1140, -950, -420)       # x=-1156 road

with paused():
    fit("medical university", "MedicalUniversity01", UPPER, near=(-1900, -424))
    fit("hospital south", "Hospital01", UPPER, near=(-1400, -424))
    fit("health research", "HealthResearchInstitute01", MID, near=(-1500, -700))
    fit("central investigation", "CentralInvestigationBureau01", MID, near=(-1900, -700))
    fit("park maintenance S", "ParkMaintenanceDepot01", EAST, near=(-1156, -600))
    fit("road maintenance S", "RoadMaintenanceDepot01", EAST, near=(-1156, -850))

    # office street between the z=-700 road and the industry
    step("office street z=-830", road("Small Road", [[-2200, -830], [-1950, -830], [-1700, -830], [-1450, -830], [-1156, -830]]))
    step("link x=-2200", road("Small Road", [[-2200, -700], [-2200, -830], [-2200, -940]]))
    step("link x=-1700", road("Small Road", [[-1700, -700], [-1700, -830]]))
    segs = [s["id"] for s in get("/nets", x=-1680, z=-830, radius=560, filter="Small Road")["segments"]
            if abs(s["start"][1] + 830) < 3 and abs(s["end"][1] + 830) < 3]
    step(f"offices z=-830 ({len(segs)})", zone("Office Low", mode="road", edges=segs, depth=6))
    mid = [s["id"] for s in get("/nets", x=-1680, z=-700, radius=600, filter="Road")["segments"]
           if abs(s["start"][1] + 700) < 15 and abs(s["end"][1] + 700) < 15]
    step(f"offices z=-700 ({len(mid)})", zone("Office Low", mode="road", edges=mid, depth=6, side="right"))
print(get("/status"))
