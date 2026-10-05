using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MiG29Instruments
{
    // Main-gear retraction path for the MiG-29, as on the real jet (user's War Thunder reference): the leg swings forward and up
    // beside the intake trunk with the wheel edge-on, through the strip door under the wing root; over the last fifth of the travel
    // the wheel turns flat and goes in through the forward door at the top of the trunk side into the glove above the intake duct.
    // The game itself folds the leg in one straight arc about the hinge parent's x axis, which MiG29Tools.MiG29MainGear yaws so the
    // arc ends in the right stow; that arc carries the wheel through the trunk skin mid-swing, so while a leg moves this re-poses it
    // each frame along blender/analysis/main_gear_path3.py's path. At rest (fully down or up) it does nothing.
    public class GearPathDriver : MonoBehaviour
    {
        // Keep in step with MiG29Tools.MiG29MainGear (StowYaw, StowFold, StowStrut) and main_gear_path3.json.
        const float StowYaw = 25f, StowFold = -76f, StowStrut = 135f;
        const float KeyT = 0.8f, KeyYaw = -10f, KeyFold = -70f, KeyStrut = 0f;   // keyframe: KeyYaw is in the unyawed mount frame

        static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly FieldInfo fHinge = typeof(LandingGear).GetField("gearHinge", F), fAircraft = typeof(LandingGear).GetField("aircraft", F),
            fFoldAmount = typeof(LandingGear).GetField("foldAmount", F), fStrutT = typeof(LandingGear).GetField("strutRotationTransform", F),
            fUnsprung = typeof(LandingGear).GetField("unsprung", F);

        class Leg { public LandingGear gear; public Transform hinge, parent, strut; }

        Aircraft ac;
        readonly List<Leg> legs = new List<Leg>();
        float nextFind;
        int finds;

        // hinge local rotations (the hinge parent is the yawed mount)
        static readonly Vector3 KeyAxis = Quaternion.AngleAxis(KeyYaw - StowYaw, Vector3.up) * Vector3.right;
        static readonly Quaternion KeyRot = Quaternion.AngleAxis(KeyFold, KeyAxis);
        static readonly Quaternion StowRot = Quaternion.Euler(StowFold, 0f, 0f);

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
                legs.Add(new Leg { gear = g, hinge = hinge, parent = hinge.parent, strut = strut });
            }
        }

        static void Sample(float t, out Quaternion rot, out float strut)
        {
            if (t <= KeyT)
            {
                float u = t / KeyT;
                rot = Quaternion.AngleAxis(KeyFold * u, KeyAxis); strut = KeyStrut * u;
            }
            else
            {
                float u = (t - KeyT) / (1f - KeyT);
                rot = Quaternion.Slerp(KeyRot, StowRot, u); strut = Mathf.Lerp(KeyStrut, StowStrut, u);
            }
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
                Sample(t, out var rot, out float strut);
                leg.hinge.localRotation = rot;
                if (leg.strut != null) leg.strut.localEulerAngles = new Vector3(0f, strut, 0f);
            }
        }
    }
}
