# Layer 0 (+ optional layer 1 tools) of docs/cloud-dev.md, as an image.
#
# Deliberately does not contain the game. Pack the assemblies on a machine with Slay the Spire 2
# (scripts/pack-game-assemblies.ps1 -> build/game-assemblies/sts2-assemblies-*.zip) and
# setup-cloud-dev.sh will automatically verify and unpack the newest workspace pack on container
# creation, or run scripts/unpack-game-assemblies.sh manually at any time.
FROM mcr.microsoft.com/dotnet/sdk:9.0

ARG DEBIAN_FRONTEND=noninteractive
ARG PWSH_VERSION=7.4.6

# python3 is for the standard-library gates only; uv brings its own interpreter for the MCP
# server. The dotnet/sdk:9.0 base image already includes ICU, avoiding version-pinned libicu72
# breakage across Debian releases.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        jq \
        python3 \
        python3-venv \
        unzip \
        xz-utils \
        zip \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH="/root/.dotnet/tools:/usr/local/bin:${PATH}"

# UV_UNMANAGED_INSTALL keeps the installer from rewriting shell profiles: the image puts both
# binaries where every shell already looks.
RUN UV_UNMANAGED_INSTALL=/usr/local/bin sh -c 'curl -LsSf https://astral.sh/uv/install.sh | sh'

RUN set -eux; \
    case "$(dpkg --print-architecture)" in \
        amd64) pwsh_arch=x64 ;; \
        arm64) pwsh_arch=arm64 ;; \
        *) echo "unsupported architecture" >&2; exit 1 ;; \
    esac; \
    curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VERSION}/powershell-${PWSH_VERSION}-linux-${pwsh_arch}.tar.gz" \
        -o /tmp/pwsh.tar.gz; \
    mkdir -p /opt/microsoft/powershell/7; \
    tar -xzf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell/7; \
    chmod +x /opt/microsoft/powershell/7/pwsh; \
    ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh; \
    rm /tmp/pwsh.tar.gz

WORKDIR /workspaces/sp
