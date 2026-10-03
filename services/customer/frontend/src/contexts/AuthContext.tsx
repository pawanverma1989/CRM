import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { setAccessToken, setRefreshCallback } from '../api/client';
import { refreshTokens } from '../api/auth';
import { decodeToken } from '../lib/jwt';
import type { UserDto, UserRole } from '../types';

/**
 * Shared with the Identity app, which owns sign-in and writes this key. Both
 * apps are served from the same origin behind nginx, so the token is visible to
 * both. The access token itself is kept in memory only.
 */
const REFRESH_TOKEN_KEY = 'crm_refresh_token';

/** The Identity app owns /login; this app never renders a sign-in screen. */
const getLoginUrl = () => `/login?from=${encodeURIComponent(window.location.pathname)}`;

interface AuthContextValue {
  user: UserDto | null;
  role: UserRole | null;
  visibleOwnerIds: string[];
  isAuthenticated: boolean;
  isLoading: boolean;
  /** True once the session could not be restored and the browser is leaving. */
  isSignedOut: boolean;
  signOut: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function readStoredRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  } catch {
    // Storage can be unavailable (private mode, blocked site data).
    return null;
  }
}

function writeStoredRefreshToken(token: string | null) {
  try {
    if (token === null) localStorage.removeItem(REFRESH_TOKEN_KEY);
    else localStorage.setItem(REFRESH_TOKEN_KEY, token);
  } catch {
    // Ignore: the in-memory access token still carries this session.
  }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [role, setRole] = useState<UserRole | null>(null);
  const [visibleOwnerIds, setVisibleOwnerIds] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSignedOut, setIsSignedOut] = useState(false);
  const redirectedRef = useRef(false);

  const goToLogin = useCallback(() => {
    setIsSignedOut(true);
    if (redirectedRef.current) return;
    redirectedRef.current = true;
    // Full navigation: /login belongs to the Identity SPA, not this router.
    window.location.assign(getLoginUrl());
  }, []);

  const clearSession = useCallback(() => {
    setAccessToken(null);
    writeStoredRefreshToken(null);
    setUser(null);
    setRole(null);
    setVisibleOwnerIds([]);
  }, []);

  const signOut = useCallback(() => {
    clearSession();
    goToLogin();
  }, [clearSession, goToLogin]);

  const signOutRef = useRef(signOut);
  signOutRef.current = signOut;

  useEffect(() => {
    let cancelled = false;

    /** Exchanges the shared refresh token for a fresh access token. */
    const doRefresh = async (): Promise<string | null> => {
      const storedRefresh = readStoredRefreshToken();
      if (!storedRefresh) {
        signOutRef.current();
        return null;
      }
      try {
        const resp = await refreshTokens(storedRefresh);
        setAccessToken(resp.accessToken);
        writeStoredRefreshToken(resp.refreshToken);
        if (!cancelled) {
          setUser(resp.user);
          const claims = decodeToken(resp.accessToken);
          setRole(claims?.role ?? resp.user.role ?? null);
          setVisibleOwnerIds(claims?.visible_owner_ids ?? []);
        }
        return resp.accessToken;
      } catch {
        signOutRef.current();
        return null;
      }
    };

    // The axios 401 interceptor refreshes and retries through this callback.
    setRefreshCallback(doRefresh);

    const restore = async () => {
      await doRefresh();
      if (!cancelled) setIsLoading(false);
    };

    restore();

    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <AuthContext.Provider
      value={{
        user,
        role,
        visibleOwnerIds,
        isAuthenticated: !!user,
        isLoading,
        isSignedOut,
        signOut,
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
