# syntax=docker/dockerfile:1
#
# One image: the API, serving the built web client from wwwroot, so the browser
# only ever talks to one origin (no CORS, and the auth cookie just works).
# Built from the repo root: docker build -t audiobook-server:latest .

# ---- Web client ----
# slim (glibc) rather than alpine: Vite's bundler ships native binaries, and the
# lockfile was written on Windows, so stay on the most common Linux target.
FROM node:24-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---- API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
# Project files first, so the restore layer is cached until dependencies change.
COPY src/AudiobookServer.Api/AudiobookServer.Api.csproj src/AudiobookServer.Api/
COPY src/AudiobookServer.Core/AudiobookServer.Core.csproj src/AudiobookServer.Core/
RUN dotnet restore src/AudiobookServer.Api/AudiobookServer.Api.csproj
COPY src/ src/
RUN dotnet publish src/AudiobookServer.Api/AudiobookServer.Api.csproj -c Release -o /app --no-restore

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# The scanner shells out to ffprobe (durations, chapters, mp3 packet counts) and
# ffmpeg (cover extraction). The aspnet image has neither.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot

# /data holds what must survive a new image: extracted covers, and the
# data-protection keys that encrypt auth cookies and tokens (lose them and every
# session ends). Owned by the image's non-root user, which a new named volume
# inherits on first mount.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Covers__Directory=/data/covers \
    DataProtection__KeysDirectory=/data/keys
RUN mkdir -p /data/covers /data/keys && chown -R "$APP_UID" /data
VOLUME /data

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "AudiobookServer.Api.dll"]
