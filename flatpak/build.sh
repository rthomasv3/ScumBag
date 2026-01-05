#!/bin/bash
set -e

echo "Building Vue.js frontend..."
npm install --legacy-peer-deps
npm run build

echo "Building .NET application..."
dotnet publish "Scum Bag.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -p:PublishTrimmed=True \
  -p:TrimMode=Full \
  -p:PublishAot=true \
  -p:AssemblyName=ScumBag \
  --source ./nuget-sources

echo "Installing application..."
mkdir -p /app/lib /app/bin

cp bin/Release/net10.0/linux-x64/publish/ScumBag /app/bin/ScumBag.real

cp bin/Release/net10.0/linux-x64/publish/libwebview.so /app/lib/ || \
cp bin/Release/net10.0/linux-x64/publish/webview.so /app/lib/ || true
cp bin/Release/net10.0/linux-x64/publish/libnfd.so /app/lib/ || \
cp bin/Release/net10.0/linux-x64/publish/nfd.so /app/lib/ || true

echo "Creating webkit2gtk symlink..."
for lib in /usr/lib*/libwebkit2gtk-4.1.so.0 /usr/lib/*/libwebkit2gtk-4.1.so.0; do
  if [ -f "$lib" ]; then
    ln -sf "$lib" /app/lib/libwebkit2gtk-4.1.so
    break
  fi
done

cat > /app/bin/scum-bag << 'EOF'
#!/bin/sh
export LD_LIBRARY_PATH=/app/lib:$LD_LIBRARY_PATH
exec /app/bin/ScumBag.real "$@"
EOF
chmod +x /app/bin/scum-bag

echo "Installing icons..."
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.64.png /app/share/icons/hicolor/64x64/apps/io.github.rthomasv3.ScumBag.png
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.128.png /app/share/icons/hicolor/128x128/apps/io.github.rthomasv3.ScumBag.png
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.256.png /app/share/icons/hicolor/256x256/apps/io.github.rthomasv3.ScumBag.png
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.512.png /app/share/icons/hicolor/512x512/apps/io.github.rthomasv3.ScumBag.png

echo "Installing desktop files..."
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.desktop /app/share/applications/io.github.rthomasv3.ScumBag.desktop
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.metainfo.xml /app/share/metainfo/io.github.rthomasv3.ScumBag.metainfo.xml

echo "Build complete!"
