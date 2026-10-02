import type {
  CustomFieldDefinitionDto,
  CustomFieldValue,
  CustomFieldValues,
  OwnerDto,
} from '../types';
import { MONEY_PATTERN, PHONE_PATTERN } from './validation';
import { formatDate, formatDateTime, formatMoney } from './utils';

/**
 * Client-side mirror of CF-3 ("values are validated against the field type and
 * options … invalid values are rejected with the field named in the error").
 * The service validates again before saving; this only gives faster feedback.
 */
export function validateCustomFields(
  definitions: CustomFieldDefinitionDto[],
  values: CustomFieldValues
): Record<string, string> {
  const errors: Record<string, string> = {};

  for (const def of definitions) {
    if (!def.isActive) continue;
    const raw = values[def.fieldKey];
    const isEmpty =
      raw === null
      || raw === undefined
      || (typeof raw === 'string' && raw.trim() === '')
      || (Array.isArray(raw) && raw.length === 0);

    if (isEmpty) {
      // CF-4: required applies to new saves; the form always asks for it, and
      // the server decides whether an existing record may stay incomplete.
      if (def.isRequired) errors[def.fieldKey] = `${def.label} is required`;
      continue;
    }

    const text = typeof raw === 'string' ? raw.trim() : '';

    switch (def.fieldType) {
      case 'number':
        if (!/^-?\d+(\.\d+)?$/.test(text)) errors[def.fieldKey] = `${def.label} must be a number`;
        break;
      case 'currency':
        if (!MONEY_PATTERN.test(text)) {
          errors[def.fieldKey] = `${def.label} must be an amount with at most 2 decimals`;
        }
        break;
      case 'date':
        if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) errors[def.fieldKey] = `${def.label} must be a date`;
        break;
      case 'datetime':
        if (Number.isNaN(new Date(text).getTime())) {
          errors[def.fieldKey] = `${def.label} must be a date and time`;
        }
        break;
      case 'email':
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(text)) {
          errors[def.fieldKey] = `${def.label} must be a valid email address`;
        }
        break;
      case 'phone':
        if (!PHONE_PATTERN.test(text)) {
          errors[def.fieldKey] = `${def.label} must be a valid phone number`;
        }
        break;
      case 'url':
        if (!/^https?:\/\/\S+$/i.test(text)) {
          errors[def.fieldKey] = `${def.label} must start with http:// or https://`;
        }
        break;
      case 'select':
        if (!(def.options ?? []).includes(text)) {
          errors[def.fieldKey] = `${def.label} must be one of its listed options`;
        }
        break;
      case 'multiselect': {
        const selected = Array.isArray(raw) ? raw : [];
        if (!selected.every((v) => (def.options ?? []).includes(v))) {
          errors[def.fieldKey] = `${def.label} may only contain its listed options`;
        }
        break;
      }
      case 'boolean':
        if (typeof raw !== 'boolean') errors[def.fieldKey] = `${def.label} must be yes or no`;
        break;
      case 'text':
      case 'textarea':
      case 'user':
        break;
    }
  }

  return errors;
}

/** The empty value a field of each type starts from. */
export function emptyCustomFieldValue(def: CustomFieldDefinitionDto): CustomFieldValue {
  if (def.fieldType === 'multiselect') return [];
  if (def.fieldType === 'boolean') return false;
  return '';
}

/** Seeds a form with a record's stored values plus blanks for the rest. */
export function initialCustomFieldValues(
  definitions: CustomFieldDefinitionDto[],
  stored: CustomFieldValues | undefined
): CustomFieldValues {
  const values: CustomFieldValues = {};
  for (const def of definitions) {
    const existing = stored?.[def.fieldKey];
    if (existing === undefined || existing === null) {
      values[def.fieldKey] = emptyCustomFieldValue(def);
    } else if (def.fieldType === 'multiselect') {
      values[def.fieldKey] = Array.isArray(existing) ? existing : [String(existing)];
    } else if (def.fieldType === 'boolean') {
      values[def.fieldKey] = Boolean(existing);
    } else {
      values[def.fieldKey] = typeof existing === 'string' ? existing : String(existing);
    }
  }
  return values;
}

/** Read-only rendering of a stored value for detail views and lists. */
export function formatCustomFieldValue(
  def: CustomFieldDefinitionDto,
  value: CustomFieldValue,
  owners: OwnerDto[] = []
): string {
  if (value === null || value === undefined || value === '') return '—';
  switch (def.fieldType) {
    case 'boolean':
      return value ? 'Yes' : 'No';
    case 'date':
      return formatDate(String(value));
    case 'datetime':
      return formatDateTime(String(value));
    case 'currency':
      return formatMoney(String(value));
    case 'multiselect':
      return Array.isArray(value) && value.length > 0 ? value.join(', ') : '—';
    case 'user': {
      const owner = owners.find((o) => o.id === String(value));
      return owner?.displayName ?? String(value);
    }
    default:
      return String(value);
  }
}

export const CUSTOM_FIELD_TYPE_LABELS: Record<CustomFieldDefinitionDto['fieldType'], string> = {
  text: 'Text',
  textarea: 'Long text',
  number: 'Number',
  currency: 'Currency',
  date: 'Date',
  datetime: 'Date & time',
  boolean: 'Yes / no',
  select: 'Single select',
  multiselect: 'Multi select',
  email: 'Email',
  phone: 'Phone',
  url: 'URL',
  user: 'User',
};

/** The two types whose options list is meaningful (CF-1). */
export function fieldTypeNeedsOptions(fieldType: CustomFieldDefinitionDto['fieldType']): boolean {
  return fieldType === 'select' || fieldType === 'multiselect';
}
