# Frontend.Dockerfile
# 构建阶段
FROM node:22-alpine AS build
WORKDIR /app

# 缓存依赖
COPY package.json pnpm-lock.yaml .npmrc ./
RUN corepack enable && pnpm install --frozen-lockfile

# 构建
COPY . .
RUN pnpm run build

# 运行阶段
FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
