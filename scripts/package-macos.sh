#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RUNTIME="${1:-osx-arm64}"
case "$RUNTIME" in
    osx-arm64) ARCH=arm64; LABEL=ARM64 ;;
    osx-x64) ARCH=x86_64; LABEL=x64 ;;
    *) printf 'Unsupported macOS runtime: %s\n' "$RUNTIME" >&2; exit 1 ;;
esac

OUTPUT="$ROOT/publish/$RUNTIME"
NATIVE="$ROOT/artifacts/sdl2-$RUNTIME"
APP="$OUTPUT/GithubLauncher.app"
mkdir -p "$NATIVE"

if [ ! -f "$NATIVE/install/lib/libSDL2-2.0.0.dylib" ]; then
    curl --fail --location --retry 3 https://github.com/libsdl-org/SDL/releases/download/release-2.32.10/SDL2-2.32.10.tar.gz -o "$NATIVE/SDL2.tar.gz"
    printf '5f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165  %s\n' "$NATIVE/SDL2.tar.gz" | shasum -a 256 -c -
    tar -xzf "$NATIVE/SDL2.tar.gz" -C "$NATIVE"
    cmake -S "$NATIVE/SDL2-2.32.10" -B "$NATIVE/build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES="$ARCH" -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0 -DCMAKE_INSTALL_PREFIX="$NATIVE/install" -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST=OFF
    cmake --build "$NATIVE/build" --config Release --parallel 3
    cmake --install "$NATIVE/build" --config Release
fi

if [ -d "$APP" ] || [ -d "$OUTPUT/files" ]; then
    printf 'Output already exists. Move publish/%s elsewhere before packaging again.\n' "$RUNTIME" >&2
    exit 1
fi

dotnet publish "$ROOT/GithubLauncher.csproj" -c Release -r "$RUNTIME" --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OUTPUT/files"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUTPUT/files/." "$APP/Contents/MacOS/"
cp "$NATIVE/install/lib/libSDL2-2.0.0.dylib" "$APP/Contents/MacOS/libSDL2.dylib"
cp "$NATIVE/SDL2-2.32.10/LICENSE.txt" "$APP/Contents/Resources/SDL2-LICENSE.txt"
chmod +x "$APP/Contents/MacOS/GithubLauncher"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>GithubLauncher</string>
    <key>CFBundleIdentifier</key><string>com.sirdiabo.githublauncher</string>
    <key>CFBundleName</key><string>Github Launcher</string>
    <key>CFBundleVersion</key><string>1.0.0</string>
    <key>CFBundleShortVersionString</key><string>1.0.0</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

plutil -lint "$APP/Contents/Info.plist"
lipo -verify_arch "$ARCH" "$APP/Contents/MacOS/GithubLauncher"
lipo -verify_arch "$ARCH" "$APP/Contents/MacOS/libSDL2.dylib"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ROOT/publish/GithubLauncher-macOS-$LABEL.zip"
printf 'App: %s\nArchive: %s\n' "$APP" "$ROOT/publish/GithubLauncher-macOS-$LABEL.zip"
