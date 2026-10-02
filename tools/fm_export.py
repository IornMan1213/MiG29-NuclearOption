"""fm_config.json (dict form, for the simulator) -> MiG29Source/fm_unity.json (array form, for JsonUtility in the builder)."""
import json, sys
c = json.load(open("tools/fm_config.json"))
e = c["engine"]
out = {
    "fuelKg": c["fuelKg"],
    "engine": {k: e[k] for k in ["dryN", "abN", "maxSpeed", "minDensity", "fuelConsumptionMin", "fuelConsumptionMax", "abFuelConsumption"]},
    "altitudeThrust": [{"t": t, "v": v} for t, v in e["altitudeThrust"]],
    "airfoils": [{"name": a["name"], "CL": [{"t": t, "v": v} for t, v in a["CL"]], "CD": [{"t": t, "v": v} for t, v in a["CD"]]} for a in c["airfoils"]],
    "parts": [],
}
for name, p in c["parts"].items():
    out["parts"].append({
        "name": name,
        "wingArea": p.get("wingArea", -1), "dragArea": p.get("dragArea", -1),
        "hasCom": "com" in p, "com": p.get("com", [0, 0, 0]),
        "hasCenterOfLift": "centerOfLift" in p, "centerOfLift": p.get("centerOfLift", [0, 0, 0]),
    })
import os
dst = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["BLUEPRINTER_PROJECT"], "MiG29Source", "fm_unity.json")
json.dump(out, open(dst, "w"), indent=1)
print("wrote", dst, len(out["parts"]), "parts")
