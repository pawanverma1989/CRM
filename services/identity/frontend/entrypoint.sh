#!/bin/sh
# Write runtime environment variables into env-config.js before nginx starts.
# The file is served as a static asset and loaded by index.html before the
# React bundle, so window.__ENV__ is available when the app initialises.
cat > /usr/share/nginx/html/env-config.js <<EOF
window.__ENV__ = {
  VITE_API_URL: "${VITE_API_URL:-/api/identity/v1}"
};
EOF

exec nginx -g 'daemon off;'
