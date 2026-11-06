using UnityEngine;

namespace MiG29Instruments
{
    // The nose leg's torque links (MiG29Tools.MiG29NoseGear): a scissor across the front of the oleo, the upper arm hinged on the
    // barrel, the lower on the piston. The oleo stands at its own extension in game and moves with the load, and steering turns the
    // piston (and the lower arm) under the barrel, so rigid arms parted at the apex. Each frame the jet is within 300 m of the camera
    // this carries the upper hinge round with the steering, then swings both arms about the piston's x axis to meet in front of the leg.
    public class TorqueLinkDriver : MonoBehaviour
    {
        Aircraft ac;
        Transform upper, lower;
        Vector3 upperHinge;   // the upper hinge on the barrel, unsteered
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
            // hinge to apex pin: the mesh's far end less the apex boss round the pin (MiG29NoseGear.ApexBoss, 0.034 m)
            float Arm(Transform t) => t.GetComponent<MeshFilter>()?.sharedMesh is Mesh m ? (m.bounds.max.z - 0.034f) * t.lossyScale.z : 0.36f;
            armU = Arm(upper); armL = Arm(lower);
            upperHinge = upper.localPosition;
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
            if (upper.parent == null || lower.parent == null) return;
            // the steering angle: the piston's turn about the barrel's axis, applied to the upper hinge
            var barrel = upper.parent; var piston = lower.parent;
            float steer = Vector3.SignedAngle(Vector3.ProjectOnPlane(barrel.forward, barrel.up), Vector3.ProjectOnPlane(piston.forward, barrel.up), barrel.up);
            upper.localPosition = Quaternion.AngleAxis(steer, Vector3.up) * upperHinge;
            Vector3 pu = upper.position, pl = lower.position;
            var d = pl - pu; float len = d.magnitude;
            if (len < 1e-4f) return;
            var dir = d / len;
            // in the steered leg's plane, perpendicular to the hinge line and towards the front of the leg
            var axis = piston.right;
            var ahead = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(piston.forward, dir), axis).normalized;
            float x = (armU * armU - armL * armL + len * len) / (2f * len);
            float h = Mathf.Sqrt(Mathf.Max(0f, armU * armU - x * x));
            var apex = pu + dir * Mathf.Min(x, armU) + ahead * h;
            Aim(upper, apex, axis); Aim(lower, apex, axis);
        }

        static void Aim(Transform arm, Vector3 apex, Vector3 axis)
        {
            var f = Vector3.ProjectOnPlane(apex - arm.position, axis);   // the arms only swing about their hinge pins
            if (f.sqrMagnitude < 1e-8f) return;
            f.Normalize();
            arm.rotation = Quaternion.LookRotation(f, Vector3.Cross(f, axis));   // keeps the arm's x on the pin
        }
    }
}
