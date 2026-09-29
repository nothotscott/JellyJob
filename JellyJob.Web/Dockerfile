# Build context is the repository root, not this directory:
#   docker build -f JellyJob.Web/Dockerfile -t jellyjob .
#
# The stage names (base, build, publish, final) are what Visual Studio's container debugging looks for;
# its fast mode runs only `base` and mounts the build output into it. Keep them if you rename things.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# The .NET 10 aspnet image is Ubuntu 24.04 (noble). Installed here in `base` so VS debugging sees them:
# - curl: the Compose healthcheck; the image ships no HTTP client.
# - ffmpeg: Ubuntu's build includes NVENC. The GPU driver itself comes from the host through the NVIDIA
#   container runtime (NVIDIA_DRIVER_CAPABILITIES must include "video"), and must be new enough for the
#   NVENC API this ffmpeg was built against (6.1: driver 530+).
# - comskip: packaged in universe, which the Ubuntu base image enables.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ffmpeg comskip \
    && rm -rf /var/lib/apt/lists/*

# Owned by the app user so a named volume mounted here starts out writable (Docker copies the image
# directory's ownership into a new named volume). Bind mounts keep the host's ownership instead.
RUN mkdir -p /data /output && chown "$APP_UID:$APP_UID" /data /output

USER $APP_UID
WORKDIR /app

# Listen on the same port inside the container as the one Jellyfin calls, so the URL is the same whether
# Jellyfin reaches JellyJob through the host (host.docker.internal:5238) or a shared network (jellyjob:5238).
ENV ASPNETCORE_HTTP_PORTS=5238
EXPOSE 5238

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore against the project files alone so the layer survives source edits. Package versions live in
# Directory.Packages.props (central package management), so restore needs it too.
COPY ["Directory.Packages.props", "./"]
COPY ["JellyJob.Core/JellyJob.Core.csproj", "JellyJob.Core/"]
COPY ["JellyJob.Web/JellyJob.Web.csproj", "JellyJob.Web/"]
RUN dotnet restore "JellyJob.Web/JellyJob.Web.csproj"

COPY . .
WORKDIR "/src/JellyJob.Web"
RUN dotnet build "JellyJob.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "JellyJob.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "JellyJob.Web.dll"]
