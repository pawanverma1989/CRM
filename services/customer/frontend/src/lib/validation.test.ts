import { describe, expect, it } from 'vitest';
import { contactFormSchema, EMAIL_OR_PHONE_MESSAGE, normalizeDomain } from './validation';

const baseContact = {
  firstName: 'Priya',
  lastName: '',
  email: '',
  phone: '',
  mobile: '',
  jobTitle: '',
  companyId: '',
  sourceId: '',
  ownerId: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  postalCode: '',
  country: 'IN',
};

// CON-1 / AC-2: a contact needs a first name and either an email or a phone.
describe('contactFormSchema — email-or-phone rule', () => {
  it('rejects a contact with neither an email nor a phone, naming both fields', () => {
    const result = contactFormSchema.safeParse(baseContact);
    expect(result.success).toBe(false);
    if (result.success) return;
    const messages = result.error.issues.map((i) => i.message);
    const paths = result.error.issues.map((i) => i.path.join('.'));
    expect(paths).toContain('email');
    expect(paths).toContain('phone');
    expect(messages).toContain(EMAIL_OR_PHONE_MESSAGE);
  });

  it('accepts a contact with an email and no phone', () => {
    const result = contactFormSchema.safeParse({ ...baseContact, email: 'priya@acme.in' });
    expect(result.success).toBe(true);
  });

  it('accepts a contact with a phone and no email', () => {
    const result = contactFormSchema.safeParse({ ...baseContact, phone: '+919876543210' });
    expect(result.success).toBe(true);
  });

  it('rejects a blank first name even when an email is present', () => {
    const result = contactFormSchema.safeParse({ ...baseContact, firstName: '  ', email: 'priya@acme.in' });
    expect(result.success).toBe(false);
  });
});

// COM-4 / AC-4: "https://www.Acme.com/" is stored as "acme.com".
describe('normalizeDomain', () => {
  it('strips the scheme, www, path and trailing dot, and lower-cases the rest', () => {
    expect(normalizeDomain('https://www.Acme.com/')).toBe('acme.com');
    expect(normalizeDomain('acme.com')).toBe('acme.com');
    expect(normalizeDomain('HTTP://Acme.COM/path?x=1')).toBe('acme.com');
  });
});
