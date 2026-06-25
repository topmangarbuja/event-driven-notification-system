# ============================
# Stage 1: Build the .NET app
# ============================
# Use the full .NET SDK image so we can restore, build, and publish the project.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Set the working directory inside the container
WORKDIR /src

# Copy the entire project into the build container
COPY . .

# Publish the application to a folder inside the build container
# This produces compiled IL + dependencies in /src/out
RUN dotnet publish -c Release -o /src/out


# ==========================================
# Stage 2: Create the final runtime image
# ==========================================
# Use the lightweight ASP.NET Core runtime image (no SDK)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Set the working directory for the runtime container
WORKDIR /app

# Copy the published output from the build stage into the runtime image
COPY --from=build /src/out .

# Start the application
ENTRYPOINT ["dotnet", "src.dll"]