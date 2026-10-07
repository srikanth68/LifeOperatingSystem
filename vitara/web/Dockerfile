# Vitara web -- the Vitara + Insight app, as its own static site behind nginx.
#
# `npm run build` is `tsc --noEmit && vite build`, so a type error fails the image build instead
# of shipping, the same rule Maaya's own frontend follows.

FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
