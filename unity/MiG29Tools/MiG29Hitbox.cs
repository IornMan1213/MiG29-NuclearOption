using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Hitboxes fitted to the MiG's own wings, tail surfaces and fins. The KR-67 hulls (x * 0.8) stay on the fuselage parts, where
    // they follow the MiG well enough; on the flying surfaces they sat up to 2 m off the skin (the stabilators were inside nothing,
    // the KR-67's canted tails floated 1.5 m above them, the fins had no hull above the booms).
    //
    // v0.2-0.4 used MiG-shaped hulls and blew apart on spawn: every part is its own rigidbody, only jointed neighbours ignore each
    // other, and those hulls overlapped. So the hulls here are built to be disjoint: each is a box-like hull around the part's own
    // skin (planes along 15 fixed directions, never thinner than MinThick), then every pair that comes closer than Gap is cut apart
    // along the direction that loses the least skin, and the build fails if any pair ends up closer than MinGap.
    //
    // A part's centre of mass is the centre of its collider (AeroPart.CreateRB sets only the mass), and lift acts at that centre
    // plus centerOfLift. Compensate() keeps every lift point and the aircraft's centre of gravity where the flight model was tuned.
    public static class MiG29Hitbox
    {
        public const float Gap = 0.05f, MinGap = 0.04f, MinThick = 0.12f;
        public const float FinFloorY = 0.45f;   // prefab frame: fin skin above this is the fin, below it the boom (as SplitAirframe)
        public static readonly string[] LeftParts = { "aileron_L", "elevator_L", "flap_L", "tail_L", "wing1_L", "wing1_FL", "wing2_L", "wing2_FL", "wingtip_L" };
        public static string Mirror(string name) => name.Substring(0, name.Length - 1) + (name.EndsWith("L") ? "R" : "L");

        public class Source { public List<Vector3> pts = new List<Vector3>(), cen = new List<Vector3>(); public List<float> area = new List<float>(); }
        public class Hull { public Vector3[] v; public int[] t; public float lost, total; }

        // how far each refitted part's centre of mass moved (new - old, aircraft frame), filled by the builder
        public static readonly Dictionary<string, Vector3> Moved = new Dictionary<string, Vector3>();

        public const string MovedPath = "MiG29Out/hitbox_moved.txt";
        public static Dictionary<string, Vector3> ReadMoved() => !System.IO.File.Exists(MovedPath) ? new Dictionary<string, Vector3>()
            : System.IO.File.ReadAllLines(MovedPath).Select(l => l.Split(' ')).Where(f => f.Length == 4).ToDictionary(f => f[0],
                f => new Vector3(float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture)));

        struct Pl { public double x, y, z, d; public Pl(double x, double y, double z, double d) { this.x = x; this.y = y; this.z = z; this.d = d; } }

        static readonly double[][] Dirs = MakeDirs();
        static double[][] MakeDirs()
        {
            var half = new List<double[]>();
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        if (x < 0 || (x == 0 && (y < 0 || (y == 0 && z < 0)))) continue;   // one of each opposite pair
                        half.Add(new double[] { x, y, z });
                    }
            // the wing's leading-edge sweep, both sides (MiG29Builder.LeSweep)
            half.Add(new[] { 0.9433, 0, 1.0 }); half.Add(new[] { -0.9433, 0, 1.0 });
            var all = new List<double[]>();
            foreach (var h in half)
            {
                double l = Math.Sqrt(h[0] * h[0] + h[1] * h[1] + h[2] * h[2]);
                all.Add(new[] { h[0] / l, h[1] / l, h[2] / l }); all.Add(new[] { -h[0] / l, -h[1] / l, -h[2] / l });
            }
            return all.ToArray();
        }

        static double Dot(double[] n, Vector3 p) => n[0] * p.x + n[1] * p.y + n[2] * p.z;
        static double Dot(double[] n, double[] p) => n[0] * p[0] + n[1] * p[1] + n[2] * p[2];

        static List<Pl> Kdop(List<Vector3> pts)
        {
            var planes = new List<Pl>();
            for (int i = 0; i < Dirs.Length; i += 2)
            {
                var n = Dirs[i];
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var p in pts) { double s = Dot(n, p); if (s < lo) lo = s; if (s > hi) hi = s; }
                if (hi - lo < MinThick) { double m = (hi + lo) / 2; lo = m - MinThick / 2.0; hi = m + MinThick / 2.0; }
                planes.Add(new Pl(n[0], n[1], n[2], hi)); planes.Add(new Pl(-n[0], -n[1], -n[2], -lo));
            }
            return planes;
        }

        // corners of the convex region inside every plane (n.x <= d)
        static List<double[]> Verts(List<Pl> pl)
        {
            var res = new List<double[]>(); var seen = new HashSet<(long, long, long)>();
            for (int i = 0; i < pl.Count; i++)
                for (int j = i + 1; j < pl.Count; j++)
                {
                    double cx = pl[i].y * pl[j].z - pl[i].z * pl[j].y, cy = pl[i].z * pl[j].x - pl[i].x * pl[j].z, cz = pl[i].x * pl[j].y - pl[i].y * pl[j].x;
                    if (cx * cx + cy * cy + cz * cz < 1e-12) continue;
                    for (int k = j + 1; k < pl.Count; k++)
                    {
                        double det = cx * pl[k].x + cy * pl[k].y + cz * pl[k].z;
                        if (Math.Abs(det) < 1e-6) continue;
                        // x = (d_i (n_j x n_k) + d_j (n_k x n_i) + d_k (n_i x n_j)) / det
                        double ax = pl[j].y * pl[k].z - pl[j].z * pl[k].y, ay = pl[j].z * pl[k].x - pl[j].x * pl[k].z, az = pl[j].x * pl[k].y - pl[j].y * pl[k].x;
                        double bx = pl[k].y * pl[i].z - pl[k].z * pl[i].y, by = pl[k].z * pl[i].x - pl[k].x * pl[i].z, bz = pl[k].x * pl[i].y - pl[k].y * pl[i].x;
                        var p = new[] { (pl[i].d * ax + pl[j].d * bx + pl[k].d * cx) / det, (pl[i].d * ay + pl[j].d * by + pl[k].d * cy) / det, (pl[i].d * az + pl[j].d * bz + pl[k].d * cz) / det };
                        bool inside = true;
                        foreach (var q in pl) if (q.x * p[0] + q.y * p[1] + q.z * p[2] - q.d > 1e-5) { inside = false; break; }
                        if (inside && seen.Add(((long)Math.Round(p[0] * 1e4), (long)Math.Round(p[1] * 1e4), (long)Math.Round(p[2] * 1e4)))) res.Add(p);
                    }
                }
            return res;
        }

        // gap between two point sets along the best of the fixed directions (negative: no direction separates them)
        static double Separation(List<double[]> a, List<double[]> b)
        {
            double best = double.MinValue;
            foreach (var n in Dirs)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var p in a) lo = Math.Min(lo, Dot(n, p));
                foreach (var p in b) hi = Math.Max(hi, Dot(n, p));
                best = Math.Max(best, lo - hi);
            }
            return best;
        }

        // Fits the left-hand parts; the right-hand ones are their mirror images. kept: the hulls that stay (aircraft frame points).
        public static Dictionary<string, Hull> Fit(Dictionary<string, Source> left, Dictionary<string, Vector3[]> kept)
        {
            var planes = left.ToDictionary(kv => kv.Key, kv => Kdop(kv.Value.pts));
            var keptPts = kept.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => new double[] { p.x, p.y, p.z }).ToList());
            var lost = left.Keys.ToDictionary(k => k, k => 0f);
            var names = left.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var report = new List<string>();

            // two fitted parts: one plane between them, wherever it costs the least skin
            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                {
                    string a = names[i], b = names[j];
                    var va = Verts(planes[a]); var vb = Verts(planes[b]);
                    if (va.Count < 4 || vb.Count < 4) throw new Exception($"[MiG29] hitbox {a}/{b}: nothing left to cut");
                    if (Separation(va, vb) >= Gap) continue;
                    Source sa = left[a], sb = left[b];
                    double bestLoss = double.MaxValue, bestD = 0; double[] bestN = null;
                    foreach (var n in Dirs)
                    {
                        // a keeps n.x >= d, b keeps n.x <= d
                        var pa = sa.cen.Select((c, k) => (s: Dot(n, c), w: (double)sa.area[k])).OrderBy(x => x.s).ToArray();
                        var pb = sb.cen.Select((c, k) => (s: Dot(n, c), w: (double)sb.area[k])).OrderBy(x => x.s).ToArray();
                        var cand = pa.Select(x => Math.Round(x.s, 2)).Concat(pb.Select(x => Math.Round(x.s, 2))).Distinct().OrderBy(x => x).ToArray();
                        double totalB = pb.Sum(x => x.w), belowA = 0, belowB = 0; int ia = 0, ib = 0;
                        var loss = new double[cand.Length];
                        for (int c = 0; c < cand.Length; c++)
                        {
                            while (ia < pa.Length && pa[ia].s < cand[c]) belowA += pa[ia++].w;
                            while (ib < pb.Length && pb[ib].s <= cand[c]) belowB += pb[ib++].w;
                            loss[c] = belowA + (totalB - belowB);
                        }
                        double min = loss.Min();
                        var ties = Enumerable.Range(0, cand.Length).Where(c => loss[c] <= min + 1e-6).ToArray();
                        if (min < bestLoss - 1e-9) { bestLoss = min; bestN = n; bestD = cand[ties[ties.Length / 2]]; }
                    }
                    planes[a].Add(new Pl(-bestN[0], -bestN[1], -bestN[2], -(bestD + Gap / 2.0)));
                    planes[b].Add(new Pl(bestN[0], bestN[1], bestN[2], bestD - Gap / 2.0));
                    report.Add($"{a}|{b} n=({bestN[0]:F2},{bestN[1]:F2},{bestN[2]:F2}) d={bestD:F2}");
                }

            // a fitted part against a hull that stays: only the fitted one gives way
            foreach (var a in names)
                foreach (var kv in keptPts.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (kv.Key == a || kv.Value.Count < 4) continue;   // the fin shares its part (and rigidbody) with the boom hull
                    var va = Verts(planes[a]);
                    if (va.Count < 4) throw new Exception($"[MiG29] hitbox {a}: nothing left after cutting");
                    if (Separation(va, kv.Value) >= Gap) continue;
                    Source sa = left[a];
                    double bestLoss = double.MaxValue, bestD = 0; double[] bestN = null;
                    foreach (var n in Dirs)
                    {
                        double d = kv.Value.Max(p => Dot(n, p)) + Gap;   // the kept hull lies in n.x <= d - Gap
                        double loss = 0; for (int k = 0; k < sa.cen.Count; k++) if (Dot(n, sa.cen[k]) < d) loss += sa.area[k];
                        if (loss < bestLoss - 1e-9) { bestLoss = loss; bestN = n; bestD = d; }
                    }
                    planes[a].Add(new Pl(-bestN[0], -bestN[1], -bestN[2], -bestD));
                    report.Add($"{a}|{kv.Key}(kept) n=({bestN[0]:F2},{bestN[1]:F2},{bestN[2]:F2}) d={bestD:F2}");
                }

            var result = new Dictionary<string, Hull>();
            var final = new Dictionary<string, List<double[]>>();
            foreach (var a in names)
            {
                var hull = Build(planes[a], out var corners, out double volume);
                final[a] = corners;
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
                foreach (var v in hull.v) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
                var ext = max - min;
                if (hull.v.Length < 4 || hull.t.Length > 255 * 3 || hull.v.Length > 255) throw new Exception($"[MiG29] hitbox {a}: {hull.v.Length} points, {hull.t.Length / 3} triangles");
                if (volume < 0.02 || Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) < 0.1f) throw new Exception($"[MiG29] hitbox {a}: too small ({volume:F3} m3, {ext})");
                if (max.x > -0.05f) throw new Exception($"[MiG29] hitbox {a}: reaches the centreline ({max.x:F2})");
                Source s = left[a];
                for (int k = 0; k < s.cen.Count; k++)
                {
                    hull.total += s.area[k];
                    foreach (var q in planes[a]) if (q.x * s.cen[k].x + q.y * s.cen[k].y + q.z * s.cen[k].z - q.d > 1e-4) { hull.lost += s.area[k]; break; }
                }
                result[a] = hull;
                result[Mirror(a)] = new Hull { v = hull.v.Select(v => new Vector3(-v.x, v.y, v.z)).ToArray(), t = Enumerable.Range(0, hull.t.Length).Select(k => hull.t[k - k % 3 + (2 - k % 3)]).ToArray(), lost = hull.lost, total = hull.total };
            }

            // the check that matters: no two bodies closer than MinGap
            double worst = double.MaxValue; string worstPair = "";
            for (int i = 0; i < names.Count; i++)
            {
                for (int j = i + 1; j < names.Count; j++) Note(names[i], names[j], Separation(final[names[i]], final[names[j]]));
                foreach (var kv in keptPts) if (kv.Key != names[i] && kv.Value.Count >= 4) Note(names[i], kv.Key, Separation(final[names[i]], kv.Value));
            }
            void Note(string a, string b, double gap) { if (gap < worst) { worst = gap; worstPair = a + "/" + b; } }
            Debug.Log($"[MiG29] hitboxes: {names.Count * 2} fitted to the MiG, closest pair {worstPair} {worst:F3} m; skin inside its hull: "
                + string.Join(", ", names.Select(n => $"{n} {100f * (1f - result[n].lost / Mathf.Max(result[n].total, 1e-6f)):0}%")) + "\n" + string.Join("\n", report));
            if (worst < MinGap) throw new Exception($"[MiG29] hitboxes {worstPair} are {worst:F3} m apart (minimum {MinGap})");
            return result;
        }

        static Hull Build(List<Pl> pl, out List<double[]> cornersOut, out double volume)
        {
            var corners = Verts(pl); cornersOut = corners; volume = 0;
            var verts = corners.Select(p => new Vector3((float)p[0], (float)p[1], (float)p[2])).ToArray();
            var tris = new List<int>();
            if (corners.Count < 4) return new Hull { v = verts, t = tris.ToArray() };
            var centre = new double[3];
            foreach (var p in corners) for (int k = 0; k < 3; k++) centre[k] += p[k] / corners.Count;
            var done = new HashSet<(long, long, long, long)>();
            foreach (var q in pl)
            {
                if (!done.Add(((long)Math.Round(q.x * 1e4), (long)Math.Round(q.y * 1e4), (long)Math.Round(q.z * 1e4), (long)Math.Round(q.d * 1e4)))) continue;
                var on = Enumerable.Range(0, corners.Count).Where(i => Math.Abs(q.x * corners[i][0] + q.y * corners[i][1] + q.z * corners[i][2] - q.d) < 1e-4).ToList();
                if (on.Count < 3) continue;
                var n = new[] { q.x, q.y, q.z };
                var seed = Math.Abs(n[0]) < 0.9 ? new[] { 1.0, 0, 0 } : new[] { 0, 1.0, 0 };
                var u = Cross(n, seed); double ul = Math.Sqrt(Dot(u, u)); for (int k = 0; k < 3; k++) u[k] /= ul;
                var w = Cross(n, u);
                var mid = new double[3];
                foreach (var i in on) for (int k = 0; k < 3; k++) mid[k] += corners[i][k] / on.Count;
                on = on.OrderBy(i => Math.Atan2(Dot(w, Sub(corners[i], mid)), Dot(u, Sub(corners[i], mid)))).ToList();
                for (int k = 1; k + 1 < on.Count; k++)
                {
                    double[] A = corners[on[0]], B = corners[on[k]], C = corners[on[k + 1]];
                    var fn = Cross(Sub(B, A), Sub(C, A));
                    if (Dot(fn, fn) < 1e-14) continue;
                    // Unity's front face: cross(b - a, c - a) pointing out
                    if (Dot(fn, n) > 0) { tris.Add(on[0]); tris.Add(on[k]); tris.Add(on[k + 1]); } else { tris.Add(on[0]); tris.Add(on[k + 1]); tris.Add(on[k]); }
                    volume += Math.Abs(Dot(Sub(A, centre), Cross(Sub(B, centre), Sub(C, centre)))) / 6.0;
                }
            }
            return new Hull { v = verts, t = tris.ToArray() };
        }

        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        // A part's centre of mass as PhysX computes it from the part's own colliders (what AeroPart.CreateRB gets)
        public static Vector3 Com(Component part)
        {
            var t = part.transform;
            var cols = part.GetComponents<MeshCollider>().Where(c => c.sharedMesh != null).ToArray();
            if (cols.Length == 0) return t.position;
            var probe = new GameObject("probe");
            probe.transform.SetPositionAndRotation(t.position, t.rotation);
            foreach (var c in cols) { var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = c.sharedMesh; mc.convex = true; }
            var rb = probe.AddComponent<Rigidbody>();
            Physics.SyncTransforms();
            rb.ResetCenterOfMass();
            var com = rb.worldCenterOfMass;
            UnityEngine.Object.DestroyImmediate(probe);
            return com;
        }

        // Runs after the flight model and the mass scale are on the prefab. Each refitted part's centerOfLift takes up the move of
        // its centre of mass, so every lift point stays where fm_config.json put it; then a little mass moves along the aircraft
        // (each part by a factor linear in its position, total unchanged) so the dry centre of gravity stays where it was too.
        public static void Compensate(Transform root)
        {
            if (Moved.Count == 0) return;
            var parts = root.GetComponentsInChildren(T("UnitPart"), true).Select(p => (part: p, so: new SerializedObject(p))).Where(x => x.so.FindProperty("mass").floatValue > 0f).ToList();
            double m0 = 0, mz = 0, my = 0, mzz = 0, myy = 0, mzy = 0, dz = 0, dy = 0;
            var at = new List<Vector3>();
            foreach (var (part, so) in parts)
            {
                var com = Com(part); at.Add(com);
                double m = so.FindProperty("mass").floatValue;
                m0 += m; mz += m * com.z; my += m * com.y; mzz += m * com.z * com.z; myy += m * com.y * com.y; mzy += m * com.z * com.y;
                if (!Moved.TryGetValue(part.name, out var moved)) continue;
                dz += m * moved.z; dy += m * moved.y;
                var cl = so.FindProperty("centerOfLift");
                if (cl == null) continue;
                var ln = so.FindProperty("liftNormal")?.objectReferenceValue as Transform ?? part.transform;
                cl.vector3Value -= Quaternion.Inverse(ln.rotation) * moved;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // mass factor 1 + a + b z + c y with: total unchanged, sum(m z) and sum(m y) back by what the hulls moved them
            double[,] A = { { m0, mz, my }, { mz, mzz, mzy }, { my, mzy, myy } };
            double[] rhs = { 0, -dz, -dy };
            double det = Det(A);
            var x = new double[3];
            for (int k = 0; k < 3; k++)
            {
                var B = (double[,])A.Clone();
                for (int r = 0; r < 3; r++) B[r, k] = rhs[r];
                x[k] = Det(B) / det;
            }
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < parts.Count; i++)
            {
                double f = 1 + x[0] + x[1] * at[i].z + x[2] * at[i].y;
                lo = Math.Min(lo, f); hi = Math.Max(hi, f);
                var mass = parts[i].so.FindProperty("mass");
                mass.floatValue = (float)(mass.floatValue * f);
                parts[i].so.ApplyModifiedPropertiesWithoutUndo();
            }
            // for tools/fm_sim.py (through MiG29AeroDump): fm_config.json gives centerOfLift from the old centres of mass
            System.IO.Directory.CreateDirectory("MiG29Out");
            System.IO.File.WriteAllLines(MovedPath, Moved.Select(kv => FormattableString.Invariant($"{kv.Key} {kv.Value.x:F4} {kv.Value.y:F4} {kv.Value.z:F4}")));
            if (lo < 0.6 || hi > 1.6) throw new Exception($"[MiG29] hitbox mass balance out of range: factors {lo:F2}..{hi:F2}");
            Debug.Log($"[MiG29] hitboxes: centre of gravity held (the new hulls alone would move it {dz / m0:F3} m along, {dy / m0:F3} m up); part masses x {lo:F2}..{hi:F2}");
        }

        static double Det(double[,] m) => m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }
}
