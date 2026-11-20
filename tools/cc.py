"""Tiny client for the ClaudeCityBridge HTTP API (same endpoints the MCP server exposes).

CLI:  python -I cc.py GET /city
      python -I cc.py POST /road '{"prefab":"Small Road","points":[[0,0],[100,0]]}'
"""
import json
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://127.0.0.1:8787"


def call(method, path, body=None, timeout=180, **query):
    query.setdefault("timeout", int(timeout * 1000) - 2000)
    if BUILD_LOCK.token and path not in ("/lock", "/unlock"):
        query.setdefault("lock", BUILD_LOCK.token)  # renews the lease; lets build calls through the busy tone
    url = BASE + path
    if query:
        url += "?" + urllib.parse.urlencode({k: v for k, v in query.items() if v is not None})
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"content-type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        try:
            return json.loads(e.read().decode())
        except Exception:
            return {"error": f"HTTP {e.code}"}


def get(path, **q):
    return call("GET", path, **q)


# endpoints that change the city: they always run while holding the build lock with the game paused
BUILD_PATHS = {"/road", "/zone", "/building", "/upgrade", "/bulldoze", "/buytile", "/roadupgrade", "/busstop", "/busline",
               "/nodeupgrade", "/roundabout", "/extension", "/area", "/area/delete", "/buildingaction"}


# builds that may remove zoned buildings (homes, shops, offices, factories) refuse to do so unless the caller passes
# allowDemolish=True (urban renewal); the API reports them in "wouldDemolish" either way
DEMOLISH_GUARDED = {"/road", "/building", "/upgrade", "/roadupgrade"}


def post(path, body=None, **q):
    if path in DEMOLISH_GUARDED and isinstance(body, dict) and "allowDemolish" not in body:
        body = {**body, "allowDemolish": False}
    if path in BUILD_PATHS and not paused.held():
        with paused():
            return post(path, body, **q)
    res = call("POST", path, body or {}, **q)
    if isinstance(res, dict) and res.get("busy") and paused.held():
        # our lease expired (no call for TTL seconds) and somebody else builds now: queue again, retry once
        print("[cc] lost the build lock (lease expired), queueing again", file=sys.stderr, flush=True)
        BUILD_LOCK.server_acquire()
        res = call("POST", path, body or {}, **q)
    return res


def short(d, keys=("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "blocked", "wouldDemolish", "conflicts")):
    return {k: d.get(k) for k in keys if d.get(k) is not None}


SETTLE_SECONDS = 1.0  # pause after each build; rapid back-to-back builds crashed the game


def _settled(res):
    import time as _t
    if isinstance(res, dict) and res.get("applied"):
        _t.sleep(SETTLE_SECONDS)
    return res


def road(prefab, points, **kw):
    return _settled(post("/road", {"prefab": prefab, "points": points, **kw}))


def zone(zone_name, **kw):
    return _settled(post("/zone", {"zone": zone_name, **kw}))


def building(prefab, **kw):
    return _settled(post("/building", {"prefab": prefab, **kw}))


def upgrade(prefab, edges, **kw):
    return _settled(post("/upgrade", {"prefab": prefab, "edges": edges, **kw}))


class _BuildLock:
    """Build lock: only one builder changes the city at a time (concurrent/rapid builds crashed the game).
    Two layers, taken in this order:
      1. the server lock in the mod ("busy tone"; GET /lock shows who builds and who waits): first come,
         first served, a lease renewed by every call (the token rides along as ?lock=...);
      2. an OS file lock (logs/build.lock): keeps cc users exclusive even across game restarts.
    Re-entrant within one process. Think/plan OUTSIDE the lock; hold it only for the actual change."""
    PATH = __import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), "..", "logs", "build.lock")
    TTL = 120

    def __init__(self):
        self.depth = 0
        self.fh = None
        self.token = None
        self.who = None

    def server_acquire(self, who=None, poll=0.5):
        """Queue for the server lock until granted. Degrades to no server lock when the game or plugin is
        down / too old (the build calls themselves then fail or are unguarded, as before)."""
        import os
        import time as _t
        who = who or self.who or os.path.basename(sys.argv[0])
        self.who = who
        self.token = None
        ticket, t0, last_note = None, _t.time(), 0.0
        while True:
            try:
                r = call("POST", "/lock", {"who": who, "ttl": self.TTL, **({"ticket": ticket} if ticket else {})}, timeout=20)
            except Exception:
                return None
            if not isinstance(r, dict) or "granted" not in r:
                return None
            if r["granted"]:
                self.token = r["token"]
                return self.token
            ticket = r.get("ticket")
            waited = _t.time() - t0
            if waited - last_note >= 60:
                last_note = waited
                h = r.get("holder") or {}
                print(f"[cc] waiting for build lock ({int(waited)} s, position {r.get('position')}, holder {h.get('who')})",
                      file=sys.stderr, flush=True)
            _t.sleep(poll)

    def _file_acquire(self, who, blocking=True, poll=0.5):
        import msvcrt
        import os
        import time as _t
        os.makedirs(os.path.dirname(self.PATH), exist_ok=True)
        fh = open(self.PATH, "a+")
        waited = 0.0
        while True:
            try:
                fh.seek(0)
                msvcrt.locking(fh.fileno(), msvcrt.LK_NBLCK, 1)
                break
            except OSError:
                if not blocking:
                    fh.close()
                    return False
                _t.sleep(poll)
                waited += poll
                if waited and int(waited) % 60 == 0 and waited == int(waited):
                    print(f"[cc] waiting for build file lock ({int(waited)} s)", file=sys.stderr, flush=True)
        self.fh = fh
        try:
            with open(self.PATH + ".holder", "w") as h:
                h.write(f"{os.getpid()} {who or os.path.basename(sys.argv[0])} {_t.strftime('%H:%M:%S')}\n")
        except OSError:
            pass
        return True

    def acquire(self, who=None, poll=0.5):
        if self.depth:
            self.depth += 1
            return
        self.server_acquire(who, poll)
        self._file_acquire(who, True, poll)
        self.depth = 1

    def try_acquire(self, who=None):
        """Non-blocking: True (and held) when nobody builds or waits, False otherwise."""
        if self.depth:
            self.depth += 1
            return True
        try:
            r = call("POST", "/lock", {"who": who or "try", "ttl": self.TTL, "noQueue": True}, timeout=20)
        except Exception:
            r = None
        if isinstance(r, dict) and "granted" in r:
            if not r["granted"]:
                return False
            self.token = r["token"]
        if not self._file_acquire(who, blocking=False):
            self._server_release()
            return False
        self.depth = 1
        return True

    def queue_length(self):
        """How many builders wait for the server lock (0 when unknown)."""
        try:
            r = call("GET", "/lock", timeout=10)
            return len(r.get("queue") or []) if isinstance(r, dict) else 0
        except Exception:
            return 0

    def _server_release(self):
        tok, self.token = self.token, None
        if tok:
            try:
                call("POST", "/unlock", {"token": tok}, timeout=10)
            except Exception:
                pass

    def release(self):
        import msvcrt
        if not self.depth:
            return
        self.depth -= 1
        if self.depth == 0:
            try:
                if self.fh:
                    try:
                        self.fh.seek(0)
                        msvcrt.locking(self.fh.fileno(), msvcrt.LK_UNLCK, 1)
                    finally:
                        self.fh.close()
                        self.fh = None
            finally:
                self._server_release()


BUILD_LOCK = _BuildLock()


class paused:
    """Hold the build lock and pause the simulation while building (rapid builds during 4x simulation
    crashed the game a few times); resumes at full speed afterwards, also when the block raises.
    Nested use is fine; only the outermost block pauses/resumes."""
    def __init__(self, who=None):
        self.who = who

    @staticmethod
    def held():
        return BUILD_LOCK.depth > 0

    def __enter__(self):
        outer = not BUILD_LOCK.depth
        BUILD_LOCK.acquire(self.who)
        self.outer = outer
        if outer:
            call("POST", "/speed", {"speed": 0})
        return self

    def __exit__(self, *exc):
        try:
            # another builder already queued: leave the game paused for them (no pause/resume churn)
            if self.outer and not BUILD_LOCK.queue_length():
                call("POST", "/speed", {"speed": 3})
        finally:
            BUILD_LOCK.release()
        return False


def find_spot(prefab, x, z, radius=400, n=8, **kw):
    """Ranked placements from GET /findspot (occupancy model of the area; ~0.2 s)."""
    return get("/findspot", prefab=prefab, x=x, z=z, radius=min(radius, 1000), n=n, **kw).get("candidates") or []


def place_smart(prefab, x, z, radius=400, n=8, dry=False, **kw):
    """Place a building at the best /findspot candidate that the game validates (dry run first).
    Returns (result, candidate) or (None, last dry-run result)."""
    last = None
    for c in find_spot(prefab, x, z, radius=radius, n=n, **kw):
        b = dict(c["building"])
        b.pop("prefab", None)
        r = post("/building", {"prefab": prefab, **b, "dryRun": True})
        last = r
        if r.get("errors") == 0:
            return (r if dry else building(prefab, **b)), c
    return None, last


def revenue_rates():
    """Monthly tax income per household and per zoned building of each kind (from /finance and the building
    counts). Used to price what a demolition costs. Industry and offices earn far more per building than homes."""
    inc = get("/finance").get("income") or {}
    zb = get("/buildings", kind="service", limit=1).get("zonedByZone") or {}
    hh = sum(d.get("households") or 0 for d in get("/districts", detail="0").get("districts") or [])

    def per(tax, word):
        n = sum(v for k, v in zb.items() if word in k)
        return (inc.get(tax) or 0) / n if n else 0.0
    return {"household": (inc.get("TaxResidential") or 0) / hh if hh else 0.0,
            "commercial": per("TaxCommercial", "Commercial"), "industrial": per("TaxIndustrial", "Industrial"),
            "office": per("TaxOffice", "Office"),
            "monthlyIncome": get("/finance").get("totalIncome") or 0}


def demolition_cost(would, rates=None):
    """Price a wouldDemolish list: households, companies by kind and tax revenue per month that goes with them.
    Abandoned/condemned/destroyed buildings cost nothing."""
    rates = rates or revenue_rates()
    out = {"buildings": 0, "households": 0, "companies": 0, "revenuePerMonth": 0.0, "byKind": {}}
    for w in would or []:
        if w.get("state") in ("abandoned", "condemned", "destroyed"):
            continue
        z = (w.get("zone") or w.get("prefab") or "")
        n = w.get("renters") or 0
        if "Industrial" in z:
            k, rev = "industrial", rates["industrial"]
        elif "Office" in z:
            k, rev = "office", rates["office"]
        elif "Commercial" in z:
            k, rev = "commercial", rates["commercial"]
        elif "Residential" in z or "Mixed" in z:
            k, rev = "residential", n * rates["household"]
        else:
            k, rev = "other", 0.0
        out["buildings"] += 1
        out["households" if k == "residential" else "companies"] += n
        out["revenuePerMonth"] += rev
        out["byKind"][k] = out["byKind"].get(k, 0) + 1
    out["revenuePerMonth"] = round(out["revenuePerMonth"])
    return out


def make_room(prefab, x, z, radius=500, max_households=60, max_companies=4, max_revenue_share=0.001,
              dry=False, log_as=None, **kw):
    """Place a building that does not fit on free land, by making room - the owner allows it, as long as
    revenue does not suffer. Order:
      1. a clean spot (nothing demolished), like place_smart;
      2. spots that replace zoned buildings, cheapest first (revenue lost per month, then households);
         within limits: <= max_households, <= max_companies, revenue <= max_revenue_share of monthly income
         (default 0.1%, about $40k/month); abandoned/condemned buildings are free.
    Industry and offices pay ~$20k+/month per building in tax, homes ~$90 per household: clearing homes is
    cheap when residential demand is high (people move elsewhere), clearing workplaces is not.
    Returns {"mode": "clean"|"room"|"none", "result", "cost", "options"}; dry=True only reports.
    When nothing fits within the limits: buy an adjacent tile (GET /tiles, cheap) or ask the conductor."""
    r, c = place_smart(prefab, x, z, radius=radius, dry=dry, **kw)
    if r is not None and r.get("errors") == 0:
        return {"mode": "clean", "result": r, "candidate": c}
    rates = revenue_rates()
    limit = max_revenue_share * (rates.get("monthlyIncome") or 0)
    options = []
    for cand in find_spot(prefab, x, z, radius=radius, n=12, growables="free", **kw):
        b = dict(cand["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True, "allowDemolish": True})
        if d.get("errors") != 0:
            continue
        cost = demolition_cost(d.get("wouldDemolish"), rates)
        ok = cost["households"] <= max_households and cost["companies"] <= max_companies and cost["revenuePerMonth"] <= limit
        options.append({"building": b, "cost": cost, "withinLimits": ok})
    options.sort(key=lambda o: (not o["withinLimits"], o["cost"]["revenuePerMonth"], o["cost"]["households"]))
    best = next((o for o in options if o["withinLimits"]), None)
    if best is None:
        return {"mode": "none", "options": options[:5], "limitRevenue": round(limit),
                "hint": "no spot within limits: buy an adjacent tile (GET /tiles) or report to the conductor"}
    if dry:
        return {"mode": "room", "result": None, "cost": best["cost"], "candidate": best["building"], "options": options[:5]}
    res = building(prefab, **best["building"], allowDemolish=True)
    try:
        import os
        import time as _t
        p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs", "council", "demolitions.jsonl")
        with open(p, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": _t.strftime("%Y-%m-%d %H:%M:%S"), "by": log_as or BUILD_LOCK.who, "prefab": prefab,
                                "placedId": res.get("placedId"), "cost": best["cost"], "pos": [best["building"].get("x"), best["building"].get("z")]}) + "\n")
    except OSError:
        pass
    return {"mode": "room", "result": res, "cost": best["cost"], "candidate": best["building"]}


def place_in_area(prefab, x, z, radius=300, filt="Small", ts=(0.5, 0.25, 0.75), max_tries=40, dry=False):
    """Find a valid spot for a building near (x, z) and build it there: /findspot first, then the old
    brute-force scan along road segments. Returns (result, tried); result is None when nothing fitted."""
    r, info = place_smart(prefab, x, z, radius=max(radius, 300), dry=dry)
    if r is not None:
        return r, info
    segs = get("/nets", x=x, z=z, radius=radius, filter=filt).get("segments") or []
    tried = 0
    last = None
    for s in segs:
        for t in ts:
            for side in ("left", "right"):
                tried += 1
                if tried > max_tries:
                    return None, last
                r = post("/building", {"prefab": prefab, "edge": s["id"], "t": t, "side": side, "dryRun": True})
                last = r
                if r.get("errors") == 0:
                    if dry:
                        return r, tried
                    return building(prefab, edge=s["id"], t=t, side=side), tried
    return None, last


def edge_near(x, z, filt=None, radius=40):
    """Id of the road/net segment closest to (x, z)."""
    d = get("/nets", x=x, z=z, radius=radius, filter=filt)
    segs = d.get("segments") or []
    return segs[0]["id"] if segs else None


ACCESS_ICONS = ("No Car Access", "No Pedestrian Access", "No Train Access", "Track Not Connected")


def access_check(ids=None):
    """Connection-warning icons (driveway or track ends without a road/track) for the given building ids,
    or all of them. 'roadConnected' only checks the lot's front centre; a wide lot can still have its
    vehicle exit where there is no road. Run after building, with the game running for a moment."""
    ids = set(ids or [])
    out = []
    for t in ACCESS_ICONS:
        for e in (get("/problems", type=t).get("problems") or [{}])[0].get("examples", []) if get else []:
            if not ids or e.get("owner") in ids:
                out.append({"type": t, **e})
    return out


FIT_TS = (0.5, 0.35, 0.65, 0.2, 0.8, 0.05, 0.95)


def segs_in(x0, x1, z0, z1, filt="Road", highways=None):
    """Road segments whose midpoint lies inside the box. Highways are skipped unless the filter names one
    (filt="Highway...") or highways=True."""
    if highways is None:
        highways = "Highway" in (filt or "")
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    r = max(x1 - x0, z1 - z0) / 2 + 60
    out = []
    for s in get("/nets", x=cx, z=cz, radius=r, filter=filt).get("segments") or []:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if x0 <= mx <= x1 and z0 <= mz <= z1 and (highways or "Highway" not in (s.get("prefab") or "")):
            out.append(s)
    return out


def fit(label, prefab, box, near=None, sides=("left", "right"), ts=FIT_TS, quiet=False):
    """Try every road segment in box (x0, x1, z0, z1) at several t and both sides, nearest to `near` first;
    build at the first spot that validates. Returns the build result or None."""
    segs = segs_in(*box)
    if near:
        segs.sort(key=lambda s: ((s["start"][0] + s["end"][0]) / 2 - near[0]) ** 2 + ((s["start"][1] + s["end"][1]) / 2 - near[1]) ** 2)
    last = None
    for s in segs:
        for t in ts:
            for side in sides:
                d = post("/building", {"prefab": prefab, "edge": s["id"], "t": t, "side": side, "dryRun": True})
                last = d
                if d.get("errors") == 0:
                    r = building(prefab, edge=s["id"], t=t, side=side)
                    if not quiet:
                        print(f"{label:<28} {short(r)}", flush=True)
                    return r
    if not quiet:
        print(f"{label:<28} no spot ({short(last or {})})", flush=True)
    return None


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    method, path = sys.argv[1].upper(), sys.argv[2]
    if "/Git/" in path and path[1:2] == ":":
        path = "/" + path.split("/Git/", 1)[1]  # Git Bash (MSYS) turned "/city" into "C:/Program Files/Git/city"
    if not path.startswith("/"):
        path = "/" + path
    body = json.loads(sys.argv[3]) if len(sys.argv) > 3 else None
    if method == "GET":
        q = dict(urllib.parse.parse_qsl(urllib.parse.urlparse(path).query))
        res = get(urllib.parse.urlparse(path).path, **q)
    else:
        res = post(path, body)
    if isinstance(res, dict) and "grid" in res:
        grid = res.pop("grid")
        print(json.dumps(res, indent=1))
        print(grid)
    else:
        print(json.dumps(res, indent=1))
