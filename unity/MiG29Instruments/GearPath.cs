using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MiG29Instruments
{
    // Main-gear retraction path for the MiG-29. The game moves a leg in a straight line from down to stowed (hinge angle, strut
    // twist and hinge position all linear in the fold amount). The KR-67 leg only fits the MiG inside the intake duct, so that straight
    // line cut through the intake's outer skin mid-swing. This re-poses the legs each frame while they move: fold and twist up into the
    // bay opening first (keyframe at 70 %), then slide inboard to the stow the prefab already holds (MiG29Builder.MainFold/MainStrut/
    // MainStowShift, from blender/analysis/main_gear_search.py and main_gear_path.py). At rest (fully down or up) it does nothing.
    public class GearPathDriver : MonoBehaviour
    {
        // left leg, MiG/aircraft frame; the right leg mirrors x. Keep the last keyframe in step with MiG29Builder.
        static readonly (float t, float fold, float strut, Vector3 shift)[] Keys =
        {
            (0f, 0f, 0f, Vector3.zero),
            (0.7f, -59f, 99f, new Vector3(-0.10f, -0.10f, -0.20f)),
            (1f, -74f, 165f, new Vector3(0.575f, -0.10f, 0.175f)),
        };

        static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly FieldInfo fHinge = typeof(LandingGear).GetField("gearHinge", F), fAircraft = typeof(LandingGear).GetField("aircraft", F),
            fFoldAmount = typeof(LandingGear).GetField("foldAmount", F), fBasePos = typeof(LandingGear).GetField("hingeBasePos", F),
            fBaseAngles = typeof(LandingGear).GetField("hingeBaseAngles", F), fStrutT = typeof(LandingGear).GetField("strutRotationTransform", F),
            fUnsprung = typeof(LandingGear).GetField("unsprung", F);

        class Leg { public LandingGear gear; public Transform hinge, parent, strut; public float side; public Matrix4x4 toLocal; }

        Aircraft ac;
        readonly List<Leg> legs = new List<Leg>();
        float nextFind;
        int finds;

        void Start() => ac = GetComponent<Aircraft>();

        void Find()
        {
            foreach (var g in FindObjectsOfType<LandingGear>())
            {
                if ((Aircraft)fAircraft.GetValue(g) != ac) continue;
                var hinge = fHinge.GetValue(g) as Transform;
                if (hinge == null || (hinge.name != "gearHinge_L" && hinge.name != "gearHinge_R") || hinge.parent == null) continue;
                var strut = fStrutT.GetValue(g) as Transform;
                if (strut == null && fUnsprung.GetValue(g) is GameObject us) strut = us.transform;
                legs.Add(new Leg
                {
                    gear = g, hinge = hinge, parent = hinge.parent, strut = strut, side = hinge.name.EndsWith("_L") ? 1f : -1f,
                    // aircraft frame -> hinge parent frame, while the airframe is still one rigid piece
                    toLocal = hinge.parent.worldToLocalMatrix * ac.transform.localToWorldMatrix,
                });
            }
        }

        static void Sample(float t, out float fold, out float strut, out Vector3 shift)
        {
            for (int i = 1; i < Keys.Length; i++)
            {
                if (t > Keys[i].t && i < Keys.Length - 1) continue;
                var a = Keys[i - 1]; var b = Keys[i];
                float u = Mathf.Clamp01((t - a.t) / (b.t - a.t));
                fold = Mathf.Lerp(a.fold, b.fold, u); strut = Mathf.Lerp(a.strut, b.strut, u); shift = Vector3.Lerp(a.shift, b.shift, u);
                return;
            }
            fold = 0f; strut = 0f; shift = Vector3.zero;
        }

        void LateUpdate()
        {
            if (ac == null) return;
            if (legs.Count == 0)
            {
                if (Time.time < nextFind || finds > 20) return;
                nextFind = Time.time + 1f; finds++;
                Find();
                return;
            }
            foreach (var leg in legs)
            {
                if (leg.gear == null || leg.hinge == null || leg.hinge.parent != leg.parent) continue;   // broken off
                float t = (float)fFoldAmount.GetValue(leg.gear);
                if (t <= 0.001f || t >= 0.999f) continue;
                Sample(t, out float fold, out float strut, out Vector3 shift);
                shift.x *= leg.side;
                leg.hinge.localEulerAngles = (Vector3)fBaseAngles.GetValue(leg.gear) + new Vector3(fold, 0f, 0f);
                leg.hinge.localPosition = (Vector3)fBasePos.GetValue(leg.gear) + leg.toLocal.MultiplyVector(shift);
                if (leg.strut != null) leg.strut.localEulerAngles = new Vector3(0f, strut, 0f);
            }
        }
    }
}
