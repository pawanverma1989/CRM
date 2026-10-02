import type { CustomFieldDefinitionDto, OwnerDto } from '../types';
import { SelectField } from './SelectField';

interface CustomFieldFilterInputProps {
  definition: CustomFieldDefinitionDto;
  value: string;
  onChange: (value: string) => void;
  owners: OwnerDto[];
}

/** LST-2: "filtered by … any custom field" — one simple input per active definition. */
export function CustomFieldFilterInput({
  definition,
  value,
  onChange,
  owners,
}: CustomFieldFilterInputProps) {
  const inputId = `cf-filter-${definition.fieldKey}`;

  if (definition.fieldType === 'select' || definition.fieldType === 'multiselect') {
    return (
      <SelectField
        label={definition.label}
        id={inputId}
        value={value}
        placeholder="Any"
        options={(definition.options ?? []).map((o) => ({ value: o, label: o }))}
        onChange={(e) => onChange(e.target.value)}
      />
    );
  }

  if (definition.fieldType === 'boolean') {
    return (
      <SelectField
        label={definition.label}
        id={inputId}
        value={value}
        placeholder="Any"
        options={[
          { value: 'true', label: 'Yes' },
          { value: 'false', label: 'No' },
        ]}
        onChange={(e) => onChange(e.target.value)}
      />
    );
  }

  if (definition.fieldType === 'user') {
    return (
      <SelectField
        label={definition.label}
        id={inputId}
        value={value}
        placeholder="Any"
        options={owners.map((o) => ({ value: o.id, label: o.displayName }))}
        onChange={(e) => onChange(e.target.value)}
      />
    );
  }

  const inputType =
    definition.fieldType === 'date' ? 'date' : definition.fieldType === 'datetime' ? 'datetime-local' : 'text';

  return (
    <div>
      <label htmlFor={inputId} className="block text-sm font-medium text-gray-700 mb-1">
        {definition.label}
      </label>
      <input
        id={inputId}
        type={inputType}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
      />
    </div>
  );
}
