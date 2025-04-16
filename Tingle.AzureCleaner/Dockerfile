#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
ENV AS_WEB_APP true

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Tingle.AzureCleaner/Tingle.AzureCleaner.csproj", "Tingle.AzureCleaner/"]
RUN dotnet restore "./Tingle.AzureCleaner/./Tingle.AzureCleaner.csproj"
COPY . .
WORKDIR "/src/Tingle.AzureCleaner"
RUN dotnet build "./Tingle.AzureCleaner.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Tingle.AzureCleaner.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Tingle.AzureCleaner.dll"]
