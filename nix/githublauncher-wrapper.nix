{ lib
, stdenvNoCC
, makeWrapper

, bash
, coreutils
, curl
, jq
, unzip

, dotnet-runtime_9
, icu
, gtk3
, glib
, fontconfig
, freetype
, libGL
, libxkbcommon
, wayland
, openssl
, zlib
, SDL2
, xorg
}:

stdenvNoCC.mkDerivation {
  pname = "githublauncher";
  version = "self-updating-wrapper";

  dontUnpack = true;

  nativeBuildInputs = [
    makeWrapper
  ];

  installPhase = ''
    runHook preInstall

    mkdir -p $out/bin
    mkdir -p $out/share/applications

    cat > $out/bin/githublauncher <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

APPDIR="''${XDG_DATA_HOME:-$HOME/.local/share}/GithubLauncher/self"
API_URL="https://api.github.com/repos/SirDiabo/GithubLauncher/releases/latest"

mkdir -p "$APPDIR"

release_json="$(curl -fsSL "$API_URL")"

tag="$(printf '%s' "$release_json" | jq -r '.tag_name')"
asset_url="$(
  printf '%s' "$release_json" \
    | jq -r '.assets[] | select(.name | test("GitHubLauncher-v.*-Linux-X64\\.zip$")) | .browser_download_url' \
    | head -n 1
)"

if [ -z "$tag" ] || [ "$tag" = "null" ]; then
  echo "Could not find latest GithubLauncher release tag." >&2
  exit 1
fi

if [ -z "$asset_url" ] || [ "$asset_url" = "null" ]; then
  echo "Could not find Linux X64 release asset for GithubLauncher $tag." >&2
  exit 1
fi

current=""
if [ -f "$APPDIR/.version" ]; then
  current="$(cat "$APPDIR/.version")"
fi

if [ "$current" != "$tag" ] || [ ! -x "$APPDIR/GithubLauncher" ]; then
  echo "Installing/updating GithubLauncher to $tag..."

  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT

  curl -fL "$asset_url" -o "$tmp/githublauncher.zip"

  mkdir -p "$tmp/extract"
  unzip -q "$tmp/githublauncher.zip" -d "$tmp/extract"

  rm -rf "$APPDIR"
  mkdir -p "$APPDIR"

  cp -r "$tmp/extract"/. "$APPDIR"/

  chmod -R u+rwX "$APPDIR"
  chmod +x "$APPDIR/GithubLauncher" || true

  echo "$tag" > "$APPDIR/.version"
fi

cd "$APPDIR"
exec "$APPDIR/GithubLauncher" "$@"
EOF

    chmod +x $out/bin/githublauncher

    cat > $out/share/applications/githublauncher.desktop <<EOF
[Desktop Entry]
Type=Application
Name=GithubLauncher
Comment=Launcher for GitHub release apps and games
Exec=$out/bin/githublauncher
Icon=github
Terminal=false
Categories=Game;Utility;
StartupNotify=true
EOF

    wrapProgram $out/bin/githublauncher \
      --prefix PATH : ${lib.makeBinPath [
        bash
        coreutils
        curl
        jq
        unzip
      ]} \
      --set DOTNET_ROOT ${dotnet-runtime_9} \
      --prefix LD_LIBRARY_PATH : ${lib.makeLibraryPath [
        icu
        gtk3
        glib
        fontconfig
        freetype
        libGL
        libxkbcommon
        wayland
        openssl
        zlib

        xorg.libX11
        xorg.libICE
        xorg.libSM
        xorg.libXi
        xorg.libXcursor
        xorg.libXrandr
        xorg.libXrender
        xorg.libxcb
        xorg.libXext
        xorg.libXfixes
        xorg.libXdamage
        xorg.libXcomposite
      ]}

    runHook postInstall
  '';

  meta = {
    description = "Self-updating Nix wrapper for GithubLauncher";
    homepage = "https://github.com/SirDiabo/GithubLauncher";
    license = lib.licenses.mit;
    platforms = [ "x86_64-linux" ];
    mainProgram = "githublauncher";
  };
}
