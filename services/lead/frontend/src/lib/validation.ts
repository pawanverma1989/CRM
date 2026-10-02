import { z } from 'zod';

export const MAX_TAGS_PER_RECORD = 20;
export const MAX_TAG_LENGTH = 40;
export const MAX_BULK_RECORDS = 500;
export const MAX_ACTIVE_CUSTOM_FIELDS = 50;
export const RECYCLE_BIN_DAYS = 30;
export const DEFAULT_PAGE_SIZE = 50;
export const MAX_PAGE_SIZE = 200;
export const PAGE_SIZE_OPTIONS = [25, 50, 100, 200] as const;

export const PHONE_PATTERN = /^[+]?[0-9][0-9\s()./-]{4,29}$/;

const blank = (v: string) => v.trim() === '';

export function normalizeTag(input: string): string {
  return input.trim().toLowerCase().slice(0, MAX_TAG_LENGTH);
}

export function validateTags(tags: string[]): string | null {
  if (tags.length > MAX_TAGS_PER_RECORD) return `At most ${MAX_TAGS_PER_RECORD} tags per record`;
  if (tags.some((t) => t.length === 0 || t.length > MAX_TAG_LENGTH)) return `Each tag must be between 1 and ${MAX_TAG_LENGTH} characters`;
  if (tags.some((t) => t !== normalizeTag(t))) return 'Tags must be lower-case and trimmed';
  return null;
}

// CAP-1: a lead needs at least an email or a phone
export const EMAIL_OR_PHONE_MESSAGE = 'Enter an email address or a phone number — a lead needs at least one of them';

export const leadFormSchema = z.object({
  firstName: z.string().trim().max(100, 'First name must be 100 characters or fewer'),
  lastName: z.string().trim().max(100, 'Last name must be 100 characters or fewer'),
  email: z.string().trim().max(254, 'Email must be 254 characters or fewer').refine((v) => blank(v) || z.string().email().safeParse(v.trim()).success, { message: 'Enter a valid email address' }),
  phone: z.string().trim().refine((v) => blank(v) || PHONE_PATTERN.test(v.trim()), { message: 'Enter a valid phone number of up to 30 characters' }),
  companyName: z.string().trim().max(200, 'Company name must be 200 characters or fewer'),
  jobTitle: z.string().trim().max(150, 'Job title must be 150 characters or fewer'),
  leadSourceId: z.string(),
  ownerId: z.string(),
  utmSource: z.string().trim().max(100, 'UTM source must be 100 characters or fewer'),
  utmMedium: z.string().trim().max(100, 'UTM medium must be 100 characters or fewer'),
  utmCampaign: z.string().trim().max(100, 'UTM campaign must be 100 characters or fewer'),
  notes: z.string().trim().max(2000, 'Notes must be 2,000 characters or fewer'),
}).superRefine((value, ctx) => {
  if (blank(value.email) && blank(value.phone)) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['email'], message: EMAIL_OR_PHONE_MESSAGE });
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['phone'], message: EMAIL_OR_PHONE_MESSAGE });
  }
});

export type LeadFormValues = z.infer<typeof leadFormSchema>;

export const customFieldDefinitionSchema = z.object({
  label: z.string().trim().min(1, 'Label is required').max(100, 'Label must be 100 characters or fewer'),
  fieldKey: z.string().trim().min(1, 'Key is required').max(60, 'Key must be 60 characters or fewer').regex(/^[a-z][a-z0-9_]*$/, 'Start with a lower-case letter; use lower-case letters, digits and underscores'),
  fieldType: z.enum(['text','textarea','number','currency','date','datetime','boolean','select','multiselect','email','phone','url','user']),
  isRequired: z.boolean(),
  sortOrder: z.string().trim().regex(/^\d{1,4}$/, 'Display order must be a whole number of 0 or more'),
  optionsText: z.string(),
});

export type CustomFieldDefinitionFormValues = z.infer<typeof customFieldDefinitionSchema>;
