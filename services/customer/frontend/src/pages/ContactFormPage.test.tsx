import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';
import { server } from '../test/server';
import { renderWithProviders, signInAs } from '../test/testUtils';
import { ContactFormPage } from './ContactFormPage';
import type { ContactDto } from '../types';

function renderCreatePage() {
  signInAs();
  return renderWithProviders(
    <Routes>
      <Route path="/contacts/new" element={<ContactFormPage />} />
    </Routes>,
    { route: '/contacts/new' }
  );
}

const existingContact: ContactDto = {
  id: 'contact-1',
  organizationId: 'org-1',
  firstName: 'Priya',
  lastName: 'Shah',
  email: 'priya@acme.in',
  phone: null,
  phoneNormalized: null,
  mobile: null,
  jobTitle: null,
  companyId: null,
  companyName: null,
  ownerId: 'owner-1',
  ownerName: 'Asha Rao',
  addressLine1: null,
  addressLine2: null,
  city: null,
  state: null,
  postalCode: null,
  country: 'IN',
  sourceId: null,
  sourceValue: null,
  sourceLeadId: null,
  tags: [],
  customFields: {},
  version: 3,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
};

describe('ContactFormPage — duplicate warning (DUP-2 / AC-5)', () => {
  it('warns about a possible duplicate but still allows saving', async () => {
    const user = userEvent.setup();
    let createCalled = false;

    server.use(
      http.post('/api/customer/v1/duplicates/check', () =>
        HttpResponse.json({
          matches: [
            {
              id: 'contact-9',
              entityType: 'contact',
              label: 'Priya Shah',
              reason: 'phone',
              matchedValue: '+919876543210',
              severity: 'soft',
            },
          ],
        })
      ),
      http.post('/api/customer/v1/contacts', async () => {
        createCalled = true;
        return HttpResponse.json({ ...existingContact, id: 'contact-new' });
      })
    );

    renderCreatePage();

    await user.type(await screen.findByRole('textbox', { name: 'First name' }), 'Priya');
    await user.type(screen.getByLabelText('Email'), 'priya@acme.in');
    await user.click(screen.getByRole('button', { name: 'Save contact' }));

    expect(await screen.findByText('This may already exist')).toBeInTheDocument();
    expect(createCalled).toBe(false);

    await user.click(screen.getByRole('button', { name: 'Save anyway' }));

    // Once "Save anyway" is clicked the warning dismisses and the save proceeds.
    await waitFor(() => expect(createCalled).toBe(true));
  });
});

describe('ContactFormPage — email-or-phone validation (CON-1 / AC-2)', () => {
  it('names both fields when neither an email nor a phone is given', async () => {
    const user = userEvent.setup();
    renderCreatePage();

    await user.type(await screen.findByRole('textbox', { name: 'First name' }), 'Priya');
    await user.click(screen.getByRole('button', { name: 'Save contact' }));

    const messages = await screen.findAllByText(
      'Enter an email address or a phone number — a contact needs at least one of them'
    );
    expect(messages.length).toBeGreaterThan(0);
  });
});

describe('ContactFormPage — version conflict (CON-6 / AC-6)', () => {
  it('shows a reload prompt when the record changed since it was loaded', async () => {
    const user = userEvent.setup();
    signInAs();

    server.use(
      http.get('/api/customer/v1/contacts/contact-1', () => HttpResponse.json(existingContact)),
      http.patch('/api/customer/v1/contacts/contact-1', () =>
        HttpResponse.json(
          { message: 'Someone else changed this record', code: 'version_conflict' },
          { status: 409 }
        )
      )
    );

    renderWithProviders(
      <Routes>
        <Route path="/contacts/:id/edit" element={<ContactFormPage />} />
      </Routes>,
      { route: '/contacts/contact-1/edit' }
    );

    await screen.findByDisplayValue('Priya');
    await user.click(screen.getByRole('button', { name: 'Save contact' }));

    expect(await screen.findByText('Someone else changed this record')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload this record' })).toBeInTheDocument();
  });
});
