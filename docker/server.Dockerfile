FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG PROJECT
WORKDIR /src
COPY global.json ./
COPY server/Directory.Build.props server/
COPY server/src/ server/src/
RUN dotnet publish "server/src/${PROJECT}/${PROJECT}.csproj" \
    -a "$TARGETARCH" -c Release --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /out/ ./
USER app
