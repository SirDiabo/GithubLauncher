#!/usr/bin/env bash
#
# Build a native arm64 macOS .app bundle for Github Launcher.
# Self-contained: bundles the .NET runtime and the official SDL2 (arm64).
# No Homebrew or other external runtime dependencies.
#
# ppy.SDL2-CS ships no arm64-macOS native, so this script downloads the official
# SDL2 release, verifies its sha256, and thins it to arm64 before publishing.
#
set -euo pipefail

cd "$(dirname "$0")"

RID="osx-arm64"
CONFIG="Release"
TFM="net9.0"
APP_NAME="Github Launcher"
APP="dist/${APP_NAME}.app"
PUBLISH="bin/${CONFIG}/${TFM}/${RID}/publish"

# Official SDL2 release (universal, self-contained: links only system frameworks).
SDL2_VERSION="2.32.10"
SDL2_DMG_URL="https://github.com/libsdl-org/SDL/releases/download/release-${SDL2_VERSION}/SDL2-${SDL2_VERSION}.dmg"
SDL2_DMG_SHA256="4a7ac31640d70214e848f994be8a12849c0f97918a7e6c2e27a40036166d1a7f"
SDL2_NATIVE="native/osx-arm64/libSDL2.dylib"

echo ">> Ensuring submodule is present..."
git submodule update --init --recursive

if [ -f "$SDL2_NATIVE" ]; then
  echo ">> SDL2 native already present ($SDL2_NATIVE)"
else
  echo ">> Fetching official SDL2 ${SDL2_VERSION}..."
  TMP="$(mktemp -d)"
  curl -fsSL -o "$TMP/sdl2.dmg" "$SDL2_DMG_URL"
  echo "${SDL2_DMG_SHA256}  ${TMP}/sdl2.dmg" | shasum -a 256 -c -
  MNT="$(hdiutil attach "$TMP/sdl2.dmg" -nobrowse -readonly | grep -o '/Volumes/.*' | head -1)"
  mkdir -p "$(dirname "$SDL2_NATIVE")"
  # Thin the universal framework binary to arm64 for this RID.
  lipo "$MNT/SDL2.framework/Versions/A/SDL2" -thin arm64 -output "$SDL2_NATIVE"
  hdiutil detach "$MNT" >/dev/null
  rm -rf "$TMP"
  echo ">> SDL2 native ready: $SDL2_NATIVE ($(file -b "$SDL2_NATIVE"))"
fi

echo ">> Publishing ${RID} (self-contained)..."
rm -rf "bin/${CONFIG}/${TFM}/${RID}"
dotnet publish GithubLauncher.csproj -c "$CONFIG" -r "$RID" --self-contained true

echo ">> Assembling ${APP}..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

echo ">> Generating icon..."
ICONSET="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$ICONSET"
sips -s format png icon.ico --out "$ICONSET/base.png" >/dev/null
for sz in 16 32 128 256 512; do
  sips -z "$sz" "$sz" "$ICONSET/base.png" --out "$ICONSET/icon_${sz}x${sz}.png" >/dev/null
  dbl=$((sz * 2))
  sips -z "$dbl" "$dbl" "$ICONSET/base.png" --out "$ICONSET/icon_${sz}x${sz}@2x.png" >/dev/null
done
rm "$ICONSET/base.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

echo ">> Writing Info.plist..."
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>Github Launcher</string>
	<key>CFBundleDisplayName</key>
	<string>Github Launcher</string>
	<key>CFBundleIdentifier</key>
	<string>com.sirdiabo.githublauncher</string>
	<key>CFBundleVersion</key>
	<string>1.0.0.0</string>
	<key>CFBundleShortVersionString</key>
	<string>1.0.0</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleExecutable</key>
	<string>GithubLauncher</string>
	<key>CFBundleIconFile</key>
	<string>AppIcon</string>
	<key>LSMinimumSystemVersion</key>
	<string>11.0</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>LSApplicationCategoryType</key>
	<string>public.app-category.games</string>
	<key>NSPrincipalClass</key>
	<string>NSApplication</string>
</dict>
</plist>
PLIST

echo ">> Code signing (ad-hoc)..."
chmod +x "$APP/Contents/MacOS/GithubLauncher"
codesign --force --deep --sign - "$APP"
codesign --verify --strict "$APP"

echo ">> Done: $APP"
