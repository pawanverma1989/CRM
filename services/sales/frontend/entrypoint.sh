#!/bin/sh
cat > /usr/share/nginx/html/sales/env-config.js <<EOF
window.__ENV__ = {
  VITE_API_URL: "${VITE_API_URL:-/api/sales/v1}",
  VITE_IDENTITY_API_URL: "${VITE_IDENTITY_API_URL:-/api/identity/v1}"
};
EOF

exec nginx -g 'daemon off;'
