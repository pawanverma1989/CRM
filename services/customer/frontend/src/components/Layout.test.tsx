import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { renderWithProviders, signInAs } from '../test/testUtils';
import { makeAccessToken } from '../test/jwt';
import { Layout } from './Layout';
import type { UserDto } from '../types';

const salesRepUser: UserDto = {
  id: 'user-2',
  organizationId: 'org-1',
  email: 'rep@example.com',
  firstName: 'Sam',
  lastName: 'Rep',
  role: 'sales_rep',
  status: 'active',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

// §2 permission matrix: only admins manage custom fields, picklists and the
// recycle bin; only admins see the reassignment queue.
describe('Layout — role-based navigation (§2)', () => {
  it('hides admin-only links for a sales rep', async () => {
    signInAs();
    server.use(
      http.post('/api/identity/v1/auth/refresh', () =>
        HttpResponse.json({
          accessToken: makeAccessToken({ role: 'sales_rep' }),
          refreshToken: 'test-refresh-token',
          expiresIn: 3600,
          tokenType: 'Bearer',
          user: salesRepUser,
        })
      )
    );

    renderWithProviders(
      <Layout>
        <div>content</div>
      </Layout>
    );

    await screen.findByText('Sam Rep');
    expect(screen.queryByRole('link', { name: 'Custom fields' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Picklists' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Recycle bin' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Reassignment' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Contacts' })).toBeInTheDocument();
  });

  it('shows admin-only links for an admin', async () => {
    signInAs();
    // The default /auth/refresh handler returns an admin token.
    renderWithProviders(
      <Layout>
        <div>content</div>
      </Layout>
    );

    await screen.findByText('Ada Admin');
    expect(screen.getByRole('link', { name: 'Custom fields' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Picklists' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Recycle bin' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Reassignment' })).toBeInTheDocument();
  });
});
