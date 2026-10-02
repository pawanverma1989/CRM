// Development / build-time defaults. In the container this file is rewritten by
// entrypoint.sh from the VITE_API_URL and VITE_IDENTITY_API_URL environment
// variables before nginx starts.
window.__ENV__ = {
  VITE_API_URL: "/api/customer/v1",
  VITE_IDENTITY_API_URL: "/api/identity/v1"
};
