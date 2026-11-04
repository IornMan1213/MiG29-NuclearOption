using UnityEngine;

namespace MiG29Instruments
{
    // The nose leg's torque links (MiG29Tools.MiG29NoseGear): a scissor across the front of the oleo, the upper arm hinged on the
    // barrel, the lower on the piston. The oleo stands at its own extension in game and moves with the load, so rigid arms parted
    // at the apex; each frame the jet is within 300 m of the camera this swings both arms, about their hinge's x axis, to meet in front of the leg.
    public class TorqueLinkDriver : MonoBehaviour
    {
        Aircraft ac;
        Transform upper, lower;
        float armU, armL, nextFind;
        int finds;

        void Start() => ac = GetComponent<Aircraft>();

        void Find()
        {
            foreach (var root in MiGParts.Roots(ac))
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "MiG29_nose_link_upper") upper = t;
                    else if (t.name == "MiG29_nose_link_lower") lower = t;
                }
            if (upper == null || lower == null) { upper = lower = null; return; }
            float Arm(Transform t) => t.GetComponent<MeshFilter>()?.sharedMesh is Mesh m ? m.bounds.max.z * t.lossyScale.z : 0.3f;
            armU = Arm(upper); armL = Arm(lower);
        }

        void LateUpdate()
        {
            if (ac == null) return;
            if (upper == null || lower == null)
            {
                if (Time.time < nextFind || finds > 20) return;
                nextFind = Time.time + 1f; finds++;
                Find();
                return;
            }
            var cam = Camera.main;   // only near the camera: far away the links are a few pixels (isVisible lags a frame and missed render-to-texture views)
            if (cam == null || (cam.transform.position - upper.position).sqrMagnitude > 300f * 300f) return;
            Vector3 pu = upper.position, pl = lower.position;
            var d = pl - pu; float len = d.magnitude;
            if (len < 1e-4f || upper.parent == null || lower.parent == null) return;
            var dir = d / len;
            // in the leg's plane, perpendicular to the hinge line and towards the front of the leg
            var ahead = Vector3.ProjectOnPlane(upper.parent.forward, dir).normalized;
            float x = (armU * armU - armL * armL + len * len) / (2f * len);
            float h = Mathf.Sqrt(Mathf.Max(0f, armU * armU - x * x));
            var apex = pu + dir * Mathf.Min(x, armU) + ahead * h;
            Aim(upper, apex); Aim(lower, apex);
        }

        static void Aim(Transform arm, Vector3 apex)
        {
            var local = arm.parent.InverseTransformDirection(apex - arm.position);
            local.x = 0f;   // the arms only swing about their hinge pins
            if (local.sqrMagnitude < 1e-8f) return;
            local.Normalize();
            arm.localRotation = Quaternion.LookRotation(local, Vector3.Cross(local, Vector3.right));   // keeps the arm's x on the pin
        }
    }
}
