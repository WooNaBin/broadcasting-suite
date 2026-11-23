# Build from repository root:
#   docker build -f nas-api/Dockerfile -t worklog-nas-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY www ./www
COPY nas-api/WorkLogNasApi.csproj ./nas-api/
COPY nas-api/Program.cs ./nas-api/
WORKDIR /src/nas-api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=
ENV PORT=17822
EXPOSE 17822
ENTRYPOINT ["dotnet", "WorkLogNasApi.dll"]
