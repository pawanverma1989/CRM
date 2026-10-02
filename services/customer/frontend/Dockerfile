FROM node:20-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.25-alpine
# curl is used by the compose healthcheck (curl -f http://localhost:80/).
RUN apk add --no-cache curl
# nginx passes the URI through unchanged, so the container receives paths that
# still start with /customer/. The Vite build (base: '/customer/') is therefore
# served from a matching sub-directory and /customer/assets/... resolves to the
# real hashed files.
COPY --from=builder /app/dist /usr/share/nginx/html/customer
COPY nginx-spa.conf /etc/nginx/conf.d/default.conf
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
EXPOSE 80
ENTRYPOINT ["/entrypoint.sh"]
