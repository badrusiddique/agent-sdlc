# BuildGenix IA/TA toolchain image.
# Used by BuilderShell as agenthub-ia-builder:local (or pull from ACR after you push).
# Contents: .NET 10 SDK, git, Node 22, Angular CLI, Python 3, Azure CLI, zip/jq/curl.
#
# Build:  ./scripts/build-ia-builder.sh
# Save:   ./scripts/save-ia-builder.sh   → tar for manual ACR / blob upload

FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG NODE_MAJOR=22
ARG DEBIAN_FRONTEND=noninteractive

USER root
WORKDIR /tmp

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        wget \
        gnupg \
        git \
        jq \
        unzip \
        zip \
        python3 \
        python3-pip \
        python3-venv \
        build-essential \
        apt-transport-https \
        lsb-release \
    && rm -rf /var/lib/apt/lists/*

# Node.js (NodeSource) + global Angular CLI
RUN curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && npm install -g @angular/cli@19 \
    && npm cache clean --force \
    && rm -rf /var/lib/apt/lists/*

# Azure CLI (official Microsoft package feed)
RUN curl -fsSL https://aka.ms/InstallAzureCLIDeb | bash \
    && rm -rf /var/lib/apt/lists/*

# Convenience: python → python3
RUN ln -sf /usr/bin/python3 /usr/local/bin/python \
    && ln -sf /usr/bin/pip3 /usr/local/bin/pip || true

# Smoke labels (queried by scripts)
LABEL org.opencontainers.image.title="agenthub-ia-builder" \
      org.opencontainers.image.description="BuildGenix IA/TA builder: dotnet10, node, ng, python, az, git" \
      org.opencontainers.image.source="https://github.com/badrusiddique/agent-sdlc"

WORKDIR /work
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NG_CLI_ANALYTICS=false \
    PATH="/root/.dotnet/tools:${PATH}"

COPY TOOLCHAIN.md /TOOLCHAIN.md

# Prove toolchains exist at build time (fails the image if any are missing)
RUN set -e; \
    git --version; \
    dotnet --list-sdks; \
    node --version; \
    npm --version; \
    command -v ng; \
    python3 --version; \
    az version; \
    zip -v | head -1; \
    jq --version; \
    test -f /TOOLCHAIN.md

CMD ["bash"]
