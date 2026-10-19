FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY server/tools/TempoDesk.DatabaseMigrator/*.csproj ./
RUN dotnet restore -a "$TARGETARCH"
COPY server/tools/TempoDesk.DatabaseMigrator/ ./
RUN dotnet publish -a "$TARGETARCH" -c Release --no-restore --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /out/ ./
COPY database/migrations/ ./database/migrations/
USER app
ENTRYPOINT ["dotnet", "TempoDesk.DatabaseMigrator.dll"]
