# =============================
# Stage 1: Build the UI assets
# =============================

# Use the Node.js image to install dependencies and build the Vite project
FROM node:22-alpine AS build

# Set the working directory for the build stage
WORKDIR /app

# Install pnpm globally (the project's package manager)
RUN npm install -g pnpm@10.18.1

# Copy dependency manifests first to leverage Docker layer caching
COPY package.json pnpm-lock.yaml ./

# Install dependencies with a frozen lockfile for reproducible builds
RUN pnpm install --frozen-lockfile

# Build the static assets (output goes to /app/dist)
RUN pnpm run build


# =================================
# Stage 2: Create the runtime image
# =================================

# Use the lightweight nginx image to serve the static files
FROM nginx:1.31.1-alpine-slim AS runtime

# Copy the built assets from the build stage into nginx's html directory
COPY --from=build /app/dist /usr/share/nginx/html

# Expose port 80 (nginx default)
EXPOSE 80