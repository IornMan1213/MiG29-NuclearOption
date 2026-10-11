using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiG29Tools
{
    // Levels of detail for the MiG's own geometry. The KR-67's LODGroup swaps its parts for simpler ones and then culls them as the
    // jet shrinks on screen, but every renderer the MiG adds (skin, control surfaces, cockpit, 96 nozzle petals, wells, doors) sat
    // outside it: each MiG in view drew ~250 objects and ~80k triangles at any distance, AI jets 20 km away included (lag reports
    // in missions with several MiGs, v0.9.1). The group keeps its size and its KR-67 transition heights, with one level added:
    //   LOD0  > 0.05   (~470 m)   everything
    //   LOD1  > 0.01   (~2.3 km)  the outside: skin, surfaces, canopy, outer nozzle petals, outer doors, airbrake panels
    //   LOD2  > 0.001  (~23 km)   skin, surfaces, canopy, afterburner flames
    //   LOD3  > 0.0005 (~46 km)   the same; culled beyond, as the KR-67 is
    // (distances for the game's ~60 deg field of view and LOD bias 1). Parts that are only ever seen up close inside wells, bays and
    // intakes also stop casting shadows.
    static class MiG29Lod
    {
        static readonly string[] Always = { "MiG29_skin_", "MiG29_aileron_", "MiG29_flap_", "MiG29_rudder_", "MiG29_stab_", "MiG29_canopy", "MiG29_abflame_", "MiG29_nozstatic_" };
        static readonly string[] Internal = { "MiG29_well_", "MiG29_airbrake_well", "MiG29_fod_", "MiG29_intake_blocker", "MiG29_noz_L_inner", "MiG29_noz_R_inner", "MiG29_noz_L_seal", "MiG29_noz_R_seal" };

        static bool Middle(string n) =>
            (n.StartsWith("MiG29_noz_") && n.Contains("_flap_")) ||
            (n.StartsWith("MiG29_door_") && !n.EndsWith("_inner")) ||
            (n.StartsWith("MiG29_airbrake_") && n.EndsWith("_panel")) ||
            n == "MiG29_ck_frames_ext";

        public static void Apply(GameObject go)
        {
            var lg = go.GetComponent<LODGroup>();
            var old = lg.GetLODs();
            if (old.Length != 3) { Debug.LogError($"[MiG29] LOD: expected the KR-67's 3 levels, found {old.Length}"); return; }
            var inGroup = new HashSet<Renderer>(old.SelectMany(l => l.renderers).Where(r => r != null));
            var all = new List<Renderer>(); var mid = new List<Renderer>(); var far = new List<Renderer>();
            int shadowsOff = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (inGroup.Contains(r) || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (r is MeshRenderer && (mf == null || mf.sharedMesh == null)) continue;
                string n = r.name;
                all.Add(r);
                if (Always.Any(p => n.StartsWith(p))) { mid.Add(r); far.Add(r); }
                else if (Middle(n)) mid.Add(r);
                if (Internal.Any(p => n.StartsWith(p)) && r.shadowCastingMode != ShadowCastingMode.Off) { r.shadowCastingMode = ShadowCastingMode.Off; shadowsOff++; }
            }
            // the KR-67's own levels (gear, wheels, pilot, afterburner glows... the rest have no mesh): LOD1 keeps its parts down to
            // the old LOD1 height, through the new LOD2
            var lods = new LOD[4];
            lods[0] = new LOD(0.05f, old[0].renderers.Concat(all).ToArray());
            lods[1] = new LOD(0.01f, old[1].renderers.Concat(mid).ToArray());
            lods[2] = new LOD(0.001f, old[1].renderers.Concat(far).ToArray());
            lods[3] = new LOD(0.0005f, old[2].renderers.Concat(far).ToArray());
            lg.SetLODs(lods);
            Debug.Log($"[MiG29] LOD: {all.Count} MiG renderers up close, {mid.Count} to ~2.3 km, {far.Count} to ~46 km; shadows off on {shadowsOff} internal parts");
        }
    }
}
