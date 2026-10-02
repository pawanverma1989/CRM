import type { UserRole } from '../types';

/**
 * Builds an unsigned-but-well-formed JWT for tests. `decodeJwt` (jose) only
 * base64url-decodes the payload — it does not verify a signature — so this is
 * enough to drive `AuthContext` / `usePermissions` in tests without a real
 * Identity service.
 */
export function makeAccessToken(claims: {
  role?: UserRole;
  userId?: string;
  visibleOwnerIds?: string[];
} = {}): string {
  const header = { alg: 'none', typ: 'JWT' };
  const payload = {
    sub: claims.userId ?? 'user-1',
    user_id: claims.userId ?? 'user-1',
    organization_id: 'org-1',
    role: claims.role ?? 'admin',
    visible_owner_ids: claims.visibleOwnerIds ?? [],
    iat: Math.floor(Date.now() / 1000),
    exp: Math.floor(Date.now() / 1000) + 3600,
  };
  const toBase64Url = (obj: object) =>
    btoa(JSON.stringify(obj)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${toBase64Url(header)}.${toBase64Url(payload)}.sig`;
}
