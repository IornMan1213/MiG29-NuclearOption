using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 main landing gear, built to move like the real one (user's War Thunder reference, v0.9.0): the leg swings forward and
    // up beside the intake trunk with the wheel edge-on, then the wheel turns flat and goes in through a door at the top of the
    // trunk side into the wing-root glove above the intake duct.
    //  - Leg: from scratch (tools/main_gear_gen.py), slim, wheel outboard on a stub axle; the KR-67 wheel (0.85 m) and every
    //    physics transform (bumpstop, castPoint, unsprung) are kept, so the ride and the suspension are unchanged.
    //  - Stow: the mount is yawed (StowYaw) about the leg axis so the game's own fold (about the hinge parent's x axis) is a
    //    single skewed-hinge arc to the stow (blender/analysis/main_gear_search2.py: every leg vertex >= 5 cm inside the skin,
    //    unsprung at rest and fully extended). The hinge itself keeps an identity local rotation: LandingGear.MoveGear starts
    //    from the angle between the hinge's and its parent's forward vectors.
    //  - Path: the MiG29Instruments plugin (GearPath.cs) steers the legs through the bay openings (main_gear_path3.py); without
    //    the plugin the game's straight arc still ends fully hidden.
    //  - Doors: the model has no door for that last step, so tools/main_bay_door.py cuts one out of the trunk skin.
    public static class MiG29MainGear
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string SourceDir = "MiG29Source";

        // left leg; the right leg mirrors it. Keep in step with MiG29Instruments/GearPath.cs.
        public const float StowYaw = 25f, StowFold = -76f, StowStrut = 135f;

        [Serializable] class Piece { public string name; public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class LegDump { public Piece[] parts; }
        [Serializable] class DoorSet { public Piece L, R; }
        [Serializable] class DoorDump { public int[] remove; public Piece skin; public DoorSet doors; public float[] hinge_L; public float open_deg; }

        static DoorDump doorDump;
        static DoorDump Doors => doorDump ?? (doorDump = JsonUtility.FromJson<DoorDump>(File.ReadAllText(Path.Combine(SourceDir, "main_bay_door.json"))));

        // Cuts the forward bay doors and the airbrake panels (MiG29Airbrakes) out of the body skin, both indexed in the original body
        // triangle order: the cut triangles go, the pieces of them outside the outlines come back as new skin triangles.
        public static void CutBodySkin(ref float[] vertices, ref float[] normals, ref float[] uvs, ref int[] triangles)
        {
            var d = Doors;
            var gone = new HashSet<int>(d.remove);
            int doors = gone.Count;
            gone.UnionWith(MiG29Airbrakes.RemovedTriangles);
            var tris = new List<int>(triangles.Length);
            for (int i = 0; i < triangles.Length / 3; i++)
                if (!gone.Contains(i)) tris.AddRange(new[] { triangles[i * 3], triangles[i * 3 + 1], triangles[i * 3 + 2] });
            var ab = MiG29Airbrakes.SkinPieces;
            foreach (var (pv, pn, pu, pt) in new[] { (d.skin.vertices, d.skin.normals, d.skin.uvs, d.skin.triangles), ab })
            {
                int baseV = vertices.Length / 3;
                tris.AddRange(pt.Select(t => t + baseV));
                vertices = vertices.Concat(pv).ToArray();
                normals = normals.Concat(pn).ToArray();
                uvs = uvs.Concat(pu).ToArray();
            }
            triangles = tris.ToArray();
            Debug.Log($"[MiG29] skin cuts: forward main-bay doors {doors} triangles out, {d.skin.triangles.Length / 3} pieces back; " +
                      $"airbrakes {gone.Count - doors} out, {ab.t.Length / 3} pieces back");
        }

        // The forward doors' meshes (MiG frame), as MiG29Polish.SetupGearDoors takes them.
        public static (float[] v, float[] n, float[] uv, int[] t) DoorMeshes()
        {
            var d = Doors;
            int nl = d.doors.L.vertices.Length / 3;
            return (d.doors.L.vertices.Concat(d.doors.R.vertices).ToArray(), d.doors.L.normals.Concat(d.doors.R.normals).ToArray(),
                    d.doors.L.uvs.Concat(d.doors.R.uvs).ToArray(), d.doors.L.triangles.Concat(d.doors.R.triangles.Select(t => t + nl)).ToArray());
        }

        public static Vector3 DoorHingeL => new Vector3(Doors.hinge_L[0], Doors.hinge_L[1], Doors.hinge_L[2]);
        public static float DoorOpenDeg => Doors.open_deg;

        public static void Setup(Transform root, Vector3 modelOffset, Material skinTemplate, Type landingGear, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            var mat = (Material)save(GearMaterial(skinTemplate), $"{ModDir}/materials/MiG29_gear.mat");
            var leg = JsonUtility.FromJson<LegDump>(File.ReadAllText(Path.Combine(SourceDir, "main_gear.json")));
            foreach (var side in new[] { "L", "R" })
            {
                var wheel = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "wheel_" + side);
                var lg = root.GetComponentsInChildren(landingGear, true).First(c =>
                {
                    var h = new SerializedObject(c).FindProperty("gearHinge").objectReferenceValue as Transform;
                    return h != null && wheel.IsChildOf(h);
                });
                var so = new SerializedObject(lg);
                var hinge = (Transform)so.FindProperty("gearHinge").objectReferenceValue;
                var mount = hinge.parent;
                var sprung = root.GetComponentsInChildren<Transform>(true).First(t => t.name == $"gear_{side}_sprung");
                var unsprung = root.GetComponentsInChildren<Transform>(true).First(t => t.name == $"gear_{side}_unsprung");
                float sx = side == "L" ? 1f : -1f;

                // new leg meshes (MiG frame, left, rest pose) into the sprung / unsprung local frames; the right leg is the mirror
                foreach (var (tr, pname) in new[] { (sprung, "sprung"), (unsprung, "unsprung") })
                {
                    var p = leg.parts.First(q => q.name == pname);
                    int n = p.vertices.Length / 3;
                    var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
                    for (int i = 0; i < n; i++)
                    {
                        var w = new Vector3(sx * p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]) + modelOffset;
                        var wn = new Vector3(sx * p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2]);
                        v[i] = tr.InverseTransformPoint(w);
                        nr[i] = tr.InverseTransformVector(wn).normalized;     // scale-aware: the right leg's chain is mirrored
                        uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
                    }
                    var t = (int[])p.triangles.Clone();
                    // a mirror in the vertex data (x for the right leg) or in the transform chain (gearMount_R is scaled -1 in x)
                    // flips the winding; flip it back when exactly one of them applies
                    bool flip = (sx < 0) != (tr.lossyScale.x * tr.lossyScale.y * tr.lossyScale.z < 0);
                    if (flip) for (int i = 0; i < t.Length; i += 3) { var k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                    var m = new Mesh { name = $"MiG29_gear_{side}_{pname}", vertices = v, normals = nr, uv = uv, triangles = t };
                    m.RecalculateBounds(); m.RecalculateTangents();
                    tr.GetComponent<MeshFilter>().sharedMesh = (Mesh)save(m, $"{ModDir}/meshes/{m.name}.asset");
                    tr.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
                }

                // skewed hinge: yaw the mount about its own y (leg) axis, keep the hinge at identity and every hinge child where it was
                // (world = mount R * yaw * mount scale * child, so the child takes scale^-1 * yaw^-1 * scale: the yaw reversed, and
                // reversed again under gearMount_R's -1 x scale; setting world rotations back would get the mirrored side wrong)
                if (Quaternion.Angle(hinge.localRotation, Quaternion.identity) > 0.01f) throw new Exception($"[MiG29] {hinge.name} is not at identity");
                // under gearMount_R's mirror the same local yaw would turn the axis the same way in the world: reverse it there
                float det = Mathf.Sign(mount.localScale.x * mount.localScale.y * mount.localScale.z);
                var undo = Quaternion.AngleAxis(-StowYaw, Vector3.up);
                mount.localRotation = mount.localRotation * Quaternion.AngleAxis(StowYaw * det, Vector3.up);
                foreach (var c in hinge.Cast<Transform>().ToList())
                {
                    var before = c.position;
                    c.localPosition = undo * c.localPosition;
                    c.localRotation = undo * c.localRotation;
                    if ((c.position - before).magnitude > 0.001f) Debug.LogWarning($"[MiG29] {c.name} moved {(c.position - before).magnitude:F3} m by the mount yaw");
                }

                so.FindProperty("foldDegrees").floatValue = StowFold;
                so.FindProperty("strutRotation").floatValue = StowStrut;
                so.FindProperty("hingeFoldMotion").vector3Value = Vector3.zero;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[MiG29] main leg {side}: mount yawed {StowYaw} deg, fold axis {hinge.right:F3}, fold {StowFold}, strut {StowStrut}");
            }
        }

        static Material GearMaterial(Material skinTemplate)
        {
            var dir = $"{ModDir}/textures";
            Texture2D Import(string file, bool srgb, bool normal)
            {
                var dst = $"{dir}/{file}";
                File.Copy(Path.Combine(SourceDir, file), dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = srgb;
                imp.mipmapEnabled = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            }
            var atlas = Import("gear_atlas.png", true, false);
            var m = new Material(skinTemplate) { name = "MiG29_gear" };
            m.SetTexture("_Basecolor", atlas); m.SetTexture("_Livery", atlas); m.SetTexture("_BasecolorDmg", atlas);
            var nrm = Import("flat_normal.png", false, true);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", Import("gear_metallic.png", false, false));
            m.SetTexture("_AO", null);
            return m;
        }
    }
}
