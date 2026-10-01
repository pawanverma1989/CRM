import { decodeJwt } from 'jose';
import type { UserRole } from '../types';

export interface JwtClaims {
  sub: string;
  user_id: string;
  organization_id: string;
  role: UserRole;
  visible_owner_ids: string[];
  exp: number;
  iat: number;
}

export function decodeToken(token: string): JwtClaims | null {
  try {
    const claims = decodeJwt(token);
    return claims as unknown as JwtClaims;
  } catch {
    return null;
  }
}

export function isTokenExpired(token: string): boolean {
  const claims = decodeToken(token);
  if (!claims) return true;
  return Date.now() / 1000 > claims.exp;
}

export function getRoleFromToken(token: string): UserRole | null {
  const claims = decodeToken(token);
  return claims?.role ?? null;
}
