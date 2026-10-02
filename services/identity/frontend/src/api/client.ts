import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';

// Runtime value (injected by entrypoint.sh into env-config.js) takes
// priority over the build-time value so the URL can be changed without
// rebuilding the image.
const BASE_URL: string =
  (window as { __ENV__?: { VITE_API_URL?: string } }).__ENV__?.VITE_API_URL
  ?? import.meta.env.VITE_API_URL
  ?? '/api/identity/v1';

// In-memory access token storage
let accessToken: string | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

export const apiClient: AxiosInstance = axios.create({
  baseURL: BASE_URL,
  headers: { 'Content-Type': 'application/json' },
});

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
  return config;
});

// Refresh logic is wired in AuthContext to avoid circular imports
let refreshCallback: (() => Promise<string | null>) | null = null;

export function setRefreshCallback(cb: () => Promise<string | null>) {
  refreshCallback = cb;
}

let isRefreshing = false;
let refreshQueue: Array<(token: string | null) => void> = [];

apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    if (error.response?.status === 401 && !originalRequest._retry && refreshCallback) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          refreshQueue.push((token) => {
            if (token) {
              originalRequest.headers.Authorization = `Bearer ${token}`;
              resolve(apiClient(originalRequest));
            } else {
              reject(error);
            }
          });
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const newToken = await refreshCallback();
        isRefreshing = false;

        if (newToken) {
          refreshQueue.forEach((cb) => cb(newToken));
          refreshQueue = [];
          originalRequest.headers.Authorization = `Bearer ${newToken}`;
          return apiClient(originalRequest);
        } else {
          refreshQueue.forEach((cb) => cb(null));
          refreshQueue = [];
          return Promise.reject(error);
        }
      } catch (refreshError) {
        isRefreshing = false;
        refreshQueue.forEach((cb) => cb(null));
        refreshQueue = [];
        return Promise.reject(refreshError);
      }
    }

    return Promise.reject(error);
  }
);

export default apiClient;
