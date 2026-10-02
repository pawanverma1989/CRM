import apiClient from './client';
import type { ContactDto, CustomFieldValues, PagedResponse } from '../types';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

/** LST-3: the four sortable columns. */
export type ContactSortField = 'name' | 'created_at' | 'updated_at' | 'owner';
export type SortDirection = 'asc' | 'desc';

/** LST-2 plus the LST-4 quick search. */
export interface ContactListParams {
  q?: string;
  ownerId?: string;
  /** Matches records with no owner — those are visible to everyone (§2). */
  unowned?: boolean;
  tags?: string[];
  companyId?: string;
  city?: string;
  state?: string;
  country?: string;
  sourceId?: string;
  createdFrom?: string;
  createdTo?: string;
  updatedFrom?: string;
  updatedTo?: string;
  /** Custom-field filters, keyed by field_key (LST-2). */
  customFields?: Record<string, string>;
  sort?: ContactSortField;
  direction?: SortDirection;
  page?: number;
  pageSize?: number;
}

export function buildContactListQuery(params: ContactListParams): URLSearchParams {
  const search = new URLSearchParams();
  if (params.q) search.set('q', params.q);
  if (params.ownerId) search.set('ownerId', params.ownerId);
  if (params.unowned) search.set('unowned', 'true');
  if (params.tags?.length) search.set('tags', params.tags.join(','));
  if (params.companyId) search.set('companyId', params.companyId);
  if (params.city) search.set('city', params.city);
  if (params.state) search.set('state', params.state);
  if (params.country) search.set('country', params.country);
  if (params.sourceId) search.set('sourceId', params.sourceId);
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

export async function getContacts(params: ContactListParams): Promise<PagedResponse<ContactDto>> {
  const { data } = await apiClient.get<PagedResponse<ContactDto>>(
    `/contacts?${buildContactListQuery(params)}`
  );
  return data;
}

export async function getContact(id: string): Promise<ContactDto> {
  const { data } = await apiClient.get<ContactDto>(`/contacts/${id}`);
  return data;
}

/** §4.2 standard fields; nulls clear a value. */
export interface ContactWritePayload {
  firstName: string;
  lastName: string | null;
  email: string | null;
  phone: string | null;
  mobile: string | null;
  jobTitle: string | null;
  companyId: string | null;
  sourceId: string | null;
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

export async function createContact(payload: ContactWritePayload): Promise<ContactDto> {
  const { data } = await apiClient.post<ContactDto>('/contacts', payload);
  return data;
}

/** CON-6 / AC-6: `version` makes the update optimistic; a stale one gets 409. */
export async function updateContact(
  id: string,
  payload: Partial<ContactWritePayload> & { version: number }
): Promise<ContactDto> {
  const { data } = await apiClient.patch<ContactDto>(`/contacts/${id}`, payload);
  return data;
}

/** DEL-1: a soft delete into the recycle bin. */
export async function deleteContact(id: string): Promise<void> {
  await apiClient.delete(`/contacts/${id}`);
}
