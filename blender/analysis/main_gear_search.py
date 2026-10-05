"""Main-gear stow search: fold angle (about the hinge), strut rotation (wheel twist about the strut) and a translation that put the
left main leg (the right one is its mirror: gearMount_R is scaled -1 in x) inside the hidden space of the MiG skin (tools/bay_grid.py) with the most margin. Gear vertices come from the
MiG29Tools.MiG29GearDump 'down' pose (MiG29Out/gear_down.json); the posing is checked against its 'up' dump first.
python main_gear_search.py <MiG29Source> <MiG29Out>"""
import json, os, sys, time
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
import bay_grid

SRC, OUT = sys.argv[1], sys.argv[2]


def rot(axis, deg):
    k = np.asarray(axis, float); k = k / np.linalg.norm(k); a = np.radians(deg)
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K      # Rodrigues, numerically what Unity's AngleAxis does


def load(pose, side="L"):
    d = json.load(open(os.path.join(OUT, f"gear_{pose}.json")))
    g = next(g for g in d["gears"] if g["name"] == f"gear_{side}_sprung")
    sprung, unsprung = [], []
    for p in d["parts"]:
        if f"gearHinge_{side}" not in p["path"] or "chock" in p["name"]: continue
        V = np.array(p["v"]).reshape(-1, 3)
        (unsprung if "_unsprung" in p["path"] else sprung).append(V)
    return g, np.concatenate(sprung), np.concatenate(unsprung)


def pose(g, S, U, fold, strut, T):
    Rs = rot(g["srt_y"], strut); sp = np.array(g["srt_pos"])
    U2 = (U - sp) @ Rs.T + sp
    Rf = rot(g["hinge_x"], fold); hp = np.array(g["hinge"])
    P = np.concatenate([S, U2])
    return (P - hp) @ Rf.T + hp + T


g, S, U = load("down")
_, Su, Uu = load("up")
up = np.concatenate([Su, Uu])
guess = pose(g, S, U, g["fold"], g["strut"], np.zeros(3))
T = (up - guess).mean(0)
res = np.abs(pose(g, S, U, g["fold"], g["strut"], T) - up).max()
print(f"[search] posing check: translation {np.round(T, 3)}, max residual {res * 1000:.1f} mm  ({len(S)}+{len(U)} verts)")
NEWLEG = os.environ.get("NEWLEG")     # tools/main_gear_gen.py leg in place of the KR-67 strut (KR-67 wheel kept)
if NEWLEG:
    mg = {p["name"]: np.array(p["vertices"]).reshape(-1, 3) for p in json.load(open(NEWLEG))["parts"]}
    d0 = json.load(open(os.path.join(OUT, "gear_down.json")))
    wheel = np.concatenate([np.array(p["v"]).reshape(-1, 3) for p in d0["parts"] if p["name"] == "wheel_L"])
    S, U = mg["sprung"], np.concatenate([mg["unsprung"], wheel])
    print(f"[search] new leg: {len(S)} sprung + {len(U)} unsprung verts (wheel included)")

# hidden space around the right main bay (the intake ducts behind the blockers count as hidden: MiG29Builder.AddIntakeBlockers)
t0 = time.time()
H = 0.025
hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), (0.35, -1.15, -1.3), (2.35, 0.85, 5.0), H,
                      boxes=[((0.42, -0.88, 3.59), (0.98, -0.30, 3.61))])
M = bay_grid.signed_margin(~hid.air, H, steps=8) - 1.5 * H   # interior walls are hidden too; keep 1.5 voxels off the outer skin
print(f"[search] grid {hid.shape}, {hid.hidden.mean() * 100:.1f}% hidden, {time.time() - t0:.0f} s")
np.save(os.path.join(OUT, "main_hidden.npy"), hid.hidden)
# doors open (as while the gear moves): the bay joins the outside air; gear deep inside what is still enclosed mid-swing is clipping
hid_open = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "canopy")), (0.35, -1.15, -1.3), (2.35, 0.85, 5.0), H,
                           boxes=[((0.42, -0.88, 3.59), (0.98, -0.30, 3.61))])
deep_open = bay_grid.signed_margin(~hid_open.air, H, steps=4) > 0.04
print(f"[search] doors open: {(~hid_open.air).mean() * 100:.1f}% enclosed")


def clipping(P):
    k = hid_open.index(P * np.array([-1.0, 1.0, 1.0]))
    ok = np.all((k >= 0) & (k < np.array(hid_open.shape)), axis=1)
    k = k[ok]
    return deep_open[k[:, 0], k[:, 1], k[:, 2]].sum() / len(P)


def margin(P):
    P = P * np.array([-1.0, 1.0, 1.0])       # left leg evaluated in the right bay grid
    k = hid.index(P)
    out = np.any((k < 0) | (k >= np.array(hid.shape)), axis=1)
    k = np.clip(k, 0, np.array(hid.shape) - 1)
    return np.where(out, -0.2, M[k[:, 0], k[:, 1], k[:, 2]])


# vertex subsample (one per 2 cm cell) so the search stays quick
allP = np.concatenate([S, U])
_, keep = np.unique(np.round(allP / 0.02).astype(int), axis=0, return_index=True)
isU = np.arange(len(allP)) >= len(S)
Ssub, Usub = allP[keep][~isU[keep]], allP[keep][isU[keep]]
print(f"[search] {len(Ssub) + len(Usub)} sample verts")

# the strut top (hinge yoke) stands above the upper skin even with the gear down and gets trimmed off: leave it out of the score
roofcut = margin(pose(g, Ssub, Usub, 0, 0, np.zeros(3)))
downP = pose(g, Ssub, Usub, 0, 0, np.zeros(3))
yoke = (downP[:, 1] > 0.0) & (roofcut < 0.0) & (not NEWLEG)
print(f"[search] {yoke.sum()} yoke verts above the skin with the gear down (excluded)")


CLIP_WEIGHT = float(os.environ.get("CLIP_WEIGHT", "0.3"))   # metres of margin traded per unit of mid-swing clipping fraction


SPAN = tuple(float(v) for v in os.environ.get("SPAN", "0.9,0.3,0.6").split(","))
MIN_MARGIN = float(os.environ.get("MIN_MARGIN", "0.03"))
T_WEIGHT = float(os.environ.get("T_WEIGHT", "0.03"))                  # margin traded per metre of stow shift (a real leg barely shifts)
FOLDS = [int(v) for v in os.environ.get("FOLDS", "-120,-55,5").split(",")]
STRUTS = [int(v) for v in os.environ.get("STRUTS", "-180,180,15").split(",")]


def best_T(P, span=SPAN, step=0.05, fold=0.0, strut=0.0, T0=np.zeros(3)):
    best = (-9, None)
    mids = [pose(g, Ssub, Usub, fold * t, strut * t, np.zeros(3)) for t in (0.25, 0.5, 0.75)]
    for dx in np.arange(-span[0], span[0] + 1e-6, step):
        for dy in np.arange(-span[1], span[1] + 1e-6, step):
            for dz in np.arange(-span[2], span[2] + 1e-6, step):
                T = np.array([dx, dy, dz]); m = margin(P + T)[~yoke].min()
                if m < MIN_MARGIN: continue                       # must stow hidden first
                Tt = T0 + T
                clip = np.mean([clipping(Pm + Tt * t) for Pm, t in zip(mids, (0.25, 0.5, 0.75))])
                sc = min(m, 0.08) - CLIP_WEIGHT * clip - T_WEIGHT * np.linalg.norm(Tt)
                if sc > best[0]: best = (sc, T)
    return best


results = []
t0 = time.time()
for fold in range(*FOLDS):
    for strut in range(*STRUTS):
        P = pose(g, Ssub, Usub, fold, strut, np.zeros(3))
        m, T = best_T(P, step=0.1, fold=fold, strut=strut)
        results.append((m, fold, strut, T))
results.sort(key=lambda r: -r[0])
print(f"[search] coarse {time.time() - t0:.0f} s")
for m, f, s, T in [r for r in results if r[3] is not None][:8]: print(f"   coarse fold {f} strut {s} T {np.round(T, 2)} score {m * 100:.1f} cm")

fine = []
for m0, f0, s0, T0 in [r for r in results if r[3] is not None][:4]:
    for fold in range(f0 - 4, f0 + 5, 2):
        for strut in range(s0 - 10, s0 + 11, 5):
            P = pose(g, Ssub, Usub, fold, strut, T0)
            m, dT = best_T(P, span=tuple(min(0.1, v) for v in SPAN), step=0.025, fold=fold, strut=strut, T0=T0)
            if dT is not None: fine.append((m, fold, strut, T0 + dT))
fine.sort(key=lambda r: -r[0])
for m, f, s, T in fine[:6]:
    P = pose(g, Ssub, Usub, f, s, T); mm = margin(P)[~yoke]
    clip = [clipping(pose(g, Ssub, Usub, f * t, s * t, T * t)) for t in (0.25, 0.5, 0.75)]
    print(f"[search] fine fold {f} strut {s} T {np.round(T, 3)}: min margin {mm.min() * 100:.1f} cm, mid-swing clipping {np.round(np.array(clip) * 100, 1)} %, wheel-ish centre {np.round(P[len(Ssub):].mean(0), 2)}")
m, f, s, T = fine[0]
json.dump({"fold": f, "strut": s, "T": T.tolist()}, open(os.path.join(OUT, os.environ.get("RESULT", "main_gear_search.json")), "w"))

# which pieces stick out at the chosen pose
d = json.load(open(os.path.join(OUT, "gear_down.json")))
for p in d["parts"]:
    if "gearHinge_L" not in p["path"] or "chock" in p["name"]: continue
    V = np.array(p["v"]).reshape(-1, 3)
    isu = "_unsprung" in p["path"]
    P = pose(g, V if not isu else np.zeros((0, 3)), V if isu else np.zeros((0, 3)), f, s, T)
    mm = margin(P)
    print(f"[search]   {p['name']:24s} {len(V):5d} verts: min {mm.min() * 100:6.1f} cm, {np.mean(mm < 0) * 100:5.1f}% outside; worst at {np.round(P[mm.argmin()], 2)}")
