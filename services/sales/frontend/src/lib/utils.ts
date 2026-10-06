import { format, formatDistanceToNow } from 'date-fns';
import { clsx, type ClassValue } from 'clsx';
import type { ApiErrorBody } from '../types';

export function cn(...inputs: ClassValue[]) {
  return clsx(inputs);
}

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

/** Display label for a deal status value. */
export function statusLabel(status: string): string {
  const labels: Record<string, string> = {
    open: 'Open',
    won: 'Won',
    lost: 'Lost',
  };
  return labels[status] ?? status;
}

/** Tailwind classes for a deal status badge. */
export function statusColor(status: string): string {
  const colors: Record<string, string> = {
    open: 'bg-blue-100 text-blue-800',
    won: 'bg-green-100 text-green-800',
    lost: 'bg-red-100 text-red-800',
  };
  return colors[status] ?? 'bg-gray-100 text-gray-800';
}

/**
 * Money is NUMERIC(16,2) + a currency code and arrives as a decimal string or
 * number. Only formatting turns it into a rendered value.
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

export function isVersionConflict(error: unknown): boolean {
  return getApiErrorStatus(error) === 409 && getApiErrorCode(error) === 'version_conflict';
}

export function getFieldErrors(error: unknown): Record<string, string> {
  const errors = errorBody(error)?.errors;
  if (!errors) return {};
  return Object.fromEntries(
    Object.entries(errors).map(([field, messages]) => [field, messages.join(' ')])
  );
}
