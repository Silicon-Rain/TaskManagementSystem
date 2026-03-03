FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Producer/Producer.csproj", "Producer/"]
COPY ["Persistence/Persistence.csproj", "Persistence/"]
COPY ["Shared/Shared.csproj", "Shared/"]
RUN dotnet restore "Producer/Producer.csproj"

COPY . .
WORKDIR "/src/Producer"
RUN dotnet build "Producer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Producer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Producer.dll"]