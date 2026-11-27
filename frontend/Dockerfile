# syntax=docker/dockerfile:1

# Production image: the built SPA served by Caddy, which is also the reverse proxy and terminates HTTPS.
# The Caddyfile is mounted at run time from deploy/Caddyfile (no rebuild to change the domain or headers).

FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies first so this layer is cached between source changes.
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

FROM caddy:2.10-alpine AS runtime
COPY --from=build /app/dist /srv
