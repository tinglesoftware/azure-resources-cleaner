#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0-bullseye-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

LABEL org.opencontainers.image.description="Cleanup tool for Azure resources based on Azure DevOps PRs" \
      org.opencontainers.image.source="https://github.com/tinglesoftware/azure-devops-cleaner"

FROM mcr.microsoft.com/dotnet/sdk:7.0-bullseye-slim AS build
WORKDIR /src
COPY ["Tingle.AzdoCleaner/Tingle.AzdoCleaner.csproj", "Tingle.AzdoCleaner/"]
RUN dotnet restore "Tingle.AzdoCleaner/Tingle.AzdoCleaner.csproj"
COPY . .
WORKDIR "/src/Tingle.AzdoCleaner"
RUN dotnet build "Tingle.AzdoCleaner.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Tingle.AzdoCleaner.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Tingle.AzdoCleaner.dll"]