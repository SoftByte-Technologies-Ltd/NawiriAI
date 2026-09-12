FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/NawiriAI.Api/NawiriAI.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/ ./
ENV ASPNETCORE_HTTP_PORTS=7860
ENV NAWIRIAI_DEMO_MODE=true
USER app
EXPOSE 7860
ENTRYPOINT ["dotnet", "NawiriAI.Api.dll"]
