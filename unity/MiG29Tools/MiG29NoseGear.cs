using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 nose gear and wheels in place of the KR-67's, the last of its parts still drawn (object count, 2026-10-30). Built on the
    // KR-67's own transforms, each mesh in its object's frame, so every physics transform (axle, bump stop, cast point, suspension,
    // fold) and the ride height stay exactly as they are; the wheels keep the KR-67's rolling radius (0.33 m nose, 0.43 m main).
    //   wheel_F         twin wheels on one axle (the real 9.12 has two 570 x 140 mm wheels; the KR-67's pair is already 0.64 m wide),
    //                   spread to clear the heavier piston
    //   gear_F_unsprung chrome piston (0.11 m), a short trailing fork to the axle (0.17 m behind the strut), axle-end nuts and a brake
    //                   line over the fork. No mudguard: the SMT in service photos runs without one.
    //   gear_F_sprung   the oleo barrel (0.17 m) up to the hinge, with its trunnion and the steering unit below it; a wiper seal, two
    //                   bands, the charging valve, the uplock roller and two clamped hydraulic lines down its back
    //   both            heavy torque links across the front of the piston, one arm on each part (the plugin keeps them joined, also
    //                   while the nose wheel steers)
    //   wheel_L / R     a KT-150-style main wheel (840 x 290 mm, the KR-67's size already)
    //   every wheel     a round-shouldered tyre with two tread grooves, a grey rim flange, and the green dished hub of Russian service
    //                   wheels with radial ribs, a raised boss with its bolt ring and a chrome cap
    //   gearLight_F     a round lamp in its can on the strut (Lamp, from the builder's PlaceLandingLight)
    // Texture: the main gear's atlas (tools/main_gear_gen.py zones: paint, hub green, chrome, dark rubber).
    public static class MiG29NoseGear
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        public const float LinkArm = 0.36f;   // each torque-link arm, hinge to apex pin: the oleo stands near full extension (0.62 m between the hinges in game), so the scissor keeps a bend (the plugin reads it from the meshes)
        static readonly Vector2 Paint = new Vector2(0.02f, 0.48f), Green = new Vector2(0.492f, 0.496f), Chrome = new Vector2(0.52f, 0.73f), Dark = new Vector2(0.77f, 0.98f);
        public const float ApexBoss = 0.034f;   // the boss round each arm's apex pin; the plugin takes it off the mesh length to find the pin
        public const float BarrelRadius = 0.085f;   // the oleo barrel (the builder hangs the landing light on its front)
        const float WheelSpread = 0.12f;   // each nose wheel's centre from the leg's axis (inner tyre walls 5 cm out, clear of the piston)

        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();

            // one quad, wound so its face points along `outward`
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 zone, float v0, float v1, Vector3 outward)
            {
                int i = v.Count;
                v.AddRange(new[] { a, b, c, d }); n.AddRange(new[] { na, nb, nc, nd });
                float u0 = zone.x, u1 = zone.y;
                uv.AddRange(new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) });
                // facing from the diagonals: a quad whose first two corners meet (a disc's centre) still gets its winding right
                bool flip = Vector3.Dot(Vector3.Cross(c - a, d - b), outward) < 0f;
                if (flip) t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                else t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            // a lathe: the profile (radius, distance along the axis; each segment's outward normal in that plane) swept round the axis
            public void Revolve(Vector3 origin, Vector3 axis, Vector3 up, (float r, float x)[] profile, (float nr, float nx)[] normals, Vector2 zone,
                int seg = 24, float a0 = 0f, float a1 = 360f)
            {
                axis.Normalize(); up = Vector3.ProjectOnPlane(up, axis).normalized;
                var side = Vector3.Cross(axis, up);
                Vector3 Radial(float a) { float r = a * Mathf.Deg2Rad; return up * Mathf.Cos(r) + side * Mathf.Sin(r); }
                for (int k = 0; k < seg; k++)
                {
                    float aA = Mathf.Lerp(a0, a1, k / (float)seg), aB = Mathf.Lerp(a0, a1, (k + 1) / (float)seg);
                    Vector3 rA = Radial(aA), rB = Radial(aB);
                    for (int s = 0; s + 1 < profile.Length; s++)
                    {
                        var p0 = profile[s]; var p1 = profile[s + 1]; var nn = normals[s];
                        Vector3 A = origin + axis * p0.x + rA * p0.r, B = origin + axis * p0.x + rB * p0.r;
                        Vector3 C = origin + axis * p1.x + rB * p1.r, D = origin + axis * p1.x + rA * p1.r;
                        Vector3 nA = (rA * nn.nr + axis * nn.nx).normalized, nB = (rB * nn.nr + axis * nn.nx).normalized;
                        Vector3 mid = (rA + rB).normalized;
                        Quad(A, B, C, D, nA, nB, nB, nA, zone, k / (float)seg, (k + 1) / (float)seg, mid * nn.nr + axis * nn.nx);
                    }
                }
            }

            public void Cylinder(Vector3 a, Vector3 b, float r, Vector2 zone, int seg = 16)
            {
                var axis = b - a; float len = axis.magnitude; axis /= len;
                var up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
                Revolve(a, axis, up, new[] { (0f, 0f), (r, 0f), (r, len), (0f, len) }, new[] { (0f, -1f), (1f, 0f), (0f, 1f) }, zone, seg);
            }

            public void Box(Vector3 center, Vector3 half, Vector2 zone)
            {
                for (int ax = 0; ax < 3; ax++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        var nrm = Vector3.zero; nrm[ax] = sg;
                        int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                        Vector3 P(float s1, float s2) { var p = center; p[ax] += sg * half[ax]; p[a1] += s1 * half[a1]; p[a2] += s2 * half[a2]; return p; }
                        Quad(P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), nrm, nrm, nrm, nrm, zone, 0f, 1f, nrm);
                    }
            }

            // a flat bar from a to b, `width` across x, `thick` in the plane the bar swings in (torque links)
            public void Bar(Vector3 a, Vector3 b, float width, float thick, Vector2 zone)
            {
                var dir = (b - a).normalized; var sx = Vector3.right * (width / 2f);
                var sn = Vector3.Cross(dir, Vector3.right).normalized * (thick / 2f);
                Vector3 P(Vector3 o, float i, float j) => o + sx * i + sn * j;
                var nx = Vector3.right; var nn = sn.normalized;
                Quad(P(a, 1, -1), P(b, 1, -1), P(b, 1, 1), P(a, 1, 1), nx, nx, nx, nx, zone, 0f, 1f, nx);
                Quad(P(a, -1, -1), P(b, -1, -1), P(b, -1, 1), P(a, -1, 1), -nx, -nx, -nx, -nx, zone, 0f, 1f, -nx);
                Quad(P(a, -1, 1), P(b, -1, 1), P(b, 1, 1), P(a, 1, 1), nn, nn, nn, nn, zone, 0f, 1f, nn);
                Quad(P(a, -1, -1), P(b, -1, -1), P(b, 1, -1), P(a, 1, -1), -nn, -nn, -nn, -nn, zone, 0f, 1f, -nn);
            }

            // a flat disc facing along `axis`
            public void Disc(Vector3 c, Vector3 axis, float r, Vector2 zone, int seg = 12)
            {
                var up = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
                Revolve(c, axis, up, new[] { (0f, 0f), (r, 0f) }, new[] { (0f, 1f) }, zone, seg);
            }

            // a hose or pipe through the points, one short cylinder per run (hydraulic and brake lines)
            public void Tube(Vector2 zone, float r, params Vector3[] pts)
            {
                for (int k = 0; k + 1 < pts.Length; k++) Cylinder(pts[k], pts[k + 1], r, zone, 8);
            }

            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        // a lathe from a (radius, distance along x) profile, one flat normal per segment. `side` -1 builds the profile as given
        // (the -x face, listed from the centre out); +1 mirrors it onto the +x face.
        static void Lathe(Builder b, Vector3 o, float side, (float r, float x)[] pts, Vector2 zone, int seg)
        {
            (float r, float x)[] p = side < 0 ? pts : pts.Select(q => (r: q.r, x: -q.x)).Reverse().ToArray();
            var nrm = new (float nr, float nx)[p.Length - 1];
            for (int k = 0; k + 1 < p.Length; k++)
            {
                float dr = p[k + 1].r - p[k].r, dx = p[k + 1].x - p[k].x, len = Mathf.Sqrt(dr * dr + dx * dx);
                nrm[k] = (dx / len, -dr / len);
            }
            b.Revolve(o, Vector3.right, Vector3.up, p, nrm, zone, seg);
        }

        // a wheel on an axle along local x, after the MiG's KT-150 main and 9.12 nose wheels: a round-shouldered tyre with two tread
        // grooves, a grey rim flange, and a green dished hub with radial ribs, a raised boss with its bolt ring and a chrome cap
        static void Wheel(Builder b, float cx, float rOut, float rIn, float width, int ribs)
        {
            float w = width / 2f, rc = Mathf.Min(w * 0.6f, (rOut - rIn) * 0.5f), cr = rOut - rc, bead = w - 0.012f;
            var o = new Vector3(cx, 0f, 0f);
            var tyre = new List<(float r, float x)> { (rIn, -bead), (rIn + (cr - rIn) * 0.5f, -w) };
            for (int k = 0; k <= 4; k++) { float a = k * 22.5f * Mathf.Deg2Rad; tyre.Add((cr + rc * Mathf.Sin(a), -(w - rc) - rc * Mathf.Cos(a))); }
            float g = (w - rc) * 0.45f, gw = Mathf.Min(0.008f, g * 0.5f);
            foreach (var gx in new[] { -g, g })
                tyre.AddRange(new[] { (rOut, gx - gw), (rOut - 0.006f, gx - gw * 0.5f), (rOut - 0.006f, gx + gw * 0.5f), (rOut, gx + gw) });
            for (int k = 4; k >= 0; k--) { float a = k * 22.5f * Mathf.Deg2Rad; tyre.Add((cr + rc * Mathf.Sin(a), (w - rc) + rc * Mathf.Cos(a))); }
            tyre.AddRange(new[] { (rIn + (cr - rIn) * 0.5f, w), (rIn, bead) });
            Lathe(b, o, -1f, tyre.ToArray(), Dark, 36);

            float face = w - 0.03f, rBoss = rIn * 0.36f, rCap = rIn * 0.2f, boss = 0.022f;
            foreach (float sd in new[] { -1f, 1f })
            {
                Lathe(b, o, sd, new[] { (0f, -face - boss - 0.012f), (rCap, -face - boss - 0.012f), (rCap, -face - boss) }, Chrome, 16);
                Lathe(b, o, sd, new[] { (rCap, -face - boss), (rBoss, -face - boss), (rBoss + 0.006f, -face) }, Green, 24);
                Lathe(b, o, sd, new[] { (rBoss + 0.006f, -face), (rIn * 0.9f, -face + 0.004f) }, Green, 28);   // the dish
                Lathe(b, o, sd, new[] { (rIn * 0.9f, -face + 0.004f), (rIn * 0.97f, -bead - 0.004f), (rIn, -bead) }, Paint, 28);   // rim flange
                for (int k = 0; k < ribs; k++)
                {
                    float a = (k + 0.5f) * 360f / ribs * Mathf.Deg2Rad;
                    var dir = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    var x = Vector3.right * sd * (face + 0.006f);
                    b.Bar(o + x + dir * rBoss, o + x + dir * rIn * 0.88f, 0.012f, 0.016f, Green);
                }
                int bolts = ribs + 2;
                for (int k = 0; k < bolts; k++)
                {
                    float a = k * 360f / bolts * Mathf.Deg2Rad;
                    var at = o + new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * ((rCap + rBoss) * 0.5f);
                    b.Cylinder(at + Vector3.right * sd * (face + boss - 0.002f), at + Vector3.right * sd * (face + boss + 0.008f), 0.007f, Chrome, 6);
                }
            }
        }

        public static void Build(Transform root, Material gearMat, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            Transform Find(string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            int done = 0;
            void Put(Transform tr, Mesh mesh)
            {
                if (tr == null) return;
                var mf = tr.GetComponent<MeshFilter>(); var mr = tr.GetComponent<MeshRenderer>();
                if (mf == null || mr == null) return;
                mf.sharedMesh = (Mesh)save(mesh, $"{ModDir}/meshes/{mesh.name}.asset");
                if (gearMat != null) mr.sharedMaterials = new[] { gearMat };
                done++;
            }
            // a torque-link arm: its own object at its hinge, the arm along +z, first posed at 45 deg towards the other arm
            void Link(Transform parent, string name, Vector3 pivot, Mesh mesh)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pivot;
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, name.EndsWith("_upper") ? -1f : 1f, 1f));
                go.AddComponent<MeshFilter>().sharedMesh = (Mesh)save(mesh, $"{ModDir}/meshes/{mesh.name}.asset");
                var mr = go.AddComponent<MeshRenderer>();
                if (gearMat != null) mr.sharedMaterials = new[] { gearMat };
                done++;
            }

            var wheelF = Find("wheel_F"); var sprung = Find("gear_F_sprung"); var unsprung = Find("gear_F_unsprung");
            float rNose = WheelRadius(wheelF, 0.33f), rMain = WheelRadius(Find("wheel_L"), 0.427f);

            // nose wheels: two 0.14 m wheels either side of the fork, and the axle through them
            var nw = new Builder();
            Wheel(nw, -WheelSpread, rNose, rNose * 0.6f, 0.14f, 6);
            Wheel(nw, WheelSpread, rNose, rNose * 0.6f, 0.14f, 6);
            nw.Cylinder(new Vector3(-WheelSpread - 0.1f, 0f, 0f), new Vector3(WheelSpread + 0.1f, 0f, 0f), 0.028f, Chrome);
            Put(wheelF, nw.Mesh("MiG29_nose_wheels"));

            var s = new Builder();
            // unsprung: piston up into the barrel, trailing fork to the axle
            if (unsprung != null && wheelF != null && sprung != null)
            {
                var axle = unsprung.InverseTransformPoint(wheelF.position);
                float barrelBottom = unsprung.InverseTransformPoint(sprung.position).y;
                var u = new Builder();
                // torque links: a scissor across the front of the piston, the lower arm hinged on the piston, the upper on the barrel.
                // The oleo stands at a different extension in game than in the prefab, so each arm is its own object along its +z
                // and the plugin's TorqueLinkDriver swings both every frame to meet at the apex pin.
                // Each arm is a forked I-section: two cheeks round the pin at its hinge, a deep web, a boss at the apex. The upper arm
                // carries its own hinge lug, so the lug turns with it when the steered lower part takes the scissor round.
                u.Box(new Vector3(0f, 0.06f, 0.07f), new Vector3(0.022f, 0.035f, 0.03f), Paint);                    // lug on the piston
                Mesh Arm(string name, float width, bool apexPin, bool ownLug)
                {
                    var a = new Builder();
                    a.Bar(new Vector3(0f, 0f, 0.03f), new Vector3(0f, 0f, LinkArm - 0.03f), width, 0.05f, Paint);   // web
                    for (int sd = -1; sd <= 1; sd += 2)
                        a.Box(new Vector3(sd * (width / 2f - 0.006f), 0f, 0.012f), new Vector3(0.008f, 0.035f, 0.035f), Paint);   // hinge cheeks
                    a.Cylinder(new Vector3(-width / 2f - 0.008f, 0f, 0f), new Vector3(width / 2f + 0.008f, 0f, 0f), 0.02f, Chrome);   // hinge pin
                    a.Cylinder(new Vector3(-width * 0.4f, 0f, LinkArm), new Vector3(width * 0.4f, 0f, LinkArm), ApexBoss, Paint);   // apex boss
                    if (apexPin) a.Cylinder(new Vector3(-width / 2f - 0.012f, 0f, LinkArm), new Vector3(width / 2f + 0.012f, 0f, LinkArm), 0.018f, Chrome);
                    if (ownLug) a.Box(new Vector3(0f, 0f, -0.035f), new Vector3(width / 2f - 0.016f, 0.03f, 0.035f), Paint);   // lug back to the barrel
                    return a.Mesh(name);
                }
                Link(unsprung, "MiG29_nose_link_lower", new Vector3(0f, 0.06f, 0.095f), Arm("MiG29_nose_link_lower", 0.085f, true, false));
                Link(sprung, "MiG29_nose_link_upper", new Vector3(0f, 0.03f, 0.125f), Arm("MiG29_nose_link_upper", 0.06f, false, true));
                JoinLinks(sprung.Find("MiG29_nose_link_upper"), unsprung.Find("MiG29_nose_link_lower"));
                Debug.Log($"[MiG29] nose leg: barrel bottom at piston y {barrelBottom:F2}, axle at {axle}, torque link arms {LinkArm} m");
                u.Cylinder(Vector3.zero, new Vector3(0f, barrelBottom + 0.28f, 0f), 0.055f, Chrome);
                u.Cylinder(new Vector3(0f, -0.03f, 0f), new Vector3(0f, 0.09f, 0f), 0.066f, Paint);                   // piston foot
                u.Box(new Vector3(0f, axle.y * 0.5f, axle.z * 0.5f), new Vector3(0.045f, 0.06f, Mathf.Abs(axle.z) * 0.5f + 0.05f), Paint);
                u.Cylinder(axle + new Vector3(-0.05f, 0f, 0f), axle + new Vector3(0.05f, 0f, 0f), 0.05f, Paint);
                for (int sd = -1; sd <= 1; sd += 2)   // axle nuts outside the wheels
                    u.Cylinder(axle + new Vector3(sd * (WheelSpread + 0.075f), 0f, 0f), axle + new Vector3(sd * (WheelSpread + 0.1f), 0f, 0f), 0.034f, Paint, 6);
                // brake line down the back of the fork to the axle, from a fitting on the piston foot
                u.Box(new Vector3(0f, 0.07f, -0.07f), new Vector3(0.022f, 0.02f, 0.014f), Paint);
                float forkTop = axle.y * 0.5f + 0.06f;
                u.Tube(Dark, 0.009f, new Vector3(0f, 0.07f, -0.084f), new Vector3(0f, forkTop + 0.01f, axle.z * 0.6f), axle + new Vector3(0f, 0.06f, 0.02f));
                Put(unsprung, u.Mesh("MiG29_nose_unsprung"));
            }

            // sprung: oleo barrel from the top of the piston to the hinge, with the trunnion across it
            if (sprung != null)
            {
                var hinge = Find("gearHinge_F");
                float top = hinge != null ? sprung.InverseTransformPoint(hinge.position).y : 1.3f;
                s.Cylinder(new Vector3(0f, -0.02f, 0f), new Vector3(0f, top, 0f), BarrelRadius, Paint);
                s.Cylinder(new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.06f, 0f), 0.097f, Paint);   // gland collar
                s.Cylinder(new Vector3(-0.18f, top, 0f), new Vector3(0.18f, top, 0f), 0.055f, Paint);
                // steering unit below the trunnion: a collar round the barrel and its two actuators either side, pointing aft
                s.Cylinder(new Vector3(0f, top - 0.30f, 0f), new Vector3(0f, top - 0.18f, 0f), 0.112f, Paint);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    s.Cylinder(new Vector3(sd * 0.13f, top - 0.24f, 0.03f), new Vector3(sd * 0.13f, top - 0.24f, -0.17f), 0.03f, Paint);
                    s.Cylinder(new Vector3(sd * 0.13f, top - 0.24f, -0.17f), new Vector3(sd * 0.13f, top - 0.24f, -0.25f), 0.015f, Chrome);
                }
                // wiper seal under the gland, two bands round the barrel, the charging valve and the uplock roller under the hinge
                s.Cylinder(new Vector3(0f, -0.055f, 0f), new Vector3(0f, -0.04f, 0f), 0.062f, Dark);
                s.Cylinder(new Vector3(0f, 0.30f, 0f), new Vector3(0f, 0.33f, 0f), 0.092f, Paint);
                s.Cylinder(new Vector3(0f, top - 0.45f, 0f), new Vector3(0f, top - 0.42f, 0f), 0.092f, Paint);
                s.Cylinder(new Vector3(0.08f, top - 0.38f, 0f), new Vector3(0.12f, top - 0.38f, 0f), 0.014f, Chrome);
                s.Cylinder(new Vector3(0.12f, top - 0.38f, 0f), new Vector3(0.132f, top - 0.38f, 0f), 0.02f, Paint);
                s.Box(new Vector3(0f, top - 0.10f, -0.10f), new Vector3(0.024f, 0.028f, 0.03f), Paint);
                s.Cylinder(new Vector3(-0.045f, top - 0.10f, -0.13f), new Vector3(0.045f, top - 0.10f, -0.13f), 0.024f, Chrome);
                // two hydraulic lines (steering and brakes) down the back of the barrel, clamped, ending in fittings on the gland
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 H(float y) => new Vector3(sd * 0.068f, y, -0.068f);
                    s.Tube(Dark, 0.011f, H(top - 0.04f), H(top - 0.18f));
                    s.Tube(Dark, 0.011f, H(top - 0.30f), H(0.08f));
                    for (float y = 0.20f; y < top - 0.35f; y += 0.25f) s.Box(H(y), new Vector3(0.017f, 0.008f, 0.017f), Paint);
                    s.Box(H(0.075f), new Vector3(0.018f, 0.016f, 0.018f), Chrome);
                }
                Debug.Log($"[MiG29] nose leg: hinge at sprung y {top:F2}, steering collar y {top - 0.30f:F2}..{top - 0.18f:F2}, lines, bands, valve and uplock roller");
                Put(sprung, s.Mesh("MiG29_nose_sprung"));
            }

            // main wheels: the left mesh on both (the right wheel is the left one turned round), centred where the KR-67's was
            var wheelL = Find("wheel_L");
            float cxMain = MeshCenterX(wheelL, -0.269f);
            var mw = new Builder();
            Wheel(mw, cxMain, rMain, rMain * 0.62f, 0.29f, 8);
            var mainMesh = mw.Mesh("MiG29_main_wheel");
            Put(wheelL, mainMesh);
            Put(Find("wheel_R"), mainMesh);
            Debug.Log($"[MiG29] nose gear and wheels: {done} meshes (nose wheel r {rNose:F3} m, main r {rMain:F3} m)");
        }

        // The landing light as a MiG lamp: the KR-67's flat panel (NavLights swaps its material to a glowing one with the gear down)
        // becomes a round lens, set in a deep can with a rim and a bracket back to the strut. Run once the lamp is placed on the
        // strut, facing `face` (world); the can hangs on the sprung leg so it folds with it.
        public static void Lamp(Transform lamp, Transform sprung, Vector3 face, Material gearMat, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            const float rLens = 0.055f, rCan = 0.068f, depth = 0.075f;
            var mf = lamp.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount != 1) { Debug.LogWarning("[MiG29] lamp: unexpected landing light mesh, kept"); return; }
            var centreW = lamp.TransformPoint(mf.sharedMesh.bounds.center);
            var lens = new Builder();
            var faceL = lamp.InverseTransformDirection(face).normalized;
            lens.Disc(mf.sharedMesh.bounds.center, faceL, rLens / lamp.lossyScale.x, Chrome, 20);
            mf.sharedMesh = (Mesh)save(lens.Mesh("MiG29_nose_lamp_lens"), $"{ModDir}/meshes/MiG29_nose_lamp_lens.asset");

            var go = new GameObject("MiG29_nose_lamp");
            go.transform.SetParent(sprung, false);
            go.transform.SetPositionAndRotation(centreW, Quaternion.LookRotation(face, Vector3.up));
            var c = new Builder();
            c.Revolve(Vector3.zero, Vector3.forward, Vector3.up,
                new[] { (rLens, 0.004f), (rCan, 0.004f), (rCan, -depth), (rCan * 0.6f, -depth - 0.02f), (0f, -depth - 0.02f) },
                new[] { (0f, 1f), (1f, 0f), (0.7f, -0.7f), (0f, -1f) }, Paint, 20);
            c.Revolve(Vector3.zero, Vector3.forward, Vector3.up, new[] { (rLens, 0.004f), (rLens, -0.01f) }, new[] { (-1f, 0f) }, Chrome, 20);
            // bracket from the back of the can to the strut
            var strutW = sprung.TransformPoint(new Vector3(0f, sprung.InverseTransformPoint(centreW).y, 0f));
            var back = go.transform.InverseTransformPoint(strutW);
            c.Bar(new Vector3(0f, 0f, -depth), back, 0.035f, 0.03f, Paint);
            go.AddComponent<MeshFilter>().sharedMesh = (Mesh)save(c.Mesh("MiG29_nose_lamp"), $"{ModDir}/meshes/MiG29_nose_lamp.asset");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { gearMat };
            Debug.Log($"[MiG29] landing light: round lens r {rLens} m in a {depth} m can, bracket {back.magnitude:F2} m to the strut");
        }

        // Pose both torque-link arms to meet at the apex pin, in front of the leg, at the prefab's extension (the plugin's
        // TorqueLinkDriver does the same each frame in game, where the oleo stands longer and the nose wheel steers)
        static void JoinLinks(Transform upper, Transform lower)
        {
            if (upper == null || lower == null) return;
            Vector3 pu = upper.position, pl = lower.position, axis = lower.parent.right;
            float len = (pl - pu).magnitude, arm = LinkArm * upper.lossyScale.z;
            if (len < 1e-4f || len >= 2f * arm) return;
            var dir = (pl - pu) / len;
            var ahead = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(lower.parent.forward, dir), axis).normalized;
            var apex = pu + dir * (len / 2f) + ahead * Mathf.Sqrt(arm * arm - len * len / 4f);
            foreach (var t in new[] { upper, lower })
            {
                var f = Vector3.ProjectOnPlane(apex - t.position, axis).normalized;
                t.rotation = Quaternion.LookRotation(f, Vector3.Cross(f, axis));
            }
            Debug.Log($"[MiG29] torque links joined at the prefab's extension: hinges {len:F2} m apart, apex {(apex - pu).magnitude:F2} m from the upper hinge");
        }

        // the gear material once MiG29MainGear has made it (Build runs before the nose gear's stow search, which fits the real meshes)
        public static void SetMaterial(Transform root, Material gearMat)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && (mf.sharedMesh.name.StartsWith("MiG29_nose_") || mf.sharedMesh.name == "MiG29_main_wheel"))
                    mf.GetComponent<MeshRenderer>().sharedMaterials = new[] { gearMat };
        }

        // the KR-67 wheel's rolling radius, from its mesh (before it is replaced)
        static float WheelRadius(Transform w, float fallback)
        {
            var m = w != null ? w.GetComponent<MeshFilter>()?.sharedMesh : null;
            return m != null ? Mathf.Max(m.bounds.extents.y, m.bounds.extents.z) : fallback;
        }

        static float MeshCenterX(Transform w, float fallback)
        {
            var m = w != null ? w.GetComponent<MeshFilter>()?.sharedMesh : null;
            return m != null ? m.bounds.center.x : fallback;
        }
    }
}
