#!/bin/bash
# Docker-based AppImage build script
# Builds the AppImage inside an Ubuntu 22.04 container for maximum compatibility

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Get real user ID even if running via sudo
if [ -n "$SUDO_UID" ]; then
    USER_ID=$SUDO_UID
    GROUP_ID=$SUDO_GID
else
    USER_ID=$(id -u)
    GROUP_ID=$(id -g)
fi

echo "=== Building AppImage in Docker (Ubuntu 22.04) ==="
echo "Project root: $PROJECT_ROOT"
echo "User ID: $USER_ID"

# Build Docker image (cached after first build)
echo -e "\n[1/3] Building Docker image..."
docker build \
    --build-arg USER_ID=$USER_ID \
    --build-arg GROUP_ID=$GROUP_ID \
    -t scumbag-appimage-builder \
    "$SCRIPT_DIR"

echo "✓ Docker image ready"

# Run build inside container
echo -e "\n[2/3] Running build inside Ubuntu 22.04 container..."
docker run --rm \
    -v "$PROJECT_ROOT:/build:z" \
    -w /build/AppImage \
    scumbag-appimage-builder \
    bash build-appimage.sh

echo "✓ Build completed in container"

# Verify output
echo -e "\n[3/3] Verifying AppImage..."
if [ -f "$SCRIPT_DIR/output/ScumBag-x86_64.AppImage" ]; then
    ls -lh "$SCRIPT_DIR/output/ScumBag-x86_64.AppImage"
    echo -e "\n=== Docker Build Complete ==="
    echo "AppImage built with Ubuntu 22.04 libraries"
    echo "Location: $SCRIPT_DIR/output/ScumBag-x86_64.AppImage"
    echo -e "\nTest on your Fedora system with:"
    echo "  $SCRIPT_DIR/output/ScumBag-x86_64.AppImage"
else
    echo "ERROR: AppImage not found!"
    exit 1
fi
