import { http, HttpResponse } from 'msw';
import { makeAccessToken } from './jwt';
import type { UserDto } from '../types';

const adminUser: UserDto = {
  id: 'user-1',
  organizationId: 'org-1',
  email: 'admin@example.com',
  firstName: 'Ada',
  lastName: 'Admin',
  role: 'admin',
  status: 'active',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

/**
 * Default MSW handlers shared by all tests. Individual test files add more
 * specific handlers with `server.use(...)` for the scenario under test —
 * e.g. a sales_rep token for role-gating tests, or a 409 for conflict tests.
 */
export const handlers = [
  http.post('/api/identity/v1/auth/refresh', () =>
    HttpResponse.json({
      accessToken: makeAccessToken({ role: 'admin' }),
      refreshToken: 'test-refresh-token',
      expiresIn: 3600,
      tokenType: 'Bearer',
      user: adminUser,
    })
  ),

  http.get('/api/customer/v1/owners', () =>
    HttpResponse.json([
      { id: 'owner-1', displayName: 'Asha Rao', isActive: true },
      { id: 'owner-2', displayName: 'Vikram Shah', isActive: true },
    ])
  ),

  http.get('/api/customer/v1/custom-fields', () => HttpResponse.json([])),

  http.get('/api/customer/v1/picklists', () => HttpResponse.json([])),

  http.post('/api/customer/v1/duplicates/check', () => HttpResponse.json({ matches: [] })),
];
