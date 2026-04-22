FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SmartLoad.Api/SmartLoad.Api.csproj SmartLoad.Api/
COPY SmartLoad.Tests/SmartLoad.Tests.csproj SmartLoad.Tests/
COPY SmartLoad.slnx ./
RUN dotnet restore SmartLoad.Api/SmartLoad.Api.csproj
COPY . .
RUN dotnet publish SmartLoad.Api/SmartLoad.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartLoad.Api.dll"]
