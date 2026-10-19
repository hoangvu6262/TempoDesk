FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /app
COPY client/web/package*.json ./
RUN npm ci
COPY client/web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine
COPY client/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
USER 101
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/ || exit 1
