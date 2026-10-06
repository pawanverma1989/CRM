// Sales service types — mirrors the SalesApi DTOs in camelCase.

export type UserRole = 'admin' | 'manager' | 'sales_rep';
export type UserStatus = 'invited' | 'active' | 'deactivated';
export type DealStatus = 'open' | 'won' | 'lost';
export type StageType = 'open' | 'won' | 'lost';

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

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: UserDto;
}

export interface OwnerDto {
  id: string;
  displayName: string;
  isActive: boolean;
}

export interface PagedResult<T> {
  data: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface ApiErrorBody {
  message: string;
  code?: string;
  errors?: Record<string, string[]>;
}

// ── Pipeline ───────────────────────────────────────────────────────────────────

export interface PipelineStageDto {
  id: string;
  pipelineId: string;
  name: string;
  position: number;
  sortOrder: number;
  probability: number;
  stageType: string;
  isActive: boolean;
}

export interface PipelineDto {
  id: string;
  organizationId: string;
  name: string;
  description?: string | null;
  isDefault: boolean;
  staleDays?: number | null;
  createdAt: string;
  updatedAt: string;
  stages: PipelineStageDto[];
}

// ── Board ──────────────────────────────────────────────────────────────────────

export interface BoardDealDto {
  id: string;
  name: string;
  companyName?: string | null;
  amount: number;
  currency: string;
  ownerId?: string | null;
  ownerName?: string | null;
  expectedCloseDate?: string | null;
  daysInStage: number;
  isStale: boolean;
  isOverdue: boolean;
  probability: number;
  tags: string[];
}

export interface BoardColumnDto {
  stageId: string;
  stageName: string;
  sortOrder: number;
  dealCount: number;
  totalAmount: number;
  weightedForecast: number;
  deals: BoardDealDto[];
}

export interface BoardPipelineDto {
  id: string;
  name: string;
  stages: PipelineStageDto[];
}

export interface BoardResponse {
  pipeline: BoardPipelineDto;
  columns: BoardColumnDto[];
}

// ── Deal ───────────────────────────────────────────────────────────────────────

export interface DealContactDto {
  contactId: string;
  role?: string | null;
  isPrimary: boolean;
  contactName?: string | null;
}

export interface StageHistoryDto {
  id: string;
  fromStageId?: string | null;
  fromStageName?: string | null;
  toStageId: string;
  toStageName: string;
  amountAtChange?: number | null;
  changedBy?: string | null;
  changedAt: string;
}

export interface DealDto {
  id: string;
  organizationId: string;
  pipelineId: string;
  pipelineName?: string | null;
  stageId: string;
  stageName?: string | null;
  ownerId?: string | null;
  ownerName?: string | null;
  companyId?: string | null;
  companyName?: string | null;
  primaryContactId?: string | null;
  primaryContactName?: string | null;
  name: string;
  amount: number;
  currency: string;
  probability?: number | null;
  expectedCloseDate?: string | null;
  status: DealStatus;
  closedAt?: string | null;
  lossReasonId?: string | null;
  lossReasonName?: string | null;
  lossNotes?: string | null;
  sourceLeadId?: string | null;
  stageEnteredAt: string;
  lastActivityAt?: string | null;
  tags: string[];
  customFields: CustomFieldValues;
  version: number;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
  contacts?: DealContactDto[] | null;
  history?: StageHistoryDto[] | null;
  isStale: boolean;
  isOverdue: boolean;
}

export interface RecycleBinDealDto {
  id: string;
  name: string;
  ownerId?: string | null;
  ownerName?: string | null;
  companyId?: string | null;
  companyName?: string | null;
  pipelineId?: string | null;
  pipelineName?: string | null;
  stageId?: string | null;
  stageName?: string | null;
  amount: number;
  currency: string;
  status: string;
  deletedAt: string;
  deletedBy?: string | null;
  deletedByName?: string | null;
  purgeAfter: string;
  version: number;
}

// ── Lookups ────────────────────────────────────────────────────────────────────

export interface LossReasonDto {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
}

export interface CustomFieldDefinitionDto {
  id: string;
  entityType: string;
  fieldKey: string;
  label: string;
  fieldType: CustomFieldType;
  options?: string[] | null;
  isRequired: boolean;
  sortOrder: number;
  isActive: boolean;
}

// ── Results ────────────────────────────────────────────────────────────────────

export interface BulkReassignResult {
  reassigned: number;
  skipped: number;
}
