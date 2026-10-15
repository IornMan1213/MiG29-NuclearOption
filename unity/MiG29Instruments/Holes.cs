using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // Holes where the shots land. The game's skin shader opens holes from the damage texture's alpha (the burn mask) as a part's
    // hit points fall, the same pattern whatever side was hit: a stock jet hides that by scattering its holes evenly, but the user
    // wanted them where the rounds actually struck (v0.9.2). So the MiG's damage map carries no holes of its own, and every hit on a
    // MiG paints one into a per-aircraft copy of the map: a jagged burnt-through core (alpha low, opens early) in a charred ring
    // (alpha just under the shader's cut-off, chars and opens as the part takes more damage), at the exact point of impact.
    //  - Bullets: BulletSim.Bullet.TrajectoryTrace is simulated on every client (visualOnly on the ones that do not own it), so
    //    everyone sees the same holes. Blasts: UnitPart.TakeShockwave (also on every client) scatters fragment holes facing the burst.
    //  - The impact on the hitbox is carried along the round's path onto the visible skin (ray against the part's skin mesh).
    //  - One 2048 canvas per MiG that has been hit, at most MaxCanvases, nearest the camera first; released when the jet goes.
    public static class Holes
    {
        public const string MiG29Key = "mig29_Fulcrum";   // MiG29Builder.JsonKey
        const int MaxCanvases = 4;
        static readonly int DmgTex = Shader.PropertyToID("_BasecolorDmg");
        static Material brush;
        static readonly System.Random rng = new System.Random();

        internal static bool IsMiG(Unit u) => u != null && u.definition != null && u.definition.jsonKey == MiG29Key;

        static Material Brush()
        {
            if (brush != null) return brush;
            var sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return null;
            brush = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            // dst = dst * src.a: each stamp scales the burn mask (and darkens the soot round it), repeated hits go deeper
            brush.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            brush.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            brush.SetInt("_Cull", 0); brush.SetInt("_ZWrite", 0); brush.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return brush;
        }

        /// <summary>A round (or fragment) struck `col` at `point` travelling along `dir`. `size`: hole radius in metres.</summary>
        internal static void Hit(Collider col, Vector3 point, Vector3 dir, float size)
        {
            try
            {
                if (col == null) return;
                var part = col.GetComponentInParent<UnitPart>();
                if (part == null || !IsMiG(part.parentUnit)) return;
                var canvas = HoleCanvas.For(part.parentUnit);
                canvas?.Stamp(part, point, dir.normalized, size);
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); }
        }

        internal static void Burst(UnitPart part, Vector3 origin, float blastPower)
        {
            try
            {
                if (part == null || !IsMiG(part.parentUnit)) return;
                var canvas = HoleCanvas.For(part.parentUnit);
                if (canvas == null) return;
                // fragments: a spray of small holes on the side facing the burst
                var c = part.transform.position;
                int n = Mathf.Clamp(Mathf.RoundToInt(3f + blastPower * 4f), 3, 14);
                for (int i = 0; i < n; i++)
                {
                    var aim = c + UnityEngine.Random.insideUnitSphere * Mathf.Clamp(0.4f + blastPower * 0.4f, 0.4f, 1.6f);
                    var dir = (aim - origin).normalized;
                    canvas.Stamp(part, aim - dir * 2f, dir, 0.03f + 0.03f * (float)rng.NextDouble());
                }
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); }
        }

        internal static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        internal static Material BrushMaterial => Brush();
        internal static int DamageTexId => DmgTex;
        internal static int Limit => MaxCanvases;
    }

    public class HoleCanvas : MonoBehaviour
    {
        static readonly List<HoleCanvas> live = new List<HoleCanvas>();
        Unit unit;
        RenderTexture rt;
        Texture original;
        readonly List<Renderer> skins = new List<Renderer>();
        readonly Dictionary<UnitPart, (MeshFilter mf, Vector3[] v, Vector2[] uv, int[] t)> meshes = new Dictionary<UnitPart, (MeshFilter, Vector3[], Vector2[], int[])>();
        bool dirty;

        internal static HoleCanvas For(Unit u)
        {
            var c = u.GetComponent<HoleCanvas>();
            if (c != null) return c.rt != null ? c : null;
            // over the limit: give up the canvas of the MiG farthest from the camera (its holes go; it keeps its stock damage)
            if (live.Count >= Holes.Limit)
            {
                var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                HoleCanvas far = null; float fd = -1f;
                foreach (var l in live) { if (l == null) continue; float d = (l.transform.position - cam).sqrMagnitude; if (d > fd) { fd = d; far = l; } }
                float mine = (u.transform.position - cam).sqrMagnitude;
                if (far == null || mine >= fd) return null;
                Destroy(far);
            }
            c = u.gameObject.AddComponent<HoleCanvas>();
            return c.Init(u) ? c : null;
        }

        bool Init(Unit u)
        {
            unit = u;
            Texture src = null;
            // in flight every part is its own physics root, no longer under the aircraft: its parts come from the unit's part list
            var renderers = new List<MeshRenderer>(u.GetComponentsInChildren<MeshRenderer>(true));
            foreach (var p in u.partLookup) if (p != null && !p.transform.IsChildOf(u.transform)) renderers.AddRange(p.GetComponentsInChildren<MeshRenderer>(true));
            foreach (var r in renderers)
            {
                if (!r.name.StartsWith("MiG29_skin_") && !r.name.StartsWith("MiG29_lod")) continue;
                var m = r.sharedMaterial;
                if (m == null || !m.HasProperty(Holes.DamageTexId)) continue;
                if (src == null) src = m.GetTexture(Holes.DamageTexId);
                skins.Add(r);
            }
            original = src;
            if (src == null || Holes.BrushMaterial == null) { MiG29InstrumentsPlugin.Log.LogWarning($"holes: no skin damage map ({skins.Count} skins) or brush shader ({Holes.BrushMaterial != null})"); return false; }
            rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { name = "MiG29_holes", useMipMap = true, autoGenerateMips = false, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            rt.Create();
            Graphics.Blit(src, rt);
            rt.GenerateMips();
            foreach (var r in skins) r.material.SetTexture(Holes.DamageTexId, rt);   // .material: the game's damage tint is per renderer too
            live.Add(this);
            return true;
        }

        void OnDestroy()
        {
            live.Remove(this);
            if (rt != null)
            {
                foreach (var r in skins) if (r != null && r.sharedMaterial != null && r.sharedMaterial.GetTexture(Holes.DamageTexId) == rt) r.sharedMaterial.SetTexture(Holes.DamageTexId, original);
                rt.Release(); Destroy(rt); rt = null;
            }
        }

        void LateUpdate()
        {
            if (!dirty || rt == null) return;
            rt.GenerateMips();
            dirty = false;
        }

        (MeshFilter mf, Vector3[] v, Vector2[] uv, int[] t) Mesh(UnitPart part)
        {
            if (meshes.TryGetValue(part, out var m)) return m;
            MeshFilter mf = null;
            foreach (Transform ch in part.transform) if (ch.name.StartsWith("MiG29_skin_")) { mf = ch.GetComponent<MeshFilter>(); break; }
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
            {
                MiG29InstrumentsPlugin.Log.LogWarning($"holes: no readable skin mesh on {part.name} ({(mf == null ? "none" : mf.sharedMesh == null ? "no mesh" : "not readable")})");
                meshes[part] = m = (null, null, null, null); return m;
            }
            var sm = mf.sharedMesh;
            meshes[part] = m = (mf, sm.vertices, sm.uv, sm.triangles);
            return m;
        }

        internal void Stamp(UnitPart part, Vector3 point, Vector3 dir, float size)
        {
            var (mf, v, uv, t) = Mesh(part);
            if (mf == null) return;
            // the skin under the impact: the first skin triangle along the round's path, from 1.5 m before the hitbox
            var tr = mf.transform;
            var o = tr.InverseTransformPoint(point - dir * 1.5f); var d = tr.InverseTransformDirection(dir).normalized;
            float best = 3.5f; int hit = -1; float bu = 0, bv = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                var a = v[t[i]]; var e1 = v[t[i + 1]] - a; var e2 = v[t[i + 2]] - a;
                var pv = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det; var tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(tv, e1); float w = Vector3.Dot(d, q) * inv; if (w < 0f || u + w > 1f) continue;
                float dist = Vector3.Dot(e2, q) * inv;
                if (dist > 0f && dist < best) { best = dist; hit = i; bu = u; bv = w; }
            }
            if (hit < 0) return;   // the round only grazed the hitbox: no skin there
            Vector2 A = uv[t[hit]], B = uv[t[hit + 1]], C = uv[t[hit + 2]];
            var centre = A + (B - A) * bu + (C - A) * bv;
            // metres -> UV here (texel density of the struck triangle)
            float m3 = (v[t[hit + 1]] - v[t[hit]]).magnitude, u3 = (B - A).magnitude;
            float k = m3 > 1e-5f ? u3 / m3 : 0.05f;
            float r = Mathf.Min(size * k, 0.012f);   // a big brush would spill over a UV island edge onto another part of the jet
            if (r < 1e-5f) return;
            Draw(centre, r);
        }

        void Draw(Vector2 c, float r)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix(); GL.LoadOrtho();
            Holes.BrushMaterial.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            // charred ring (scales the mask to ~0.7: chars soon, opens once the part is badly hurt), then the burnt-through core
            // (~0.3: opens after the first few hits). Jagged outlines, a little rotated each time.
            Poly(c, r * Holes.Rand(2.0f, 2.8f), 0.72f, 13);
            Poly(c, r * Holes.Rand(0.9f, 1.2f), 0.30f, 11);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
            dirty = true;
        }

        static void Poly(Vector2 c, float r, float alpha, int n)
        {
            var col = new Color(1f, 1f, 1f, alpha);
            float a0 = Holes.Rand(0f, Mathf.PI * 2f);
            Vector2 prevP = Vector2.zero, first = Vector2.zero;
            for (int i = 0; i <= n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / n;
                float rr = i == n ? 0f : r * Holes.Rand(0.55f, 1.0f);
                var p = i == n ? first : c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                if (i == 0) first = p;
                else
                {
                    GL.Color(col); GL.Vertex3(c.x, c.y, 0f);
                    GL.Color(col); GL.Vertex3(prevP.x, prevP.y, 0f);
                    GL.Color(col); GL.Vertex3(p.x, p.y, 0f);
                }
                prevP = p;
            }
        }
    }

    // ---- hooks ----

    [HarmonyPatch]
    static class BulletHolePatch
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(BulletSim.Bullet), "TrajectoryTrace");

        static void Prefix(BulletSim.Bullet __instance, out (bool impacted, Vector3 vel) __state) => __state = (__instance.impacted, __instance.velocity);

        static void Postfix(BulletSim.Bullet __instance, (bool impacted, Vector3 vel) __state)
        {
            if (__state.impacted || !__instance.impacted) return;
            var vel = __state.vel; if (vel.sqrMagnitude < 1f) return;
            var dir = vel.normalized;
            var at = __instance.position.ToLocalPosition();   // set to the hit point less 0.1 m along the path
            if (Physics.Raycast(at, dir, out var hit, 0.6f, ~(int)PhysicsLayers.ExclusionZonesMask, QueryTriggerInteraction.Ignore))
                Holes.Hit(hit.collider, hit.point, dir, 0.045f);
        }
    }

    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.TakeShockwave))]
    static class BlastHolePatch
    {
        static void Postfix(AeroPart __instance, Vector3 origin, float overpressure, float blastPower)
        {
            if (overpressure > 2000f) Holes.Burst(__instance, origin, blastPower);
        }
    }
}
