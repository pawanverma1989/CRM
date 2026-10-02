// Shapes returned by the Lead service API (/api/lead/v1) and the two
// Identity endpoints this app calls. Mirrors the Identity frontend's camelCase
// DTO convention; column names come from
// services/lead/db/migrations/V1__initial_schema.sql.

export type UserRole = 'admin' | 'manager' | 'sales_rep';
export type UserStatus = 'invited' | 'active' | 'deactivated';

export type LeadStatus = 'new' | 'contacted' | 'qualified' | 'disqualified' | 'converted';

/** custom_field_definitions.field_type CHECK — the 13 types. */
export type CustomFieldType =
  | 'text'
  | 'textarea'
  | 'number'
  | 'currency'
  | 'date'
  | 'datetime'
  | 'boolean'
  | 'select'
  | 'multiselect'
  | 'email'
  | 'phone'
  | 'url'
  | 'user';

/** A single custom_fields JSONB value. */
export type CustomFieldValue = string | number | boolean | string[] | null;
export type CustomFieldValues = Record<string, CustomFieldValue>;

export interface UserDto {
  id: string;
  organizationId: string;
  teamId?: string;
  email: string;
  firstName: string;
  lastName?: string;
  phone?: string;
  role: UserRole;
  status: UserStatus;
  lastLoginAt?: string;
  createdAt: string;
  updatedAt: string;
}

/** Identity POST /auth/refresh response. */
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: UserDto;
}

/** GET /owners — assignable owners, from the local user_refs copy. */
export interface OwnerDto {
  id: string;
  displayName: string;
  isActive: boolean;
}

export interface LeadDto {
  id: string;
  organizationId: string;
  ownerId?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  email?: string | null;
  phone?: string | null;
  companyName?: string | null;
  jobTitle?: string | null;
  status: LeadStatus;
  disqualifyReason?: string | null;
  disqualifyReasonId?: string | null;
  leadSourceId?: string | null;
  leadSourceName?: string | null;
  webFormId?: string | null;
  utmSource?: string | null;
  utmMedium?: string | null;
  utmCampaign?: string | null;
  notes?: string | null;
  tags: string[];
  customFields: CustomFieldValues;
  convertedAt?: string | null;
  convertedContactId?: string | null;
  convertedCompanyId?: string | null;
  convertedDealId?: string | null;
  version: number;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface RecycleBinLeadDto {
  id: string;
  firstName?: string | null;
  lastName?: string | null;
  email?: string | null;
  phone?: string | null;
  ownerId?: string | null;
  deletedAt: string;
  purgeAfter: string;
  version: number;
}

export interface LeadSourceDto {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
}

export interface DisqualifyReasonDto {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
}

export interface WebFormDto {
  id: string;
  name: string;
  publicKey: string;
  fields: unknown;
  requiredFields: unknown;
  leadSourceId?: string | null;
  defaultOwnerId?: string | null;
  consentText?: string | null;
  successMessage?: string | null;
  redirectUrl?: string | null;
  captchaEnabled: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface WebFormEmbedDto {
  id: string;
  publicKey: string;
  submitUrl: string;
  embedSnippet: string;
}

export interface ConversionDto {
  id: string;
  leadId: string;
  status: string;
  companyId?: string | null;
  contactId?: string | null;
  dealId?: string | null;
  lastError?: string | null;
  attempts: number;
  createdAt: string;
  updatedAt: string;
}

export interface CustomFieldDefinitionDto {
  id: string;
  organizationId: string;
  entityType: 'lead';
  fieldKey: string;
  label: string;
  fieldType: CustomFieldType;
  options?: string[] | null;
  isRequired: boolean;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DuplicateLeadDto {
  id: string;
  firstName?: string | null;
  lastName?: string | null;
  email?: string | null;
  phone?: string | null;
  status: LeadStatus;
  reason: string;
}

export interface DuplicateCheckResult {
  hasDuplicates: boolean;
  matches: DuplicateLeadDto[];
}

export interface PagedResult<T> {
  data: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface BulkAssignResult {
  assigned: number;
  skipped: number;
}

/** The record an error points at. */
export interface ConflictRef {
  id: string;
  type: string;
  label: string;
}

export interface ApiErrorBody {
  message: string;
  /** 'version_conflict' | 'duplicate' | … — distinguishes the two 409 cases. */
  code?: string;
  errors?: Record<string, string[]>;
  conflict?: ConflictRef;
}
