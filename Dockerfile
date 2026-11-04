# Image: routego/notification. Runs on the routego-core EC2 instance via docker compose (5007:8080).
# Build from the repo root:  docker build -t routego/notification .

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Root build files first: the coding-standard analyzers and .editorconfig run during the build,
# and BannedSymbols.txt comes from the docs/coding-standards submodule.
COPY Directory.Build.props .editorconfig ./
COPY docs/coding-standards/enforcement/ docs/coding-standards/enforcement/

# Restore on the csproj alone so the package layer is cached until dependencies change.
COPY Notification/Notification.csproj Notification/
RUN dotnet restore Notification/Notification.csproj -r linux-x64

COPY Notification/ Notification/
# -r linux-x64 is required by PublishReadyToRun; framework-dependent, so the aspnet image supplies the runtime.
RUN dotnet publish Notification/Notification.csproj -c Release -r linux-x64 --self-contained false \
    --no-restore -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The Postgres driver probes for the Kerberos library at startup and prints an error when the slim
# image lacks it; installing it keeps the container log clean.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# The aspnet image already listens on 8080 and runs as the non-root 'app' user.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Notification.dll"]
