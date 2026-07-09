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
# The base image's default non-root "app" user (uid 64198) already owns /app.
USER app

COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "PSMPE.Portal.WebAPI.dll"]
