"""Intake ducts that reach the engines. The model's ducts are a low-poly tube that ends 3.4 m behind the lower lip in a flat
12-sided cap (z 0.746), still a metre short of the RD-33's front face, and its walls carry the livery at full brightness. Stock jets
whose ducts reach the engine (T/A-30, Darkreach, Alkyon) darken them toward the back and end them on the engine's own face.

This measures the model duct from its centreline (rays every 360/M degrees, every 0.1 m), and builds a liner 1.5 cm inside it: from
just behind the lip, along the model walls to the cap, then on through a smooth transition to the engine front (MiG29Cut.Engines:
radius EngineRadius at the front of the EngineLength body), where the engine's fan disc closes it. A dark disc just behind the fan
face closes it beyond the distance the fan disc is drawn. The builder removes the model's cap and fits the liner (MiG29Duct.cs);
bay_grid.skin_triangles sees the same, so the well and bay tools treat the open duct as outside air.

UV: u round the duct, v = depth along it / DEPTH_UV metres; duct_basecolor.png and duct_ao.png are depth gradients (grey at the lip,
near black from ~3 m), the darkening stock ducts get from their AO maps.
python intake_duct.py <MiG29Source>  ->  MiG29Source/intake_duct.json (both ducts, MiG frame), MiG29Source/duct_basecolor.png,
duct_ao.png, duct_metallic.png"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
CAP_Z = 0.746                                  # the model's duct end cap (all 20 triangles lie in this plane)
ENGINE_LENGTH, ENGINE_RADIUS = 2.6, 0.42       # keep in step with MiG29Builder.EngineLength / EngineRadius
END_RADIUS = 0.415                             # inside the engine casing, under the fan disc's rim
INSET = 0.015
M = 64                                         # points round the duct
Z_FRONT, Z_LAST, DZ = 4.05, 0.80, 0.10         # measured stations (left duct, MiG frame)
TRANSITION = 14                                # rings from the last station to the engine front
MOUTH = (-0.832, 4.164, 1.668)                 # the raked mouth: z = z0 + (y - y0) * slope (both side-wall leading edges, lower lip to roof)
DEPTH_UV = 6.0

mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in mesh["parts"] if p["name"] == "body")
V = np.array(body["vertices"]).reshape(-1, 3); T = np.array(body["triangles"]).reshape(-1, 3)
P0 = V[T[:, 0]]; E1 = V[T[:, 1]] - P0; E2 = V[T[:, 2]] - P0
cap = np.all(np.abs(V[T][:, :, 2] - CAP_Z) < 0.005, axis=1)
P0, E1, E2 = P0[~cap], E1[~cap], E2[~cap]      # the rays look past the cap


def ray(o, d):
    """Distance to the first body triangle along d from o (Moller-Trumbore over every triangle), or inf."""
    h = np.cross(d, E2); a = np.einsum("ij,ij->i", E1, h); ok = np.abs(a) > 1e-12
    f = np.where(ok, 1.0 / np.where(ok, a, 1.0), 0.0); s = o - P0
    u = f * np.einsum("ij,ij->i", s, h); q = np.cross(s, E1); v = f * (q @ d); t = f * np.einsum("ij,ij->i", E2, q)
    m = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-4)
    return t[m].min() if m.any() else np.inf


ANG = np.linspace(0, 2 * np.pi, M, endpoint=False)
DIRS = np.stack([np.cos(ANG), np.sin(ANG), np.zeros(M)], 1)


def section(z, centre):
    """Wall distances round the duct at station z, and the centre of the walls found (two passes)."""
    c = np.array([centre[0], centre[1], z])
    for _ in range(2):
        d = np.array([ray(c, k) for k in DIRS])
        if not np.all(np.isfinite(d) & (d < 0.6)): raise SystemExit(f"intake_duct: open wall at z {z:.2f}")
        c = np.append((c[:2] + d[:, None] * DIRS[:, :2]).mean(0), z)
    return c, np.array([ray(c, k) for k in DIRS])


# --- the model duct, left side: measured stations from the lip region back to the cap ---
centres, dist = [], []
c = np.array([-0.72, -0.56])
for z in np.arange(Z_FRONT, Z_LAST - 1e-6, -DZ):
    cc, d = section(z, c); c = cc[:2]
    centres.append(cc); dist.append(d)
# a ray that slips through a gap between the model's roof panels (a 6 cm slot at z 3.84..3.90) finds the skin above instead: any
# station further out than both its neighbours by more than 2 cm takes their mean
dist = np.array(dist)
for r in range(1, len(dist) - 1):
    mid = (dist[r - 1] + dist[r + 1]) / 2
    spike = dist[r] - mid > 0.02
    dist[r][spike] = mid[spike]
rings = [cc + DIRS * (d - INSET)[:, None] for cc, d in zip(centres, dist)]
# the front edge: each line along the wall carried forward until it runs out of duct or reaches the raked mouth (the roof goes on
# forward as the wing-root underside, outside the intake; below the lip's height the floor ends where the rays run out), stopping
# 3 cm short of the lip
c0 = centres[0]; front = []
for k in range(M):
    z, last = Z_FRONT, rings[0][k]
    while z < 5.4:
        z += 0.02; t = ray(np.array([c0[0], c0[1], z]), DIRS[k])
        if not (np.isfinite(t) and t < 0.6): break
        p = np.array([c0[0], c0[1], z]) + DIRS[k] * (t - INSET)
        if p[1] >= MOUTH[0] and p[2] > MOUTH[1] + (p[1] - MOUTH[0]) * MOUTH[2]: break
        last = p
    front.append(last - np.array([0, 0, 0.03]) if last[2] - 0.03 > Z_FRONT + 0.02 else rings[0][k] + np.array([0, 0, 0.02]))
rings.insert(0, np.array(front)); centres.insert(0, c0)

# --- transition: from the last station to the engine front, a smooth path, the section easing to the round engine ---
lay = json.load(open(os.path.join(SRC, "nozzles.json")))["layout"]
root = np.array([-lay["axis_root"][0], lay["axis_root"][1], lay["axis_root"][2]])
end = np.array([-lay["axis_end"][0], lay["axis_end"][1], lay["axis_end"][2]])
fwd = (root - end) / np.linalg.norm(root - end)
F = root + fwd * ENGINE_LENGTH
cL = centres[-1]; aft = centres[-1] - centres[-2]; aft /= np.linalg.norm(aft)
dL = np.linalg.norm(rings[-1] - cL, axis=1)
span = np.linalg.norm(F - cL)
m0, m1 = span * aft, -span * fwd                                                      # Hermite tangents: leaving the duct, entering the engine
for i in range(1, TRANSITION + 1):
    s = i / TRANSITION; e = s * s * (3 - 2 * s)
    p = (2 * s**3 - 3 * s**2 + 1) * cL + (s**3 - 2 * s**2 + s) * m0 + (-2 * s**3 + 3 * s**2) * F + (s**3 - s**2) * m1
    tan = (6 * s**2 - 6 * s) * cL + (3 * s**2 - 4 * s + 1) * m0 + (-6 * s**2 + 6 * s) * F + (3 * s**2 - 2 * s) * m1
    tan /= np.linalg.norm(tan)
    # the ring's frame: y as near up as the path allows, x right (the stations' frame, so the angles line up)
    ey = np.array([0.0, 1.0, 0.0]) - tan * tan[1]; ey /= np.linalg.norm(ey); ex = np.cross(tan, ey)
    r = (1 - e) * dL + e * END_RADIUS
    rings.append(p + (np.cos(ANG)[:, None] * ex + np.sin(ANG)[:, None] * ey) * r[:, None]); centres.append(p)

# --- keep the liner inside the model walls: their facets kink inward between the measured points, where a liner edge would pass
# behind the wall and the livery show through. Any liner edge (round, along, or the quads' diagonal) that crosses a model triangle
# moves both its ends 1 cm toward the duct centre, until none does ---
R = len(rings)
pts = np.array(rings)                                                                 # R x M x 3
lo, hi = pts.reshape(-1, 3).min(0) - 0.1, pts.reshape(-1, 3).max(0) + 0.1
near = np.all((P0 > lo) & (P0 < hi), axis=1)
Q0, Q1, Q2 = P0[near], E1[near], E2[near]


def crosses(a, b):
    """For each segment a[i]-b[i], whether it passes through one of the nearby model triangles."""
    out = np.zeros(len(a), bool)
    for i, (p, q) in enumerate(zip(a, b)):
        d = q - p; L = np.linalg.norm(d); d = d / L
        h = np.cross(d, Q2); aa = np.einsum("ij,ij->i", Q1, h); ok = np.abs(aa) > 1e-12
        f = np.where(ok, 1.0 / np.where(ok, aa, 1.0), 0.0); s = p - Q0
        u = f * np.einsum("ij,ij->i", s, h); qq = np.cross(s, Q1); v = f * (qq @ d); t = f * np.einsum("ij,ij->i", Q2, qq)
        out[i] = np.any(ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-4) & (t < L - 1e-4))
    return out


rr, kk = np.meshgrid(np.arange(R), np.arange(M), indexing="ij")
EDGES = [((rr, kk), (rr, (kk + 1) % M)),                                              # round
         ((rr[:-1], kk[:-1]), (rr[1:], kk[1:])),                                      # along
         ((rr[:-1], (kk[:-1] + 1) % M), (rr[1:], kk[1:]))]                            # diagonal
moved = np.zeros((R, M))
for it in range(20):
    bad = np.zeros((R, M), bool)
    for (ra, ka), (rb, kb) in EDGES:
        ra, ka, rb, kb = ra.ravel(), ka.ravel(), rb.ravel(), kb.ravel()
        x = crosses(pts[ra, ka], pts[rb, kb])
        bad[ra[x], ka[x]] = True; bad[rb[x], kb[x]] = True
    if not bad.any(): break
    for r, k in zip(*np.where(bad)):
        c = np.array(centres[r]); v = c - pts[r, k]; pts[r, k] += v / np.linalg.norm(v) * 0.01; moved[r, k] += 0.01
else:
    raise SystemExit(f"intake_duct: liner still crosses the model walls at {int(bad.sum())} points")
print(f"intake_duct: {int((moved > 0).sum())} liner points moved in (up to {moved.max() * 100:.0f} cm) to clear the model walls")

# --- mesh: rings joined round and along; normals from the neighbouring points (the sections are rounded rectangles: radial normals
# shaded the flat walls as curved), turned inward; v by depth along each wall line ---
along = pts[np.minimum(np.arange(R) + 1, R - 1)] - pts[np.maximum(np.arange(R) - 1, 0)]
round_ = np.roll(pts, -1, axis=1) - np.roll(pts, 1, axis=1)
nrm = np.cross(round_, along); nrm /= np.linalg.norm(nrm, axis=2)[:, :, None]
inward = np.array([[c[0], c[1], 0.0] for c in centres])[:, None, :] - pts * np.array([1.0, 1.0, 0.0])
if np.einsum("rkj,rkj->", nrm, inward) < 0: nrm = -nrm
depth = np.zeros((R, M))
for r in range(1, R): depth[r] = depth[r - 1] + np.linalg.norm(pts[r] - pts[r - 1], axis=1)
verts, norms, uvs, tris = [], [], [], []
for r in range(R):
    for k in range(M + 1):                                                            # seam column repeated for the UVs
        kk = k % M; p = pts[r, kk]
        verts.append(p); norms.append(nrm[r, kk]); uvs.append((k / M, depth[r, kk] / DEPTH_UV))
for r in range(R - 1):
    for k in range(M):
        a, b, c, d = r * (M + 1) + k, r * (M + 1) + k + 1, (r + 1) * (M + 1) + k, (r + 1) * (M + 1) + k + 1
        tris += [(a, b, c), (b, d, c)]
# the end disc, 2 cm behind the engine front (hidden by the fan disc up close), facing forward
cd = F - fwd * 0.02; i0 = len(verts)
verts.append(cd); norms.append(fwd); uvs.append((0.5, 0.995))
for k in range(M):
    verts.append(cd + (pts[-1, k] - F)); norms.append(fwd); uvs.append((k / M, 0.995))
for k in range(M): tris.append((i0, i0 + 1 + k, i0 + 1 + (k + 1) % M))


def flange(width):
    """The front ring and the same ring pushed out toward the model wall by width(k): a band facing the mouth."""
    out = pts[0] - centres[0]; out[:, 2] = 0; out /= np.linalg.norm(out, axis=1)[:, None]
    return np.concatenate([pts[0], pts[0] + out * width[:, None]])


# the front edge closed against the model wall (the liner sits 1.5 cm or more inside it): a slit there would show the livery. The
# wall is measured again at each front point's own height (it flares toward the lip)
gap = np.array([ray(np.array([c0[0], c0[1], pts[0, k, 2]]), DIRS[k]) - np.linalg.norm(pts[0, k, :2] - c0[:2]) for k in range(M)])
gap = np.where(np.isfinite(gap) & (gap > 0) & (gap < 0.2), gap, INSET + moved[0])
fl = flange(gap - 0.003); i0 = len(verts)
for k in range(2 * M): verts.append(fl[k]); norms.append(np.array([0.0, 0.0, 1.0])); uvs.append(((k % M) / M, 0.0))
for k in range(M):
    a, b, c, d = i0 + k, i0 + (k + 1) % M, i0 + M + k, i0 + M + (k + 1) % M
    tris += [(a, b, c), (b, d, c)]
verts, norms, tris = np.array(verts), np.array(norms), np.array(tris)
# for the hidden-space tools only (bay_grid): a wider band, well past the model wall, so their flood fill cannot slip between the
# liner and the model walls (which end at the old cap) into the fuselage
sl = flange(np.full(M, 0.10))
seal_t = np.array([(k, (k + 1) % M, M + k) for k in range(M)] + [((k + 1) % M, M + (k + 1) % M, M + k) for k in range(M)])
# winding: the mesh's convention (MiG29Source triangles: cross(b - a, c - a) along the normal)
fn = np.cross(verts[tris[:, 1]] - verts[tris[:, 0]], verts[tris[:, 2]] - verts[tris[:, 0]])
flip = np.einsum("ij,ij->i", fn, norms[tris].mean(1)) < 0
tris[flip] = tris[flip][:, [0, 2, 1]]
print(f"intake_duct: {R} rings x {M}, {len(tris)} triangles per side; front z {pts[0, :, 2].min():.2f}..{pts[0, :, 2].max():.2f}, "
      f"cap at {CAP_Z} removed ({int(cap.sum())} triangles), engine front {np.round(F, 3).tolist()}, longest wall {depth[-1].max():.2f} m")


def side(sx):
    """The left duct as built (sx 1), or mirrored to the right (sx -1)."""
    m = np.array([sx, 1.0, 1.0])
    t = tris if sx > 0 else tris[:, [0, 2, 1]]                                        # a mirror turns the faces inside out
    return dict(vertices=np.round(verts * m, 5).ravel().tolist(), normals=np.round(norms * m, 5).ravel().tolist(),
                uvs=np.round(np.array(uvs), 5).ravel().tolist(), triangles=t.ravel().tolist(),
                seal=dict(vertices=np.round(sl * m, 5).ravel().tolist(), triangles=seal_t.ravel().tolist()))


json.dump(dict(cap_z=CAP_Z, L=side(1.0), R=side(-1.0)), open(os.path.join(SRC, "intake_duct.json"), "w"))

# --- the depth gradients: colour (sRGB) and ambient occlusion (linear, as the stock _AO maps). The game lights the inside of a duct
# as brightly as the outside (sky light is not shadowed), so the darkening is mostly in the AO, as stock ducts have it (AO ~0.3-0.6 at
# the lip to ~0.03-0.16 deep); a low gloss keeps sky reflections off the walls ---
from PIL import Image
H = 256
d = (np.arange(H) + 0.5) / H * DEPTH_UV
col = 0.10 + 0.30 * np.exp(-d / 1.2)
ao = 0.06 + 0.60 * np.exp(-d / 1.0)
def column(v, tint=(1.0, 1.0, 1.0)):   # v up: row 0 is the top of the image
    row = np.clip(np.stack([v * tint[0], v * tint[1], v * tint[2]], 1) * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(np.repeat(row[:, None, :], 4, axis=1)[::-1])
column(col, (1.0, 1.0, 1.03)).save(os.path.join(SRC, "duct_basecolor.png"))
column(ao).save(os.path.join(SRC, "duct_ao.png"))
Image.new("RGBA", (8, 8), (0, 0, 0, 30)).save(os.path.join(SRC, "duct_metallic.png"))   # metallic 0, smoothness 0.12
