# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY . .

RUN dotnet restore Nika.slnx

RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet publish src/Nika.Cli/Nika.Cli.csproj \
      --configuration Release \
      --runtime "$RID" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:EnableCompressionInSingleFile=true \
      --output /app/publish && \
    mv /app/publish/Nika.Cli /app/publish/nika && \
    chmod +x /app/publish/nika

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /app

COPY --from=build /app/publish/ .

ENTRYPOINT ["./nika"]
