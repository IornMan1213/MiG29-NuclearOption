"""Hidden space inside the MiG skin (body + closed gear doors), MiG frame, for the gear stow searches and the wheel-well meshes.

Columns on an (x, z) grid are cast upward through the skin; a span runs from a skin face seen from outside (entering) to the next
face seen from inside (leaving). Inner faces in between (the model's shallow well roofs) are ignored: once the doors close, anything
between the outer skins is hidden. signed_margin() turns that into a voxel field: distance to the nearest outside voxel (positive,
hidden) or minus the distance to the nearest hidden voxel."""
import json, os
import numpy as np


def skin_triangles(src, parts=("body", "gear_doors_closed")):
    d = json.load(open(os.path.join(src, "mig29_mesh.json")))
    out = []
    for name in parts:
        p = next(p for p in d["parts"] if p["name"] == name)
        V = np.array(p["vertices"], float).reshape(-1, 3); T = np.array(p["triangles"]).reshape(-1, 3)
        out.append(V[T])
    return np.concatenate(out)


class Columns:
    def __init__(self, tris, x0, x1, z0, z1, h):
        c = tris.mean(1)
        pad = 0.6
        tris = tris[(c[:, 0] > x0 - pad) & (c[:, 0] < x1 + pad) & (c[:, 2] > z0 - pad) & (c[:, 2] < z1 + pad)]
        self.A = tris[:, 0]; self.E1 = tris[:, 1] - self.A; self.E2 = tris[:, 2] - self.A
        self.xs = np.round(np.arange(x0, x1 + 1e-6, h), 4); self.zs = np.round(np.arange(z0, z1 + 1e-6, h), 4); self.h = h
        self.spans = {}
        for i, z in enumerate(self.zs):
            for j, x in enumerate(self.xs):
                self.spans[i, j] = self._spans(x, z)

    def _spans(self, x, z, y0=-3.0):
        o = np.array([x, y0, z]); dv = np.array([0.0, 1.0, 0.0])
        p = np.cross(dv, self.E2); det = (self.E1 * p).sum(1); ok = np.abs(det) > 1e-10
        inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
        s = o - self.A; u = (s * p).sum(1) * inv; q = np.cross(s, self.E1); v = (dv * q).sum(1) * inv; t = (self.E2 * q).sum(1) * inv
        m = ok & (u >= -1e-7) & (v >= -1e-7) & (u + v <= 1 + 1e-7) & (t > 0)
        order = np.argsort(t[m]); ys = t[m][order] + y0
        # Unity front faces wind clockwise; with this cross-product order, det > 0 means the ray meets the face from outside
        enter = det[m][order] > 0
        spans = []; lo = None
        for y, e in zip(ys, enter):
            if e and lo is None: lo = y
            elif not e and lo is not None:
                if y - lo > 0.004: spans.append((lo, y))
                lo = None
        return spans

    def voxels(self, y0, y1):
        ys = np.round(np.arange(y0, y1 + 1e-6, self.h), 4)
        vox = np.zeros((len(self.zs), len(self.xs), len(ys)), bool)
        for (i, j), sp in self.spans.items():
            for lo, hi in sp:
                vox[i, j, (ys > lo) & (ys < hi)] = True
        return ys, vox


def signed_margin(vox, h, steps=10):
    """Approximate signed distance (m): + inside the hidden space, - outside, clamped at +-steps*h (26-neighbour chamfer)."""
    big = steps * h * 2
    offs = [(a, b, c) for a in (-1, 0, 1) for b in (-1, 0, 1) for c in (-1, 0, 1) if (a, b, c) != (0, 0, 0)]

    def dist(mask):            # distance from each True voxel to the nearest False voxel
        d = np.where(mask, big, 0.0)
        for _ in range(steps):
            nd = d.copy()
            for a, b, c in offs:
                w = h * np.sqrt(a * a + b * b + c * c)
                sh = np.roll(np.roll(np.roll(d, a, 0), b, 1), c, 2) + w
                nd = np.minimum(nd, sh)
            d = np.where(mask, nd, 0.0)
        return d
    return np.minimum(dist(vox), steps * h) - np.minimum(dist(~vox), steps * h)


class Hidden:
    """Voxels no outside eye can reach: skin triangles (plus extra wall boxes such as the intake blockers) are rasterised into wall
    voxels, sealed by one voxel of dilation, and air is flood-filled from everything outside the skin envelope (below the lowest or
    above the highest skin crossing of each column). What the flood never reaches is hidden: inside the skin, the closed bays, and
    the intake ducts behind the blockers."""

    def __init__(self, tris, lo, hi, h, boxes=()):
        self.lo = np.asarray(lo, float); self.h = h
        self.shape = tuple(np.ceil((np.asarray(hi, float) - self.lo) / h).astype(int) + 1)
        wall = np.zeros(self.shape, bool)
        c = tris.mean(1)
        sel = np.all((c > self.lo - 0.5) & (c < np.asarray(hi) + 0.5), axis=1)
        for t in tris[sel]:
            a, b, cc = t
            n = int(np.ceil(max(np.linalg.norm(b - a), np.linalg.norm(cc - a), np.linalg.norm(cc - b)) / (h * 0.5))) + 1
            u, v = np.meshgrid(np.linspace(0, 1, n + 1), np.linspace(0, 1, n + 1))
            m = u + v <= 1
            pts = a + np.outer(u[m], b - a) + np.outer(v[m], cc - a)
            self._mark(wall, pts)
        for bmin, bmax in boxes:
            g = [np.arange(bmin[k], bmax[k] + h * 0.5, h * 0.5) for k in range(3)]
            pts = np.stack(np.meshgrid(*g, indexing="ij"), -1).reshape(-1, 3)
            self._mark(wall, pts)
        wall = wall | self._grow(wall)
        # seeds: voxels outside the skin envelope of their column (x, z)
        env_lo = np.full(self.shape[0:3:2], np.inf); env_hi = np.full(self.shape[0:3:2], -np.inf)
        idx = np.argwhere(wall)
        np.minimum.at(env_lo, (idx[:, 0], idx[:, 2]), idx[:, 1]); np.maximum.at(env_hi, (idx[:, 0], idx[:, 2]), idx[:, 1])
        ky = np.arange(self.shape[1])[None, :, None]
        air = (ky < env_lo[:, None, :]) | (ky > env_hi[:, None, :])
        air &= ~wall
        free = ~wall
        while True:
            grown = (air | self._grow(air)) & free
            if grown.sum() == air.sum(): break
            air = grown
        self.wall, self.air = wall, air
        self.hidden = ~air & ~wall

    def _mark(self, vox, pts):
        k = np.round((pts - self.lo) / self.h).astype(int)
        ok = np.all((k >= 0) & (k < np.array(self.shape)), axis=1)
        k = k[ok]; vox[k[:, 0], k[:, 1], k[:, 2]] = True

    @staticmethod
    def _grow(m):
        g = m.copy()
        g[1:, :, :] |= m[:-1, :, :]; g[:-1, :, :] |= m[1:, :, :]
        g[:, 1:, :] |= m[:, :-1, :]; g[:, :-1, :] |= m[:, 1:, :]
        g[:, :, 1:] |= m[:, :, :-1]; g[:, :, :-1] |= m[:, :, 1:]
        return g

    def index(self, P):
        return np.round((np.asarray(P) - self.lo) / self.h).astype(int)
