"""Back up a built .nobp: copy it into builds/ (local, ignored on main) and commit it to the `builds` branch on GitHub with a
regenerated download table (README.md: version, link, size, SHA-256).

python tools/backup_build.py "<path to MiG-29 Fulcrum_x.y.z.nobp>" [--note "text for this version"] [--no-push]

Works from a temporary git worktree, so the main checkout and its branch are never touched.
"""
import argparse, hashlib, json, os, re, shutil, subprocess, sys, tempfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
URL = "https://github.com/IornMan1213/MiG29-NuclearOption/raw/builds/"


def git(*args, cwd=REPO):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


def version(name):
    m = re.search(r"_(\d+\.\d+\.\d+)\.nobp$", name)
    if not m:
        sys.exit(f"can't read a version from {name!r} (expected 'MiG-29 Fulcrum_x.y.z.nobp')")
    return m.group(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("nobp")
    ap.add_argument("--note", default="")
    ap.add_argument("--no-push", action="store_true")
    a = ap.parse_args()
    src = os.path.abspath(a.nobp); name = os.path.basename(src); ver = version(name)

    local = os.path.join(REPO, "builds"); os.makedirs(local, exist_ok=True)
    if os.path.abspath(os.path.join(local, name)) != src:
        shutil.copy2(src, local)
    # the instruments plugin built next to the .nobp is kept as MiG29Instruments_<ver>.dll
    dll_name = f"MiG29Instruments_{ver}.dll"
    dll_src = os.path.join(os.path.dirname(src), "MiG29Instruments.dll")
    if os.path.exists(dll_src):
        shutil.copy2(dll_src, os.path.join(local, dll_name))
    dll_src = os.path.join(local, dll_name) if os.path.exists(os.path.join(local, dll_name)) else None
    # the all-in-one release zip is kept locally (not on the builds branch: it duplicates the .nobp)
    zip_src = os.path.join(os.path.dirname(src), f"MiG29-Fulcrum_{ver}.zip")
    if os.path.exists(zip_src) and os.path.abspath(zip_src) != os.path.abspath(os.path.join(local, os.path.basename(zip_src))):
        shutil.copy2(zip_src, local)

    wt = tempfile.mkdtemp(prefix="mig29_builds_")
    shutil.rmtree(wt)
    git("fetch", "-q", "origin", "builds")
    git("worktree", "add", "-q", wt, "builds")
    try:
        shutil.copy2(src, os.path.join(wt, name))
        if dll_src:
            shutil.copy2(dll_src, os.path.join(wt, dll_name))
        notes_path = os.path.join(wt, "notes.json")
        notes = json.load(open(notes_path)) if os.path.exists(notes_path) else {}
        if a.note:
            notes[ver] = a.note
        json.dump(notes, open(notes_path, "w"), indent=1, sort_keys=True)

        files = sorted((f for f in os.listdir(wt) if f.endswith(".nobp")), key=lambda f: [int(x) for x in version(f).split(".")], reverse=True)
        rows = []
        for f in files:
            data = open(os.path.join(wt, f), "rb").read()
            v = version(f)
            dll = f"MiG29Instruments_{v}.dll"
            extra = f" + [{dll}]({URL}{dll})" if os.path.exists(os.path.join(wt, dll)) else ""
            rows.append(f"| {v} | [{f}]({URL}{f.replace(' ', '%20')}){extra} | {len(data) / 1e6:.1f} MB | `{hashlib.sha256(data).hexdigest()}` | {notes.get(v, '')} |")
        open(os.path.join(wt, "README.md"), "w", encoding="utf-8").write(
            "# MiG-29 Fulcrum builds\n\n"
            "Every released build of the mod, kept here so nothing gets lost. Put **one** `.nobp` in "
            "`BepInEx/plugins/MiG-29_Fulcrum/` (delete older ones). Requires BepInEx 5 and Blueprinter 2.0.1+. From 0.7.0 also put "
            "`MiG29Instruments_<version>.dll` there, renamed to `MiG29Instruments.dll`: it makes the cockpit instruments work.\n\n"
            "| Version | File | Size | SHA-256 | Notes |\n|---|---|---|---|---|\n" + "\n".join(rows) + "\n\n"
            "Builds before 0.4.1 predate the repository and were not kept; 0.4.2 was not kept either.\n\n"
            "The mod contains a modified version of \"MiG-29 - Fighter Jet - Free\" by bohmerang (CC BY-NC-SA 4.0); the builds are "
            "distributed under the same licence: free, non-commercial, credit required.\n")
        git("add", "-A", cwd=wt)
        if git("status", "--porcelain", cwd=wt):
            git("commit", "-q", "-m", f"Build {ver}\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>", cwd=wt)
            if not a.no_push:
                git("push", "-q", "origin", "builds", cwd=wt)
            print(f"backed up {name} -> builds/ and the builds branch" + ("" if not a.no_push else " (not pushed)"))
        else:
            print(f"{name} was already backed up")
        print(f"download: {URL}{name.replace(' ', '%20')}")
    finally:
        git("worktree", "remove", "--force", wt)


if __name__ == "__main__":
    main()
