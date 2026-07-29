# Document Hub client — built with Node, served by nginx. The runtime image contains
# static files only; Node never ships.
#
# Build from the repository root:
#   docker build -f client/Dockerfile -t documenthub-client .

FROM node:22-alpine AS build
WORKDIR /src

# Manifests first so an unchanged dependency set re-uses the cached install.
COPY client/package.json client/package-lock.json ./
# `npm ci` rather than `install`: it installs exactly the lockfile and fails if
# the two have drifted, which is what makes a build reproducible.
RUN npm ci

COPY client/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime

# Rendered at container start by the nginx entrypoint. The filter restricts
# substitution to this one name, so nginx's own $uri, $host and friends survive
# — without it envsubst would happily blank out any that matched an env var.
COPY client/nginx.conf.template /etc/nginx/templates/default.conf.template
ENV NGINX_ENVSUBST_FILTER="API_ORIGIN"
ENV API_ORIGIN="http://api:8080"

COPY --from=build /src/dist/client/browser /usr/share/nginx/html

EXPOSE 80
