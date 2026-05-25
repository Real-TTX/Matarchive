FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP_PORT=8077
RUN apt-get update \
    && apt-get install -y --no-install-recommends smbclient \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN dotnet publish src/Matarchive.Web/Matarchive.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG APP_PORT=8077
RUN apt-get update \
    && apt-get install -y --no-install-recommends smbclient \
    && rm -rf /var/lib/apt/lists/*
ENV APP_PORT=${APP_PORT}
ENV ASPNETCORE_URLS=http://+:${APP_PORT}
ENV ASPNETCORE_HTTP_PORTS=${APP_PORT}
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8077
ENTRYPOINT ["dotnet", "Matarchive.Web.dll"]
