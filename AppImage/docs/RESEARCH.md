# AppImage Research: WebKit2GTK Segfault Analysis

**Date:** 2026-01-01
**Issue:** AppImage segfaults on Arch Linux/KDE systems
**Status:** Root cause identified with recommended solutions

---

## Executive Summary

The segfault on Arch Linux/KDE is caused by **library mixing conflicts**, not the WebKit path handling itself. The current implementation has an excellent solution for WebKit's hardcoded paths (binary patching + symlinks), but it bundles **too many system libraries** that conflict with Arch's host system.

---

## Root Cause Analysis

### Current Problem in build-appimage.sh:83-116

The `copy_deps()` function indiscriminately bundles ALL dependencies via `ldd`, including critical system libraries that should **never** be bundled:

```bash
copy_deps() {
    local binary=$1
    ldd "$binary" 2>/dev/null | grep "=> /" | awk '{print $3}' | while read lib; do
        if [ -f "$lib" ]; then
            cp -vn "$lib" . 2>/dev/null || true
        fi
    done
}
```

### Libraries Currently Being Bundled (Problematic)

From the dependency analysis of `libwebview.so`, the current approach bundles:

**Core System Libraries (NEVER bundle these):**
- `libstdc++.so.6`, `libgcc_s.so.1` - Causes GLIBCXX version conflicts
- `libc.so.6`, `libm.so.6`, `libpthread.so.0` - Core glibc components
- `libGL.so.1`, `libEGL.so.1`, `libGLX.so.0`, `libGLdispatch.so.0` - Must match host GPU drivers
- `libX11.so.6`, `libxcb.so.1`, `libwayland-client.so.0` - Display server ABI issues
- `libfontconfig.so.1`, `libfreetype.so.6`, `libharfbuzz.so.0` - Font rendering conflicts
- `libdrm.so.2`, `libgbm.so.1` - Direct rendering infrastructure
- `libexpat.so.1`, `libuuid.so.1`, `libz.so.1`, `libffi.so.8` - Base system libraries

**Audio Libraries (Host-dependent):**
- `libasound.so.2`, `libpulse*.so*` - Must match host audio system

**System Integration:**
- `libblkid.so.1`, `libmount.so.1`, `libselinux.so.1` - System integration
- `libsystemd.so.0`, `libdbus-1.so.3` - System services

### Why It Works on Fedora but Fails on Arch

| System | Behavior | Reason |
|--------|----------|--------|
| **Fedora** | ✅ Works | Has similar/compatible library versions as what's being bundled (likely built on Fedora) |
| **Arch Linux** | ❌ Segfaults | Rolling release with newer library versions that have ABI incompatibilities with bundled older versions |

**The Conflict Mechanism:**
1. AppImage bundles old versions of system libraries (built on Fedora)
2. Arch Linux has newer versions of the same libraries
3. WebKit spawns helper processes that load a **mix** of bundled and system libraries
4. Symbol conflicts and ABI mismatches cause segfaults

---

## Dependency Analysis

### Files Analyzed

```
/home/admin/Code/ScumBag/tmp/appimage-build/publish/
├── ScumBag (28MB) - Main .NET AOT binary
├── libwebview.so (352KB) - WebView wrapper
└── libnfd.so (22KB) - Native file dialog
```

### ScumBag Binary Dependencies
- `libm.so.6`, `libc.so.6` - Minimal, only core glibc (good!)
- Self-contained .NET AOT binary

### libwebview.so Dependencies (The Problem)
- **Direct:** `libwebkit2gtk-4.1.so.0`, `libgtk-3.so.0`, `libjavascriptcoregtk-4.1.so.0`
- **Transitive:** ~150+ system libraries via WebKit

### libnfd.so Dependencies
- **Direct:** `libgtk-3.so.0`, `libgobject-2.0.so.0`, `libglib-2.0.so.0`
- **Transitive:** GTK3 and dependencies

---

## What Tauri Does Differently

Research from Tauri's implementation (PR #2940, Issue #12463):

### 1. Uses linuxdeploy-plugin-gtk
- Automatically handles library exclusion
- Bundles only necessary GTK/WebKit dependencies
- Excludes system libraries per AppImage best practices

### 2. Binary Patches WebKit
Similar to our approach:
```python
# Replace hardcoded paths (must be same length!)
"/usr/libexec/webkit2gtk-4.1" → "/tmp/app-webkit/libexec"
```

### 3. Implements Proper Exclusion Lists
Uses the official AppImage excludelist to avoid bundling:
- Core system libraries (glibc, libstdc++)
- Graphics drivers (libGL, libEGL)
- Display server libraries (libX11, libwayland)

### 4. Runtime Library Checking
- Uses `linuxdeploy-plugin-checkrt` for version comparison
- Prefers system libraries when they're newer
- Falls back to bundled versions when needed

### Key Tauri Findings

**From PR #2940:**
> "We didn't include files that webkit needs at runtime... somewhat risky as we have to search for those files as each distro stores them somewhere else."

**From Issue #12463:**
> Ubuntu's `/lib` → `/usr/lib` symlink caused path mismatches. Solution: Correct the search path logic in the bundler.

---

## Official AppImage Excludelist

Based on the [official AppImage excludelist](https://raw.githubusercontent.com/AppImage/pkg2appimage/master/excludelist):

### Libraries to NEVER Bundle

#### GNU C Library (glibc)
```
ld-linux.so.2
ld-linux-x86-64.so.2
libanl.so.1
libBrokenLocale.so.1
libc.so.6
libcidn.so.1
libdl.so.2
libm.so.6
libmvec.so.1
libnss_*.so.2
libpthread.so.0
libresolv.so.2
librt.so.1
libthread_db.so.1
libutil.so.1
```

#### C++ and Compiler Support
```
libgcc_s.so.1
libstdc++.so.6
```
**Reason:** Bundling causes `GLIBCXX_3.4.X` version conflicts

#### Graphics and GPU Drivers
```
libdrm.so.2
libEGL.so.1
libgbm.so.1
libGL.so.1
libGLdispatch.so.0
libglapi.so.0
libGLX.so.0
libOpenGL.so.0
```
**Reason:** Must match host GPU drivers exactly

#### X11 and Display Server
```
libX11.so.6
libX11-xcb.so.1
libxcb.so.1
libxcb-dri2.so.0
libxcb-dri3.so.0
libwayland-client.so.0
```
**Reason:** Display server ABI must match host

#### Font and Text Rendering
```
libfontconfig.so.1
libfreetype.so.6
libfribidi.so.0
libharfbuzz.so.0
```
**Reason:** Font rendering conflicts

#### Audio Libraries
```
libasound.so.2
libjack.so.0
libpipewire-0.3.so.0
libpulse.so.0
libpulsecommon-*.so
```
**Reason:** Must match host audio system ABI

#### Other Base System Libraries
```
libblkid.so.1
libcom_err.so.2
libcrypto.so.3
libdbus-1.so.3
libexpat.so.1
libffi.so.8
libgmp.so.10
libgpg-error.so.0
libICE.so.6
libmount.so.1
libpcre2-8.so.0
libselinux.so.1
libSM.so.6
libsystemd.so.0
libusb-1.0.so.0
libuuid.so.1
libz.so.1
```

### What TO Bundle

**WebKit and JavaScript Core:**
- `libwebkit2gtk-4.1.so.0`
- `libjavascriptcoregtk-4.1.so.0`

**WebKit Helper Processes:**
- `WebKitNetworkProcess`
- `WebKitWebProcess`
- `WebKitGPUProcess`
- `libwebkit2gtkinjectedbundle.so`

**GStreamer (for media support):**
- `libgst*.so.0` (if not in excludelist)

**Application-Specific:**
- `libnfd.so`
- `libwebview.so`

**WebKit-Specific Dependencies (not in excludelist):**
- Libraries specifically required by WebKit that aren't base system libraries
- Check each dependency against the excludelist

---

## Recommended Solutions

### Solution 1: Implement Library Exclusion (Recommended)

Modify the `copy_deps()` function to exclude system libraries:

```bash
# Create excludelist
cat > excludelist.txt << 'EOF'
# glibc
ld-linux.so.2
ld-linux-x86-64.so.2
libc.so.6
libdl.so.2
libm.so.6
libmvec.so.1
libpthread.so.0
libresolv.so.2
librt.so.1
libutil.so.1
libanl.so.1
libBrokenLocale.so.1
libcidn.so.1
libnss_compat.so.2
libnss_dns.so.2
libnss_files.so.2
libnss_hesiod.so.2
libnss_nisplus.so.2
libnss_nis.so.2

# C++ runtime
libstdc++.so.6
libgcc_s.so.1

# Graphics
libGL.so.1
libEGL.so.1
libGLdispatch.so.0
libGLX.so.0
libOpenGL.so.0
libdrm.so.2
libglapi.so.0
libgbm.so.1

# X11 and Display
libX11.so.6
libX11-xcb.so.1
libxcb.so.1
libxcb-dri2.so.0
libxcb-dri3.so.0
libxcb-glx.so.0
libxcb-present.so.0
libxcb-randr.so.0
libxcb-render.so.0
libxcb-shape.so.0
libxcb-shm.so.0
libxcb-sync.so.1
libxcb-xfixes.so.0
libwayland-client.so.0
libwayland-cursor.so.0
libwayland-egl.so.1
libwayland-server.so.0

# Fonts
libfontconfig.so.1
libfreetype.so.6
libharfbuzz.so.0
libharfbuzz-icu.so.0
libfribidi.so.0

# Audio
libasound.so.2
libjack.so.0
libpipewire-0.3.so.0
libpulse.so.0
libpulse-simple.so.0

# Base system
libexpat.so.1
libffi.so.8
libgpg-error.so.0
libICE.so.6
libSM.so.6
libusb-1.0.so.0
libuuid.so.1
libz.so.1
libblkid.so.1
libmount.so.1
libselinux.so.1
libpcre2-8.so.0
libdbus-1.so.3
libsystemd.so.0
libcom_err.so.2
libgmp.so.10
libcrypto.so.3

# X11 extensions
libXau.so.6
libXcursor.so.1
libXdamage.so.1
libXdmcp.so.6
libXext.so.6
libXfixes.so.3
libXi.so.6
libXinerama.so.1
libXrandr.so.2
libXrender.so.1
libXxf86vm.so.1
libXcomposite.so.1

# Other
libkeyutils.so.1
libresolv.so.2
libxkbcommon.so.0
libpixman-1.so.0
libcap.so.2
EOF

# Updated copy_deps function
copy_deps() {
    local binary=$1
    echo "Copying dependencies for: $binary"

    ldd "$binary" 2>/dev/null | grep "=> /" | awk '{print $3}' | while read lib; do
        if [ -f "$lib" ]; then
            local libname=$(basename "$lib")

            # Check if library is in excludelist
            if grep -qx "$libname" "$BUILD_DIR/excludelist.txt"; then
                echo "  [SKIP] $libname (excluded)"
                continue
            fi

            # Skip if already copied
            if [ -f "$libname" ]; then
                continue
            fi

            echo "  [COPY] $libname"
            cp -n "$lib" . 2>/dev/null || true
        fi
    done
}
```

**Pros:**
- Fixes the library mixing issue
- Maintains your excellent binary patching approach
- Relatively simple to implement

**Cons:**
- Requires maintaining the excludelist
- May need adjustments for different distributions

### Solution 2: Use linuxdeploy with Proper Exclusion

Replace manual dependency copying with linuxdeploy:

```bash
# After creating AppDir structure and copying binaries
echo -e "\n[5/9] Using linuxdeploy to bundle dependencies..."
./linuxdeploy-x86_64.AppImage \
    --appdir AppDir \
    --executable AppDir/usr/bin/ScumBag \
    --library AppDir/usr/lib/libnfd.so \
    --library AppDir/usr/lib/libwebview.so \
    --library /usr/lib64/libwebkit2gtk-4.1.so.0 \
    --library /usr/lib64/libjavascriptcoregtk-4.1.so.0

# Then apply binary patching (step 6-7)
# Then create AppRun (step 8)
# Then package (step 9) but DON'T use linuxdeploy's output, use appimagetool
```

**Pros:**
- Automatic dependency resolution with proper exclusion
- Industry-standard tool
- Handles edge cases

**Cons:**
- Less control over what gets bundled
- Needs to integrate with your binary patching workflow
- May still need custom excludelist

### Solution 3: Add Runtime Library Checker (Advanced)

Download and integrate linuxdeploy-plugin-checkrt:

```bash
# Download plugin
wget https://github.com/Optiligence/linuxdeploy-plugin-checkrt/releases/download/continuous/linuxdeploy-plugin-checkrt-x86_64.sh
chmod +x linuxdeploy-plugin-checkrt-x86_64.sh

# Add to AppDir
./linuxdeploy-plugin-checkrt-x86_64.sh --appdir AppDir

# Modify AppRun to use checkrt
```

This compares library versions at runtime and uses system versions when they're newer.

**Pros:**
- Best compatibility across distributions
- Handles version mismatches gracefully
- Future-proof

**Cons:**
- More complex
- Adds runtime overhead
- Larger AppImage size

### Solution 4: Hybrid Approach (Best)

Combine solutions 1 and 3:

1. Implement proper excludelist (Solution 1)
2. Add runtime library checking (Solution 3)
3. Keep your binary patching approach

This gives maximum compatibility while maintaining control.

---

## Debugging the Current Issue

To verify library mixing is the problem:

### 1. Run with Library Debugging

```bash
LD_DEBUG=libs ./ScumBag-x86_64.AppImage 2>&1 | tee debug.log
grep -i "error\|conflict\|version" debug.log
```

### 2. Check for Missing Symbols

```bash
LD_DEBUG=symbols ./ScumBag-x86_64.AppImage 2>&1 | grep -i "undefined"
```

### 3. Compare Library Versions

```bash
# Check bundled webkit version
strings AppDir/usr/lib/libwebkit2gtk-4.1.so.0 | grep -i version

# Check system webkit version (on Arch)
strings /usr/lib64/libwebkit2gtk-4.1.so.0 | grep -i version
```

### 4. Test with Minimal Bundling

Temporarily modify build script to NOT bundle system libraries and run:

```bash
LD_LIBRARY_PATH=AppDir/usr/lib:$LD_LIBRARY_PATH AppDir/usr/bin/ScumBag
```

If this works, it confirms the bundled libraries are the problem.

---

## Additional Findings

### 1. Symlink Issue (README:37-40)

The README mentions needing a symlink:
```bash
sudo ln -s /usr/lib/x86_64-linux-gnu/libwebkit2gtk-4.1.so.0 \
            /usr/lib/x86_64-linux-gnu/libwebkit2gtk-4.1.so
```

**Status:** ✅ Already handled in build-appimage.sh:102
```bash
ln -sf libwebkit2gtk-4.1.so.0 libwebkit2gtk-4.1.so
```

### 2. Binary Patching Approach

**Status:** ✅ Excellent implementation

Your binary patching approach (build-appimage.sh:123-180) is solid and matches Tauri's solution:
- Binary-safe path replacement
- Same-length strings
- Absolute /tmp paths
- Runtime symlink creation
- Cleanup on exit

This is **not** the source of the segfault.

### 3. WebKit Helper Processes

**Status:** ✅ Correctly bundled

Lines 100-111 correctly bundle:
- `WebKitNetworkProcess`
- `WebKitWebProcess`
- `WebKitGPUProcess`
- `libwebkit2gtkinjectedbundle.so`

And create them in the right locations.

### 4. AppImage vs Flatpak

Your APPIMAGE-NOTES.md correctly identifies that **Flatpak is superior for WebKit apps**:

| Aspect | AppImage | Flatpak |
|--------|----------|---------|
| **Runtime Isolation** | ❌ Mixes host/bundled libs | ✅ Proper isolation |
| **Size** | ❌ ~120MB | ✅ ~50MB (shares runtimes) |
| **Compatibility** | ⚠️ Fragile on rolling distros | ✅ Consistent across distros |
| **WebKit Support** | ⚠️ Complex, requires hacks | ✅ Native support |
| **Maintenance** | ❌ High (library tracking) | ✅ Low (runtime updates) |

**Recommendation:** Continue supporting Flatpak as primary distribution method. AppImage is acceptable as secondary option with proper library exclusion.

---

## Implementation Priority

### High Priority (Fixes Arch segfault)
1. ✅ **Implement library excludelist** (Solution 1)
2. Test on Arch Linux/KDE
3. Document which libraries are bundled vs excluded

### Medium Priority (Improves compatibility)
1. Add runtime library checking (Solution 3)
2. Test on multiple distributions (Ubuntu, Debian, openSUSE)
3. Add version detection to build script

### Low Priority (Nice to have)
1. Switch to linuxdeploy (Solution 2)
2. Automate excludelist updates
3. Add CI/CD testing on multiple distros

---

## Testing Checklist

After implementing fixes:

- [ ] Test on Fedora (current working system)
- [ ] Test on Arch Linux / Manjaro (currently failing)
- [ ] Test on Ubuntu 22.04 / 24.04
- [ ] Test on Debian 12
- [ ] Test on openSUSE Tumbleweed (rolling release)
- [ ] Test on Steam Deck (Arch-based)
- [ ] Verify webkit helper processes launch correctly
- [ ] Check file size reduction (should be smaller with exclusions)
- [ ] Test all app features (save backup, restore, screenshots)

---

## References

### Tauri Implementation
- [Tauri PR #2940 - WebKit path patching](https://github.com/tauri-apps/tauri/pull/2940)
- [Tauri Issue #12463 - WebKit injected bundle](https://github.com/tauri-apps/tauri/issues/12463)

### AppImage Documentation
- [AppImage Best Practices](https://docs.appimage.org/reference/best-practices.html)
- [AppImage Official Excludelist](https://raw.githubusercontent.com/AppImage/pkg2appimage/master/excludelist)
- [AppImage Manual Packaging Guide](https://docs.appimage.org/packaging-guide/manual.html)

### Library Compatibility Issues
- [OrcaSlicer #3046 - Arch Linux webkit2gtk bundling](https://github.com/OrcaSlicer/OrcaSlicer/issues/3046)
- [OrcaSlicer #4616 - Appimage webkit2gtk error](https://github.com/SoftFever/OrcaSlicer/issues/4616)
- [Cemu #662 - AppImage on Steam Deck](https://github.com/cemu-project/Cemu/issues/662)
- [GitButler #4955 - Segfault on startup](https://github.com/gitbutlerapp/gitbutler/issues/4955)

### Tools
- [linuxdeploy-plugin-checkrt](https://github.com/Optiligence/linuxdeploy-plugin-checkrt)
- [pkg2appimage excludelist](https://github.com/AppImageCommunity/pkg2appimage/blob/master/excludelist)

### Additional Research
- [AppImageKit #1335 - glibc version mismatch](https://github.com/AppImage/AppImageKit/issues/1335)
- [appimage-builder FAQ](https://appimage-builder.readthedocs.io/en/latest/faq.html)
- [Briefcase AppImage reference](https://briefcase.readthedocs.io/en/stable/reference/platforms/linux/appimage.html)

---

## Conclusion

The segfault issue is **solvable** by implementing proper library exclusion. The core WebKit handling (binary patching + symlinks) is well-designed and not the source of the problem.

**Recommended Action:** Implement Solution 1 (library excludelist) as it provides the best balance of simplicity and effectiveness.

**Alternative:** Continue recommending Flatpak as the primary distribution method, as it sidesteps these issues entirely and is already working for this project.
