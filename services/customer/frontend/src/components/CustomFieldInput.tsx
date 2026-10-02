import { useId } from 'react';
import type { CustomFieldDefinitionDto, CustomFieldValue, OwnerDto } from '../types';
import { FormField } from './FormField';
import { SelectField } from './SelectField';
import { TextAreaField } from './TextAreaField';

interface CustomFieldInputProps {
  definition: CustomFieldDefinitionDto;
  value: CustomFieldValue;
  onChange: (value: CustomFieldValue) => void;
  owners: OwnerDto[];
  error?: string;
  disabled?: boolean;
}

/** One input per custom-field type (CF-2: the 13 supported types). */
export function CustomFieldInput({
  definition,
  value,
  onChange,
  owners,
  error,
  disabled,
}: CustomFieldInputProps) {
  const groupId = useId();
  const options = definition.options ?? [];
  const asText = typeof value === 'string' ? value : value === null ? '' : String(value);

  switch (definition.fieldType) {
    case 'textarea':
      return (
        <TextAreaField
          label={definition.label}
          name={`cf-${definition.fieldKey}`}
          value={asText}
          required={definition.isRequired}
          disabled={disabled}
          error={error}
          onChange={(e) => onChange(e.target.value)}
        />
      );

    case 'boolean':
      return (
        <div>
          <span className="block text-sm font-medium text-gray-700 mb-1">{definition.label}</span>
          <label className="inline-flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={value === true}
              disabled={disabled}
              onChange={(e) => onChange(e.target.checked)}
              className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Yes
          </label>
          {error && <p className="mt-1 text-xs text-red-600">{error}</p>}
        </div>
      );

    case 'select':
      return (
        <SelectField
          label={definition.label}
          name={`cf-${definition.fieldKey}`}
          value={asText}
          required={definition.isRequired}
          disabled={disabled}
          error={error}
          placeholder="Not set"
          options={options.map((o) => ({ value: o, label: o }))}
          onChange={(e) => onChange(e.target.value)}
        />
      );

    case 'multiselect': {
      const selected = Array.isArray(value) ? value : [];
      return (
        <fieldset aria-describedby={error ? `${groupId}-error` : undefined}>
          <legend className="block text-sm font-medium text-gray-700 mb-1">
            {definition.label}
          </legend>
          <div className="flex flex-wrap gap-x-4 gap-y-1.5 px-3 py-2 border border-gray-300 rounded-md bg-white">
            {options.length === 0 && (
              <span className="text-sm text-gray-500">No options defined</span>
            )}
            {options.map((option) => (
              <label key={option} className="inline-flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  checked={selected.includes(option)}
                  disabled={disabled}
                  onChange={(e) =>
                    onChange(
                      e.target.checked
                        ? [...selected, option]
                        : selected.filter((v) => v !== option)
                    )
                  }
                  className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
                {option}
              </label>
            ))}
          </div>
          {error && (
            <p id={`${groupId}-error`} className="mt-1 text-xs text-red-600">
              {error}
            </p>
          )}
        </fieldset>
      );
    }

    case 'user':
      return (
        <SelectField
          label={definition.label}
          name={`cf-${definition.fieldKey}`}
          value={asText}
          required={definition.isRequired}
          disabled={disabled}
          error={error}
          placeholder="Not set"
          options={owners.map((o) => ({
            value: o.id,
            label: o.isActive ? o.displayName : `${o.displayName} (deactivated)`,
          }))}
          onChange={(e) => onChange(e.target.value)}
        />
      );

    default: {
      const inputTypes: Record<string, string> = {
        text: 'text',
        number: 'number',
        currency: 'text',
        date: 'date',
        datetime: 'datetime-local',
        email: 'email',
        phone: 'tel',
        url: 'url',
      };
      const hints: Record<string, string | undefined> = {
        currency: 'Amount with at most 2 decimals',
        url: 'Must start with http:// or https://',
        phone: 'Without a country code the number is treated as Indian (+91)',
      };
      return (
        <FormField
          label={definition.label}
          name={`cf-${definition.fieldKey}`}
          type={inputTypes[definition.fieldType] ?? 'text'}
          inputMode={definition.fieldType === 'currency' ? 'decimal' : undefined}
          value={asText}
          required={definition.isRequired}
          disabled={disabled}
          error={error}
          hint={hints[definition.fieldType]}
          onChange={(e) => onChange(e.target.value)}
        />
      );
    }
  }
}
