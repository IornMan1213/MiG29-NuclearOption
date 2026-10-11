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
    //  - Along every seam the skin gets a dark inside (the cut triangles' back faces, 4 mm in), so a broken edge reads as a wall
    //    with depth instead of a paper-thin rim with the sky behind it. Close-up level only, no shadows (MiG29Lod).
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
        const float InnerInset = 0.004f;

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

        // The dark inside along the seams: the bucket's cut triangles reversed, normals flipped, all on one flat atlas cell, inset
        // a few mm. World space in and out, like the bucket.
        public static (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t) Inner(Bucket b, Vector2 cell)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var map = new Dictionary<int, int>();
            int Add(int i)
            {
                if (map.TryGetValue(i, out int j)) return j;
                j = v.Count; map[i] = j;
                v.Add(b.v[i] - b.n[i] * InnerInset); n.Add(-b.n[i]); uv.Add(cell);
                return j;
            }
            for (int i = 0; i < b.t.Count; i += 3)
            {
                if (!b.seam[i / 3]) continue;
                t.Add(Add(b.t[i])); t.Add(Add(b.t[i + 2])); t.Add(Add(b.t[i + 1]));
            }
            return (v, n, uv, t);
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
