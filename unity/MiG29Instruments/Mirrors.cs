using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiG29Instruments
{
    // Folds the MiG-29's three rear-view mirrors (MiG29_ins_mirror_<c|l|r>, built by blender/cockpit_build.py) flat along the inside
    // of the canopy, or swings them back out. Driven by the "Fold mirrors" option; the fold key flips that option mid-flight (and it
    // stays as left). The mirrors only show the pilot's cockpit, so this works on the aircraft the player flies.
    public class MirrorFolder : MonoBehaviour
    {
        const string MiG29Key = "mig29_Fulcrum";
        const float FallbackFold = 60f;   // degrees about the hinge, for a build without the "folded" pose
        const float Duration = 0.6f;      // seconds to fold or unfold

        readonly List<(Transform needle, Quaternion folded)> mirrors = new List<(Transform, Quaternion)>();
        Aircraft found;
        float angle = -1f;                // current fold; -1 = snap to the option on the next find

        void Update()
        {
            try
            {
                if (MiG29InstrumentsPlugin.MirrorFoldKey.Value.IsDown())
                    MiG29InstrumentsPlugin.FoldMirrors.Value = !MiG29InstrumentsPlugin.FoldMirrors.Value;

                var hud = SceneSingleton<CombatHUD>.i;
                var ac = hud != null ? hud.aircraft : null;
                if (ac == null || ((Unit)ac).definition == null || ((Unit)ac).definition.jsonKey != MiG29Key) { found = null; mirrors.Clear(); return; }
                if (ac != found)
                {
                    found = ac; mirrors.Clear(); angle = -1f;
                    foreach (var t in InstrumentDriver.Root(ac).GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("MiG29_ins_mirror_") && t.Find("needle") is Transform n)
                            mirrors.Add((n, t.Find("folded") is Transform f ? f.localRotation : Quaternion.Euler(FallbackFold, 0f, 0f)));
                }
                if (mirrors.Count == 0) return;

                // angle here is the fold fraction, 0 = out, 1 = folded flat against the canopy
                float target = MiG29InstrumentsPlugin.FoldMirrors.Value ? 1f : 0f;
                angle = angle < 0f ? target : Mathf.MoveTowards(angle, target, Time.deltaTime / Duration);
                float k = angle * angle * (3f - 2f * angle);
                foreach (var (n, folded) in mirrors) if (n != null) n.localRotation = Quaternion.Slerp(Quaternion.identity, folded, k);
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); enabled = false; }
        }
    }
}
