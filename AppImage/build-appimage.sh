#!/bin/bash
# Build script for creating Scum Bag AppImage with manual dependency bundling
# This script uses manual ldd-based dependency copying with system library exclusion
#
# Key improvements over old approach:
# - Excludes system libraries (glibc, libstdc++, graphics drivers, etc.) using excludelist
# - Fixes Arch Linux segfault caused by bundling system libraries
# - Copies libraries without modification (no patchelf/RUNPATH changes)
# - Should work on both Fedora (like old build) and Arch (via exclusion)
#
# Build process:
# 1. Build frontend assets (npm)
# 2. Publish .NET AOT binary
# 3. Download/cache appimagetool
# 4. Create AppDir structure and copy binaries
# 5. Manual ldd-based dependency bundling with system library exclusion
# 6. Binary patch WebKit library for path workaround
# 7. Create AppRun launcher with WebKit symlink magic
# 8. Package into AppImage
# 9. Cleanup temporary files

set -e

# ============================================================================
# Environment Setup
# ============================================================================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BUILD_DIR="$SCRIPT_DIR/tmp"
TOOLS_DIR="$BUILD_DIR/tools"
PUBLISH_DIR="$BUILD_DIR/publish"
APPDIR="$BUILD_DIR/AppDir"
OUTPUT_DIR="$SCRIPT_DIR/output"
CONFIG_DIR="$SCRIPT_DIR/config"
SCRIPTS_DIR="$SCRIPT_DIR/scripts"
ASSETS_DIR="$SCRIPT_DIR/assets"

# Detect platform and set library paths
# Ubuntu/Debian uses /usr/lib/x86_64-linux-gnu, Fedora/RHEL uses /usr/lib64
if [ -d "/usr/lib/x86_64-linux-gnu" ]; then
    # Ubuntu/Debian
    SYSTEM_LIB_DIR="/usr/lib/x86_64-linux-gnu"
    WEBKIT_LIB_DIR="$SYSTEM_LIB_DIR"
    WEBKIT_EXEC_DIR="/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1"
    WEBKIT_BUNDLE_DIR="$SYSTEM_LIB_DIR/webkit2gtk-4.1/injected-bundle"
    PLATFORM="Ubuntu/Debian"
else
    # Fedora/RHEL
    SYSTEM_LIB_DIR="/usr/lib64"
    WEBKIT_LIB_DIR="/usr/lib64"
    WEBKIT_EXEC_DIR="/usr/libexec/webkit2gtk-4.1"
    WEBKIT_BUNDLE_DIR="/usr/lib64/webkit2gtk-4.1/injected-bundle"
    PLATFORM="Fedora/RHEL"
fi

echo "=== Building Scum Bag AppImage ==="
echo "Platform: $PLATFORM"
echo "Project root: $PROJECT_ROOT"
echo "Build dir: $BUILD_DIR"
echo "Output dir: $OUTPUT_DIR"
echo "System lib dir: $SYSTEM_LIB_DIR"

# Create directories
mkdir -p "$TOOLS_DIR" "$PUBLISH_DIR" "$OUTPUT_DIR"
rm -rf "$APPDIR"

# ============================================================================
# Step 1: Build Frontend Assets
# ============================================================================
echo -e "\n[1/9] Building frontend assets..."
cd "$PROJECT_ROOT"
echo "Installing npm dependencies..."
npm install
echo "Building frontend..."
npm run build

# ============================================================================
# Step 2: Publish .NET AOT Binary
# ============================================================================
echo -e "\n[2/9] Publishing .NET AOT build..."
# Use exact command from GitHub workflow (.github/workflows/build-release.yml:52)
dotnet publish "$PROJECT_ROOT/Scum Bag.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishTrimmed=True \
    -p:TrimMode=Full \
    -p:PublishAot=true \
    -p:AssemblyName=ScumBag \
    -o "$PUBLISH_DIR"

# ============================================================================
# Step 3: Download AppImage Tools (cached)
# ============================================================================
echo -e "\n[3/9] Downloading/checking AppImage tools..."
cd "$TOOLS_DIR"

# Download appimagetool if not cached
if [ ! -f "appimagetool-x86_64.AppImage" ]; then
    echo "Downloading appimagetool..."
    wget -q --show-progress \
        https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x appimagetool-x86_64.AppImage
else
    echo "✓ Using cached appimagetool"
fi

# ============================================================================
# Step 4: Create AppDir Structure
# ============================================================================
echo -e "\n[4/9] Creating AppDir structure..."
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/lib"
mkdir -p "$APPDIR/usr/libexec/webkit2gtk-4.1"
mkdir -p "$APPDIR/usr/lib64/webkit2gtk-4.1/injected-bundle"

# Copy binaries and libraries
echo "Copying application binaries and libraries..."
cp "$PUBLISH_DIR/ScumBag" "$APPDIR/usr/bin/"
cp "$PUBLISH_DIR/libnfd.so" "$APPDIR/usr/lib/"
cp "$PUBLISH_DIR/libwebview.so" "$APPDIR/usr/lib/"

# ============================================================================
# Step 5: Bundle Dependencies (Manual ldd with Exclusion)
# ============================================================================
echo -e "\n[5/9] Bundling library dependencies (excluding system libraries)..."
cd "$APPDIR/usr/lib"

# Load excludelist into array
mapfile -t EXCLUDE_LIBS < "$CONFIG_DIR/excludelist.txt"

# Function to check if library should be excluded
should_exclude() {
    local libname=$(basename "$1")
    for excluded in "${EXCLUDE_LIBS[@]}"; do
        if [ "$libname" = "$excluded" ]; then
            return 0
        fi
    done
    return 1
}

# Function to copy library dependencies recursively
copy_deps() {
    local binary=$1
    local copied_something=0

    ldd "$binary" 2>/dev/null | grep "=> /" | awk '{print $3}' | while read lib; do
        if [ -f "$lib" ]; then
            local libname=$(basename "$lib")

            # Skip if already copied
            if [ -f "$libname" ]; then
                continue
            fi

            # Skip if in excludelist
            if should_exclude "$lib"; then
                echo "  Excluding: $libname (system library)"
                continue
            fi

            # Copy the library
            echo "  Bundling: $libname"
            cp "$lib" . 2>/dev/null || true
            copied_something=1
        fi
    done

    return $copied_something
}

# Copy dependencies for our binaries
echo "Copying dependencies for libnfd.so..."
copy_deps "libnfd.so"

echo "Copying dependencies for libwebview.so..."
copy_deps "libwebview.so"

# Copy WebKit library and helpers
echo "Copying WebKit2GTK library and helper processes..."
cp "$WEBKIT_LIB_DIR/libwebkit2gtk-4.1.so.0" .
ln -sf libwebkit2gtk-4.1.so.0 libwebkit2gtk-4.1.so

cp "$WEBKIT_EXEC_DIR"/{WebKitNetworkProcess,WebKitWebProcess,WebKitGPUProcess} \
    "$APPDIR/usr/libexec/webkit2gtk-4.1/"

cp "$WEBKIT_BUNDLE_DIR/libwebkit2gtkinjectedbundle.so" \
    "$APPDIR/usr/lib64/webkit2gtk-4.1/injected-bundle/"

# Copy WebKit dependencies
echo "Copying dependencies for WebKit..."
copy_deps "libwebkit2gtk-4.1.so.0"

# Recursively copy transitive dependencies until nothing new is found
echo "Resolving transitive dependencies..."
iteration=1
while true; do
    echo "  Iteration $iteration..."
    found_new=0

    for lib in *.so*; do
        if [ -f "$lib" ] && [ ! -L "$lib" ]; then
            if copy_deps "$lib"; then
                found_new=1
            fi
        fi
    done

    if [ $found_new -eq 0 ]; then
        echo "  ✓ All dependencies resolved"
        break
    fi

    iteration=$((iteration + 1))
    if [ $iteration -gt 10 ]; then
        echo "  ⚠ Warning: Stopped after 10 iterations"
        break
    fi
done

cd "$BUILD_DIR"
echo "✓ Dependency bundling completed"

# ============================================================================
# Step 6: Binary Patch WebKit
# ============================================================================
echo -e "\n[6/9] Binary patching WebKit library..."
# This patches hardcoded paths in WebKit:
#   /usr/libexec/webkit2gtk-4.1 → /tmp/scumbag-webkit/libexec
#   /usr/lib64/webkit2gtk-4.1/injected-bundle/ → /tmp/scumbag-webkit/lib64/injected-bundle/
python3 "$SCRIPTS_DIR/patch-webkit.py" "$APPDIR"

# Verify patches worked
echo "Verifying patches:"
strings "$APPDIR/usr/lib/libwebkit2gtk-4.1.so.0" | grep -E "(scumbag-webkit|/tmp)" | head -5 || echo "⚠ Warning: Could not verify patches"

# ============================================================================
# Step 7: Create AppRun Launcher
# ============================================================================
echo -e "\n[7/9] Creating AppRun launcher..."
# This creates the launcher script that:
# - Sets up LD_LIBRARY_PATH
# - Creates symlinks in /tmp/scumbag-webkit/ for WebKit helper processes
# - Executes the application
"$SCRIPTS_DIR/create-apprun.sh" "$APPDIR"

# Copy desktop file and icons
cp "$CONFIG_DIR/io.github.rthomasv3.ScumBag.desktop" "$APPDIR/scum-bag.desktop"
cp "$ASSETS_DIR/icons/io.github.rthomasv3.ScumBag.256.png" "$APPDIR/.DirIcon"
cp "$ASSETS_DIR/icons/io.github.rthomasv3.ScumBag.256.png" "$APPDIR/io.github.rthomasv3.ScumBag.png"

# ============================================================================
# Step 8: Package AppImage
# ============================================================================
echo -e "\n[8/9] Creating AppImage..."
rm -f "$OUTPUT_DIR/ScumBag-x86_64.AppImage"

# Use APPIMAGE_EXTRACT_AND_RUN to avoid needing FUSE (important for Docker)
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOLS_DIR/appimagetool-x86_64.AppImage" \
    --no-appstream \
    "$APPDIR" \
    "$OUTPUT_DIR/ScumBag-x86_64.AppImage"

# ============================================================================
# Step 9: Cleanup
# ============================================================================
echo -e "\n[9/9] Cleaning up temporary files..."
rm -rf "$APPDIR"
rm -rf "$PUBLISH_DIR"

# ============================================================================
# Done!
# ============================================================================
echo -e "\n=== Build Complete ==="
ls -lh "$OUTPUT_DIR/ScumBag-x86_64.AppImage"
echo -e "\nAppImage location: $OUTPUT_DIR/ScumBag-x86_64.AppImage"
echo -e "Size: $(du -h $OUTPUT_DIR/ScumBag-x86_64.AppImage | cut -f1)"
echo -e "\nHow it works:"
echo "  1. Manual ldd-based bundling with excludelist for system libraries"
echo "  2. WebKit library is binary-patched to look for helpers in /tmp/scumbag-webkit/"
echo "  3. AppRun creates symlinks at that location pointing to bundled files"
echo "  4. Libraries copied without modification (no patchelf/RUNPATH changes)"
echo -e "\nExpected Results:"
echo "  - Works on Fedora (like old build) AND Arch Linux (via exclusion)"
echo "  - System libraries (glibc, GL, X11, etc.) use host versions"
echo "  - All libraries unmodified from system originals"
echo -e "\nTest with: $OUTPUT_DIR/ScumBag-x86_64.AppImage"
