using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Dev check: poses the built prefab's landing gear exactly as LandingGear.MoveGear animates it (hinge fold, hinge fold motion,
    // strut rotation, moving parts, door euler lerp) and writes every visible mesh, MiG frame, per pose to MiG29Out/gear_<pose>.json
    // for blender/analysis/gear_look.py. Also logs each leg's serialized fold settings.
    public static class MiG29GearDump
    {
        const string PrefabPath = "Assets/Blueprinter/Mods/mig29/MiG29.prefab";
        static readonly Vector3 ModelOffset = new Vector3(0f, -0.44f, -2.655f);

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory("MiG29Out");
                // pose: gear fold fraction, door open fraction (doors snap to localEulerAngles zero once retracted)
                foreach (var (pose, fold, door) in new[] { ("down", 0f, 1f), ("q1", 0.25f, 1f), ("mid", 0.5f, 1f), ("q3", 0.75f, 1f), ("up", 1f, -1f), ("doorhalf", 0f, 0.5f) })
                    Dump(pose, fold, door, pose == "down");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Dump(string pose, float t, float door, bool log)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var inv = CultureInfo.InvariantCulture;
            string V3(Vector3 v) => $"[{v.x.ToString("F5", inv)},{v.y.ToString("F5", inv)},{v.z.ToString("F5", inv)}]";
            var gears = new StringBuilder();
            foreach (var lg in go.GetComponentsInChildren(T("LandingGear"), true))
            {
                var so = new SerializedObject(lg);
                var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
                float fold = so.FindProperty("foldDegrees").floatValue, strut = so.FindProperty("strutRotation").floatValue;
                var motion = so.FindProperty("hingeFoldMotion").vector3Value;
                var srt = so.FindProperty("strutRotationTransform").objectReferenceValue as Transform;
                if (srt == null && so.FindProperty("unsprung").objectReferenceValue is GameObject us) srt = us.transform;
                if (hinge != null && srt != null)
                    gears.Append(gears.Length == 0 ? "" : ",").Append($"{{\"name\":\"{lg.name}\",\"hinge\":{V3(hinge.position - ModelOffset)},\"hinge_x\":{V3(hinge.right)},\"srt\":\"{srt.name}\",\"srt_pos\":{V3(srt.position - ModelOffset)},\"srt_y\":{V3(srt.up)},\"fold\":{fold.ToString(inv)},\"strut\":{strut.ToString(inv)}}}");
                if (log)
                {
                    foreach (var l in lg.GetComponentsInChildren<Light>(true))
                        Debug.Log($"[MiG29] light {l.name}: {l.type} intensity {l.intensity} range {l.range} angle {l.spotAngle} pos {l.transform.position - ModelOffset:F3} fwd {l.transform.forward:F2} active {l.gameObject.activeSelf}");
                    var sb = new StringBuilder();
                    sb.Append($"[MiG29] gear {lg.name}: travel {so.FindProperty("suspensionTravel").floatValue:F3} wheelRadius {so.FindProperty("wheelRadius").floatValue:F3}, hinge {hinge?.name} (parent {hinge?.parent?.name}) base euler {hinge?.localEulerAngles:F1} pos {hinge?.localPosition:F3}, fold {fold:F1}, motion {motion:F3}, strut {strut:F1} on {srt?.name}");
                    var mp = so.FindProperty("movingParts");
                    for (int i = 0; i < mp.arraySize; i++)
                    {
                        var e = mp.GetArrayElementAtIndex(i);
                        var tr = e.FindPropertyRelative("transform").objectReferenceValue as Transform;
                        var tg = e.FindPropertyRelative("target").objectReferenceValue as Transform;
                        sb.Append($" | moving {tr?.name} target {tg?.name} foldAngles {e.FindPropertyRelative("foldAngles").vector3Value:F1}");
                    }
                    var jt = so.FindProperty("joints");
                    for (int i = 0; i < jt.arraySize; i++)
                    {
                        var e = jt.GetArrayElementAtIndex(i);
                        sb.Append($" | joint {(e.FindPropertyRelative("hinge1").objectReferenceValue as Transform)?.name}/{(e.FindPropertyRelative("hinge2").objectReferenceValue as Transform)?.name}/{(e.FindPropertyRelative("elbow").objectReferenceValue as Transform)?.name}");
                    }
                    var gd = so.FindProperty("gearDoors");
                    for (int i = 0; i < gd.arraySize; i++)
                    {
                        var e = gd.GetArrayElementAtIndex(i);
                        sb.Append($" | door {(e.FindPropertyRelative("transform").objectReferenceValue as Transform)?.name} closed {e.FindPropertyRelative("closedAngle").vector3Value:F1} open {e.FindPropertyRelative("openAngle").vector3Value:F1}");
                    }
                    Debug.Log(sb.ToString());
                    // physics transforms (MiG frame): the suspension cast and the unsprung placement hang off these
                    var tf = new StringBuilder($"[MiG29] gear {lg.name} transforms:");
                    foreach (var pn in new[] { "castPoint", "bumpStop", "axle", "unsprung", "gearCollider" })
                    {
                        var o = so.FindProperty(pn).objectReferenceValue;
                        var pt = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
                        if (pt != null) tf.Append($" | {pn} {pt.name} pos {V3(pt.position - ModelOffset)} up {V3(pt.up)} right {V3(pt.right)} parent {pt.parent?.name}");
                    }
                    var wh = so.FindProperty("wheels");
                    for (int i = 0; i < wh.arraySize; i++)
                        if (wh.GetArrayElementAtIndex(i).objectReferenceValue is Transform w)
                            tf.Append($" | wheel {w.name} pos {V3(w.position - ModelOffset)} right {V3(w.right)} parent {w.parent?.name}");
                    if (hinge != null)
                        tf.Append($" | hinge local euler {hinge.localEulerAngles:F2} | mount {hinge.parent?.name} pos {V3(hinge.parent.position - ModelOffset)} euler {hinge.parent.eulerAngles:F2} scale {hinge.parent.lossyScale:F2}");
                    Debug.Log(tf.ToString());
                }
                if (hinge != null)
                {
                    var basePos = hinge.localPosition;
                    hinge.localEulerAngles += new Vector3(fold * t, 0f, 0f);
                    hinge.localPosition = Vector3.Lerp(basePos, basePos + motion, t);
                    if (srt != null) srt.localEulerAngles = new Vector3(0f, Mathf.Abs(t) * strut, 0f);
                }
                var mps = so.FindProperty("movingParts");
                for (int i = 0; i < mps.arraySize; i++)
                {
                    var e = mps.GetArrayElementAtIndex(i);
                    var tr = e.FindPropertyRelative("transform").objectReferenceValue as Transform;
                    var tg = e.FindPropertyRelative("target").objectReferenceValue as Transform;
                    var fa = e.FindPropertyRelative("foldAngles").vector3Value;
                    if (tr == null) break;
                    if (tg != null) tr.LookAt(tg);
                    if (fa != Vector3.zero) tr.localEulerAngles = Vector3.Lerp(Vector3.zero, fa, t);
                }
                var doors = so.FindProperty("gearDoors");
                for (int i = 0; i < doors.arraySize; i++)
                {
                    var e = doors.GetArrayElementAtIndex(i);
                    var tr = e.FindPropertyRelative("transform").objectReferenceValue as Transform;
                    if (tr == null) continue;
                    tr.localEulerAngles = door < 0f ? Vector3.zero : Vector3.Lerp(e.FindPropertyRelative("closedAngle").vector3Value, e.FindPropertyRelative("openAngle").vector3Value, door);
                }
            }

            var js = new StringBuilder("{\"gears\":[" + gears + "],\"parts\":[");
            bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                var mesh = mf.sharedMesh;

                var m = mf.transform.localToWorldMatrix;
                var path = mf.name; for (var p = mf.transform.parent; p != null && p != go.transform; p = p.parent) path = p.name + "/" + path;
                js.Append(first ? "" : ",").Append("{\"name\":\"").Append(mf.name).Append("\",\"path\":\"").Append(path).Append("\",\"v\":[");
                first = false;
                var vs = mesh.vertices;
                for (int i = 0; i < vs.Length; i++)
                {
                    var w = m.MultiplyPoint3x4(vs[i]) - ModelOffset;
                    js.Append(i == 0 ? "" : ",").Append(w.x.ToString("F4", inv)).Append(',').Append(w.y.ToString("F4", inv)).Append(',').Append(w.z.ToString("F4", inv));
                }
                js.Append("],\"t\":[").Append(string.Join(",", mesh.triangles)).Append("]}");
            }
            js.Append("]}");
            File.WriteAllText($"MiG29Out/gear_{pose}.json", js.ToString());
            Debug.Log($"[MiG29] gear dump {pose} written");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
