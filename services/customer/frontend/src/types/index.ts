// Shapes returned by the Customer service API (/api/customer/v1) and the two
// Identity endpoints this app calls. Mirrors the Identity frontend's camelCase
// DTO convention; column names come from
// services/customer/db/migrations/V1__initial_schema.sql and V2__*.sql.

export type UserRole = 'admin' | 'manager' | 'sales_rep';
export type UserStatus = 'invited' | 'active' | 'deactivated';

export type EntityType = 'contact' | 'company';

/** custom_field_definitions.field_type CHECK — the 13 types of CF-2. */
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

/** picklists.list_type CHECK. */
export type PicklistType = 'company_industry' | 'contact_source';

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

export interface CompanyDto {
  id: string;
  organizationId: string;
  ownerId?: string | null;
  ownerName?: string | null;
  name: string;
  domain?: string | null;
  industryId?: string | null;
  industryValue?: string | null;
  employeeCount?: number | null;
  /** NUMERIC(16,2) carried as a decimal string so no float rounding occurs. */
  annualRevenue?: string | null;
  currency?: string | null;
  phone?: string | null;
  website?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  gstin?: string | null;
  tags: string[];
  customFields: CustomFieldValues;
  mergedIntoId?: string | null;
  contactCount?: number;
  version: number;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
}

/** GET /companies/{id} — the company plus its contacts (COM-2). */
export interface CompanyDetailDto extends CompanyDto {
  contacts: ContactDto[];
}

export interface ContactDto {
  id: string;
  organizationId: string;
  companyId?: string | null;
  companyName?: string | null;
  ownerId?: string | null;
  ownerName?: string | null;
  firstName: string;
  lastName?: string | null;
  email?: string | null;
  phone?: string | null;
  /** E.164 form produced by the server (CON-4). */
  phoneNormalized?: string | null;
  mobile?: string | null;
  jobTitle?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  sourceId?: string | null;
  sourceValue?: string | null;
  sourceLeadId?: string | null;
  tags: string[];
  customFields: CustomFieldValues;
  mergedIntoId?: string | null;
  version: number;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
}

export interface CustomFieldDefinitionDto {
  id: string;
  organizationId: string;
  entityType: EntityType;
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

export interface PicklistItemDto {
  id: string;
  listType: PicklistType;
  value: string;
  sortOrder: number;
  isActive: boolean;
}

/** Why a candidate looks like a duplicate (DUP-1..DUP-3). */
export type DuplicateReason = 'email' | 'domain' | 'phone' | 'similar_name';

export interface DuplicateMatchDto {
  id: string;
  entityType: EntityType;
  /** Display label of the existing record, e.g. "Priya Sharma". */
  label: string;
  reason: DuplicateReason;
  matchedValue?: string | null;
  /** 'hard' breaks DUP-1 and cannot be saved; 'soft' is only a warning. */
  severity: 'hard' | 'soft';
}

export interface DuplicateCheckResponse {
  matches: DuplicateMatchDto[];
}

/** A record sitting in the recycle bin (DEL-2). */
export interface DeletedRecordDto {
  id: string;
  recordType: EntityType;
  label: string;
  secondaryLabel?: string | null;
  ownerName?: string | null;
  deletedAt: string;
  purgeAt: string;
}

/** An entry of the reassignment queue (OWN-3). */
export interface ReassignmentQueueItemDto {
  id: string;
  recordType: EntityType;
  recordId: string;
  label: string;
  previousOwnerId: string;
  previousOwnerName?: string | null;
  queuedAt: string;
}

export interface PagedResponse<T> {
  data: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface BulkResultDto {
  succeeded: number;
  failed: Array<{ id: string; message: string }>;
}

/** The record an error points at, e.g. the contact already using an email. */
export interface ConflictRef {
  id: string;
  type: EntityType;
  label: string;
}

export interface ApiErrorBody {
  message: string;
  /** 'version_conflict' | 'duplicate' | … — distinguishes the two 409 cases. */
  code?: string;
  errors?: Record<string, string[]>;
  conflict?: ConflictRef;
}
