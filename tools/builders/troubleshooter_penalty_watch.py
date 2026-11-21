"""TrafficPenalty watch: samples the TrafficPenalty factor, penalised citizens (GET /stuck) per district, district
residents (GET /districts) and the daily move-in count, every --every seconds for --n samples, and appends one JSON
line per sample to logs/council/penalty_watch.jsonl.

Purpose: tell move-in failures (households arriving from outside connections whose first trip home fails and who get
teleported home with m_PenaltyCounter = 255) apart from commute failures. If a growing district's penalised count
rises about as fast as its residents, its move-ins are failing.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_penalty_watch.py --n 6 --every 90
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get  # noqa: E402

LOG = r"C:\soft\cityskyline\logs\council\penalty_watch.jsonl"


def arg(name, default):
    if name in sys.argv:
        return type(default)(sys.argv[sys.argv.index(name) + 1])
    return default


def district_of(x, z, boxes):
    for name, b in boxes:
        if b[0] <= x <= b[2] and b[1] <= z <= b[3]:
            return name
    return "other"


def sample(boxes):
    city = get("/city")
    hap = get("/happiness")
    tp = next(f["x"] for f in hap["factors"] if f["factor"] == "TrafficPenalty")
    st = get("/stuck", cell=256, limit=100)
    cit = st["citizens"]
    pen = {}
    for c in cit["byHomeCell"]:
        d = district_of(c["centre"][0], c["centre"][1], boxes)
        pen[d] = pen.get(d, 0) + c["count"]
    dist = get("/districts")
    dl = dist.get("districts") if isinstance(dist, dict) else dist
    res = {d.get("name"): d.get("residents") for d in dl}
    mv = get("/stats", names="CitizensMovedIn", n=1)["stats"]["CitizensMovedIn"]["value"]
    moved = mv["current"] if isinstance(mv, dict) else mv
    return {"date": city["date"], "population": city["population"], "trafficPenalty": tp,
            "penalised": cit["withPenalty"], "above128": cit["penaltyAbove128"], "movedInPerDay": moved,
            "penalisedByDistrict": dict(sorted(pen.items(), key=lambda t: -t[1])), "residents": res}


def main():
    n = arg("--n", 1)
    every = arg("--every", 90)
    reg = json.load(open(r"C:\soft\cityskyline\docs\council\districts.json"))["districts"]
    # smaller boxes first so nested districts win
    boxes = sorted([(d.get("gameDistrict") or d["name"], d["bbox"]) for d in reg],
                   key=lambda t: (t[1][2] - t[1][0]) * (t[1][3] - t[1][1]))
    for i in range(n):
        s = sample(boxes)
        with open(LOG, "a") as f:
            f.write(json.dumps(s) + "\n")
        top = list(s["penalisedByDistrict"].items())[:8]
        print(s["date"], "TP", s["trafficPenalty"], "pen", s["penalised"], ">128", s["above128"],
              "movedIn/day", s["movedInPerDay"], top, flush=True)
        if i < n - 1:
            time.sleep(every)


if __name__ == "__main__":
    main()
