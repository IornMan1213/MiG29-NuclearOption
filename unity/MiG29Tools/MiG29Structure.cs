using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiG29Tools
{
    // The MiG's internal structure, from scratch: fuselage ring frames, frames round each engine, and the wing's ribs and spars.
    // A broken stock jet shows ribs and spars in its wings and frames in its fuselage (the KR-67's outer wing: ribs about every
    // 0.5 m and two spars); the MiG showed only the lining a few centimetres inside its skin, so a broken wing read as a hollow
    // shell (user, 2026-11-11: "full fidelity").
    //  - Frames: rings in the cross-section plane, from the skin inward by FrameDepth, found by rays from the section's centre to
    //    the skin. Ribs and spars (and the LERX's ribs): webs from the lower skin to the upper skin, found by rays down through them.
    //  - Each quad goes to the part its centre lies in (MiG29Parts.Label), so it leaves with that part; none inside the cockpit,
    //    the intake ducts or outside the skin (the builder's tests).
    //  - Two-sided, in the stock structure material. Close-up level only, like the rest of the inside (MiG29Lod "MiG29_inner_").
    // Prefab frame. The skin is a collider on layer SkinLayer with back faces hit (set up by the builder for the inner hull).
    static class MiG29Structure
    {
        const float FrameDepth = 0.12f, FrameStep = 0.5f, MaxGap = 0.35f, Inset = 0.01f;
        const int FrameRays = 64;

        public class Piece { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<Color32> c = new List<Color32>(); public List<int> t = new List<int>(); }

        public static Dictionary<string, Piece> Build(int skinLayer, Func<Vector3, bool> drop, Func<float, float> leadingEdgeZ, Func<float, float> trailingEdgeZ,
            IList<(Vector3 rear, Vector3 fwd)> engines, float tilesPerMetre, Color32 tint, out string report)
        {
            var parts = new Dictionary<string, Piece>();
            int mask = 1 << skinLayer, quads = 0, dropped = 0;

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                var cen = (a + b + c + d) / 4f;
                if (drop(a) || drop(b) || drop(c) || drop(d)) { dropped++; return; }
                var name = MiG29Parts.Label(cen);
                if (!MiG29Parts.Skinned.Contains(name)) { dropped++; return; }
                if (!parts.TryGetValue(name, out var p)) parts[name] = p = new Piece();
                var an = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                Vector2 Q(Vector3 q) => (an.x >= an.y && an.x >= an.z ? new Vector2(q.z, q.y) : an.y >= an.z ? new Vector2(q.z, q.x) : new Vector2(q.x, q.y)) * tilesPerMetre;
                for (int side = 0; side < 2; side++)
                {
                    int b0 = p.v.Count;
                    foreach (var q in new[] { a, b, c, d }) { p.v.Add(q); p.n.Add(side == 0 ? normal : -normal); p.uv.Add(Q(q)); p.c.Add(tint); }
                    foreach (int k in side == 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 }) p.t.Add(b0 + k);
                }
                quads++;
            }

            bool Hit(Vector3 from, Vector3 dir, float max, out Vector3 at)
            {
                at = default;
                if (!Physics.Raycast(from, dir, out var h, max, mask, QueryTriggerInteraction.Ignore)) return false;
                at = h.point; return true;
            }

            // a ring frame in the plane through centre normal to axis: rays out to the skin, a band FrameDepth deep inside it
            void Ring(Vector3 centre, Vector3 axis, float maxRadius)
            {
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(axis, u);
                var outer = new Vector3?[FrameRays]; var inner = new Vector3[FrameRays];
                for (int i = 0; i < FrameRays; i++)
                {
                    float a = i * Mathf.PI * 2f / FrameRays;
                    var dir = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                    if (!Hit(centre, dir, maxRadius, out var s)) continue;
                    float depth = Mathf.Min(FrameDepth, (s - centre).magnitude * 0.5f);
                    outer[i] = s - dir * Inset; inner[i] = s - dir * depth;
                }
                for (int i = 0; i < FrameRays; i++)
                {
                    int j = (i + 1) % FrameRays;
                    if (outer[i] == null || outer[j] == null || (outer[i].Value - outer[j].Value).magnitude > MaxGap) continue;
                    Quad(outer[i].Value, outer[j].Value, inner[j], inner[i], axis);
                }
            }

            // the middle of the fuselage's cross-section at z: halfway between the skin above and below, on the centreline or (under
            // the canopy, which is not skin) beside it, or from the cockpit floor
            Vector3? Middle(float z)
            {
                foreach (var x in new[] { 0f, 0.3f, -0.3f })
                    for (float y0 = -0.4f; y0 <= 0.2f; y0 += 0.2f)
                    {
                        var p = new Vector3(x, y0, z);
                        if (Hit(p, Vector3.up, 2f, out var top) && Hit(p, Vector3.down, 2f, out var bottom)) return new Vector3(0f, (top.y + bottom.y) / 2f, z);
                    }
                // under the canopy: the cockpit floor's skin and the fuselage's depth there
                if (Hit(new Vector3(0f, 0.2f, z), Vector3.down, 2f, out var floor)) return new Vector3(0f, floor.y + 0.55f, z);
                return new Vector3(0f, -0.25f, z);   // over the nose gear well: the fuselage's middle height there
            }

            int frames = 0;
            for (float z = 5.3f; z > -4.7f; z -= FrameStep)
            {
                var m = Middle(z);
                if (m == null) continue;
                Ring(m.Value, Vector3.forward, 2.6f); frames++;
            }
            foreach (var (rear, fwd) in engines)
                for (float s = 0.3f; s < 3.0f; s += FrameStep)
                {
                    var c = rear + fwd * s;
                    if (c.z > -1.9f) break;
                    Ring(c, fwd, 0.9f); frames++;
                }

            // a web from the wing's lower skin to its upper skin along a line through the wing (x, z as a function of t)
            int webs = 0;
            void Web(Func<float, Vector3> at, float t0, float t1, float step, Vector3 normal, float yTop = -0.1f)
            {
                Vector3? pu = null, pl = null;
                for (float t = t0; t <= t1 + 1e-4f; t += step)
                {
                    var q = at(t);
                    Vector3? up = null, lo = null;
                    if (Hit(new Vector3(q.x, yTop, q.z), Vector3.down, 0.6f + yTop + 0.1f, out var top) && Hit(top + Vector3.down * 0.004f, Vector3.down, 0.5f, out var bottom))
                    { up = top + Vector3.down * Inset; lo = bottom + Vector3.up * Inset; }
                    if (up != null && pu != null && (up.Value - pu.Value).magnitude < MaxGap && (lo.Value - pl.Value).magnitude < MaxGap && up.Value.y - lo.Value.y > 0.01f)
                        Quad(pl.Value, lo.Value, up.Value, pu.Value, normal);
                    pu = up; pl = lo;
                }
                webs++;
            }
            foreach (var sx in new[] { -1f, 1f })
            {
                // ribs, streamwise, every 0.5 m, and a pair at each joint (the root rib, the flap/aileron rib, the tip rib), one on each
                // side of the break, as a production joint has
                foreach (var ax in new[] { 1.73f, 1.77f, 2.25f, 2.75f, 3.25f, 3.51f, 3.55f, 4.0f, 4.4f, 4.78f, 4.82f, 5.2f })
                {
                    float zl = leadingEdgeZ(ax) - 0.02f, zt = trailingEdgeZ(ax) + 0.02f;
                    Web(t => new Vector3(sx * ax, 0f, t), zt, zl, 0.04f, Vector3.right);
                }
                // front and rear spars, 15 % and 65 % of the chord
                foreach (var f in new[] { 0.15f, 0.65f })
                    Web(ax => new Vector3(sx * ax, 0f, Mathf.Lerp(leadingEdgeZ(ax), trailingEdgeZ(ax), f)), 1.78f, 5.3f, 0.05f,
                        new Vector3(0f, 0f, 1f));
            }
            // the LERX's ribs, square to the fuselage, from its root to its edge
            foreach (var sx in new[] { -1f, 1f })
                for (float z = 0.6f; z < 4.7f; z += FrameStep)
                {
                    float zz = z;
                    Web(t => new Vector3(sx * t, 0f, zz), 0.55f, 1.7f, 0.04f, Vector3.forward, 0.35f);
                }
            report = $"{frames} frames, {webs} rib and spar webs, {quads} quads on {parts.Count} parts ({dropped} dropped in the cockpit, the ducts or off the parts)";
            return parts;
        }
    }
}
