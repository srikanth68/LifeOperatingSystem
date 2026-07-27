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
# browsers gate behind a secure context.
#
# The cert MUST carry a Subject Alternative Name — since Chrome 58 the CN is
# ignored, and a cert with no SAN fails with ERR_CERT_COMMON_NAME_INVALID and
# can't even be fixed by trusting it. TLS_SAN lists every host you reach the
# dashboard by; override at build time if your Meshnet IP changes:
#   docker compose build --build-arg TLS_SAN="IP:100.x.x.x,DNS:host.nord,DNS:localhost"
# Once deployed, trust maaya.crt on your devices to drop the warning for good
# (see DOCKER.md → "Trusting the dashboard certificate").
ARG TLS_SAN="IP:100.126.41.41,DNS:srp6888everest.nord,DNS:localhost,IP:127.0.0.1"
RUN apk add --no-cache openssl \
 && mkdir -p /etc/nginx/certs /etc/nginx/snippets \
 && openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
      -keyout /etc/nginx/certs/maaya.key -out /etc/nginx/certs/maaya.crt \
      -subj "/CN=Maaya OS" \
      -addext "subjectAltName=${TLS_SAN}"
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-locations.conf /etc/nginx/snippets/maaya.conf
COPY --from=build /app/dist /usr/share/nginx/html
