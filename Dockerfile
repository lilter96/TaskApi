# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY .config/dotnet-tools.json .config/
RUN dotnet tool restore

COPY Directory.Build.props ./
COPY src/TaskApi/TaskApi.csproj src/TaskApi/
RUN dotnet restore src/TaskApi/TaskApi.csproj

COPY src/TaskApi/ src/TaskApi/
RUN dotnet publish src/TaskApi/TaskApi.csproj --configuration Release --no-restore --output /app
RUN dotnet ef migrations bundle --project src/TaskApi --configuration Release --output /migrations/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS migrations
COPY --from=build /migrations/efbundle /efbundle
ENTRYPOINT ["/efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS api
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "TaskApi.dll"]
