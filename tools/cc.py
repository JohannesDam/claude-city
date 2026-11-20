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


def post(path, body=None, **q):
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


class paused:
    """Pause the simulation while building (rapid builds during 4x simulation crashed the game a
    few times); resumes at full speed afterwards, also when the block raises."""
    def __enter__(self):
        post("/speed", {"speed": 0})
        return self

    def __exit__(self, *exc):
        post("/speed", {"speed": 3})
        return False


def place_in_area(prefab, x, z, radius=300, filt="Small", ts=(0.5, 0.25, 0.75), max_tries=40, dry=False):
    """Find a valid spot for a building along any road segment near (x, z) and build it there.
    Returns (result, tried); result is None when nothing fitted."""
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


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    method, path = sys.argv[1].upper(), sys.argv[2]
    if not path.startswith("/"):
        path = "/" + path  # Git Bash rewrites leading-slash args into Windows paths
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
