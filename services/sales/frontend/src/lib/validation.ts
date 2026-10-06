import { z } from 'zod';

export const MAX_TAGS_PER_RECORD = 20;
export const MAX_TAG_LENGTH = 40;
export const MAX_BULK_RECORDS = 500;
export const MAX_ACTIVE_CUSTOM_FIELDS = 50;
export const RECYCLE_BIN_DAYS = 30;
export const DEFAULT_PAGE_SIZE = 50;
export const MAX_PAGE_SIZE = 200;
export const PAGE_SIZE_OPTIONS = [25, 50, 100, 200] as const;

export const MONEY_PATTERN = /^\d{1,14}(\.\d{1,2})?$/;

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

export const dealFormSchema = z.object({
  name: z.string().trim().min(1, 'Deal name is required').max(200, 'Name must be 200 characters or fewer'),
  pipelineId: z.string().min(1, 'Pipeline is required'),
  stageId: z.string().min(1, 'Stage is required'),
  amount: z.string().trim().refine(
    (v) => blank(v) || MONEY_PATTERN.test(v),
    { message: 'Enter a valid amount (e.g. 50000 or 50000.00)' }
  ),
  currency: z.string().length(3, 'Currency must be a 3-letter code'),
  ownerId: z.string(),
  companyId: z.string(),
  primaryContactId: z.string(),
  probability: z.string().trim().refine(
    (v) => blank(v) || (/^\d+$/.test(v) && Number(v) >= 0 && Number(v) <= 100),
    { message: 'Probability must be a whole number 0–100' }
  ),
  expectedCloseDate: z.string().trim().refine(
    (v) => blank(v) || /^\d{4}-\d{2}-\d{2}$/.test(v),
    { message: 'Enter a date as YYYY-MM-DD' }
  ),
});

export type DealFormValues = z.infer<typeof dealFormSchema>;

export const customFieldDefinitionSchema = z.object({
  label: z.string().trim().min(1, 'Label is required').max(100, 'Label must be 100 characters or fewer'),
  fieldKey: z.string().trim().min(1, 'Key is required').max(60, 'Key must be 60 characters or fewer').regex(/^[a-z][a-z0-9_]*$/, 'Start with a lower-case letter; use lower-case letters, digits and underscores'),
  fieldType: z.enum(['text','textarea','number','currency','date','datetime','boolean','select','multiselect','email','phone','url','user']),
  isRequired: z.boolean(),
  sortOrder: z.string().trim().regex(/^\d{1,4}$/, 'Display order must be a whole number of 0 or more'),
  optionsText: z.string(),
});

export type CustomFieldDefinitionFormValues = z.infer<typeof customFieldDefinitionSchema>;
