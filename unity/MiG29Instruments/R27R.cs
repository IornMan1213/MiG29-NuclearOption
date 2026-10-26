using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // R-27R fired with no target. The game lets a missile launch with an empty target list (WeaponManager.Fire passes target null),
    // and SARHSeeker.Seek detonates a missile that has no target as soon as its warhead is armed. On the SAM it was made for that is
    // harmless, but it blew the R-27R up at the pylon (user report, v0.9.0; MiG29Weapons now also spawns it unarmed). Like the
    // radar and IR missiles, an R-27R without a target now flies straight ahead unguided: it arms after its arm delay and
    // SARHSeeker.SlowChecks still self-destructs it once the motor is out and it slows down. With a target, the stock seeker runs.
    // Every R-27R seen here is also handed to R27RCue for the "hold lock" cue.
    [HarmonyPatch(typeof(SARHSeeker), "Seek")]
    static class R27RNoTarget
    {
        const float ArmDelay = 1f;     // MiG29Weapons TuneSeeker armDelay for the R-27R
        static readonly AccessTools.FieldRef<MissileSeeker, Missile> MissileOf = AccessTools.FieldRefAccess<MissileSeeker, Missile>("missile");
        internal static readonly AccessTools.FieldRef<MissileSeeker, Unit> TargetOf = AccessTools.FieldRefAccess<MissileSeeker, Unit>("targetUnit");

        static bool Prefix(SARHSeeker __instance)
        {
            var m = MissileOf(__instance);
            if (m == null || !m.name.StartsWith("mig29_R27R")) return true;
            R27RCue.Track(m, __instance);
            if (TargetOf(__instance) != null) return true;
            if (!m.IsArmed() && m.timeSinceSpawn > ArmDelay) m.Arm();
            m.SetAimpoint(m.GlobalPosition() + m.transform.forward * 10000f, Vector3.zero);
            return false;
        }
    }

    // "Hold lock" cue for the player's R-27Rs. The R-27R is semi-active: it only guides while the launching jet keeps the target,
    // and players lose shots by switching targets or turning away after launch. While one of your R-27Rs is guiding, a line
    // under the crosshair says so with the time to impact; if the target drops out of your target list it says the lock is lost.
    public class R27RCue : MonoBehaviour
    {
        class Shot { public Missile m; public SARHSeeker seeker; public float dist = -1f, time, tti = -1f; public bool lost; }

        static readonly List<Shot> shots = new List<Shot>();
        static readonly HashSet<Missile> known = new HashSet<Missile>();

        string text;
        float nextUpdate, lostUntil;
        GUIStyle style, shadow;

        internal static void Track(Missile m, SARHSeeker seeker)
        {
            if (known.Add(m)) shots.Add(new Shot { m = m, seeker = seeker });
        }

        void Update()
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 0.2f;
            text = null;
            for (int i = shots.Count - 1; i >= 0; i--)
                if (shots[i].m == null) { known.Remove(shots[i].m); shots.RemoveAt(i); }
            if (!MiG29InstrumentsPlugin.R27RCue.Value) return;
            var hud = SceneSingleton<CombatHUD>.i;
            var player = hud != null ? hud.aircraft : null;
            if (player == null) return;

            int guiding = 0; float nearest = -1f;
            foreach (var s in shots)
            {
                if (s.m.owner != player || s.lost) continue;
                var target = R27RNoTarget.TargetOf(s.seeker);
                if (target == null) continue;   // fired without a target: it flies straight, nothing to hold
                float d = Vector3.Distance(s.m.transform.position, target.transform.position), now = Time.time;
                if (s.dist >= 0f && now > s.time)
                {
                    float closure = (s.dist - d) / (now - s.time);
                    s.tti = closure > 30f ? d / closure : -1f;
                }
                s.dist = d; s.time = now;
                bool held = player.weaponManager != null && player.weaponManager.CheckIsTarget(target);
                if (!held) { s.lost = true; lostUntil = Time.unscaledTime + 3f; continue; }
                guiding++;
                if (s.tti > 0f && (nearest < 0f || s.tti < nearest)) nearest = s.tti;
            }
            if (guiding > 0)
                text = (guiding > 1 ? $"{guiding} x R-27R guiding: hold the lock" : "R-27R guiding: hold the lock") +
                       (nearest > 0f ? $"  ({Mathf.CeilToInt(nearest)} s)" : "");
            else if (Time.unscaledTime < lostUntil)
                text = "R-27R: lock lost";
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(text)) return;
            float scale = Screen.height / 1080f;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            }
            style.fontSize = shadow.fontSize = Mathf.RoundToInt(20 * scale);
            style.normal.textColor = text.EndsWith("lost") ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.78f, 0.3f);
            var r = new Rect(0f, Screen.height * 0.62f, Screen.width, 30f * scale);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
        }
    }
}
