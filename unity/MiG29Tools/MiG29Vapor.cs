using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // High-alpha wing vapour. The KR-67's VaporEffect drives one wingVapor puff per side from a point on the KR-67 wing, which on the
    // MiG sat ahead of the wing beside the LERX: in a hard pull the vapour came off the fuselage side as a small puff (user video,
    // v0.9.0). The real MiG-29 pulls a sheet of vapour over the whole upper wing, from the LERX junction to near the tip, starting
    // just behind the leading edge (user photo). Here each side gets a row of wingVapor emitters along the span, each sized to the
    // local chord. The game's emitter rotates each one to face the airflow and plays it while alpha is above its speed curve.
    static class MiG29Vapor
    {
        // MiG frame (mig29_mesh.json): just behind the leading edge (LE z ~2.48 - 0.955 (x - 2.5), 42 deg sweep), about 0.2 m over
        // the upper surface; scale follows the chord, radius is the spawn disc across the flow.
        static readonly (float x, float y, float z, float scale, float radius)[] Stations =
        {
            (1.75f, 0.60f, 2.40f, 1.00f, 0.70f),   // LERX / wing junction
            (2.40f, 0.40f, 2.15f, 0.90f, 0.60f),
            (3.15f, 0.33f, 1.45f, 0.75f, 0.50f),
            (3.90f, 0.26f, 0.75f, 0.60f, 0.40f),
            (4.65f, 0.18f, 0.05f, 0.45f, 0.30f),
        };

        public static void Apply(Transform root, Vector3 modelOffset)
        {
            var fx = root.GetComponentsInChildren<MonoBehaviour>(true).First(m => m != null && m.GetType().Name == "VaporEffect");
            var so = new SerializedObject(fx);
            var emitters = so.FindProperty("emitters");
            int added = 0;
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1 : 1;
                var orig = root.GetComponentsInChildren<ParticleSystem>(true).First(p => p.name == "wingVapor_" + s);
                int idx = Enumerable.Range(0, emitters.arraySize).First(i => emitters.GetArrayElementAtIndex(i).FindPropertyRelative("particles").objectReferenceValue == orig);
                for (int k = 0; k < Stations.Length; k++)
                {
                    var st = Stations[k];
                    ParticleSystem ps;
                    if (k == 0) ps = orig;
                    else
                    {
                        ps = Object.Instantiate(orig.gameObject, orig.transform.parent).GetComponent<ParticleSystem>();
                        ps.name = "wingVapor_" + s + (k + 1);
                        emitters.InsertArrayElementAtIndex(idx);   // duplicates entry idx (same curve and settings)
                        var e = emitters.GetArrayElementAtIndex(idx + 1);
                        e.FindPropertyRelative("particles").objectReferenceValue = ps;
                        var et = e.FindPropertyRelative("emitTransforms");
                        et.arraySize = 1;
                        et.GetArrayElementAtIndex(0).objectReferenceValue = ps.transform;
                        added++;
                    }
                    ps.transform.position = new Vector3(sx * st.x, st.y, st.z) + modelOffset;
                    var main = ps.main;
                    main.startSize = new ParticleSystem.MinMaxCurve(1.4f * st.scale, 3.0f * st.scale);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);         // drifts back over the chord
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                    main.maxParticles = 30;
                    var shape = ps.shape;
                    shape.radius = st.radius;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] wing vapour: {Stations.Length} emitters per side along the span ({added} added)");
        }
    }
}
