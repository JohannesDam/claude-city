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
BUILD_PATHS = {"/road", "/zone", "/building", "/upgrade", "/bulldoze", "/buytile", "/roadupgrade", "/busstop", "/busline"}


def post(path, body=None, **q):
    if path in BUILD_PATHS and not paused.held():
        with paused():
            return call("POST", path, body or {}, **q)
    return call("POST", path, body or {}, **q)


def short(d, keys=("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning")):
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
    """Cross-process build lock (several council workers may plan in parallel, but only one may build at a
    time: concurrent/rapid builds crashed the game natively). Uses an OS file lock, so a crashed holder
    releases it automatically. Re-entrant within one process."""
    PATH = __import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), "..", "logs", "build.lock")

    def __init__(self):
        self.depth = 0
        self.fh = None

    def acquire(self, who=None, poll=0.5):
        import msvcrt
        import os
        import time as _t
        if self.depth:
            self.depth += 1
            return
        os.makedirs(os.path.dirname(self.PATH), exist_ok=True)
        fh = open(self.PATH, "a+")
        waited = 0.0
        while True:
            try:
                fh.seek(0)
                msvcrt.locking(fh.fileno(), msvcrt.LK_NBLCK, 1)
                break
            except OSError:
                _t.sleep(poll)
                waited += poll
                if waited and int(waited) % 60 == 0 and waited == int(waited):
                    print(f"[cc] waiting for build lock ({int(waited)} s)", file=sys.stderr, flush=True)
        self.fh = fh
        self.depth = 1
        try:
            with open(self.PATH + ".holder", "w") as h:
                h.write(f"{os.getpid()} {who or os.path.basename(sys.argv[0])} {_t.strftime('%H:%M:%S')}\n")
        except OSError:
            pass

    def try_acquire(self, who=None):
        """Non-blocking: True (and held) when the lock was free, False when another process builds."""
        import msvcrt
        import os
        if self.depth:
            self.depth += 1
            return True
        os.makedirs(os.path.dirname(self.PATH), exist_ok=True)
        fh = open(self.PATH, "a+")
        try:
            fh.seek(0)
            msvcrt.locking(fh.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            fh.close()
            return False
        self.fh = fh
        self.depth = 1
        return True

    def release(self):
        import msvcrt
        if not self.depth:
            return
        self.depth -= 1
        if self.depth == 0 and self.fh:
            try:
                self.fh.seek(0)
                msvcrt.locking(self.fh.fileno(), msvcrt.LK_UNLCK, 1)
            finally:
                self.fh.close()
                self.fh = None


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
            if self.outer:
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
