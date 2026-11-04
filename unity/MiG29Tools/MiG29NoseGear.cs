using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 nose gear and wheels in place of the KR-67's, the last of its parts still drawn (object count, 2026-10-30). Built on the
    // KR-67's own transforms, each mesh in its object's frame, so every physics transform (axle, bump stop, cast point, suspension,
    // fold) and the ride height stay exactly as they are; the wheels keep the KR-67's rolling radius (0.33 m nose, 0.43 m main).
    //   wheel_F         twin wheels on one axle (the real 9.12 has two 570 x 140 mm wheels; the KR-67's pair is already 0.64 m wide)
    //   gear_F_unsprung chrome piston, a short trailing fork to the axle (0.17 m behind the strut) and the MiG's mudguard behind the
    //                   wheels, which keeps debris out of the intakes on rough fields
    //   gear_F_sprung   a slim oleo barrel up to the hinge, with its trunnion and the steering unit below it
    //   both            the torque links across the front of the piston, one arm on each part
    //   wheel_L / R     a KT-150-style main wheel (840 x 290 mm, the KR-67's size already): tyre, rim and hub with lightening holes
    //   gearLight_F     a round lamp in its can on the strut (Lamp, from the builder's PlaceLandingLight)
    // Texture: the main gear's atlas (tools/main_gear_gen.py zones: paint, chrome, dark rubber).
    public static class MiG29NoseGear
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        static readonly Vector2 Paint = new Vector2(0.02f, 0.48f), Chrome = new Vector2(0.52f, 0.73f), Dark = new Vector2(0.77f, 0.98f);

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
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
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

            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        // a wheel on an axle along local x: tyre (rounded tread and shoulders), rim lips and a recessed hub with its boss
        static void Wheel(Builder b, float cx, float rOut, float rIn, float width)
        {
            float w = width / 2f, sh = Mathf.Min(0.045f, w * 0.4f);
            var o = new Vector3(cx, 0f, 0f);
            b.Revolve(o, Vector3.right, Vector3.up,
                new[] { (rIn, -w), (rOut - sh, -w), (rOut, -w + sh), (rOut, w - sh), (rOut - sh, w), (rIn, w) },
                new[] { (0f, -1f), (0.7f, -0.7f), (1f, 0f), (0.7f, 0.7f), (0f, 1f) }, Dark, 32);
            float hw = w - 0.025f;   // hub faces sit a little inside the tyre walls
            b.Revolve(o, Vector3.right, Vector3.up,
                new[] { (rIn, -w), (rIn, -hw), (0.06f, -hw), (0.06f, -hw - 0.02f), (0f, -hw - 0.02f) },
                new[] { (-1f, 0f), (0f, -1f), (1f, 0f), (0f, -1f) }, Paint, 24);
            b.Revolve(o, Vector3.right, Vector3.up,
                new[] { (rIn, w), (rIn, hw), (0.06f, hw), (0.06f, hw + 0.02f), (0f, hw + 0.02f) },
                new[] { (-1f, 0f), (0f, 1f), (1f, 0f), (0f, 1f) }, Paint, 24);
            // six lightening holes round each hub face (dark discs just proud of it; they turn with the wheel)
            float rh = (0.06f + rIn) / 2f, hr = Mathf.Min((rIn - 0.06f) * 0.3f, 0.04f);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                var off = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * rh;
                b.Disc(o + off + Vector3.right * (hw + 0.003f), Vector3.right, hr, Dark);
                b.Disc(o + off - Vector3.right * (hw + 0.003f), -Vector3.right, hr, Dark);
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

            var wheelF = Find("wheel_F"); var sprung = Find("gear_F_sprung"); var unsprung = Find("gear_F_unsprung");
            float rNose = WheelRadius(wheelF, 0.33f), rMain = WheelRadius(Find("wheel_L"), 0.427f);

            // nose wheels: two 0.14 m wheels either side of the fork, and the axle through them
            var nw = new Builder();
            Wheel(nw, -0.105f, rNose, rNose * 0.6f, 0.14f);
            Wheel(nw, 0.105f, rNose, rNose * 0.6f, 0.14f);
            nw.Cylinder(new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f), 0.025f, Chrome);
            Put(wheelF, nw.Mesh("MiG29_nose_wheels"));

            // torque links: a scissor across the front of the piston, lower arm on the piston, upper arm on the barrel, meeting at the
            // apex pin (the arms close up a little as the oleo compresses, as on the real leg)
            var s = new Builder();
            // unsprung: piston up into the barrel, trailing fork to the axle, mudguard over the back of the wheels
            if (unsprung != null && wheelF != null && sprung != null)
            {
                var axle = unsprung.InverseTransformPoint(wheelF.position);
                float barrelBottom = unsprung.InverseTransformPoint(sprung.position).y;
                var u = new Builder();
                var low = new Vector3(0f, 0.05f, 0.05f);
                var apex = new Vector3(0f, (0.05f + barrelBottom) / 2f, 0.15f);
                u.Bar(low, apex, 0.05f, 0.03f, Paint);
                u.Box(low, new Vector3(0.035f, 0.025f, 0.02f), Paint);                                       // lug on the piston
                u.Cylinder(apex + new Vector3(-0.035f, 0f, 0f), apex + new Vector3(0.035f, 0f, 0f), 0.016f, Chrome);   // apex pin
                var apexS = sprung.InverseTransformPoint(unsprung.TransformPoint(apex));
                s.Bar(new Vector3(0f, 0.02f, 0.075f), apexS, 0.04f, 0.03f, Paint);
                s.Box(new Vector3(0f, 0.02f, 0.08f), new Vector3(0.03f, 0.02f, 0.02f), Paint);                // lug on the barrel
                Debug.Log($"[MiG29] nose leg: barrel bottom at piston y {barrelBottom:F2}, torque link apex {apex:F2}");
                u.Cylinder(Vector3.zero, new Vector3(0f, barrelBottom + 0.28f, 0f), 0.042f, Chrome);
                u.Box(new Vector3(0f, axle.y * 0.5f, axle.z * 0.5f), new Vector3(0.032f, 0.045f, Mathf.Abs(axle.z) * 0.5f + 0.04f), Paint);
                u.Cylinder(axle + new Vector3(-0.04f, 0f, 0f), axle + new Vector3(0.04f, 0f, 0f), 0.04f, Paint);
                // mudguard: a thin shell round the axle from the top of the wheels to behind and below them
                // (angles from straight up towards -z, the rear: Revolve sweeps up -> +z for positive angles)
                u.Revolve(axle, Vector3.right, Vector3.up,
                    new[] { (rNose + 0.045f, -0.23f), (rNose + 0.045f, 0.23f) }, new[] { (1f, 0f) }, Paint, 12, -125f, -10f);
                u.Revolve(axle, Vector3.right, Vector3.up,
                    new[] { (rNose + 0.038f, -0.23f), (rNose + 0.038f, 0.23f) }, new[] { (-1f, 0f) }, Paint, 12, -125f, -10f);
                u.Box(axle + new Vector3(0f, rNose * 0.75f, -0.06f), new Vector3(0.028f, rNose * 0.3f, 0.025f), Paint);   // the guard's stay, between the wheels
                Put(unsprung, u.Mesh("MiG29_nose_unsprung"));
            }

            // sprung: oleo barrel from the top of the piston to the hinge, with the trunnion across it
            if (sprung != null)
            {
                var hinge = Find("gearHinge_F");
                float top = hinge != null ? sprung.InverseTransformPoint(hinge.position).y : 1.3f;
                s.Cylinder(new Vector3(0f, -0.02f, 0f), new Vector3(0f, top, 0f), 0.068f, Paint);
                s.Cylinder(new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.05f, 0f), 0.078f, Paint);   // gland collar
                s.Cylinder(new Vector3(-0.16f, top, 0f), new Vector3(0.16f, top, 0f), 0.045f, Paint);
                // steering unit below the trunnion: a collar round the barrel and its two actuators either side, pointing aft
                s.Cylinder(new Vector3(0f, top - 0.30f, 0f), new Vector3(0f, top - 0.18f, 0f), 0.092f, Paint);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    s.Cylinder(new Vector3(sd * 0.105f, top - 0.24f, 0.03f), new Vector3(sd * 0.105f, top - 0.24f, -0.17f), 0.024f, Paint);
                    s.Cylinder(new Vector3(sd * 0.105f, top - 0.24f, -0.17f), new Vector3(sd * 0.105f, top - 0.24f, -0.25f), 0.012f, Chrome);
                }
                Debug.Log($"[MiG29] nose leg: hinge at sprung y {top:F2}, steering collar y {top - 0.30f:F2}..{top - 0.18f:F2}");
                Put(sprung, s.Mesh("MiG29_nose_sprung"));
            }

            // main wheels: the left mesh on both (the right wheel is the left one turned round), centred where the KR-67's was
            var wheelL = Find("wheel_L");
            float cxMain = MeshCenterX(wheelL, -0.269f);
            var mw = new Builder();
            Wheel(mw, cxMain, rMain, rMain * 0.62f, 0.29f);
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
