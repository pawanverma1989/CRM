import apiClient from './client';
import type {
  CompanyDetailDto,
  CompanyDto,
  CustomFieldValues,
  PagedResponse,
} from '../types';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';
import type { SortDirection } from './contacts';

export type CompanySortField = 'name' | 'created_at' | 'updated_at' | 'owner';

export interface CompanyListParams {
  q?: string;
  ownerId?: string;
  unowned?: boolean;
  tags?: string[];
  city?: string;
  state?: string;
  country?: string;
  industryId?: string;
  createdFrom?: string;
  createdTo?: string;
  updatedFrom?: string;
  updatedTo?: string;
  customFields?: Record<string, string>;
  sort?: CompanySortField;
  direction?: SortDirection;
  page?: number;
  pageSize?: number;
}

export function buildCompanyListQuery(params: CompanyListParams): URLSearchParams {
  const search = new URLSearchParams();
  if (params.q) search.set('q', params.q);
  if (params.ownerId) search.set('ownerId', params.ownerId);
  if (params.unowned) search.set('unowned', 'true');
  if (params.tags?.length) search.set('tags', params.tags.join(','));
  if (params.city) search.set('city', params.city);
  if (params.state) search.set('state', params.state);
  if (params.country) search.set('country', params.country);
  if (params.industryId) search.set('industryId', params.industryId);
  if (params.createdFrom) search.set('createdFrom', params.createdFrom);
  if (params.createdTo) search.set('createdTo', params.createdTo);
  if (params.updatedFrom) search.set('updatedFrom', params.updatedFrom);
  if (params.updatedTo) search.set('updatedTo', params.updatedTo);
  for (const [key, value] of Object.entries(params.customFields ?? {})) {
    if (value !== '') search.set(`cf.${key}`, value);
  }
  search.set('sort', params.sort ?? 'updated_at');
  search.set('direction', params.direction ?? 'desc');
  search.set('page', String(params.page ?? 1));
  search.set('pageSize', String(params.pageSize ?? DEFAULT_PAGE_SIZE));
  return search;
}

export async function getCompanies(params: CompanyListParams): Promise<PagedResponse<CompanyDto>> {
  const { data } = await apiClient.get<PagedResponse<CompanyDto>>(
    `/companies?${buildCompanyListQuery(params)}`
  );
  return data;
}

/** COM-2: the company and the contacts linked to it. */
export async function getCompany(id: string): Promise<CompanyDetailDto> {
  const { data } = await apiClient.get<CompanyDetailDto>(`/companies/${id}`);
  return data;
}

export interface CompanyWritePayload {
  name: string;
  /** Sent as typed; the server normalises it to lower case without www (COM-4). */
  domain: string | null;
  industryId: string | null;
  employeeCount: number | null;
  /** Decimal string, so NUMERIC(16,2) never passes through a float. */
  annualRevenue: string | null;
  currency: string | null;
  phone: string | null;
  website: string | null;
  gstin: string | null;
  ownerId: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  country: string | null;
  tags: string[];
  customFields: CustomFieldValues;
}

export async function createCompany(payload: CompanyWritePayload): Promise<CompanyDto> {
  const { data } = await apiClient.post<CompanyDto>('/companies', payload);
  return data;
}

/** COM-3: `version` is required; a stale one gets 409 Conflict. */
export async function updateCompany(
  id: string,
  payload: Partial<CompanyWritePayload> & { version: number }
): Promise<CompanyDto> {
  const { data } = await apiClient.patch<CompanyDto>(`/companies/${id}`, payload);
  return data;
}

/** DEL-1 / COM-5: soft delete; contacts are kept but unlinked. */
export async function deleteCompany(id: string): Promise<void> {
  await apiClient.delete(`/companies/${id}`);
}
