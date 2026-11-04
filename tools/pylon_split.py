"""The wing pylon fairings as parts of their own, so the game can hide a pylon that carries nothing, as it does on its own jets
(GitHub issue #13). Hardpoint.ShowPylon shows a station's pylonOptions renderers while it carries a store and hides them when
it is empty; the MiG's fairings were part of the always-visible skin, so empty pylons stayed on.

The fairings are loose islands of the model, sitting on an unbroken wing skin, so lifting them out leaves no hole:
  inner and middle (x ~2.37 and ~3.10): the 'rails' part, four islands per pylon (fairing, nose cap, two small fittings);
  outer (x ~3.76): two islands of the body under each wing tip (the fairing and its rail).
Each pylon becomes a part pylon_L1 / R1 / L2 / R2 / L3 / R3 (L: -x, as the game's hardpoint names), material and UVs unchanged;
the rails part goes, and the body keeps its vertices (other scripts hold body vertex indices) with the outer islands' triangles
dropped. MiG29Builder puts each part on its own renderer and in its station's pylon options.

Edits <source>/mig29_mesh.json in place, once (info.pylon_split). Run after tools/normals_by_angle.py.
python pylon_split.py <source dir>"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
PATH = os.path.join(SRC, "mig29_mesh.json")
OUTER = dict(x=(3.62, 3.90), y_max=-0.05, z=(-0.90, 1.45), max_tris=200)   # the outer fairing islands' box (either side)


def islands(V, T):
    """Island id of each triangle: triangles joined through shared (welded) vertex positions."""
    _, w = np.unique(np.round(V, 4), axis=0, return_inverse=True); w = w.ravel()
    par = np.arange(w.max() + 1)

    def find(x):
        while par[x] != x:
            par[x] = par[par[x]]; x = par[x]
        return x
    for a, b, c in w[T]:
        ra = find(a); par[find(b)] = ra; par[find(c)] = ra
    return np.array([find(x) for x in w[T[:, 0]]])


def sub_part(p, name, tris):
    """A part made of the given triangles of p, its vertices renumbered."""
    V = np.array(p["vertices"]).reshape(-1, 3); N = np.array(p["normals"]).reshape(-1, 3); UV = np.array(p["uvs"]).reshape(-1, 2)
    used, inv = np.unique(tris.ravel(), return_inverse=True)
    return dict(name=name, material=p["material"], vertices=V[used].ravel().tolist(), normals=N[used].ravel().tolist(),
                uvs=UV[used].ravel().tolist(), triangles=inv.astype(int).tolist())


def side(x): return "L" if x < 0 else "R"


mesh = json.load(open(PATH))
info = mesh.setdefault("info", {})
if info.get("pylon_split"):
    print("[pylon_split] already done"); sys.exit(0)
parts = {p["name"]: p for p in mesh["parts"]}
groups = {}   # pylon name -> list of (part, triangles)

rails = parts["rails"]
V = np.array(rails["vertices"]).reshape(-1, 3); T = np.array(rails["triangles"]).reshape(-1, 3)
isl = islands(V, T)
for i in np.unique(isl):
    t = T[isl == i]; x = V[t.ravel(), 0].mean()
    groups.setdefault(f"pylon_{side(x)}{1 if abs(x) < 2.75 else 2}", []).append(t)

body = parts["body"]
V = np.array(body["vertices"]).reshape(-1, 3); T = np.array(body["triangles"]).reshape(-1, 3)
isl = islands(V, T)
drop = np.zeros(len(T), bool)
for i in np.unique(isl):
    sel = isl == i
    if sel.sum() > OUTER["max_tris"]: continue
    v = V[T[sel].ravel()]
    lo, hi = v.min(0), v.max(0)
    if OUTER["x"][0] <= abs(lo[0]) <= OUTER["x"][1] and OUTER["x"][0] <= abs(hi[0]) <= OUTER["x"][1] and np.sign(lo[0]) == np.sign(hi[0]) \
            and hi[1] <= OUTER["y_max"] and OUTER["z"][0] <= lo[2] and hi[2] <= OUTER["z"][1]:
        groups.setdefault(f"pylon_{side(lo[0])}3", []).append(("body", T[sel]))
        drop |= sel

new = []
for name, pieces in sorted(groups.items()):
    if name.endswith("3"):
        new.append(sub_part(body, name, np.concatenate([t for _, t in pieces])))
    else:
        new.append(sub_part(rails, name, np.concatenate(pieces)))
expected = {f"pylon_{s}{n}" for s in "LR" for n in "123"}
if {p["name"] for p in new} != expected:
    sys.exit(f"[pylon_split] expected {sorted(expected)}, found {sorted(p['name'] for p in new)}: the model changed?")

body["triangles"] = T[~drop].ravel().tolist()
mesh["parts"] = [p for p in mesh["parts"] if p["name"] != "rails"] + new
info["pylon_split"] = {p["name"]: len(p["triangles"]) // 3 for p in new}
tmp = PATH + ".tmp"
with open(tmp, "w") as f:
    json.dump(mesh, f)
os.replace(tmp, PATH)
print(f"[pylon_split] " + ", ".join(f"{k} {v} tris" for k, v in info["pylon_split"].items()) + f"; {int(drop.sum())} body tris moved")
