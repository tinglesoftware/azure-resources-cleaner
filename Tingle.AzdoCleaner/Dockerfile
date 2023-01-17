#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0-bullseye-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

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