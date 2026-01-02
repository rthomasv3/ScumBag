# AppImage Build Notes

## Summary

This document explains the AppImage build process for Scum Bag, including the challenges with WebKit2GTK and the solutions implemented.

## The Problem

WebKit2GTK is notoriously difficult to package in AppImages because:

1. **Hardcoded Absolute Paths**: WebKit has helper processes (`WebKitNetworkProcess`, `WebKitWebProcess`, `WebKitGPUProcess`) with paths hardcoded in the binary:
   - `/usr/libexec/webkit2gtk-4.1/`
   - `/usr/lib64/webkit2gtk-4.1/injected-bundle/`

2. **Subprocess Spawning**: When WebKit spawns these helper processes, it uses the hardcoded paths, which don't exist in the AppImage mount point.

3. **WEBKIT_EXEC_PATH Limitation**: This environment variable could tell WebKit where to find helpers, but it only works in developer builds (compiled with `-DDEVELOPER_MODE=ON`), not in distribution packages.

4. **Library Dependencies**: WebKit pulls in ~100MB of dependencies (GTK3, GStreamer, etc.) that all need to be bundled.

## Our Solution

### 1. Binary Patching (Python Script)

We use a Python script to binary-patch the WebKit library, replacing hardcoded paths:

```python
# Replace paths (must be same length!)
"/usr/libexec/webkit2gtk-4.1" → "/tmp/scumbag-webkit/libexec"  # 27 chars
"/usr/lib64/webkit2gtk-4.1/injected-bundle/" → "/tmp/scumbag-webkit/lib64/injected-bundle/"  # 42 chars
```

**Why this works**:
- Replaces strings byte-for-byte without corrupting the binary
- Uses absolute paths so CWD doesn't matter
- Points to a predictable location we control

### 2. Runtime Symlinks (AppRun Script)

The AppRun launcher creates symlinks at the patched paths:

```bash
WEBKIT_TMP="/tmp/scumbag-webkit"
mkdir -p "${WEBKIT_TMP}/libexec"
mkdir -p "${WEBKIT_TMP}/lib64/injected-bundle"

ln -sf "${APPDIR}/usr/libexec/webkit2gtk-4.1/WebKitNetworkProcess" "${WEBKIT_TMP}/libexec/"
# ... more symlinks ...

trap "rm -rf '${WEBKIT_TMP}'" EXIT  # Cleanup
```

**Why this works**:
- WebKit looks for `/tmp/scumbag-webkit/libexec/WebKitNetworkProcess` (absolute path)
- Symlink points to bundled file in `$APPDIR/usr/libexec/webkit2gtk-4.1/WebKitNetworkProcess`
- Works regardless of CWD or where user runs AppImage from

### 3. Manual Dependency Bundling

We manually bundle all dependencies instead of using linuxdeploy-plugin-gtk because:
- We need to patch files AFTER bundling (linuxdeploy overwrites files)
- Gives us control over exactly what's included
- Uses `ldd` to find all transitive dependencies

## Build Process

1. **Build .NET AOT binary** - Self-contained native executable
2. **Create AppDir structure** - Standard AppImage layout
3. **Bundle dependencies** - Copy all `.so` files using `ldd`
4. **Binary patch WebKit** - Replace hardcoded paths
5. **Create AppRun** - Launcher that sets up symlinks
6. **Package AppImage** - Use `appimagetool`

## Why It (Mostly) Works

✅ **On Fedora/similar systems**: All dependencies are compatible
✅ **Solves hardcoded path issue**: Binary patching + symlinks work
✅ **Self-contained**: Bundles all webkit dependencies (~120MB total)

❌ **On Steam Deck**: Segfaults due to library incompatibilities
❌ **Size**: 120MB vs ~50MB flatpak (which shares runtimes)
❌ **Fragile**: WebKit version mismatches can cause issues

## Lessons Learned

1. **Relative paths don't work** for subprocess spawning because CWD is unpredictable
2. **`././` pattern** from AppImage docs doesn't work for spawned processes
3. **`sed` can corrupt binaries** - use Python's binary-safe `replace()` instead
4. **Path lengths must match exactly** when binary patching
5. **WebKit in AppImages is hard** - even Tauri/Briefcase recommend against it

## Alternative Approaches Tried

| Approach | Result | Why It Failed |
|----------|--------|---------------|
| Relative paths (`usr/libexec/...`) | ❌ | CWD is unpredictable |
| `././` pattern | ❌ | Doesn't resolve correctly for spawned processes |
| `./../` pattern | ❌ | Still relative to CWD, not binary location |
| `sed` binary patching | ❌ | Corrupted the library (segfault) |
| `WEBKIT_EXEC_PATH` env var | ❌ | Only works in developer builds |
| `$ORIGIN` RPATH | ❌ | Only for library loading, not spawning |

## Recommendation

**Use Flatpak instead** for distributing this app:
- ✅ Proper runtime isolation
- ✅ Smaller size (shares system runtimes)
- ✅ Better compatibility across systems
- ✅ Already working for this project

AppImage works on some systems but Flatpak is more reliable for WebKit-based apps.

## References

- [Tauri PR #2940 - WebKit path patching](https://github.com/tauri-apps/tauri/pull/2940)
- [AppImage Manual Packaging Docs](https://docs.appimage.org/packaging-guide/manual.html)
- [Tauri Issue #12463 - WebKit injected bundle](https://github.com/tauri-apps/tauri/issues/12463)
- [WebKit AppImage Compatibility Issues](https://github.com/beeware/briefcase/issues/1029)

## Usage

```bash
# Build the AppImage
./build-appimage.sh

# Test it
./tmp/appimage-build/ScumBag-x86_64.AppImage
```

The script is fully automated and reproducible. All dependencies are downloaded and bundled automatically.
