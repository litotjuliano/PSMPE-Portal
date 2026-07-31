# Build context is ./src (see docker-compose.yml). Multi-stage build for a small runtime image.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first so `dotnet restore` is cached unless a .csproj actually changes.
COPY PSMPE.Portal.Domain/*.csproj PSMPE.Portal.Domain/
COPY PSMPE.Portal.Application/*.csproj PSMPE.Portal.Application/
COPY PSMPE.Portal.Infrastructure/*.csproj PSMPE.Portal.Infrastructure/
COPY PSMPE.Portal.WebAPI/*.csproj PSMPE.Portal.WebAPI/
RUN dotnet restore PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj

COPY . .
RUN dotnet publish PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# SkiaSharp (ReceiptGenerator) needs an actual font file + fontconfig to draw text on Linux - the
# base aspnet image ships neither, so text would silently render blank without this.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

# The base image's default non-root "app" user (uid 64198) already owns /app.
USER app

COPY --from=build /app .

# Uploads live on a mounted volume, not in the published output above - create it now (as the
# non-root "app" user) so a *fresh* volume inherits app ownership on first mount instead of the
# root ownership Docker would otherwise assign, which blocks writes at runtime (uid mismatch).
RUN mkdir -p wwwroot/uploads

EXPOSE 8080
ENTRYPOINT ["dotnet", "PSMPE.Portal.WebAPI.dll"]
