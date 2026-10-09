using System.Collections.Generic;
using MiG29Tools;
using UnityEngine;

namespace MiG29Instruments
{
    // Things the real MiG-29 does on the ground. Both run for every MiG (player or AI), from the aircraft's own state.
    //  - IntakeDriver: the intake FOD doors (MiG29_fod_<L|R>, built open by MiG29Builder.AddIntakeBlockers) close on the ground
    //    below 200 km/h with an engine running, when the cockpit's ПЗУ light is on, and open again on the take-off roll. Visual only.
    //  - ChuteDriver: the brake chute streams from its housing at the tip of the tail after touchdown and is dropped when the jet has
    //    slowed to a walk (or the pilot goes around). Its drag is applied where the aircraft is simulated (the owner).

    static class MiGParts
    {
        // aircraft parts are separate physics roots in flight: look through every part of this aircraft
        public static IEnumerable<Transform> Named(Aircraft ac, string prefix)
        {
            foreach (var part in Object.FindObjectsOfType<UnitPart>())
            {
                if (part.parentUnit != ac) continue;
                foreach (var t in part.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(prefix)) yield return t;
            }
        }

        public static float RpmPercent(float ratio) => ratio <= 0.33f ? ratio / 0.33f * 70f : 70f + (ratio - 0.33f) / 0.67f * 30f;

        public static bool OnGround(Aircraft ac) => ac.radarAlt < 1.0f;   // as the cockpit instruments judge it

        public static float IasKmh(Aircraft ac) => ac.speed * Mathf.Sqrt(Mathf.Max(ac.GetAirDensity(), 0f) / 1.225f) * 3.6f;
    }

    public class IntakeDriver : MonoBehaviour
    {
        Aircraft ac;
        Cockpit cockpit;
        readonly List<(Transform t, Quaternion open)> doors = new List<(Transform, Quaternion)>();
        readonly List<Renderer> doorRenderers = new List<Renderer>();
        float amount, nextFind;   // 0 open .. 1 closed
        int finds;

        void Start() => ac = GetComponent<Aircraft>();

        void LateUpdate()
        {
            if (ac == null) return;
            if (doors.Count == 0)
            {
                if (Time.time < nextFind || finds > 20) return;
                nextFind = Time.time + 1f; finds++;
                foreach (var t in MiGParts.Named(ac, "MiG29_fod_"))
                    if (t.name.Length == "MiG29_fod_L".Length) { doors.Add((t, t.localRotation)); doorRenderers.AddRange(t.GetComponentsInChildren<Renderer>(true)); }
                return;
            }
            if (cockpit == null && ac.cockpit != null) cockpit = ((Component)ac.cockpit).GetComponentInChildren<Cockpit>(true);
            var engines = cockpit != null ? cockpit.GetEngineStates() : null;
            float rl = engines != null && engines.Count > 0 ? MiGParts.RpmPercent(engines[0].GetRPMRatio()) : 0f;
            float rr = engines != null && engines.Count > 1 ? MiGParts.RpmPercent(engines[1].GetRPMRatio()) : rl;
            bool closed = MiG29InstrumentMath.FodClosed(MiGParts.OnGround(ac), MiGParts.IasKmh(ac), rl, rr);
            amount = Mathf.MoveTowards(amount, closed ? 1f : 0f, Time.deltaTime / 0.8f);   // hydraulic, under a second
            var turn = Quaternion.Euler(Mathf.SmoothStep(0f, 1f, amount) * MiG29InstrumentMath.FodClosedDeg, 0f, 0f);
            foreach (var (t, open) in doors) if (t != null) t.localRotation = open * turn;
            // folded away up into the duct roof: out of sight, as the real door becomes part of the roof
            bool show = amount > 0.02f;
            foreach (var r in doorRenderers) if (r != null && r.enabled != show) r.enabled = show;
        }
    }

    public class ChuteDriver : MonoBehaviour
    {
        // PT-29 type cruciform brake chute, ~17 m2: two crossed 1.9 x 5.2 m arms. Drag = 0.5 * rho * CdS * v^2.
        const float ArmWidth = 1.9f, ArmHalf = 2.6f, Billow = 2.3f;   // a deep dome: the arm tips curl toward the jet
        const float RiserLen = 3.0f, LineLen = 6.0f, CdS = 11f;
        const float DeploySpeed = 30f, ReleaseSpeed = 8f;   // m/s: streamed above ~110 km/h, dropped below ~30 km/h
        // housing at the tip of the tail between the engines (MiG frame (0, -0.04, -3.35), aircraft frame below)
        static readonly Vector3 HousingInAircraft = new Vector3(0f, -0.48f, -6.0f);

        static Mesh mesh;
        Aircraft ac;
        Transform attachPart;
        Vector3 attachLocal;
        GameObject chute;
        float deployT, onGroundT;
        bool armed, deployed;
        Vector3 releaseVel;
        float releaseT;

        void Start()
        {
            ac = GetComponent<Aircraft>();
            armed = !MiGParts.OnGround(ac);   // a jet spawned on the ground has not landed yet
        }

        void OnDestroy() { if (chute != null) Destroy(chute); }

        bool FindAttach()
        {
            // the part carrying the tail section: the airbrakes' holder lives on it (MiG29Airbrakes); else the aircraft itself
            foreach (var t in MiGParts.Named(ac, "MiG29_airbrakes")) { attachPart = t.parent; break; }
            if (attachPart == null) attachPart = ac.transform;
            attachLocal = attachPart.InverseTransformPoint(ac.transform.TransformPoint(HousingInAircraft));
            return true;
        }

        static Material ChuteMaterial(Aircraft ac)
        {
            foreach (var t in MiGParts.Named(ac, "MiG29_intake_blocker"))   // the flat-colour missile atlas
                if (t.TryGetComponent<Renderer>(out var r) && r.sharedMaterial != null) return r.sharedMaterial;
            return null;
        }

        void Update()
        {
            if (ac == null) return;
            bool ground = MiGParts.OnGround(ac);
            if (!ground && ac.radarAlt > 10f) armed = true;
            onGroundT = ground ? onGroundT + Time.deltaTime : 0f;
            var inputs = ac.GetInputs();
            float throttle = inputs != null ? inputs.throttle : 0f;

            if (!deployed && chute == null && armed && onGroundT > 0.6f && ac.gearState == LandingGear.GearState.LockedExtended
                && ac.speed > DeploySpeed && throttle < 0.25f)
                Deploy();
            else if (deployed && (ac.speed < ReleaseSpeed || throttle > 0.8f || ac.radarAlt > 5f))
                Release();

            if (chute == null) return;
            if (deployed) Pose();
            else Fall();
        }

        void Deploy()
        {
            if (attachPart == null && !FindAttach()) return;
            var mat = ChuteMaterial(ac);
            if (mat == null) return;
            if (mesh == null) mesh = BuildMesh();
            chute = new GameObject("MiG29_brake_chute");
            chute.AddComponent<MeshFilter>().sharedMesh = mesh;
            chute.AddComponent<MeshRenderer>().sharedMaterial = mat;
            deployed = true; armed = false; deployT = 0f;
            Pose();
        }

        void Release()
        {
            deployed = false; releaseT = 0f;
            var rb = attachPart != null ? attachPart.GetComponentInParent<Rigidbody>() : null;
            releaseVel = rb != null ? rb.velocity * 0.6f : Vector3.zero;
        }

        // trails behind the housing against the jet's motion, swaying a little, while it streams out and fills
        void Pose()
        {
            deployT += Time.deltaTime;
            if (attachPart == null) { Destroy(chute); deployed = false; return; }
            var a = attachPart.TransformPoint(attachLocal);
            var rb = attachPart.GetComponentInParent<Rigidbody>();
            var v = rb != null ? rb.velocity : ac.transform.forward * ac.speed;
            var back = v.sqrMagnitude > 4f ? -v.normalized : -ac.transform.forward;
            float sway = Mathf.Sin(Time.time * 7.5f + GetInstanceID());
            var dir = Quaternion.AngleAxis(2.5f * sway, Vector3.up) * Quaternion.AngleAxis(-2f + 1.5f * Mathf.Sin(Time.time * 5.3f), Vector3.Cross(back, Vector3.up)) * back;
            float stream = Mathf.SmoothStep(0.25f, 1f, deployT / 0.5f);
            float fill = Mathf.SmoothStep(0.12f, 1f, (deployT - 0.3f) / 0.9f) * (1f + 0.025f * sway);
            chute.transform.SetPositionAndRotation(a, Quaternion.LookRotation(dir, Vector3.up));
            chute.transform.localScale = new Vector3(fill, fill, stream);

            if (rb != null && !ac.remoteSim)
            {
                float q = 0.5f * ac.GetAirDensity() * v.sqrMagnitude;
                rb.AddForceAtPosition(-v.normalized * q * CdS * fill * fill, a);
            }
        }

        // dropped: it drifts on, collapses and settles, then goes
        void Fall()
        {
            releaseT += Time.deltaTime;
            releaseVel = Vector3.MoveTowards(releaseVel, Vector3.down * 1.5f, Time.deltaTime * 12f);
            var p = chute.transform.position + releaseVel * Time.deltaTime;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 4f, -1, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null)
                p.y = Mathf.Max(p.y, hit.point.y + 0.05f);
            chute.transform.position = p;
            float k = 1f - Mathf.Exp(-Time.deltaTime / 1.2f);   // collapses over a few seconds
            var s = chute.transform.localScale;
            chute.transform.localScale = Vector3.Lerp(s, new Vector3(0.35f, 0.08f, 0.6f), k);
            var flat = Vector3.ProjectOnPlane(chute.transform.forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f)
                chute.transform.rotation = Quaternion.Slerp(chute.transform.rotation, Quaternion.LookRotation(flat.normalized + Vector3.down * 0.15f, Vector3.up), k);
            if (releaseT > 12f) { Destroy(chute); chute = null; }
        }

        // Local frame: origin at the housing, +z down the riser to the canopy. Riser, rigging lines from the confluence to the canopy
        // edge, and the cross-shaped canopy billowing away from the jet, both faces drawn.
        static Mesh BuildMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            var white = new Vector2(0.125f, 0.875f); var line = new Vector2(0.375f, 0.875f);   // missile atlas cells (missile_gen.py)
            float zEdge = RiserLen + LineLen;
            float Dome(float x, float y)
            {
                float r = Mathf.Sqrt(x * x + y * y) / Mathf.Sqrt(ArmHalf * ArmHalf + ArmWidth * ArmWidth / 4f);
                return zEdge + Billow * (1f - r * r);
            }
            void Strip(Vector3 a, Vector3 b, float w)
            {
                var d = (b - a).normalized;
                foreach (var side in new[] { Vector3.Cross(d, Vector3.up).normalized, Vector3.Cross(d, Vector3.right).normalized })
                {
                    int i = v.Count;
                    v.Add(a - side * w); v.Add(a + side * w); v.Add(b + side * w); v.Add(b - side * w);
                    var nn = Vector3.Cross(side, d).normalized;
                    for (int k = 0; k < 4; k++) { n.Add(nn); uv.Add(line); }
                    tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
                }
            }
            var conf = new Vector3(0f, 0f, RiserLen);
            Strip(Vector3.zero, conf, 0.02f);
            float hw = ArmWidth / 2f;
            var edge = new List<Vector2>();
            foreach (var (sx, sy) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
            {
                var along = new Vector2(sx, sy); var across = new Vector2(-sy, sx);
                edge.Add(along * ArmHalf + across * hw); edge.Add(along * ArmHalf - across * hw);
                edge.Add(along * hw + across * hw);
            }
            foreach (var e in edge) Strip(conf, new Vector3(e.x, e.y, Dome(e.x, e.y)), 0.008f);

            // canopy: a grid over the cross, cells kept where either arm covers them
            int N = 22; float step = 2f * ArmHalf / N;
            var idx = new Dictionary<(int, int), int>();
            int V(int i, int j)
            {
                if (idx.TryGetValue((i, j), out var k)) return k;
                float x = -ArmHalf + i * step, y = -ArmHalf + j * step;
                k = v.Count; idx[(i, j)] = k;
                v.Add(new Vector3(x, y, Dome(x, y))); n.Add(new Vector3(-x * 0.3f, -y * 0.3f, -1f).normalized); uv.Add(white);
                return k;
            }
            var back = new List<int>();
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    float cx = -ArmHalf + (i + 0.5f) * step, cy = -ArmHalf + (j + 0.5f) * step;
                    if (Mathf.Abs(cx) > hw + 1e-3f && Mathf.Abs(cy) > hw + 1e-3f) continue;
                    int a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                    tris.AddRange(new[] { a, b, c, a, c, d });
                    back.AddRange(new[] { a, c, b, a, d, c });
                }
            // the far face: same vertices duplicated with flipped normals so both sides light
            var map = new Dictionary<int, int>();
            foreach (var k in back)
            {
                if (!map.TryGetValue(k, out var m2)) { m2 = v.Count; map[k] = m2; v.Add(v[k]); n.Add(-n[k]); uv.Add(white); }
                tris.Add(m2);
            }
            var mesh = new Mesh { name = "MiG29_brake_chute" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
