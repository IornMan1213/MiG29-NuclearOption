"""Offline flight-model check for the MiG-29 mod.

Re-implements Nuclear Option's aero force model (NuclearOption.Jobs.AeroJob_Math + Turbojet/JetNozzle thrust) and uses the
per-part data dumped from the built prefab (tools/aero_reference.json, a copy of MiG29Out/aero.json) plus the FM config below to compute:
  trim (AoA + stabilator for 1 g), static margin, max level speed vs altitude, sustained/instantaneous turn, climb, stall.

Usage: python tools/fm_sim.py [config.json]     (default: tools/fm_config.json)
       python tools/fm_sim.py --check            headline numbers against tools/fm_checks.json (exit 1 if out of range)
       python tools/fm_sim.py --selftest         the vectorised force model against the reference loop
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
cfg = json.load(open(ARGS[0] if len(ARGS) > 0 else HERE + "/fm_config.json"))
aero = json.load(open(ARGS[1] if len(ARGS) > 1 else HERE + "/aero_reference.json"))
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


def forces_loop(V, h, alpha, delta, beta=0.0):
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


# The same model with every part at once (NumPy arrays instead of a Python loop over ~40 parts): the report used to take ~3
# minutes, most of it in np.cross on single 3-vectors. forces_loop above is the reference; --selftest compares the two.
_R = np.array([p["right"] for p in parts]); _FW = np.array([p["fwd"] for p in parts])
_S = np.array([p["S"] for p in parts], float); _DA = np.array([p["dA"] for p in parts], float)
_AF = np.array([p["af"] for p in parts]); _COM = np.array([p["com"] for p in parts], float); _CLP = np.array([p["cL"] for p in parts])
_PITCH = np.array([p["ctrl"] == "pitch" for p in parts])
_AFS = sorted(set(_AF.tolist()))
_CURVES = {af: (np.array([k[0] for k in cfg["airfoils"][af]["CL"]]), np.array([k[1] for k in cfg["airfoils"][af]["CL"]]),
                np.array([k[0] for k in cfg["airfoils"][af]["CD"]]), np.array([k[1] for k in cfg["airfoils"][af]["CD"]])) for af in _AFS}


def forces(V, h, alpha, delta, beta=0.0):
    """As forces_loop, vectorised over the parts."""
    rho, a = isa(h)
    vdir = np.array([math.sin(beta), -math.sin(alpha), math.cos(alpha)])
    v = V * vdir
    q = 0.5 * rho * V * V
    right, fwd = _R, _FW.copy()
    if delta != 0.0 and _PITCH.any():   # stabilators: fwd turned about right (Rodrigues; right is a unit vector here)
        k = right[_PITCH] / np.linalg.norm(right[_PITCH], axis=1, keepdims=True); f0 = fwd[_PITCH]
        c, s_ = math.cos(delta), math.sin(delta)
        fwd[_PITCH] = f0 * c + np.cross(k, f0) * s_ + k * np.sum(k * f0, axis=1, keepdims=True) * (1 - c)
    up = np.cross(fwd, right)
    al = np.arctan2(up @ v, fwd @ v)
    n = np.cross(np.broadcast_to(v, right.shape), right)
    nn = np.linalg.norm(n, axis=1)
    cl = np.zeros(len(parts)); cd = np.zeros(len(parts))
    for af in _AFS:
        m = _AF == af; xs, ys, dxs, dys = _CURVES[af]
        cl[m] = np.copysign(np.interp(np.abs(al[m]), xs, ys), al[m])
        cd[m] = np.interp(np.abs(al[m]), dxs, dys)
    safe = np.where(nn < 1e-9, 1.0, nn)
    lift = np.where((nn < 1e-9)[:, None], 0.0, -(n / safe[:, None]) * (cl * q * _S)[:, None])
    drag_mag = cd * q * _S + 0.5 * q * _DA
    M_ = V / a
    bump = 1.0
    if 0.8 < M_ < 1.2:   # transonic bump (drag only)
        k_ = (0.2 - min(abs((a - V) / a), 0.2)) / 0.2
        bump = 1 + 0.15 * k_ ** 3
    f = lift - vdir[None, :] * (drag_mag * bump)[:, None]
    F = f.sum(axis=0)
    M = np.cross(_COM - cg, f).sum(axis=0)
    rl = _CLP[:, 0:1] * right + _CLP[:, 1:2] * up + _CLP[:, 2:3] * fwd
    M += np.cross(f, -rl).sum(axis=0)
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


def static_arm():
    """Moment arm of incremental lift at 200 m/s, 3 km: > 0 means the neutral point is behind the CG (stable)."""
    F1, M1 = forces(200, 3000, 0.05, 0); F2, M2 = forces(200, 3000, 0.07, 0)
    return (M2[0] - M1[0]) / (F2[1] - F1[1])   # Unity: +x torque = nose down


def max_speed(h):
    best = None
    for V in np.arange(80, 900, 5):
        t = trim(V, h)
        if t and thrust(V, h) >= t[2]:
            best = V
    return best


def stall():
    for V in np.arange(40, 120, 2):
        t = trim(V, 0)
        if t and math.degrees(t[0]) <= cfg["alphaLimitDeg"]:
            return V, math.degrees(t[0])
    return None, None


def turn(V, h=1000):
    """(instantaneous g, sustained g) at V: alpha limit and 9 g for the first, thrust = drag for the second."""
    n_inst = None; n_sus = None
    for n in np.arange(1, 9.05, 0.25):
        t = trim(V, h, n)
        if not t or math.degrees(t[0]) > cfg["alphaLimitDeg"]:
            break
        n_inst = n
        if thrust(V, h) >= t[2]:
            n_sus = n
    return n_inst, n_sus


def turn_rate(n, V):
    return math.degrees(G * math.sqrt(max(n * n - 1, 0)) / V) if n else 0


def climb(h):
    W = total_mass() * G
    return max(((thrust(V, h) - (trim(V, h) or (0, 0, 1e9))[2]) * V / W, V) for V in np.arange(150, 600, 10))


def report():
    print(f"dry mass {mass_dry:.0f} kg, fuel {fuel:.0f} kg, test mass (50% fuel) {total_mass():.0f} kg; dry CG {np.round(cg, 2)}")
    S_tot = sum(p['S'] for p in parts)
    print(f"total wing area {S_tot:.1f} m2;  parasite CdA0 = {sum(0.5 * p['dA'] + CD(p['af'], 0) * p['S'] for p in parts):.3f} m2")
    xnp = static_arm()
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
        best = max_speed(h)
        print(f"  {h:6d} m: {best} m/s" + (f"  (M{best / a:.2f}, {best * 3.6:.0f} km/h)" if best else ""))
    print("\nstall (1 g, alpha <= 28 deg):")
    V, al = stall()
    if V:
        print(f"  {V} m/s ({V * 3.6:.0f} km/h), alpha {al:.1f}")
    print("\nturn performance at 1000 m (50% fuel):")
    for V in [150, 180, 200, 230, 260, 300]:
        n_inst, n_sus = turn(V)
        print(f"  {V} m/s: instantaneous {n_inst} g ({turn_rate(n_inst, V):.1f} deg/s), sustained {n_sus} g ({turn_rate(n_sus, V):.1f} deg/s)")
    print("\nclimb (Ps = (T-D)V/W) at 50% fuel:")
    for h in [0, 5000, 10000]:
        best = climb(h)
        print(f"  {h:5d} m: {best[0]:.0f} m/s at {best[1]:.0f} m/s")


def measure():
    """The headline numbers --check compares against tools/fm_checks.json."""
    _, a13 = isa(13000)
    turns = [(V,) + turn(V) for V in [150, 180, 200, 230, 260, 300]]
    return {
        "static_arm_m": static_arm(),
        "top_speed_sl_kmh": (max_speed(0) or 0) * 3.6,
        "mach_13km": (max_speed(13000) or 0) / a13,
        "stall_kmh": (stall()[0] or 0) * 3.6,
        "instantaneous_turn_deg_s": max(turn_rate(ni, V) for V, ni, ns in turns),
        "sustained_turn_deg_s": max(turn_rate(ns, V) for V, ni, ns in turns),
        "climb_sl_m_s": climb(0)[0],
    }


def check(path):
    """Fail (exit 1) when a headline number leaves its range: run it after every flight-model change."""
    limits = json.load(open(path))["limits"]
    got = measure(); bad = 0
    for k, (lo, hi) in limits.items():
        ok = lo <= got[k] <= hi; bad += not ok
        print(f"  {'ok  ' if ok else 'FAIL'} {k:26s} {got[k]:9.2f}   ({lo} .. {hi})")
    print("flight model check:", "passed" if not bad else f"{bad} out of range")
    return bad == 0


def selftest():
    """The vectorised forces() against the reference loop over random states."""
    import random
    random.seed(1); worst = 0.0
    for _ in range(300):
        args = (random.uniform(40, 700), random.uniform(0, 16000), random.uniform(-0.6, 1.2), random.uniform(-0.6, 0.6), random.uniform(-0.1, 0.1))
        (F1, M1), (F2, M2) = forces_loop(*args), forces(*args)
        scale = max(np.abs(F1).max(), np.abs(M1).max(), 1.0)
        worst = max(worst, np.abs(F1 - F2).max() / scale, np.abs(M1 - M2).max() / scale)
    print(f"forces vs forces_loop: max relative difference {worst:.1e}")
    return worst < 1e-9


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(0 if selftest() else 1)
    if "--check" in sys.argv:
        sys.exit(0 if check(HERE + "/fm_checks.json") else 1)
    report()
