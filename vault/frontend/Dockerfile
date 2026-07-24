# Maaya frontend — static Vite build served by nginx (HTTP :80 + HTTPS :443).
# Uses `npx vite build` (not `npm run build`) intentionally: the repo carries a
# few known pre-existing tsc errors that don't affect the runtime bundle.

FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npx vite build

FROM nginx:alpine
# Self-signed TLS cert so the mic (getUserMedia) works over Meshnet/.nord, which
# browsers gate behind a secure context. Personal use — accept the warning once.
RUN apk add --no-cache openssl \
 && mkdir -p /etc/nginx/certs /etc/nginx/snippets \
 && openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
      -keyout /etc/nginx/certs/maaya.key -out /etc/nginx/certs/maaya.crt \
      -subj "/CN=maaya-os"
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-locations.conf /etc/nginx/snippets/maaya.conf
COPY --from=build /app/dist /usr/share/nginx/html
