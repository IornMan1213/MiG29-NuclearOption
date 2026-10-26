"""Finds the texture fonts on Windows or Linux. The Windows fonts win when present (copy them into ~/.local/share/fonts or
point MIG29_FONTS at them for byte-identical textures); otherwise metric-compatible fallbacks with Cyrillic are used."""
import os

_DIRS = [os.environ.get("MIG29_FONTS", ""), "C:/Windows/Fonts", os.path.expanduser("~/.local/share/fonts"),
         os.path.expanduser("~/.fonts"), "/usr/local/share/fonts", "/usr/share/fonts"]
_index = None


def _build_index():
    idx = {}
    for d in _DIRS:
        if not d or not os.path.isdir(d):
            continue
        for root, _, files in os.walk(d):
            for f in files:
                idx.setdefault(f.lower(), os.path.join(root, f))
    return idx


def find(*names):
    global _index
    if _index is None:
        _index = _build_index()
    for n in names:
        if n.lower() in _index:
            return _index[n.lower()]
    raise FileNotFoundError(f"none of these fonts found: {', '.join(names)} (install fonts-liberation / fonts-dejavu, "
                            "or set MIG29_FONTS to a folder with the Windows fonts)")
