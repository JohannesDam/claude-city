"""Council brief: one snapshot per council round, read by every department.

Writes logs/council/brief.json with the dashboard, the finance rule (reserve, capex envelope, upkeep cap),
happiness factors, the complaints-desk tickets per department, service diagnoses, coverage at homes,
zoning and development points.

  python -I council_brief.py            # write the brief and print a short summary
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get  # noqa: E402
import desk  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "logs", "council")


def finance_rule(city):
    """Budget from cash and cash flow.
    reserve  = max(20M, 2 months of expenses)            never spend below this
    envelope = 50% of cash above the reserve + 1 month of surplus (capex for this round)
    upkeep   = new monthly upkeep may use at most 50% of the current monthly surplus
    austerity when the monthly balance is below 2M: only essential/revenue-positive work."""
    money = city.get("money") or 0
    balance = city.get("balance") or 0
    expenses = abs(city.get("expenses") or 0)
    reserve = max(20_000_000, 2 * expenses)
    free = max(0, money - reserve)
    envelope = int(0.5 * free + max(0, balance))
    upkeep_cap = int(0.5 * max(0, balance))
    return {"money": money, "monthlyBalance": balance, "monthlyIncome": city.get("income"), "monthlyExpenses": -expenses,
            "reserve": reserve, "freeCash": free, "roundEnvelope": envelope, "newUpkeepCapPerMonth": upkeep_cap,
            "austerity": balance < 2_000_000,
            "rule": "reserve=max(20M, 2x monthly expenses); envelope=50% of cash above reserve + 1 month surplus; new upkeep <= 50% of monthly surplus"}


def main():
    city = get("/city")
    fin = finance_rule(city)
    hap = get("/happiness").get("factors") or []
    snap, tickets = desk.intake(hotspot_limit=3)
    services = get("/services", kind="all", brief=1).get("kinds") or {}
    cov = {}
    for svc in ("Park", "Healthcare", "Police", "FireRescue", "Education", "PostService"):
        c = get("/coverage", service=svc, layer="homes", cell=300, limit=5)
        cov[svc] = {"summary": c.get("summary"), "worst": [{k: w.get(k) for k in ("center", "households", "avgCoverage", "pctOfScale")} for w in (c.get("worstResidentialCells") or [])[:5]]}
    dev = get("/devtree")
    brief = {
        "time": time.strftime("%Y-%m-%d %H:%M:%S"),
        "city": {k: city.get(k) for k in ("cityName", "date", "population", "happiness", "health", "demand", "electricity", "water", "sewage", "milestone", "taxRate")},
        "finance": fin,
        "happiness": {"negative": [f for f in hap if f["x"] < 0], "positive": [f for f in hap if f["x"] > 0]},
        "tickets": [{k: t[k] for k in ("id", "type", "department", "count", "delta", "severity", "hotspots", "medianDistanceToFacility", "diagnosis")} for t in tickets],
        "services": {k: {"totals": v.get("totals"), "diagnosis": v.get("diagnosis")} for k, v in services.items()},
        "coverageAtHomes": cov,
        "zoning": get("/zonestats"),
        "devPoints": dev.get("points"),
        "buyableDevNodes": [n["name"] + f" ({n['cost']})" for n in dev.get("nodes", []) if n.get("buyable")],
        "masterplan": "docs/council/masterplan.json",
    }
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "brief.json"), "w", encoding="utf-8") as f:
        json.dump(brief, f, indent=1)
    return brief


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    b = main()
    f = b["finance"]
    print(f"{b['city']['date']}  pop {b['city']['population']}  happiness {b['city']['happiness']}")
    print(f"money {f['money']/1e6:.1f}M  balance {f['monthlyBalance']/1e6:+.1f}M/month  reserve {f['reserve']/1e6:.0f}M  "
          f"envelope {f['roundEnvelope']/1e6:.1f}M  upkeep cap {f['newUpkeepCapPerMonth']/1e6:.1f}M/month")
    print("negative happiness:", ", ".join(f"{h['factor']} {h['x']}" for h in b["happiness"]["negative"]))
    by = {}
    for t in b["tickets"]:
        by.setdefault(t["department"], []).append(f"{t['type']} {t['count']}")
    for d, ts in by.items():
        print(f"  {d}: {', '.join(ts)}")
    for svc, c in b["coverageAtHomes"].items():
        s = c["summary"] or {}
        print(f"  coverage {svc}: none {s.get('householdsPct', {}).get('none(<5%)')}%")
    print("dev points:", b["devPoints"])
