# ---- Stage 1: build the Solid 2 admin UI ----
FROM node:24-alpine AS web
RUN corepack enable
WORKDIR /web
COPY web/package.json web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY web/ .
RUN pnpm build

# ---- Stage 2: publish the ASP.NET host with the UI embedded ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ProxyManager.slnx ./
COPY src/ ./src/
COPY tests/ ./tests/
RUN dotnet restore ProxyManager.slnx
RUN dotnet publish src/ProxyManager.Api/ProxyManager.Api.csproj -c Release -o /app/publish --no-restore
COPY --from=web /web/dist/client /app/publish/wwwroot

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV Data__Directory=/data
COPY --from=build /app/publish .
EXPOSE 80 81
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fs http://127.0.0.1:81/healthz || exit 1
ENTRYPOINT ["dotnet", "ProxyManager.Api.dll"]
