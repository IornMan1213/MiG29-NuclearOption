#!/usr/bin/env bash
# Sets up a Linux machine to build the mod: build tools, Python deps, fonts, .NET SDK, PowerShell, Blender, Unity Hub.
# Usage: tools/setup_linux.sh [<Blueprinter-Editor project folder>]
# Supports apt (Ubuntu/Debian/Mint/Pop), dnf (Fedora) and pacman (Arch/Manjaro/EndeavourOS). Safe to re-run.
set -euo pipefail

BLENDER_VERSION="${BLENDER_VERSION:-5.2.3}"
PWSH_VERSION="${PWSH_VERSION:-7.4.6}"
OPT="$HOME/.local/opt"
BIN="$HOME/.local/bin"
PROJECT="${1:-${BLUEPRINTER_PROJECT:-}}"
mkdir -p "$OPT" "$BIN"

say() { printf '\n\033[1;36m== %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m!! %s\033[0m\n' "$*"; }

say "System packages"
if command -v apt-get >/dev/null; then
    PM=apt
    sudo apt-get update
    sudo apt-get install -y git curl ca-certificates xz-utils python3 python3-numpy python3-pil \
        fonts-liberation fonts-dejavu libxi6 libxxf86vm1 libxfixes3 libxrender1 libgl1 libxkbcommon0 libsm6
    sudo apt-get install -y dotnet-sdk-8.0 || warn "dotnet-sdk-8.0 not in your apt sources; see https://learn.microsoft.com/dotnet/core/install/linux"
elif command -v dnf >/dev/null; then
    PM=dnf
    sudo dnf install -y git curl xz python3 python3-numpy python3-pillow liberation-sans-fonts dejavu-sans-fonts \
        dotnet-sdk-8.0 libXi libXxf86vm libXfixes libXrender mesa-libGL libxkbcommon libSM
elif command -v pacman >/dev/null; then
    PM=pacman
    sudo pacman -S --needed --noconfirm git curl xz python python-numpy python-pillow ttf-liberation ttf-dejavu \
        dotnet-sdk libxi libxxf86vm libxfixes libxrender libglvnd libxkbcommon libsm
else
    warn "Unknown package manager: install git, python3 + numpy + Pillow, Liberation and DejaVu fonts and the .NET 8 SDK yourself."
    PM=none
fi

say "PowerShell $PWSH_VERSION (runs tools/build_mig29.ps1)"
if [ ! -x "$OPT/powershell-$PWSH_VERSION/pwsh" ]; then
    mkdir -p "$OPT/powershell-$PWSH_VERSION"
    curl -fL "https://github.com/PowerShell/PowerShell/releases/download/v$PWSH_VERSION/powershell-$PWSH_VERSION-linux-x64.tar.gz" \
        | tar -xz -C "$OPT/powershell-$PWSH_VERSION"
    chmod +x "$OPT/powershell-$PWSH_VERSION/pwsh"
fi
ln -sf "$OPT/powershell-$PWSH_VERSION/pwsh" "$BIN/pwsh"

say "Blender $BLENDER_VERSION"
BDIR="$OPT/blender-$BLENDER_VERSION-linux-x64"
if [ ! -x "$BDIR/blender" ]; then
    curl -fL "https://download.blender.org/release/Blender${BLENDER_VERSION%.*}/blender-$BLENDER_VERSION-linux-x64.tar.xz" | tar -xJ -C "$OPT"
fi
ln -sf "$BDIR/blender" "$BIN/blender"

say "Unity Hub"
if command -v unityhub >/dev/null; then
    echo "already installed"
elif [ "$PM" = apt ]; then
    curl -fsSL https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor --yes -o /usr/share/keyrings/Unity_Technologies_ApS.gpg
    echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" \
        | sudo tee /etc/apt/sources.list.d/unityhub.list >/dev/null
    sudo apt-get update && sudo apt-get install -y unityhub
elif [ "$PM" = dnf ]; then
    sudo tee /etc/yum.repos.d/unityhub.repo >/dev/null <<'EOF'
[unityhub]
name=Unity Hub
baseurl=https://hub.unity3d.com/linux/repos/rpm/stable
enabled=1
gpgcheck=1
gpgkey=https://hub.unity3d.com/linux/repos/rpm/stable/repodata/repomd.xml.key
repo_gpgcheck=1
EOF
    sudo dnf install -y unityhub
else
    warn "Install Unity Hub yourself (Arch: the 'unityhub' AUR package)."
fi

say "Nuclear Option"
GAME=""
# ~/.steam/steam points at the Steam install in use (a leftover ~/.local/share/Steam can hold a stale copy)
for g in "$HOME/.steam/steam/steamapps/common/Nuclear Option" "$HOME/.local/share/Steam/steamapps/common/Nuclear Option" \
         "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Nuclear Option"; do
    [ -d "$g" ] && GAME="$g" && break
done
if [ -z "$GAME" ]; then
    warn "Game not found in the usual Steam folders. Install it, then set GameDir in ~/.profile to its folder."
else
    echo "found: $GAME"
    [ -f "$GAME/BepInEx/core/BepInEx.dll" ] || warn "BepInEx not installed yet: unzip the Windows x64 BepInEx 5 build into the game folder (the game runs through Proton)."
fi

say "Environment (~/.profile)"
MARK="# >>> MiG29 build >>>"
# the MiG-29SMT repo's setup writes the same settings under its own marker: either one counts
if ! grep -qF "$MARK" "$HOME/.profile" 2>/dev/null && ! grep -qF "# >>> MiG29SMT build >>>" "$HOME/.profile" 2>/dev/null; then
    {
        echo "$MARK"
        echo 'case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *) export PATH="$HOME/.local/bin:$PATH" ;; esac'
        echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
        [ -n "$GAME" ] && echo "export GameDir=\"$GAME\""
        [ -n "$PROJECT" ] && echo "export BLUEPRINTER_PROJECT=\"$PROJECT\""
        echo "# <<< MiG29 build <<<"
    } >> "$HOME/.profile"
    echo "added PATH$([ -n "$GAME" ] && echo ', GameDir')$([ -n "$PROJECT" ] && echo ', BLUEPRINTER_PROJECT') to ~/.profile (log out and in, or: source ~/.profile)"
else
    echo "already configured; edit the MiG29 (or MiG29SMT) block in ~/.profile to change it"
fi
export PATH="$BIN:$PATH"

say "Check"
for c in git python3 dotnet pwsh blender unityhub; do
    printf '  %-9s ' "$c"; command -v "$c" >/dev/null && echo ok || echo MISSING
done
python3 -c "import numpy, PIL" && echo "  numpy/Pillow ok" || warn "numpy/Pillow missing"
python3 -c "import sys; sys.path.insert(0, '$(dirname "$0")'); import fonts; print('  fonts:', fonts.find('arialbd.ttf', 'LiberationSans-Bold.ttf'))" \
    || warn "texture fonts missing"

cat <<EOF

Still to do by hand:
  1. Open Unity Hub, sign in, and install editor 2022.3.62f2 (Installs > Install Editor > Archive, or the unityhub:// link
     on https://unity.com/releases/editor/archive). The build expects it at ~/Unity/Hub/Editor/2022.3.62f2/Editor/Unity
     (set UNITY_EDITOR if it is elsewhere). Add the "Windows Build Support (Mono)" module too (Installs > 2022.3.62f2 > Add
     modules): the game is a Windows build, also under Proton, so the mod is packaged for Windows.
  2. Copy the Blueprinter Editor project over from Windows (leave out its Library folder; Unity rebuilds it), or set it up
     again per Blueprinter's instructions. Then run this script again with its path, or set BLUEPRINTER_PROJECT.
  3. In Steam, Nuclear Option > Properties > Launch Options:  WINEDLLOVERRIDES="winhttp=n,b" %command%
     (Proton otherwise ignores BepInEx's winhttp.dll and no plugins load.) Blueprinter goes in BepInEx/plugins as before.
  4. Build:  pwsh tools/build_mig29.ps1 -Project "\$BLUEPRINTER_PROJECT"
EOF
