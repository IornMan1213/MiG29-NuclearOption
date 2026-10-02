"""Offline flight-model check for the MiG-29 mod.

Re-implements Nuclear Option's aero force model (NuclearOption.Jobs.AeroJob_Math + Turbojet/JetNozzle thrust) and uses the
per-part data dumped from the built prefab (tools/aero_reference.json, a copy of MiG29Out/aero.json) plus the FM config below to compute:
  trim (AoA + stabilator for 1 g), static margin, max level speed vs altitude, sustained/instantaneous turn, climb, stall.

Usage: python tools/fm_sim.py [config.json]     (default: tools/fm_config.json)
"""
import json, math, sys
import numpy as np

HERE = __file__.rsplit("\\", 1)[0].rsplit("/", 1)[0]
cfg = json.load(open(sys.argv[1] if len(sys.argv) > 1 else HERE + "/fm_config.json"))
aero = json.load(open(sys.argv[2] if len(sys.argv) > 2 else HERE + "/aero_reference.json"))
G = 9.81


def isa(h):
    """ISA density (kg/m3) and speed of sound (m/s)."""
    if h < 11000:
        T = 288.15 - 0.0065 * h
        p = 101325 * (T / 288.15) ** 5.2559
    else:
        T = 216.65
        p = 22632 * math.exp(-G * (h - 11000) / (287.05 * T))
    return p / (287.05 * T), math.sqrt(1.4 * 287.05 * T)


def curve(points, x):
    xs, ys = zip(*points)
    return float(np.interp(x, xs, ys))


def sym(points):  # airfoil curves are given for alpha >= 0; CL odd, CD even
    return points


def CL(af, a):
    pts = cfg["airfoils"][af]["CL"]
    return math.copysign(curve(pts, abs(a)), a)


def CD(af, a):
    return curve(cfg["airfoils"][af]["CD"], abs(a))


# ---- parts: apply the FM config overrides on top of the dumped geometry
parts = []
for p in aero["parts"]:
    o = cfg["parts"].get(p["name"], {})
    q = dict(p)
    q["S"] = o.get("wingArea", p["wingArea"])
    q["dA"] = o.get("dragArea", p["dragArea"])
    q["af"] = o.get("airfoil", p["airfoil"])
    q["mass"] = o.get("mass", p["mass"])
    q["com"] = np.array(o.get("com", p["com"]))
    q["right"] = np.array(o.get("lnRight", p["lnRight"]), float)
    q["fwd"] = np.array(o.get("lnFwd", p["lnFwd"]), float)
    q["cL"] = np.array(o.get("centerOfLift", p["centerOfLift"]), float)
    q["ctrl"] = o.get("ctrl")  # "pitch" -> deflects with stabilator
    parts.append(q)

fuel = cfg["fuelKg"]
mass_dry = sum(p["mass"] for p in parts)
cg = sum(p["mass"] * p["com"] for p in parts) / mass_dry
fuel_pos = np.array(cfg.get("fuelPos", cg.tolist()))


def total_mass(fuel_frac=0.5, stores=0.0):
    return mass_dry + fuel * fuel_frac + stores


def rot(axis, ang, v):
    axis = axis / np.linalg.norm(axis)
    return v * math.cos(ang) + np.cross(axis, v) * math.sin(ang) + axis * np.dot(axis, v) * (1 - math.cos(ang))


def forces(V, h, alpha, delta, beta=0.0):
    """Body frame x right, y up, z forward. alpha: nose above velocity. delta: stabilator angle (rad, + = TE up).
    Returns total force (body) and moment about the dry CG."""
    rho, a = isa(h)
    vdir = np.array([math.sin(beta), -math.sin(alpha), math.cos(alpha)])  # airflow velocity of the aircraft in body axes
    v = V * vdir
    q = 0.5 * rho * V * V
    F = np.zeros(3); M = np.zeros(3)
    for p in parts:
        right, fwd = p["right"], p["fwd"]
        if p["ctrl"] == "pitch":
            fwd = rot(right, delta, fwd)  # Unity: +angle about local +X tilts forward down = trailing edge up
        up = np.cross(fwd, right)  # Unity left-handed: up = forward x right
        vl = np.array([np.dot(v, right), np.dot(v, up), np.dot(v, fwd)])
        al = math.atan2(vl[1], vl[2])
        n = np.cross(v, right)
        nn = np.linalg.norm(n)
        lift = np.zeros(3) if nn < 1e-9 else -(n / nn) * CL(p["af"], al) * q * p["S"]
        num8 = 0.0  # wingEffectiveness = 1
        drag_mag = CD(p["af"], al) * q * p["S"] + 0.5 * q * (p["dA"] + num8)
        f = lift - vdir * drag_mag
        # transonic bump (applies to drag only)
        M_ = V / a
        if 0.8 < M_ < 1.2:
            k = (0.2 - min(abs((a - V) / a), 0.2)) / 0.2
            f = lift - vdir * drag_mag * (1 + 0.15 * k ** 3)
        r = p["com"] - cg
        F += f
        M += np.cross(r, f)
        if np.any(p["cL"]):
            # torque = Cross(F, -(liftRot * centerOfLift)); liftRot maps local (x,y,z) -> right, up, fwd
            rl = p["cL"][0] * right + p["cL"][1] * up + p["cL"][2] * fwd
            M += np.cross(f, -rl)
    return F, M


def thrust(V, h, ab=True):
    rho, _ = isa(h)
    e = cfg["engine"]
    dry = e["dryN"] * curve(e["altitudeThrust"], h)
    wet = e["abN"] * min(max(rho, 0.4), 1.0) if ab else 0.0
    t = dry + wet
    if V > e["maxSpeed"]:
        t *= max(1 - 5 * (V - e["maxSpeed"]) / e["maxSpeed"], 0)
    if rho < e.get("minDensity", 0):
        t = 0
    return e["count"] * t


def trim(V, h, n=1.0, W=None):
    """Solve alpha, delta for lift = n*W (perp to velocity) and zero pitching moment. Returns (alpha, delta, drag)."""
    W = W or total_mass() * G
    a, d = 0.05, 0.0
    for _ in range(60):
        def res(a_, d_):
            F, M = forces(V, h, a_, d_)
            vdir = np.array([0, -math.sin(a_), math.cos(a_)])
            liftdir = np.array([0, math.cos(a_), math.sin(a_)])
            return np.array([np.dot(F, liftdir) - n * W, M[0]]), -np.dot(F, vdir)
        r, D = res(a, d)
        if abs(r[0]) < 1 and abs(r[1]) < 1:
            return a, d, D
        J = np.zeros((2, 2)); eps = 1e-4
        J[:, 0] = (res(a + eps, d)[0] - r) / eps
        J[:, 1] = (res(a, d + eps)[0] - r) / eps
        try:
            step = np.linalg.solve(J, -r)
        except np.linalg.LinAlgError:
            return None
        step = np.clip(step, -0.1, 0.1)
        a += step[0]; d += step[1]
        if abs(a) > 1.2 or abs(d) > 1.0:
            return None
    return None


def report():
    W = total_mass() * G
    print(f"dry mass {mass_dry:.0f} kg, fuel {fuel:.0f} kg, test mass (50% fuel) {total_mass():.0f} kg; dry CG {np.round(cg, 2)}")
    S_tot = sum(p['S'] for p in parts)
    print(f"total wing area {S_tot:.1f} m2;  parasite CdA0 = {sum(0.5 * p['dA'] + CD(p['af'], 0) * p['S'] for p in parts):.3f} m2")
    # static margin: dCm/dalpha at 200 m/s, 3 km
    F1, M1 = forces(200, 3000, 0.05, 0); F2, M2 = forces(200, 3000, 0.07, 0)
    dL = (F2[1] - F1[1]); dM = (M2[0] - M1[0])
    xnp = dM / dL  # moment arm of incremental lift: >0 nose-down? (Unity: +x torque = nose down)
    print(f"incremental-lift moment arm {xnp:+.2f} m ({'stable' if xnp > 0 else 'UNSTABLE'}; + = neutral point behind CG)")
    print("\n  alt    V(m/s)  Mach  alpha  stab   drag(kN) thrust(kN)")
    for h in [0, 3000, 6000, 11000, 15000]:
        rho, a = isa(h)
        for M_ in [0.3, 0.6, 0.9, 1.2, 1.6, 2.0, 2.3]:
            V = M_ * a
            t = trim(V, h)
            if not t:
                continue
            al, de, D = t
            T = thrust(V, h)
            print(f"{h:6d} {V:7.0f} {M_:5.2f} {math.degrees(al):6.1f} {math.degrees(de):6.1f} {D / 1000:9.1f} {T / 1000:9.1f}{'  <- excess' if T > D else ''}")
    print("\nmax level speed (AB):")
    for h in [0, 3000, 6000, 9000, 11000, 13000, 15000, 18000]:
        rho, a = isa(h)
        best = None
        for V in np.arange(80, 900, 5):
            t = trim(V, h)
            if t and thrust(V, h) >= t[2]:
                best = V
        print(f"  {h:6d} m: {best} m/s" + (f"  (M{best / a:.2f}, {best * 3.6:.0f} km/h)" if best else ""))
    print("\nstall (1 g, alpha <= 28 deg):")
    for V in np.arange(40, 120, 2):
        t = trim(V, 0)
        if t and math.degrees(t[0]) <= cfg["alphaLimitDeg"]:
            print(f"  {V} m/s ({V * 3.6:.0f} km/h), alpha {math.degrees(t[0]):.1f}"); break
    print("\nturn performance at 1000 m (50% fuel):")
    for V in [150, 180, 200, 230, 260, 300]:
        n_inst = None; n_sus = None
        for n in np.arange(1, 9.05, 0.25):
            t = trim(V, 1000, n)
            if not t or math.degrees(t[0]) > cfg["alphaLimitDeg"]:
                break
            n_inst = n
            if thrust(V, 1000) >= t[2]:
                n_sus = n
        rate = lambda n: math.degrees(G * math.sqrt(max(n * n - 1, 0)) / V) if n else 0
        print(f"  {V} m/s: instantaneous {n_inst} g ({rate(n_inst):.1f} deg/s), sustained {n_sus} g ({rate(n_sus):.1f} deg/s)")
    print("\nclimb (Ps = (T-D)V/W) at 50% fuel:")
    for h in [0, 5000, 10000]:
        best = max(((thrust(V, h) - (trim(V, h) or (0, 0, 1e9))[2]) * V / W, V) for V in np.arange(150, 600, 10))
        print(f"  {h:5d} m: {best[0]:.0f} m/s at {best[1]:.0f} m/s")


if __name__ == "__main__":
    report()
