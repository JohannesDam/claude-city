"""Keeps Claude City running between planning rounds.

Every interval: makes sure the simulation runs (closes popups that pause it), logs the dashboard
to logs/city.jsonl, saves every few minutes, and flags shortages. Exits after --minutes with a
short summary of what changed, so a planning round can follow.

usage: python -I autopilot.py [--minutes 20] [--interval 30] [--save-every 5]
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, post  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
LOG_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs")
os.makedirs(LOG_DIR, exist_ok=True)


def ratio(d, a, b):
    try:
        return d[a] / d[b] if d[b] else 0.0
    except (KeyError, TypeError):
        return 0.0


def shortages(c):
    out = []
    el, wa, se = c.get("electricity", {}), c.get("water", {}), c.get("sewage", {})
    if ratio(el, "consumption", "production") > 0.8:
        out.append(f"electricity {el.get('consumption')}/{el.get('production')}")
    if ratio(wa, "consumption", "capacity") > 0.8:
        out.append(f"water {wa.get('consumption')}/{wa.get('capacity')}")
    if ratio(se, "consumption", "capacity") > 0.8:
        out.append(f"sewage {se.get('consumption')}/{se.get('capacity')}")
    if c.get("money", 1) < 50000:
        out.append(f"money low {c.get('money')}")
    return out


def clear_ruins():
    """Demolish destroyed/abandoned buildings while paused, in small batches; returns count."""
    ids = []
    for kind in ("destroyed", "abandoned"):
        for b in get("/buildings", kind=kind, timeout=30).get("buildings", []):
            if b["pos"][0] < -300 and b.get("roadConnected"):
                ids.append(b["id"])
    if not ids:
        return 0
    n = 0
    post("/speed", {"speed": 0}, timeout=20)
    try:
        for k in range(0, len(ids), 5):
            r = post("/bulldoze", {"ids": ids[k:k + 5]}, timeout=60)
            if r.get("applied"):
                n += r.get("targets", 0)
            time.sleep(1)
    finally:
        post("/speed", {"speed": 3}, timeout=20)
    print(f"{time.strftime('%H:%M:%S')} cleared {n} ruins", flush=True)
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--minutes", type=float, default=20)
    ap.add_argument("--interval", type=float, default=30)
    ap.add_argument("--save-every", type=float, default=5)
    ap.add_argument("--speed", type=int, default=3)
    ap.add_argument("--min-minutes", type=float, default=20, help="never stop for a stall before this")
    ap.add_argument("--clear-every", type=float, default=15, help="minutes between clearing ruins/abandoned buildings (0 = never)")
    ap.add_argument("--stall-window", type=float, default=10, help="minutes of history for the stall test")
    ap.add_argument("--stall-growth", type=int, default=300, help="stop when population grew less than this over the window")
    a = ap.parse_args()
    end = time.time() + a.minutes * 60
    last_save = time.time()
    first = None
    last = None
    seen_problems = {}
    alerts = []
    failures = 0
    recoveries = 0
    start = time.time()
    history = []  # (time, population)
    stop_reason = "time limit"
    last_clear = time.time()
    cleared = 0
    while time.time() < end:
        st = get("/status", timeout=20)
        if "error" in st or st.get("mode") != "Game":
            failures += 1
            print(f"{time.strftime('%H:%M:%S')} bridge/game not ready: {st}")
            if failures >= 6:
                print("GAME DOWN: bridge unreachable or not in game for 3 minutes -> recovering", flush=True)
                recoveries += 1
                if recoveries > 2:
                    print("GIVING UP after repeated recoveries")
                    break
                import subprocess
                sh = os.path.join(os.path.dirname(os.path.abspath(__file__)), "recover.sh")
                out = subprocess.run(["bash", sh], capture_output=True, text=True, timeout=900)
                print(out.stdout[-600:], flush=True)
                alerts.append(f"recovered from crash at {time.strftime('%H:%M')}")
                failures = 0
            time.sleep(30)
            continue
        failures = 0
        if not st.get("speed"):
            post("/ui", {"action": "resume"}, timeout=20)
            post("/speed", {"speed": a.speed}, timeout=20)
        c = get("/city", timeout=30)
        if "error" in c:
            time.sleep(a.interval)
            continue
        first = first or c
        last = c
        now = time.time()
        history.append((now, c.get("population", 0)))
        window = [h for h in history if h[0] >= now - a.stall_window * 60]
        if now - start >= a.min_minutes * 60 and history[0][0] <= now - a.stall_window * 60 and window:
            grown = window[-1][1] - window[0][1]
            if grown < a.stall_growth:
                stop_reason = f"growth stalled (+{grown} in {a.stall_window:g} min)"
        if now - start >= a.min_minutes * 60 and shortages(c):
            stop_reason = "shortage: " + ", ".join(shortages(c))
        rec = {k: c.get(k) for k in ("date", "population", "money", "income", "expenses", "balance", "happiness", "health", "demand", "electricity", "water", "sewage", "milestone")}
        rec["t"] = time.strftime("%Y-%m-%d %H:%M:%S")
        probs = get("/problems", timeout=30).get("problems", [])
        zs = {z["zone"]: z["freePct"] for z in get("/zonestats", timeout=30).get("zones", [])}
        rec["zoneFreePct"] = zs
        res_free = [v for k, v in zs.items() if "Residential" in k]
        if res_free and max(res_free) < 5 and "residential full" not in alerts:
            alerts.append("residential full")
        rec["problems"] = {p["type"]: p["count"] for p in probs}
        with open(os.path.join(LOG_DIR, "city.jsonl"), "a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")
        for s in shortages(c):
            if s.split()[0] not in " ".join(alerts):
                alerts.append(s)
        for p in probs:
            if p["type"] == "Powerline Not Connected":
                continue
            prev = seen_problems.get(p["type"], 0)
            if p["count"] > prev:
                seen_problems[p["type"]] = p["count"]
        print(f"{time.strftime('%H:%M:%S')} {c.get('date')} pop={c.get('population')} money={c.get('money')} "
              f"bal={c.get('balance')} R={c['demand']['residentialLow']}/{c['demand']['household']} "
              f"C={c['demand']['commercial']} I={c['demand']['industrial']} ms={c['milestone']['achieved']} "
              f"free={zs} probs={rec['problems']}", flush=True)
        if stop_reason != "time limit":
            break
        if a.clear_every and time.time() - last_clear > a.clear_every * 60:
            last_clear = time.time()
            cleared += clear_ruins()
        if time.time() - last_save > a.save_every * 60:
            r = post("/save", {"name": "Claude City"}, timeout=120)
            last_save = time.time()
            if not r.get("saved"):
                print("save failed:", r)
        time.sleep(a.interval)

    if last:
        post("/save", {"name": "Claude City"}, timeout=120)
        print("\n=== summary ===")
        print(f"stopped: {stop_reason} after {round((time.time() - start) / 60)} min")
        print(f"date {first.get('date')} -> {last.get('date')}")
        print(f"population {first.get('population')} -> {last.get('population')}")
        print(f"money {first.get('money')} -> {last.get('money')} (balance/month {last.get('balance')})")
        print(f"milestone {first['milestone']['achieved']} -> {last['milestone']['achieved']} (xp {last['milestone']['xp']}/{last['milestone']['nextRequiredXP']})")
        print(f"demand {last.get('demand')}")
        print(f"utilities el={last.get('electricity')} water={last.get('water')} sewage={last.get('sewage')}")
        print(f"alerts: {alerts}")
        print(f"problems seen (max counts): {seen_problems}")
        print(f"ruins cleared: {cleared}")


if __name__ == "__main__":
    main()
