import type { CustomFieldValues } from '../types';

/**
 * §4: "Every text field is trimmed; empty strings are stored as no value."
 * The forms keep optional fields as '' so inputs stay controlled; this turns
 * them into an explicit null, which also lets PATCH clear a field.
 */
export function emptyToNull(value: string | undefined | null): string | null {
  if (value === undefined || value === null) return null;
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

/** A whole-number field, e.g. employee count. */
export function emptyToIntOrNull(value: string | undefined | null): number | null {
  const text = emptyToNull(value);
  if (text === null) return null;
  const parsed = Number.parseInt(text, 10);
  return Number.isNaN(parsed) ? null : parsed;
}

/**
 * Money stays a decimal string all the way to the server (NUMERIC(16,2)), so no
 * binary-float rounding can creep into the stored amount.
 */
export function emptyToDecimalStringOrNull(value: string | undefined | null): string | null {
  return emptyToNull(value);
}

/** Drops keys the caller left undefined so a PATCH only sends real changes. */
export function pruneUndefined<T extends Record<string, unknown>>(payload: T): Partial<T> {
  return Object.fromEntries(
    Object.entries(payload).filter(([, v]) => v !== undefined)
  ) as Partial<T>;
}

/** Removes values that are empty for their type, so '' never reaches JSONB. */
export function cleanCustomFields(values: CustomFieldValues): CustomFieldValues {
  const cleaned: CustomFieldValues = {};
  for (const [key, value] of Object.entries(values)) {
    if (value === null || value === undefined) continue;
    if (typeof value === 'string' && value.trim() === '') continue;
    if (Array.isArray(value) && value.length === 0) continue;
    cleaned[key] = typeof value === 'string' ? value.trim() : value;
  }
  return cleaned;
}
