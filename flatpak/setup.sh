mkdir -p /app/lib /app/bin

# Install icons
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.64.png /app/share/icons/hicolor/64x64/apps/io.github.rthomasv3.ScumBag.png
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.128.png /app/share/icons/hicolor/128x128/apps/io.github.rthomasv3.ScumBag.png
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.256.png /app/share/icons/hicolor/256x256/apps/io.github.rthomasv3.ScumBag.png
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.512.png /app/share/icons/hicolor/512x512/apps/io.github.rthomasv3.ScumBag.png

# Install application
install -Dm755 ScumBag /app/bin/ScumBag.real
install -Dm644 libnfd.so /app/lib/libnfd.so
install -Dm644 libwebview.so /app/lib/libwebview.so

# Find and create webkit symlink
for lib in /usr/lib*/libwebkit2gtk-4.1.so.0 /usr/lib/*/libwebkit2gtk-4.1.so.0; do 
    if [ -f "$lib" ]; then 
        ln -sf "$lib" /app/lib/libwebkit2gtk-4.1.so
        break
    fi
done

# Create wrapper script
cat > /app/bin/scum-bag << 'EOF'
#!/bin/sh
export LD_LIBRARY_PATH=/app/lib:$LD_LIBRARY_PATH
exec /app/bin/ScumBag.real "$@"
EOF
chmod +x /app/bin/scum-bag

# Install desktop files
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.desktop /app/share/applications/io.github.rthomasv3.ScumBag.desktop
install -Dm644 flatpak/io.github.rthomasv3.ScumBag.metainfo.xml /app/share/metainfo/io.github.rthomasv3.ScumBag.metainfo.xml
