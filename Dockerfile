# ==========================================
# Multi-stage Dockerfile for AioKin (.NET 9)
# Optimized for Render, Fly.io, and Docker
# ==========================================

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (layer cache optimization)
COPY ["AioKin/AioKin.csproj", "AioKin/"]
RUN dotnet restore "AioKin/AioKin.csproj"

# Copy full application source code
COPY ["AioKin/", "AioKin/"]
WORKDIR "/src/AioKin"

# Build and publish release binaries
RUN dotnet publish "AioKin.csproj" -c Release -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

# Stage 2: Runtime Environment
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published artifacts from build stage
COPY --from=build /app/publish .

# Default ASP.NET port (will be dynamically overridden by $PORT on Render)
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Security: Run as non-root user (app user UID 1654 built into .NET 8/9 images)
USER $APP_UID

# Entrypoint supporting Render dynamic $PORT (or fallback to 8080)
ENTRYPOINT ["sh", "-c", "export ASPNETCORE_HTTP_PORTS=${PORT:-8080} && exec dotnet AioKin.dll"]
