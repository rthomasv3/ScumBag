#!/usr/bin/env python3
"""
Binary patcher for WebKit2GTK helper process paths.
Replaces hardcoded /usr paths with absolute /tmp paths that AppRun will populate.
"""
import sys
import os

def patch_binary(filepath, old_path, new_path):
    """Binary-safe path replacement. Paths must be same length!"""
    old_bytes = old_path.encode('utf-8')
    new_bytes = new_path.encode('utf-8')

    if len(old_bytes) != len(new_bytes):
        raise ValueError(f"Paths must be same length! {len(old_bytes)} != {len(new_bytes)}")

    with open(filepath, 'rb') as f:
        content = f.read()

    count = content.count(old_bytes)
    print(f"  Found {count} occurrences of '{old_path}'")

    if count > 0:
        new_content = content.replace(old_bytes, new_bytes)
        with open(filepath, 'wb') as f:
            f.write(new_content)
        print(f"  ✓ Replaced {count} occurrences")
        return count
    return 0

def main():
    appdir = sys.argv[1] if len(sys.argv) > 1 else "AppDir"

    files_to_patch = [
        f"{appdir}/usr/lib/libwebkit2gtk-4.1.so.0",
        f"{appdir}/usr/lib/libjavascriptcoregtk-4.1.so.0",
    ]

    # Patch hardcoded paths to absolute /tmp paths
    # AppRun will create symlinks at these locations to the bundled files
    # Try both Fedora paths (/usr/libexec, /usr/lib64) and Ubuntu paths (/usr/lib/x86_64-linux-gnu)
    patches = [
        # Fedora/RHEL paths
        ("/usr/libexec/webkit2gtk-4.1", "/tmp/scumbag-webkit/libexec"),  # 27 chars each
        ("/usr/lib64/webkit2gtk-4.1/injected-bundle/", "/tmp/scumbag-webkit/lib64/injected-bundle/"),  # 42 chars
        # Ubuntu/Debian paths
        ("/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1", "/tmp/scumbag-webkit/lib/x86_64-linux-gnu"),  # 42 chars each
        ("/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1/injected-bundle/", "/tmp/scumbag-webkit/lib/x86_64-linux-gnu/injected-bundle/"),  # 59 chars
    ]

    for filepath in files_to_patch:
        if not os.path.exists(filepath):
            print(f"⚠ Skipping {filepath} (not found)")
            continue

        print(f"\nPatching: {filepath}")
        for old_path, new_path in patches:
            patch_binary(filepath, old_path, new_path)

if __name__ == "__main__":
    main()
