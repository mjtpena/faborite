# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Faborite.Api/Faborite.Api.csproj", "src/Faborite.Api/"]
COPY ["src/Faborite.Core/Faborite.Core.csproj", "src/Faborite.Core/"]
COPY ["Directory.Build.props", "."]

# Restore dependencies
RUN dotnet restore "src/Faborite.Api/Faborite.Api.csproj"

# Copy all source files
COPY . .

# Build and publish
WORKDIR "/src/src/Faborite.Api"
RUN dotnet publish "Faborite.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "Faborite.Api.dll"]
