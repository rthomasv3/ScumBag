# AppImage build environment based on Ubuntu 22.04 (Jammy)
# Provides webkit2gtk-4.1 support with good cross-distro compatibility
FROM ubuntu:22.04

# Prevent interactive prompts during package installation
ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=UTC

# Install system dependencies
RUN apt-get update && apt-get install -y \
    wget \
    curl \
    git \
    file \
    build-essential \
    python3 \
    python3-pip \
    ca-certificates \
    # WebKit 4.1 and GTK dependencies
    libwebkit2gtk-4.1-dev \
    libgtk-3-dev \
    # Additional libraries for .NET and app dependencies
    libc6-dev \
    libglib2.0-dev \
    libsecret-1-dev \
    # Cleanup
    && rm -rf /var/lib/apt/lists/*

# Install .NET SDK 10.0 (supports AOT compilation)
RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# Install Node.js 20.x (LTS)
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Set working directory
WORKDIR /build

# Create non-root user for building (same UID as host user for file permissions)
ARG USER_ID=1000
ARG GROUP_ID=1000
RUN groupadd -g ${GROUP_ID} builder || true \
    && useradd -m -u ${USER_ID} -g ${GROUP_ID} builder || true

# Switch to non-root user
USER builder

# Set environment variables
ENV PATH="/home/builder/.dotnet/tools:${PATH}"
ENV DOTNET_ROOT="/usr/share/dotnet"

# Default command
CMD ["/bin/bash"]
