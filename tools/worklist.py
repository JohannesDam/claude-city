"""Shared worklist of the builders (logs/council/worklist.json), written by the advisory board.

Each item: {id, title, goal, district, owner, kind, priority, budget, upkeep, steps, status, claimedBy,
            claimedAt, doneAt, spent, note, batch}
status: open -> claimed -> done | failed | deferred.

CLI (builders):
  python -I worklist.py next <builder>          claim the best open item for this builder (prints it as JSON)
  python -I worklist.py mine <builder>          claimed + open items for this builder
  python -I worklist.py done <id> [--spent N] [--note TEXT]
  python -I worklist.py fail <id> --note TEXT
  python -I worklist.py defer <id> --note TEXT
CLI (conductor):
  python -I worklist.py status                  progress per owner and batch; "refill" when the board should run
  python -I worklist.py add <file.json>         merge a board result (list of items or {"items": [...]})
  python -I worklist.py assign <id> <builder>
"""
import json
import os
import sys
import time

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
PATH = os.path.join(ROOT, "logs", "council", "worklist.json")
REGISTER = os.path.join(ROOT, "docs", "council", "districts.json")
REFILL_AT = 0.5  # the board starts on the next list when half of the current batch is finished


class _FileLock:
    def __enter__(self):
        import msvcrt
        os.makedirs(os.path.dirname(PATH), exist_ok=True)
        self.fh = open(PATH + ".lock", "a+")
        while True:
            try:
                self.fh.seek(0)
                msvcrt.locking(self.fh.fileno(), msvcrt.LK_NBLCK, 1)
                return self
            except OSError:
                time.sleep(0.2)

    def __exit__(self, *exc):
        import msvcrt
        try:
            self.fh.seek(0)
            msvcrt.locking(self.fh.fileno(), msvcrt.LK_UNLCK, 1)
        finally:
            self.fh.close()


def load():
    if not os.path.exists(PATH):
        return {"items": [], "batches": []}
    with open(PATH, encoding="utf-8") as f:
        return json.load(f)


def save(d):
    tmp = PATH + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)
    os.replace(tmp, PATH)


def builders_districts():
    """builder -> set of district names it owns (from the district register)."""
    try:
        with open(REGISTER, encoding="utf-8") as f:
            reg = json.load(f)
    except OSError:
        return {}
    out = {}
    for d in reg.get("districts", []):
        if d.get("owner"):
            out.setdefault(d["owner"], set()).add(d["name"])
    return out


def for_builder(items, who):
    mine = builders_districts().get(who, set())
    return [i for i in items if i.get("owner") == who or (not i.get("owner") and i.get("district") in mine)]


def _rank(i):
    return (-(i.get("priority") or 0), i.get("id"))


def cmd_next(who):
    with _FileLock():
        d = load()
        claimed = [i for i in for_builder(d["items"], who) if i["status"] == "claimed" and i.get("claimedBy") == who]
        if claimed:
            print(json.dumps({"alreadyClaimed": sorted(claimed, key=_rank)[0]}, indent=1, ensure_ascii=True))
            return
        open_ = sorted((i for i in for_builder(d["items"], who) if i["status"] == "open"), key=_rank)
        if not open_:
            print(json.dumps({"empty": True, "hint": "do the standing list in the builder handbook, then report"}))
            return
        it = open_[0]
        it.update(status="claimed", claimedBy=who, claimedAt=time.strftime("%Y-%m-%d %H:%M:%S"))
        save(d)
        print(json.dumps(it, indent=1, ensure_ascii=True))


def cmd_mine(who):
    d = load()
    for i in sorted(for_builder(d["items"], who), key=_rank):
        if i["status"] in ("open", "claimed"):
            print(f"{i['id']:>12} {i['status']:8} p{i.get('priority', 0):<3} {i.get('district', ''):22} {i['title']}")


def _close(item_id, status, spent=None, note=None):
    with _FileLock():
        d = load()
        for i in d["items"]:
            if i["id"] == item_id:
                i["status"] = status
                i["doneAt"] = time.strftime("%Y-%m-%d %H:%M:%S")
                if spent is not None:
                    i["spent"] = spent
                if note:
                    i["note"] = note
                save(d)
                print(json.dumps({"id": item_id, "status": status}))
                return
    print(json.dumps({"error": f"no item {item_id}"}))


def cmd_status():
    d = load()
    by = {}
    for i in d["items"]:
        k = i.get("owner") or ("(" + (i.get("district") or "?") + ")")
        by.setdefault(k, {}).setdefault(i["status"], 0)
        by[k][i["status"]] += 1
    for k, v in sorted(by.items()):
        print(f"{k:24} {v}")
    batches = {}
    for i in d["items"]:
        b = batches.setdefault(i.get("batch", 0), [0, 0])
        b[0] += 1
        b[1] += i["status"] in ("done", "failed", "deferred")
    for b, (n, fin) in sorted(batches.items()):
        print(f"batch {b}: {fin}/{n} finished")
    if batches:
        last = max(batches)
        n, fin = batches[last]
        print("refill" if n and fin / n >= REFILL_AT else "ok")


def cmd_add(path):
    with open(path, encoding="utf-8") as f:
        new = json.load(f)
    items = new.get("items", new) if isinstance(new, dict) else new
    with _FileLock():
        d = load()
        batch = max([i.get("batch", 0) for i in d["items"]] + [0]) + 1
        have = {i["id"] for i in d["items"]}
        n = 0
        for it in items:
            it.setdefault("status", "open")
            it["batch"] = batch
            base, k = it["id"], 2
            while it["id"] in have:
                it["id"] = f"{base}-{k}"
                k += 1
            have.add(it["id"])
            d["items"].append(it)
            n += 1
        d.setdefault("batches", []).append({"batch": batch, "added": n, "at": time.strftime("%Y-%m-%d %H:%M:%S"), "source": os.path.basename(path)})
        save(d)
    print(json.dumps({"batch": batch, "added": n}))


def cmd_assign(item_id, who):
    with _FileLock():
        d = load()
        for i in d["items"]:
            if i["id"] == item_id:
                i["owner"] = who
                save(d)
                print(json.dumps({"id": item_id, "owner": who}))
                return
    print(json.dumps({"error": f"no item {item_id}"}))


def _opt(name, cast=str):
    if name in sys.argv:
        return cast(sys.argv[sys.argv.index(name) + 1])
    return None


def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        return
    c = a[0]
    if c == "next":
        cmd_next(a[1])
    elif c == "mine":
        cmd_mine(a[1])
    elif c in ("done", "fail", "defer"):
        _close(a[1], {"done": "done", "fail": "failed", "defer": "deferred"}[c], _opt("--spent", float), _opt("--note"))
    elif c == "status":
        cmd_status()
    elif c == "add":
        cmd_add(a[1])
    elif c == "assign":
        cmd_assign(a[1], a[2])
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
