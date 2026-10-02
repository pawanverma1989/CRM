import { z } from 'zod';

// Field rules of Customer Service — Requirements §4, kept in step with the
// CHECK constraints in services/customer/db/migrations/V2__*.sql so the form
// rejects what the database would reject.
//
// Every schema here keeps empty optional fields as the empty string so the
// form's input and output types are identical (no zod transform surprises in
// react-hook-form). `emptyToNull` in api/payload.ts turns "" into null, which
// is how §4 says a blank text field is stored: "empty strings are stored as no
// value".

export const MAX_TAGS_PER_RECORD = 20;
export const MAX_TAG_LENGTH = 40;
export const MAX_BULK_RECORDS = 500;
export const MAX_ACTIVE_CUSTOM_FIELDS = 50;
export const RECYCLE_BIN_DAYS = 30;
export const DEFAULT_PAGE_SIZE = 50;
export const MAX_PAGE_SIZE = 200;
export const PAGE_SIZE_OPTIONS = [25, 50, 100, 200] as const;

/** chk_companies_gstin */
export const GSTIN_PATTERN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$/;
/** chk_companies_country / chk_contacts_country */
export const COUNTRY_CODE_PATTERN = /^[A-Z]{2}$/;
/** Deliberately permissive: the server produces the authoritative E.164 value. */
export const PHONE_PATTERN = /^[+]?[0-9][0-9\s()./-]{4,29}$/;
export const DOMAIN_PATTERN = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/;
/** NUMERIC(16,2): at most 14 integer digits and 2 decimals, never negative. */
export const MONEY_PATTERN = /^\d{1,14}(\.\d{1,2})?$/;

const blank = (v: string) => v.trim() === '';

function optionalText(max: number, label: string) {
  return z.string().trim().max(max, `${label} must be ${max} characters or fewer`);
}

function optionalMatch(pattern: RegExp, message: string, max?: number) {
  return z
    .string()
    .trim()
    .refine((v) => blank(v) || ((max === undefined || v.length <= max) && pattern.test(v)), {
      message,
    });
}

/**
 * COM-4: "https://www.Acme.com/" is stored as "acme.com". Mirrors the
 * normalisation the server applies, so the form can show the user what will
 * actually be saved before they submit.
 */
export function normalizeDomain(input: string): string {
  return input
    .trim()
    .toLowerCase()
    .replace(/^[a-z][a-z0-9+.-]*:\/\//, '')
    .replace(/^www\./, '')
    .replace(/[/?#].*$/, '')
    .replace(/:\d+$/, '')
    .replace(/\.$/, '');
}

/** TAG-2: lower-cased, trimmed, at most 40 characters. */
export function normalizeTag(input: string): string {
  return input.trim().toLowerCase().slice(0, MAX_TAG_LENGTH);
}

export function validateTags(tags: string[]): string | null {
  if (tags.length > MAX_TAGS_PER_RECORD) {
    return `At most ${MAX_TAGS_PER_RECORD} tags per record`;
  }
  if (tags.some((t) => t.length === 0 || t.length > MAX_TAG_LENGTH)) {
    return `Each tag must be between 1 and ${MAX_TAG_LENGTH} characters`;
  }
  if (tags.some((t) => t !== normalizeTag(t))) {
    return 'Tags must be lower-case and trimmed';
  }
  return null;
}

const addressShape = {
  addressLine1: optionalText(200, 'Address line 1'),
  addressLine2: optionalText(200, 'Address line 2'),
  city: optionalText(100, 'City'),
  state: optionalText(100, 'State'),
  postalCode: optionalText(20, 'Postal code'),
  country: optionalMatch(COUNTRY_CODE_PATTERN, 'Use a two-letter ISO country code, e.g. IN'),
};

/** §4.1/§4.2: a 6-digit postal code when the country is India. */
function checkIndianPostalCode(
  value: { country: string; postalCode: string },
  ctx: z.RefinementCtx
) {
  if (value.country.trim().toUpperCase() === 'IN' && !blank(value.postalCode)) {
    if (!/^\d{6}$/.test(value.postalCode.trim())) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['postalCode'],
        message: 'An Indian postal code is 6 digits',
      });
    }
  }
}

const companyShape = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(200, 'Name must be 200 characters or fewer'),
  domain: z
    .string()
    .trim()
    .refine((v) => blank(v) || normalizeDomain(v).length <= 253, {
      message: 'Domain must be 253 characters or fewer',
    })
    .refine((v) => blank(v) || DOMAIN_PATTERN.test(normalizeDomain(v)), {
      message: 'Enter a valid domain, e.g. acme.com',
    }),
  industryId: z.string(),
  employeeCount: optionalMatch(
    /^\d{1,9}$/,
    'Employee count must be a whole number of 0 or more'
  ),
  annualRevenue: optionalMatch(
    MONEY_PATTERN,
    'Enter an amount of 0 or more with at most 2 decimals'
  ),
  currency: optionalMatch(/^[A-Z]{3}$/, 'Use a three-letter currency code, e.g. INR'),
  phone: optionalMatch(PHONE_PATTERN, 'Enter a valid phone number of up to 30 characters', 30),
  website: optionalMatch(/^https?:\/\/\S+$/i, 'Website must start with http:// or https://'),
  gstin: optionalMatch(GSTIN_PATTERN, 'Enter a valid 15-character GSTIN, e.g. 29ABCDE1234F1Z5'),
  ownerId: z.string(),
  ...addressShape,
});

export const companyFormSchema = companyShape.superRefine(checkIndianPostalCode);
export type CompanyFormValues = z.infer<typeof companyShape>;

const contactShape = z.object({
  firstName: z
    .string()
    .trim()
    .min(1, 'First name is required')
    .max(100, 'First name must be 100 characters or fewer'),
  lastName: optionalText(100, 'Last name'),
  email: z
    .string()
    .trim()
    .max(254, 'Email must be 254 characters or fewer')
    .refine((v) => blank(v) || z.string().email().safeParse(v.trim()).success, {
      message: 'Enter a valid email address',
    }),
  phone: optionalMatch(PHONE_PATTERN, 'Enter a valid phone number of up to 30 characters', 30),
  mobile: optionalMatch(PHONE_PATTERN, 'Enter a valid mobile number of up to 30 characters', 30),
  jobTitle: optionalText(150, 'Job title'),
  companyId: z.string(),
  sourceId: z.string(),
  ownerId: z.string(),
  ...addressShape,
});

/** CON-1 / AC-2: the error names both missing fields. */
export const EMAIL_OR_PHONE_MESSAGE =
  'Enter an email address or a phone number — a contact needs at least one of them';

export const contactFormSchema = contactShape.superRefine((value, ctx) => {
  checkIndianPostalCode(value, ctx);
  if (blank(value.email) && blank(value.phone)) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['email'], message: EMAIL_OR_PHONE_MESSAGE });
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['phone'], message: EMAIL_OR_PHONE_MESSAGE });
  }
});

export type ContactFormValues = z.infer<typeof contactShape>;

export const customFieldDefinitionSchema = z.object({
  entityType: z.enum(['contact', 'company']),
  label: z
    .string()
    .trim()
    .min(1, 'Label is required')
    .max(100, 'Label must be 100 characters or fewer'),
  // custom_field_definitions.field_key CHECK (field_key ~ '^[a-z][a-z0-9_]*$')
  fieldKey: z
    .string()
    .trim()
    .min(1, 'Key is required')
    .max(60, 'Key must be 60 characters or fewer')
    .regex(
      /^[a-z][a-z0-9_]*$/,
      'Start with a lower-case letter; use lower-case letters, digits and underscores'
    ),
  fieldType: z.enum([
    'text',
    'textarea',
    'number',
    'currency',
    'date',
    'datetime',
    'boolean',
    'select',
    'multiselect',
    'email',
    'phone',
    'url',
    'user',
  ]),
  isRequired: z.boolean(),
  sortOrder: z.string().trim().regex(/^\d{1,4}$/, 'Display order must be a whole number of 0 or more'),
  /** One option per line; required for select and multi-select (CF-1). */
  optionsText: z.string(),
});

export type CustomFieldDefinitionFormValues = z.infer<typeof customFieldDefinitionSchema>;

export const picklistItemSchema = z.object({
  listType: z.enum(['company_industry', 'contact_source']),
  value: z
    .string()
    .trim()
    .min(1, 'Value is required')
    .max(100, 'Value must be 100 characters or fewer'),
  sortOrder: z.coerce.number().int().min(0, 'Display order must be 0 or more'),
});

export type PicklistItemFormValues = z.input<typeof picklistItemSchema>;
