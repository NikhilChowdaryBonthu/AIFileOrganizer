#!/bin/zsh
set -euo pipefail

project_root="${0:A:h:h}"
output_root="${1:-$project_root/dist}"
publish_dir="$output_root/publish"
app_bundle="$output_root/AI File Organizer.app"

rm -rf "$publish_dir" "$app_bundle"

dotnet publish "$project_root/AIFileOrganizer.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  --property:PublishSingleFile=true \
  --property:IncludeNativeLibrariesForSelfExtract=true \
  --output "$publish_dir"

mkdir -p "$app_bundle/Contents/MacOS"
cp "$publish_dir/AIFileOrganizer" "$app_bundle/Contents/MacOS/AIFileOrganizer"
chmod +x "$app_bundle/Contents/MacOS/AIFileOrganizer"

cat > "$app_bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key>
  <string>AI File Organizer</string>
  <key>CFBundleExecutable</key>
  <string>AIFileOrganizer</string>
  <key>CFBundleIdentifier</key>
  <string>com.nikhilchowdary.aifileorganizer</string>
  <key>CFBundleName</key>
  <string>AI File Organizer</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>2.1.0</string>
  <key>CFBundleVersion</key>
  <string>3</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.0</string>
</dict>
</plist>
PLIST

codesign --force --sign - "$app_bundle"
codesign --verify --deep --strict "$app_bundle"

echo "Created: $app_bundle"
