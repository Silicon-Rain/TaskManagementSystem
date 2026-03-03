FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Worker/Worker.csproj", "Worker/"]
COPY ["Persistence/Persistence.csproj", "Persistence/"]
COPY ["Shared/Shared.csproj", "Shared/"]
RUN dotnet restore "Worker/Worker.csproj"

COPY . .
WORKDIR "/src/Worker"
RUN dotnet build "Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Worker.dll"]