using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MiG29Tools
{
    // Clean damage-part seams. The skin used to go to the KR-67 damage parts a whole triangle at a time, by the triangle's centre,
    // and the model's long fuselage and wing triangles left saw-tooth edges: a part that came off was a fan of spikes, and with
    // single-sided skin the jet looked paper thin through every gap (user screenshots after being shot down, v0.9.2).
    //  - The parts are the hitbox hulls (Hulls below), so what breaks off is what was hit, along diagonal cuts like a stock jet's
    //    panels (KR-67 part boxes gave staircase outlines on the swept wings). A triangle that straddles a boundary is cut there,
    //    only by planes that actually separate parts; anything left uncertain is bisected down to ~20 cm.
    //  - Behind the skin every part gets an inside like the stock jets' (Interior, Engines), so burnt-through holes and broken
    //    edges show structure and engines instead of the sky (user screenshots, v0.9.2). Close-up level only, no shadows (MiG29Lod).
    static class MiG29Cut
    {
        public struct Vtx { public Vector3 p, n; public Vector2 uv; }
        public struct Plane : IEquatable<Plane>
        {
            public Vector3 n; public float d;                         // points p with n.p = d; n.p - d > 0 is outside
            public Plane(Vector3 n, float d)                          // rounded, so the same face found twice is one plane
            {
                var m = n.magnitude; n /= m; d /= m;
                this.n = new Vector3(Mathf.Round(n.x * 1e5f) / 1e5f, Mathf.Round(n.y * 1e5f) / 1e5f, Mathf.Round(n.z * 1e5f) / 1e5f);
                this.d = Mathf.Round(d * 1e5f) / 1e5f;
            }
            public Plane(int axis, float d) : this(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward, d) { }
            public float Dist(Vector3 p) => Vector3.Dot(n, p) - d;
            public bool Equals(Plane o) => n == o.n && d == o.d;
            public override int GetHashCode() => n.GetHashCode() * 397 ^ d.GetHashCode();
        }

        // The damage parts as convex hulls (each part's hitbox, MiG frame): a point belongs to the hull it is deepest inside, or
        // outside them all, the one it is least outside (the larger of its face distances). Boundaries are hull faces, or where
        // two hulls are equally far, the plane halfway between their nearest faces: flat cuts either way. `fixedPart` can claim
        // points first (the MiG fins, which the KR-67 has no hull for), with `fixedFaces` bounding its region.
        public class Hulls
        {
            public List<Plane>[] faces;
            public Func<Vector3, int> fixedPart;                      // -1: no claim
            public List<Plane> fixedFaces = new List<Plane>();

            public float Depth(int k, Vector3 p, out int face)
            {
                float m = float.MinValue; face = -1;
                var f = faces[k];
                for (int i = 0; i < f.Count; i++) { var s = f[i].Dist(p); if (s > m) { m = s; face = i; } }
                return m;
            }

            public int Label(Vector3 p)
            {
                if (fixedPart != null) { int o = fixedPart(p); if (o >= 0) return o; }
                int best = -1; float bd = float.MaxValue;
                for (int k = 0; k < faces.Length; k++)
                {
                    if (faces[k] == null || faces[k].Count == 0) continue;
                    var s = Depth(k, p, out _);
                    if (s < bd) { bd = s; best = k; }
                }
                return best;
            }

            // candidate cuts for a piece touching these parts: their faces, and the halfway planes between the faces that are
            // nearest at the piece's corners
            public IEnumerable<Plane> Candidates(HashSet<int> parts, List<Vtx> poly)
            {
                var set = new HashSet<Plane>(fixedFaces);
                foreach (var k in parts) if (faces[k] != null) set.UnionWith(faces[k]);
                var list = parts.Where(k => faces[k] != null && faces[k].Count > 0).ToList();
                foreach (var v in poly)
                    for (int i = 0; i < list.Count; i++)
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            Depth(list[i], v.p, out int fa); Depth(list[j], v.p, out int fb);
                            var A = faces[list[i]][fa]; var B = faces[list[j]][fb];
                            var nn = A.n - B.n;
                            if (nn.sqrMagnitude > 1e-6f) set.Add(new Plane(nn, A.d - B.d));
                        }
                return set;
            }
        }

        public class Bucket
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();
            public List<bool> seam = new List<bool>();                 // per triangle: a piece of a cut triangle (on a seam)
            readonly Dictionary<(int, int, int, int, int, int, int, int), int> weld = new Dictionary<(int, int, int, int, int, int, int, int), int>();

            public int Index(Vtx x)
            {
                var key = (Mathf.RoundToInt(x.p.x * 1e4f), Mathf.RoundToInt(x.p.y * 1e4f), Mathf.RoundToInt(x.p.z * 1e4f),
                           Mathf.RoundToInt(x.n.x * 1e3f), Mathf.RoundToInt(x.n.y * 1e3f), Mathf.RoundToInt(x.n.z * 1e3f),
                           Mathf.RoundToInt(x.uv.x * 1e5f), Mathf.RoundToInt(x.uv.y * 1e5f));
                if (weld.TryGetValue(key, out int i)) return i;
                i = v.Count; weld[key] = i;
                v.Add(x.p); n.Add(x.n); uv.Add(x.uv);
                return i;
            }
        }

        const float MinEdge = 0.2f;
        const float InnerInset = 0.002f;

        // pieces in world space (MiG frame); buckets per hull index
        public static Dictionary<int, Bucket> Split(IEnumerable<(Vector3[] p, Vector3[] n, Vector2[] uv, int[] tris)> pieces, Hulls hulls)
        {
            Func<Vector3, int> label = hulls.Label;
            var buckets = new Dictionary<int, Bucket>();
            int whole = 0, cut = 0, bisected = 0;
            void Emit(int part, Vtx a, Vtx b, Vtx c, bool seam)
            {
                if (Vector3.Cross(b.p - a.p, c.p - a.p).sqrMagnitude < 1e-12f) return;   // slivers left on a plane
                if (!buckets.TryGetValue(part, out var bk)) buckets[part] = bk = new Bucket();
                bk.t.Add(bk.Index(a)); bk.t.Add(bk.Index(b)); bk.t.Add(bk.Index(c));
                bk.seam.Add(seam);
            }
            foreach (var (p, n, uv, tris) in pieces)
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vtx V(int i) => new Vtx { p = p[i], n = n[i], uv = uv[i] };
                    var r = Triangle(V(tris[t]), V(tris[t + 1]), V(tris[t + 2]), hulls, Emit);
                    if (r == 0) whole++; else if (r == 1) cut++; else bisected++;
                }
            Debug.Log($"[MiG29] cut: {whole} triangles whole, {cut} cut along part planes, {bisected} bisected along curved boundaries");
            return buckets;
        }

        // 0 = one part, 1 = cut by planes, 2 = needed bisection somewhere
        static int Triangle(Vtx a, Vtx b, Vtx c, Hulls hulls, Action<int, Vtx, Vtx, Vtx, bool> emit)
        {
            Func<Vector3, int> label = hulls.Label;
            var labels = new HashSet<int> { label(a.p), label(b.p), label(c.p), label((a.p + b.p) / 2), label((b.p + c.p) / 2), label((c.p + a.p) / 2), label((a.p + b.p + c.p) / 3) };
            if (labels.Count == 1) { emit(labels.First(), a, b, c, false); return 0; }

            int result = 1;
            Divide(new List<Vtx> { a, b, c }, hulls, emit, 0, ref result);
            return result;
        }

        // parts seen in a convex piece: its centre, and its corners and edge midpoints pulled 5% toward the centre
        static HashSet<int> Labels(List<Vtx> poly, Func<Vector3, int> label)
        {
            var cen = poly.Aggregate(Vector3.zero, (s, v) => s + v.p) / poly.Count;
            var set = new HashSet<int> { label(cen) };
            for (int i = 0; i < poly.Count; i++)
            {
                set.Add(label(Vector3.Lerp(poly[i].p, cen, 0.05f)));
                set.Add(label(Vector3.Lerp((poly[i].p + poly[(i + 1) % poly.Count].p) / 2, cen, 0.05f)));
            }
            return set;
        }

        // cut only where a plane actually separates parts (best: both halves one part), so a long triangle crossed by many box
        // faces it does not care about stays whole
        static void Divide(List<Vtx> poly, Hulls hulls, Action<int, Vtx, Vtx, Vtx, bool> emit, int depth, ref int result)
        {
            Func<Vector3, int> label = hulls.Label;
            var ls = Labels(poly, label);
            if (ls.Count == 1)
            {
                for (int i = 1; i + 1 < poly.Count; i++) emit(ls.First(), poly[0], poly[i], poly[i + 1], true);
                return;
            }
            List<Vtx> bestN = null, bestP = null; int bestScore = -1;
            foreach (var pl in hulls.Candidates(ls, poly))
            {
                float dmin = float.MaxValue, dmax = float.MinValue;
                foreach (var v in poly) { var s = pl.Dist(v.p); dmin = Mathf.Min(dmin, s); dmax = Mathf.Max(dmax, s); }
                if (dmin > -1e-4f || dmax < 1e-4f) continue;                        // does not cross the piece
                var halves = new List<List<Vtx>>();
                Clip(poly, pl, halves);
                if (halves.Count != 2) continue;
                var sn = Labels(halves[0], label); var sp = Labels(halves[1], label);
                if (sn.SetEquals(sp)) continue;                                  // separates nothing
                int score = (sn.Count == 1 ? 2 : 0) + (sp.Count == 1 ? 2 : 0) + (sn.Count < ls.Count || sp.Count < ls.Count ? 1 : 0);
                if (score > bestScore) { bestScore = score; bestN = halves[0]; bestP = halves[1]; if (score >= 4) break; }
            }
            if (bestN == null || depth > 12)
            {
                for (int i = 1; i + 1 < poly.Count; i++) Bisect(poly[0], poly[i], poly[i + 1], label, emit, 0);
                result = 2;
                return;
            }
            Divide(bestN, hulls, emit, depth + 1, ref result);
            Divide(bestP, hulls, emit, depth + 1, ref result);
        }

        static void Clip(List<Vtx> poly, Plane pl, List<List<Vtx>> outList)
        {
            var neg = new List<Vtx>(); var pos = new List<Vtx>();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                float dp = pl.Dist(p.p), dq = pl.Dist(q.p);
                if (dp <= 0) neg.Add(p);
                if (dp >= 0) pos.Add(p);
                if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
                {
                    var x = OnPlane(p, q, pl);
                    neg.Add(x); pos.Add(x);
                }
            }
            if (neg.Count >= 3) outList.Add(neg);
            if (pos.Count >= 3) outList.Add(pos);
        }

        // the same point whichever triangle of an edge computes it (no cracks along shared edges)
        static Vtx OnPlane(Vtx p, Vtx q, Plane pl)
        {
            if (Less(q.p, p.p)) { var s = p; p = q; q = s; }
            float dp = pl.Dist(p.p), dq = pl.Dist(q.p);
            return Mix(p, q, dp / (dp - dq));
        }

        static bool Less(Vector3 a, Vector3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        static Vtx Mix(Vtx p, Vtx q, float f) =>
            new Vtx { p = Vector3.Lerp(p.p, q.p, f), n = Vector3.Lerp(p.n, q.n, f).normalized, uv = Vector2.Lerp(p.uv, q.uv, f) };

        static void Bisect(Vtx a, Vtx b, Vtx c, Func<Vector3, int> label, Action<int, Vtx, Vtx, Vtx, bool> emit, int depth)
        {
            var cen = (a.p + b.p + c.p) / 3f; int l = label(cen);
            float ab = (a.p - b.p).sqrMagnitude, bc = (b.p - c.p).sqrMagnitude, ca = (c.p - a.p).sqrMagnitude;
            float longest = Mathf.Sqrt(Mathf.Max(ab, Mathf.Max(bc, ca)));
            bool uniform = label(Vector3.Lerp(a.p, cen, 0.05f)) == l && label(Vector3.Lerp(b.p, cen, 0.05f)) == l && label(Vector3.Lerp(c.p, cen, 0.05f)) == l;
            if (uniform || longest < MinEdge || depth > 18) { emit(l, a, b, c, true); return; }
            // split the longest edge
            if (ab >= bc && ab >= ca) { var m = Mid(a, b); Bisect(a, m, c, label, emit, depth + 1); Bisect(m, b, c, label, emit, depth + 1); }
            else if (bc >= ca) { var m = Mid(b, c); Bisect(a, b, m, label, emit, depth + 1); Bisect(a, m, c, label, emit, depth + 1); }
            else { var m = Mid(c, a); Bisect(a, b, m, label, emit, depth + 1); Bisect(m, b, c, label, emit, depth + 1); }
        }

        static Vtx Mid(Vtx p, Vtx q) { if (Less(q.p, p.p)) { var s = p; p = q; q = s; } return Mix(p, q, 0.5f); }

        // The part's inside, as on the stock jets (each KR-67 part mesh carries an "aircraft_structure" submesh behind its skin, and
        // "honeycomb" in the radome). A shell lining the skin alone left the jet hollow: through a hole you looked across the whole
        // fuselage, the nose gear from behind the cockpit (user, v0.9.2). So the inside is an inner hull: the part's skin again, moved
        // in along its normal by up to InnerMax (a third of the airframe's thickness there, measured by `depth`, so it never reaches the
        // far skin of a thin wing or fin) and two-sided. Through a hole you see structure a few centimetres behind the skin; where a
        // part has broken away you look into a structure-walled shell. Box-projected UVs at the stock texel density (0.46 tiles per
        // metre), one vertex colour (the stock structure shader tints by it). World space in and out, like the bucket.
        public const float InnerMax = 0.10f;
        public static (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color32> c, List<int> t) Interior(Bucket b, float tilesPerMetre, Color32 tint, Func<Vector3, Vector3, float> depth)
        {
            // one inset per position (the bucket splits vertices at UV seams; separate insets would crack the hull there)
            var key2 = new Dictionary<(int, int, int), int>(); var pid = new int[b.v.Count]; var nsum = new List<Vector3>(); var ppos = new List<Vector3>();
            for (int i = 0; i < b.v.Count; i++)
            {
                var k = (Mathf.RoundToInt(b.v[i].x * 1e4f), Mathf.RoundToInt(b.v[i].y * 1e4f), Mathf.RoundToInt(b.v[i].z * 1e4f));
                if (!key2.TryGetValue(k, out int p)) { p = ppos.Count; key2[k] = p; ppos.Add(b.v[i]); nsum.Add(Vector3.zero); }
                pid[i] = p; nsum[p] += b.n[i];
            }
            // which way is in: the model's normals point inward in places (and the inset then went 10 cm OUT of the skin: structure
            // standing off the nacelles and the nozzle covers, user screenshots); so a ray each way, and the side that finds the far
            // skin is the inside. Thin open sheets (no far skin either way: flaps, the covers over the nozzles) get no inner hull.
            var inner = new Vector3[ppos.Count]; var inN = new Vector3[ppos.Count]; var open = new bool[ppos.Count];
            for (int p = 0; p < ppos.Count; p++)
            {
                var nn = nsum[p].sqrMagnitude > 1e-8f ? nsum[p].normalized : Vector3.up;
                float d = depth(ppos[p], -nn);                                   // distance to the airframe's far side, inward
                if (d < 0f) { float d2 = depth(ppos[p], nn); if (d2 > 0f) { nn = -nn; d = d2; } }
                if (d < 0f) { open[p] = true; inner[p] = ppos[p]; inN[p] = nn; continue; }
                inN[p] = nn;
                inner[p] = ppos[p] - nn * Mathf.Clamp(d * 0.33f, InnerInset, InnerMax);
            }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color32>(); var t = new List<int>();
            for (int i = 0; i < b.t.Count; i += 3)
            {
                if (open[pid[b.t[i]]] || open[pid[b.t[i + 1]]] || open[pid[b.t[i + 2]]]) continue;
                var a0 = inner[pid[b.t[i]]]; var a1 = inner[pid[b.t[i + 1]]]; var a2 = inner[pid[b.t[i + 2]]];
                var fn = Vector3.Cross(a1 - a0, a2 - a0);
                if (fn.sqrMagnitude < 1e-12f) continue;
                var a = new Vector3(Mathf.Abs(fn.x), Mathf.Abs(fn.y), Mathf.Abs(fn.z));
                Vector2 Q(Vector3 p) => (a.x >= a.y && a.x >= a.z ? new Vector2(p.z, p.y) : a.y >= a.z ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y)) * tilesPerMetre;
                // outward face (seen through holes in the skin, like the skin's own winding) and inward face (seen into a broken part)
                for (int side = 0; side < 2; side++)
                {
                    int b0 = v.Count;
                    foreach (var k in side == 0 ? new[] { i, i + 1, i + 2 } : new[] { i, i + 2, i + 1 })
                    {
                        var nn = inN[pid[b.t[k]]];
                        v.Add(inner[pid[b.t[k]]]); n.Add(side == 0 ? nn : -nn); uv.Add(Q(inner[pid[b.t[k]]])); col.Add(tint);
                    }
                    t.Add(b0); t.Add(b0 + 1); t.Add(b0 + 2);
                }
            }
            return (v, n, uv, col, t);
        }
        // RD-33 engine bodies inside the nacelles ("jet_mechanical", like the stock jets' internals): a ring-segmented casing along
        // each engine axis with the fan face in front, every 0.4 m segment on the part its centre lies in. World space.
        public static Dictionary<int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)> Engines(
            IEnumerable<(Vector3 rear, Vector3 fwd)> axes, float length, float radius, Func<Vector3, int> label)
        {
            const int Sides = 16; const float Seg = 0.4f;
            var outp = new Dictionary<int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)>();
            (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t) Get(int k)
            {
                if (!outp.TryGetValue(k, out var m)) outp[k] = m = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
                return m;
            }
            foreach (var (rear, fwd) in axes)
            {
                var side = Vector3.Cross(fwd, Vector3.up).normalized; var up = Vector3.Cross(side, fwd);
                int segs = Mathf.CeilToInt(length / Seg);
                for (int s = 0; s < segs; s++)
                {
                    float z0 = s * Seg, z1 = Mathf.Min(length, z0 + Seg);
                    var m = Get(label(rear + fwd * (z0 + z1) / 2));
                    int b0 = m.v.Count;
                    for (int i = 0; i <= Sides; i++)
                    {
                        float ang = i * Mathf.PI * 2 / Sides;
                        var rad = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                        // casing band of the jet_mechanical atlas (v 0.80-0.97), twice round
                        float u = i * 2f / Sides;
                        m.v.Add(rear + fwd * z0 + rad * radius); m.n.Add(rad); m.uv.Add(new Vector2(u, 0.80f));
                        m.v.Add(rear + fwd * z1 + rad * radius); m.n.Add(rad); m.uv.Add(new Vector2(u, 0.97f));
                    }
                    for (int i = 0; i < Sides; i++)
                    {
                        int a = b0 + i * 2;
                        m.t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });   // clockwise seen from outside (Unity front face)
                    }
                }
                // fan face at the front: the fan disc of the atlas (centre 0.80, 0.52, radius 0.085)
                var f = Get(label(rear + fwd * (length - 0.2f)));
                int c0 = f.v.Count; var front = rear + fwd * length;
                f.v.Add(front); f.n.Add(fwd); f.uv.Add(new Vector2(0.80f, 0.52f));
                for (int i = 0; i <= Sides; i++)
                {
                    float ang = i * Mathf.PI * 2 / Sides;
                    var rad = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    f.v.Add(front + rad * radius); f.n.Add(fwd); f.uv.Add(new Vector2(0.80f + 0.085f * Mathf.Cos(ang), 0.52f + 0.085f * Mathf.Sin(ang)));
                }
                for (int i = 0; i < Sides; i++) f.t.AddRange(new[] { c0, c0 + 2 + i, c0 + 1 + i });
            }
            return outp;
        }
        // MIG29_SPLIT_DUMP=<folder>: every part's skin (and inside) as an OBJ in world metres, for checking the seams in Blender
        public static void Dump(string name, List<Vector3> v, List<int> t)
        {
            var dir = Environment.GetEnvironmentVariable("MIG29_SPLIT_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            using (var w = new StreamWriter(Path.Combine(dir, name + ".obj")))
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var p in v) w.WriteLine(string.Format(ci, "v {0} {1} {2}", p.x, p.y, p.z));
                for (int i = 0; i < t.Count; i += 3) w.WriteLine($"f {t[i] + 1} {t[i + 1] + 1} {t[i + 2] + 1}");
            }
        }
    }
}
