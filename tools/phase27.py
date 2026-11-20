"""Phase 27 - University quarter. Two new map tiles north of the office park (bought for ~$0.55M) get a
University Avenue (z=4250) with the technical university, university and college on the north side and
city hall, police headquarters, welfare office and small parks on the south side."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import fit, get, paused, road, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, r):
    print(f"{label:<28} {short(r)}", flush=True)
    return r


NORTH = (-2180, -940, 4245, 4680)   # segments on the avenue (both sides are tried)
AVE = (-2180, -940, 4200, 4300)
SOUTH = (-2180, -940, 3700, 4250)

with paused():
    step("avenue z=4250", road("Medium Road", [[-2150, 4250], [-1909, 4250], [-1689, 4250], [-1469, 4250], [-1249, 4250], [-1000, 4250]]))
    step("link x=-1909", road("Small Road", [[-1909, 3900], [-1909, 4075], [-1909, 4250]]))
    step("link x=-1469", road("Small Road", [[-1469, 3900], [-1469, 4075], [-1469, 4250]]))
    step("link x=-1249", road("Small Road", [[-1249, 3700], [-1249, 3900], [-1249, 4075], [-1249, 4250]]))

    fit("technical university", "TechnicalUniversity01", AVE, near=(-1950, 4250))
    fit("university", "University01", AVE, near=(-1550, 4250))
    fit("college", "College01", AVE, near=(-1150, 4250))
    fit("medical university", "MedicalUniversity01", AVE, near=(-1300, 4250))
    fit("city hall", "CityHall01", SOUTH, near=(-1689, 4150))
    fit("police headquarters", "PoliceHeadquarters01", SOUTH, near=(-1300, 4100))
    fit("welfare office", "WelfareOffice01", SOUTH, near=(-2050, 4100))
    for prefab, near in (("CityPark10", (-1580, 4100)), ("CityPark08", (-1800, 4000)), ("DogPark02", (-1350, 3950)),
                         ("CityPark09", (-2000, 3950)), ("BasketballCourt04", (-1100, 4100)), ("TelecomTower01", (-2100, 3800))):
        fit(prefab, prefab, SOUTH, near=near)
print(get("/status"))
