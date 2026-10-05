"""Gear posing shared by the main-gear analysis scripts: exactly what LandingGear.MoveGear does to a leg (strut rotation about the
strut axis, fold about the hinge x axis, hinge translation), numerically as Unity's AngleAxis."""
import numpy as np


def rot(axis, deg):
    k = np.asarray(axis, float); k = k / np.linalg.norm(k); a = np.radians(deg)
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K


def pose_points(g, V, unsprung, fold, strut, T):
    if unsprung:
        sp = np.array(g["srt_pos"]); V = (V - sp) @ rot(g["srt_y"], strut).T + sp
    hp = np.array(g["hinge"])
    return (V - hp) @ rot(g["hinge_x"], fold).T + hp + T
