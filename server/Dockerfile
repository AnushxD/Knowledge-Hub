# Document Hub API — multi-stage so the shipped image carries the runtime and the
# published output, never the SDK or the source.
#
# Build from the repository root, not from server/:
#   docker build -f server/Dockerfile -t documenthub-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, on their own layer. Restore is the slow step and depends
# only on these, so editing a .cs file re-uses the cached package download
# instead of repeating it.
COPY server/DocHub.slnx ./
COPY server/src/DocHub.Api/DocHub.Api.csproj server/src/DocHub.Api/
COPY server/src/DocHub.Services/DocHub.Services.csproj server/src/DocHub.Services/
COPY server/src/DocHub.DataAccess/DocHub.DataAccess.csproj server/src/DocHub.DataAccess/
COPY server/src/DocHub.Integrations/DocHub.Integrations.csproj server/src/DocHub.Integrations/

RUN dotnet restore server/src/DocHub.Api/DocHub.Api.csproj

COPY server/src/ server/src/

# The schema script lives at the repository root and the API project links it
# into its publish output, so the published folder always carries the schema
# matching its binaries. It is outside server/src/, so it has to be copied
# explicitly — without it `dotnet publish` fails on the missing link, which is
# a build error and not a quiet omission.
COPY documenthub-v2-schema.sql ./

# No app host: the image starts the app with `dotnet DocHub.Api.dll`, so the
# native launcher would be dead weight.
RUN dotnet publish server/src/DocHub.Api/DocHub.Api.csproj \
    --configuration Release \
    --no-restore \
    /p:UseAppHost=false \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet image ships a non-root "app" user. Running as it means a container
# breakout starts from an unprivileged account rather than from root.
USER app

# Kestrel binds this; the container publishes it and a reverse proxy or the
# compose network maps it. 8080 rather than 80 because a non-root process
# cannot bind a privileged port.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# Provisioning stays explicit — this image never migrates the database or
# creates the blob container on boot. Those are operator commands run against
# the same image:
#   docker run --rm documenthub-api dotnet DocHub.Api.dll init-storage
#   docker run --rm documenthub-api dotnet DocHub.Api.dll seed-admin
# and `dotnet ef database update` from the repository.
ENTRYPOINT ["dotnet", "DocHub.Api.dll"]
