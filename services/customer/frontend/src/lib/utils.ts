import { format, formatDistanceToNow } from 'date-fns';
import { clsx, type ClassValue } from 'clsx';
import type { ApiErrorBody, ConflictRef } from '../types';

export function cn(...inputs: ClassValue[]) {
  return clsx(inputs);
}

// Timestamps arrive as UTC TIMESTAMPTZ; `new Date` parses the offset and
// date-fns renders in the viewer's local zone — display only, never stored.
export function formatDate(dateStr: string): string {
  try {
    return format(new Date(dateStr), 'MMM d, yyyy');
  } catch {
    return dateStr;
  }
}

export function formatDateTime(dateStr: string): string {
  try {
    return format(new Date(dateStr), 'MMM d, yyyy HH:mm');
  } catch {
    return dateStr;
  }
}

export function formatRelative(dateStr: string): string {
  try {
    return formatDistanceToNow(new Date(dateStr), { addSuffix: true });
  } catch {
    return dateStr;
  }
}

export function getInitials(firstName: string, lastName?: string | null): string {
  const first = firstName.charAt(0).toUpperCase();
  const last = lastName ? lastName.charAt(0).toUpperCase() : '';
  return first + last;
}

export function getRoleLabel(role: string): string {
  const labels: Record<string, string> = {
    admin: 'Admin',
    manager: 'Manager',
    sales_rep: 'Sales Rep',
  };
  return labels[role] ?? role;
}

export function contactName(c: { firstName: string; lastName?: string | null }): string {
  return [c.firstName, c.lastName].filter(Boolean).join(' ');
}

/**
 * Money is NUMERIC(16,2) + a currency code and arrives as a decimal string so
 * no float rounding happens on the way through. Only formatting turns it into a
 * number, and the result is never fed back into arithmetic.
 */
export function formatMoney(amount: string | number | null | undefined, currency = 'INR'): string {
  if (amount === null || amount === undefined || amount === '') return '—';
  const value = typeof amount === 'string' ? Number(amount) : amount;
  if (!Number.isFinite(value)) return String(amount);
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
  } catch {
    return `${currency} ${value.toFixed(2)}`;
  }
}

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  try {
    return new Intl.NumberFormat().format(value);
  } catch {
    return String(value);
  }
}

/** '—' for anything the server left empty, so detail views never show "null". */
export function orDash(value: string | number | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  return String(value);
}

function errorBody(error: unknown): ApiErrorBody | undefined {
  const e = error as { response?: { data?: ApiErrorBody } } | undefined;
  const body = e?.response?.data;
  if (body && typeof body === 'object' && typeof body.message === 'string') return body;
  return undefined;
}

export function getApiErrorMessage(error: unknown): string {
  if (!error) return 'An unexpected error occurred';
  if (typeof error === 'string') return error;
  const body = errorBody(error);
  if (body?.message) return body.message;
  const e = error as { response?: { data?: { title?: string } }; message?: string };
  return e.response?.data?.title ?? e.message ?? 'An unexpected error occurred';
}

export function getApiErrorStatus(error: unknown): number | undefined {
  return (error as { response?: { status?: number } } | undefined)?.response?.status;
}

export function getApiErrorCode(error: unknown): string | undefined {
  return errorBody(error)?.code;
}

/** The existing record an error names — DUP-1, DEL-2 / AC-3, AC-14. */
export function getConflictRef(error: unknown): ConflictRef | undefined {
  const conflict = errorBody(error)?.conflict;
  if (conflict && typeof conflict.id === 'string' && typeof conflict.label === 'string') {
    return conflict;
  }
  return undefined;
}

/** True when a 409 means "the record moved on since you loaded it" (CON-6/AC-6). */
export function isVersionConflict(error: unknown): boolean {
  return getApiErrorStatus(error) === 409 && getApiErrorCode(error) === 'version_conflict';
}

/** Per-field messages a server-side validation failure named (CF-3, AC-10). */
export function getFieldErrors(error: unknown): Record<string, string> {
  const errors = errorBody(error)?.errors;
  if (!errors) return {};
  return Object.fromEntries(
    Object.entries(errors).map(([field, messages]) => [field, messages.join(' ')])
  );
}

export function detailPath(type: 'contact' | 'company', id: string): string {
  return type === 'contact' ? `/contacts/${id}` : `/companies/${id}`;
}
