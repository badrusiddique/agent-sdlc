# Implementation Agent builder image — .NET 10 SDK + git.
# Node/Angular CLI can be layered later; keep the base image lean for local e2e.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends git curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /work
CMD ["bash"]
