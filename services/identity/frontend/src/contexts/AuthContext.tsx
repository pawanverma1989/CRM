import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { setAccessToken, setRefreshCallback } from '../api/client';
import { login as apiLogin, logout as apiLogout, refreshTokens } from '../api/auth';
import { decodeToken } from '../lib/jwt';
import { clearPasswordPromptDismissals } from '../lib/passwordPrompt';
import type { UserDto, UserRole } from '../types';

const REFRESH_TOKEN_KEY = 'crm_refresh_token';

interface AuthContextValue {
  user: UserDto | null;
  role: UserRole | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  updateUser: (user: UserDto) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [role, setRole] = useState<UserRole | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const logoutCallbackRef = useRef<(() => void) | null>(null);

  const performLogout = useCallback(() => {
    setAccessToken(null);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    clearPasswordPromptDismissals();
    setUser(null);
    setRole(null);
  }, []);

  logoutCallbackRef.current = performLogout;

  // Wire up the refresh callback so axios interceptor can use it
  useEffect(() => {
    const doRefresh = async (): Promise<string | null> => {
      const storedRefresh = localStorage.getItem(REFRESH_TOKEN_KEY);
      if (!storedRefresh) {
        logoutCallbackRef.current?.();
        return null;
      }
      try {
        const resp = await refreshTokens(storedRefresh);
        setAccessToken(resp.accessToken);
        localStorage.setItem(REFRESH_TOKEN_KEY, resp.refreshToken);
        setUser(resp.user);
        const claims = decodeToken(resp.accessToken);
        setRole(claims?.role ?? null);
        return resp.accessToken;
      } catch {
        logoutCallbackRef.current?.();
        return null;
      }
    };

    setRefreshCallback(doRefresh);

    // Attempt silent restore on mount
    const restore = async () => {
      const stored = localStorage.getItem(REFRESH_TOKEN_KEY);
      if (stored) {
        await doRefresh();
      }
      setIsLoading(false);
    };

    restore();
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const resp = await apiLogin({ email, password });
    setAccessToken(resp.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, resp.refreshToken);
    setUser(resp.user);
    const claims = decodeToken(resp.accessToken);
    setRole(claims?.role ?? null);
  }, []);

  const logout = useCallback(async () => {
    try {
      await apiLogout();
    } catch {
      // ignore errors on logout
    } finally {
      performLogout();
    }
  }, [performLogout]);

  const updateUser = useCallback((updated: UserDto) => {
    setUser(updated);
  }, []);

  return (
    <AuthContext.Provider
      value={{
        user,
        role,
        isAuthenticated: !!user,
        isLoading,
        login,
        logout,
        updateUser,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
