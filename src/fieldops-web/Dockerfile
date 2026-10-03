# Day 97: multi-stage build for fieldops-web — a DIFFERENT shape from
# FieldOps.Api's (Day 58) multi-stage Dockerfile. The backend's final stage
# still RUNS a process (dotnet FieldOps.Api.dll); Angular has no process to
# run — "building" it just produces static HTML/CSS/JS files, so the final
# stage only ever needs a web server to hand those files out.

FROM node:24-alpine AS build
WORKDIR /app

# Copy only package.json/package-lock.json first — lets Docker cache the
# "npm ci" layer separately from the actual source, same reasoning as
# FieldOps.Api's Dockerfile caching "dotnet restore" before the real source.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# The much smaller final image: no Node, no npm, no source code — only the
# already-built static files plus a web server to serve them.
FROM nginx:alpine AS final
COPY --from=build /app/dist/fieldops-web/browser /usr/share/nginx/html
# Day 98: replaces nginx's own default site config, which has no idea about
# Angular's client-side routing (Day 91) — without this, any direct hit on
# a route like /work-orders/28 404s, because nginx looks for a literal file
# at that path instead of handing the request to Angular's router.
COPY nginx.conf /etc/nginx/conf.d/default.conf
