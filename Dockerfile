# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project files
COPY ["SW.TCPLoadBalancer.Server/SW.TCPLoadBalancer.Server.csproj", "SW.TCPLoadBalancer.Server/"]
COPY ["SW.TCPLoadBalancer.Tests/SW.TCPLoadBalancer.Tests.csproj", "SW.TCPLoadBalancer.Tests/"]

# Restore dependencies
RUN dotnet restore "SW.TCPLoadBalancer.Server/SW.TCPLoadBalancer.Server.csproj"

# Copy all source code
COPY . .

# Build and publish the application
WORKDIR "/src/SW.TCPLoadBalancer.Server"
RUN dotnet publish "SW.TCPLoadBalancer.Server.csproj" -c Release -o /app/publish \
    --no-restore \
    --runtime linux-x64 \
    --self-contained false

# Use the official .NET runtime image for the final stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app

# Install dependencies for better compatibility
RUN apk add --no-cache icu-libs

# Create a non-root user
RUN adduser -D -s /bin/sh appuser
USER appuser

# Copy the published application
COPY --from=build /app/publish .

# Expose the default port (you may want to adjust this based on your configuration)
EXPOSE 3401

ENTRYPOINT ["dotnet", "SW.TCPLoadBalancer.Server.dll"]