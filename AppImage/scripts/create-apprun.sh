#!/bin/bash
# Creates the AppRun launcher script for Scum Bag AppImage
# Usage: create-apprun.sh <AppDir>

set -e

APPDIR="${1:-AppDir}"

cat > "$APPDIR/AppRun" << 'EOF'
#!/bin/sh
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export APPDIR="${APPDIR:-${HERE}}"

# Default to dark mode (user can override with GTK_THEME environment variable)
# Example: GTK_THEME=Adwaita:light ./ScumBag.AppImage
export GTK_THEME="${GTK_THEME:-Adwaita:dark}"

# Set up library path
export LD_LIBRARY_PATH="${APPDIR}/usr/lib:${LD_LIBRARY_PATH}"

# Create symlinks in /tmp for webkit helper processes
# WebKit has hardcoded absolute paths that we binary-patched to point here
WEBKIT_TMP="/tmp/scumbag-webkit"
mkdir -p "${WEBKIT_TMP}/libexec"
mkdir -p "${WEBKIT_TMP}/lib64/injected-bundle"
mkdir -p "${WEBKIT_TMP}/lib/x86_64-linux-gnu/injected-bundle"

# Symlink helper processes to where webkit expects them (Fedora paths)
ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitNetworkProcess" "${WEBKIT_TMP}/libexec/" 2>/dev/null || true
ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitWebProcess" "${WEBKIT_TMP}/libexec/" 2>/dev/null || true
ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitGPUProcess" "${WEBKIT_TMP}/libexec/" 2>/dev/null || true
ln -sf "${APPDIR}/usr/lib64/webkit2gtk-4.1/injected-bundle/libwebkit2gtkinjectedbundle.so" "${WEBKIT_TMP}/lib64/injected-bundle/" 2>/dev/null || true

# Symlink helper processes to where webkit expects them (Ubuntu paths)
ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitNetworkProcess" "${WEBKIT_TMP}/lib/x86_64-linux-gnu/" 2>/dev/null || true
ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitWebProcess" "${WEBKIT_TMP}/lib/x86_64-linux-gnu/" 2>/dev/null || true
ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitGPUProcess" "${WEBKIT_TMP}/lib/x86_64-linux-gnu/" 2>/dev/null || true
ln -sf "${APPDIR}/usr/lib64/webkit2gtk-4.1/injected-bundle/libwebkit2gtkinjectedbundle.so" "${WEBKIT_TMP}/lib/x86_64-linux-gnu/injected-bundle/" 2>/dev/null || true

# Cleanup on exit
trap "rm -rf '${WEBKIT_TMP}'" EXIT

# Execute the application
exec "${APPDIR}/usr/bin/ScumBag" "$@"
EOF

chmod +x "$APPDIR/AppRun"
echo "✓ Created AppRun launcher at $APPDIR/AppRun"
