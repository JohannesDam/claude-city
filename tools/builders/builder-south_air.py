"""builder-south: air pollution at given points, from GET /pollution (a small grid around each point; the 'worst'
list of a 4-cell grid centred on the point holds the cell values)."""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get

POINTS = [(-2380, -1080), (-2400, -800), (-2323, -442)]


def air_at(x, z, cell=64):
    p = get("/pollution", type="air", x=x, z=z, size=cell * 4, cell=cell)
    vals = [w for w in p.get("worst") or [] if abs(w[0] - x) <= cell and abs(w[1] - z) <= cell]
    vals.sort(key=lambda w: (w[0] - x) ** 2 + (w[1] - z) ** 2)
    return vals[0][2] if vals else None, p.get("max")


if __name__ == "__main__":
    print(get("/city")["date"], json.dumps({f"{x},{z}": air_at(x, z) for x, z in POINTS}))
