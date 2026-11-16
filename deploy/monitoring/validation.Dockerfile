# Disposable validation only; not an application or production collector image.
FROM mcr.microsoft.com/playwright@sha256:3bed4b1a12f2338642f3d8cba28e291deef3c66bd4a964bbeb3e57bbff511dbd
RUN apt-get update && apt-get install --yes --no-install-recommends systemd && rm -rf /var/lib/apt/lists/*
