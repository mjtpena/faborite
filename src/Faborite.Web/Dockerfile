# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Faborite.Web/Faborite.Web.csproj", "src/Faborite.Web/"]
COPY ["Directory.Build.props", "."]

# Restore dependencies
RUN dotnet restore "src/Faborite.Web/Faborite.Web.csproj"

# Copy all source files
COPY . .

# Build and publish
WORKDIR "/src/src/Faborite.Web"
RUN dotnet publish "Faborite.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage - use nginx to serve Blazor WASM
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# Copy published Blazor app
COPY --from=build /app/publish/wwwroot .

# Copy nginx config
COPY <<EOF /etc/nginx/conf.d/default.conf
server {
    listen 80;
    location / {
        root /usr/share/nginx/html;
        try_files \$uri \$uri/ /index.html =404;
    }
}
EOF

EXPOSE 80
