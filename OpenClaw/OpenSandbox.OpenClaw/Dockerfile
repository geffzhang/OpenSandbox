FROM node:20 AS web-build
WORKDIR /src/OpenClaw/web
COPY OpenClaw/web/package*.json /src/OpenClaw/web/
COPY OpenClaw/web/packages /src/OpenClaw/web/packages
COPY OpenClaw/web/apps/web /src/OpenClaw/web/apps/web
RUN npm --prefix /src/OpenClaw/web ci
RUN npm --prefix /src/OpenClaw/web/apps/web run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS publish
WORKDIR /src
COPY . .
COPY --from=web-build /src/OpenClaw/web/apps/web/dist /src/OpenClaw/web/apps/web/dist
RUN dotnet publish OpenClaw/OpenSandbox.OpenClaw/OpenSandbox.OpenClaw.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenSandbox.OpenClaw.dll"]
