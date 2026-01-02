# Scum Bag AppImage Build System

This directory contains everything needed to build Scum Bag as an AppImage for Linux x86_64.

## Quick Start

```bash
cd AppImage
./build-appimage.sh
```

The built AppImage will be in `AppImage/output/ScumBag-x86_64.AppImage`.

## Requirements

### Build System
- **Linux x86_64** (Fedora, Ubuntu, Arch, etc.)
- **Node.js 20.x** - For frontend build
- **npm** - Node package manager
- **.NET 10 SDK** - For AOT compilation
- **webkit2gtk-4.1** - WebView runtime
  - Fedora: `dnf install webkit2gtk4.1-devel`
  - Ubuntu/Debian: `apt install libwebkit2gtk-4.1-dev`
  - Arch: `pacman -S webkit2gtk-4.1`

### Runtime (for testing)
- **Linux x86_64** with:
  - GTK3
  - WebKit2GTK 4.1
  - Standard system libraries (glibc, libstdc++, etc.)

## Build Process

The build script performs these steps:

1. **Build Frontend** - Compiles JavaScript/CSS with Vite
2. **Publish .NET AOT** - Creates self-contained native binary
3. **Download Tools** - Fetches linuxdeploy & appimagetool (cached after first run)
4. **Create AppDir** - Stages application files and WebKit components
5. **Bundle Dependencies** - Uses linuxdeploy with automatic library exclusion
6. **Patch WebKit** - Binary patches hardcoded paths for portability
7. **Create AppRun** - Generates launcher with WebKit symlink workarounds
8. **Package AppImage** - Creates final compressed AppImage
9. **Cleanup** - Removes temporary files

## Output

- **Location**: `AppImage/output/ScumBag-x86_64.AppImage`
- **Size**: ~50-60MB (significantly smaller than old ~120MB approach)
- **Format**: Type 2 AppImage (compressed SquashFS)

## Testing

```bash
# Run the AppImage
./AppImage/output/ScumBag-x86_64.AppImage

# Run with debug output
DEBUG=1 ./AppImage/output/ScumBag-x86_64.AppImage

# Extract and inspect contents
./AppImage/output/ScumBag-x86_64.AppImage --appimage-extract
ls squashfs-root/
```

## Directory Structure

```
AppImage/
├── build-appimage.sh          # Main build script
├── config/                    # Configuration files
│   ├── excludelist.txt        # Library exclusion reference (not used by default)
│   └── *.desktop              # Desktop entry file
├── scripts/                   # Helper scripts
│   ├── patch-webkit.py        # Binary patcher for WebKit
│   └── create-apprun.sh       # AppRun launcher generator
├── assets/                    # Icons and resources
│   └── icons/                 # Symlinks to flatpak icons
├── docs/                      # Documentation
│   ├── NOTES.md              # Technical notes on WebKit challenges
│   └── RESEARCH.md           # Research on AppImage compatibility
├── tmp/                       # Temporary build files (gitignored)
│   ├── tools/                # Cached: linuxdeploy, appimagetool
│   ├── publish/              # .NET build output
│   └── AppDir/               # Staging directory
├── output/                    # Final AppImage output (gitignored)
│   └── ScumBag-x86_64.AppImage
└── README.md                  # This file
```

## How It Works

### Library Bundling Strategy

Unlike the old approach which bundled ALL dependencies (causing segfaults on Arch Linux), this system uses **linuxdeploy** which:

- Automatically excludes system libraries (glibc, libstdc++, graphics drivers, X11, etc.)
- Bundles only application-specific libraries (WebKit, GTK, GLib, etc.)
- Uses the official AppImage excludelist as a baseline
- Results in smaller size and better cross-distribution compatibility

**What Gets Bundled:**
- ✅ libwebkit2gtk-4.1.so.0 (WebKit engine)
- ✅ libjavascriptcoregtk-4.1.so.0 (JavaScript engine)
- ✅ WebKit helper processes (NetworkProcess, WebProcess, GPUProcess)
- ✅ GTK3 and GLib libraries
- ✅ Application libraries (libnfd.so, libwebview.so)

**What Gets Excluded (uses host system):**
- ❌ glibc (libc, libm, libpthread, etc.)
- ❌ C++ runtime (libstdc++, libgcc_s)
- ❌ Graphics drivers (libGL, libEGL, libGLX, libdrm)
- ❌ Display server (libX11, libxcb, libwayland)
- ❌ Font rendering (libfontconfig, libfreetype)
- ❌ Audio (libasound, libpulse)

### WebKit Path Workaround

WebKit has helper processes with **hardcoded absolute paths**:
- `/usr/libexec/webkit2gtk-4.1/WebKitNetworkProcess`
- `/usr/lib64/webkit2gtk-4.1/injected-bundle/libwebkit2gtkinjectedbundle.so`

These paths don't exist in the AppImage mount point. Our solution:

1. **Binary Patch** the WebKit library to look for helpers at:
   - `/tmp/scumbag-webkit/libexec/`
   - `/tmp/scumbag-webkit/lib64/injected-bundle/`

2. **AppRun launcher** creates symlinks from `/tmp` to the bundled files in `$APPDIR`

3. **Cleanup** removes `/tmp/scumbag-webkit/` on exit

This is the same approach used by Tauri and other WebKit-based AppImages.

## Compatibility

### Tested Platforms
- ✅ **Fedora** - Works (build system)
- ⚠️ **Arch Linux** - Should work (segfault issue fixed with linuxdeploy)
- ⚠️ **Ubuntu 22.04/24.04** - Should work
- ⚠️ **Debian 12** - Should work
- ⚠️ **Steam Deck** - May still have issues (needs testing)

### Known Issues

1. **WebKit in AppImages is inherently fragile**
   - Different distributions have different WebKit versions
   - Binary patching is a workaround, not a perfect solution
   - Flatpak is the recommended distribution method

2. **Size vs Compatibility Trade-off**
   - Smaller AppImage = relies more on host system
   - If you encounter "library not found" errors, you may need to bundle more libs
   - Use `config/excludelist.txt` as reference for manual bundling

3. **Cache Directory**
   - Tools are cached in `tmp/tools/` (~20MB)
   - Delete this directory to force re-download of latest tools
   - Output directory `output/` can grow over time with old builds

## Troubleshooting

### Build Fails

**Problem**: `dotnet: command not found`
- **Solution**: Install .NET 10 SDK

**Problem**: `webkit2gtk-4.1 not found`
- **Solution**: Install webkit2gtk development package for your distro

**Problem**: `npm run build` fails
- **Solution**: Run `npm install` in project root first

### AppImage Fails to Run

**Problem**: Segfault on launch
- **Possible Causes**:
  - Library version mismatch (especially on older systems)
  - Missing system libraries
- **Debug**: Run with `DEBUG=1` or `LD_DEBUG=libs`
- **Solution**: May need to adjust excludelist to bundle more libraries

**Problem**: "libwebkit2gtk-4.1.so.0: cannot open shared object"
- **Solution**: Install webkit2gtk-4.1 on target system
- **Note**: This library IS bundled, so this error means something else is wrong

**Problem**: Blank window or crashes during rendering
- **Cause**: WebKit helper process issue
- **Debug**: Check if `/tmp/scumbag-webkit/` is created and has symlinks
- **Solution**: Run `patch-webkit.py` manually and verify patches

### Size Issues

**Problem**: AppImage is still ~120MB
- **Cause**: linuxdeploy may not have excluded system libraries
- **Debug**: Extract AppImage and check `squashfs-root/usr/lib/` for libstdc++, libGL, etc.
- **Solution**: Manually exclude libraries using `--exclude-library` flag in build script

**Problem**: AppImage is too small and missing dependencies
- **Cause**: Too many libraries excluded
- **Solution**: Reduce exclusions or bundle specific libraries manually

## Advanced Usage

### Manual Exclusion

If you need to manually exclude additional libraries:

1. Edit `build-appimage.sh` step 5
2. Add `--exclude-library "libname.so.X"` flags to linuxdeploy call
3. Or use `config/excludelist.txt` by passing: `--exclude-library "$(cat config/excludelist.txt | tr '\n' ';')"`

### Custom Icon/Desktop File

- Edit: `config/io.github.rthomasv3.ScumBag.desktop`
- Icons are symlinked from `../flatpak/*.png` (single source of truth)
- To use different icons, replace symlinks in `assets/icons/`

### Build on Different Distribution

The AppImage is most compatible when built on an **older base system** (older glibc):
- Best: CentOS 7, Ubuntu 20.04 (older glibc)
- Good: Ubuntu 22.04, Fedora 38
- Risky: Arch, Fedora latest (newest glibc - may not work on older systems)

Rule: **Build on the oldest system you want to support**

## Comparison to Old Approach

| Aspect | Old (manual ldd) | New (linuxdeploy) |
|--------|------------------|-------------------|
| **Size** | ~120MB | ~50-60MB |
| **Bundled Libraries** | ~150+ | ~30-40 |
| **Fedora** | Works | Works |
| **Arch Linux** | Segfaults | Should work |
| **Build Time** | ~2 min | ~2.5 min |
| **Maintenance** | Manual exclusion | Automatic |

## See Also

- **docs/NOTES.md** - Detailed technical notes on WebKit challenges
- **docs/RESEARCH.md** - Research on library compatibility and solutions
- **Main README** - Overall project documentation
- [AppImage Documentation](https://docs.appimage.org/)
- [linuxdeploy](https://github.com/linuxdeploy/linuxdeploy)

## Recommendation

**For most users**: Use the Flatpak version instead of AppImage
- Better compatibility across distributions
- Smaller size (shares system runtimes)
- Proper sandboxing and permissions
- No WebKit path hacks needed

**AppImage is provided as an alternative** for users who:
- Don't want to use Flatpak
- Need a single-file distribution
- Are willing to accept potential compatibility issues
