import { forwardRef, type SelectHTMLAttributes } from 'react';
import { cn } from '../lib/utils';

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  options: SelectOption[];
  error?: string;
  hint?: string;
  /** Shown as a selectable empty choice, e.g. "No company". */
  placeholder?: string;
  required?: boolean;
}

export const SelectField = forwardRef<HTMLSelectElement, SelectFieldProps>(
  ({ label, options, error, hint, placeholder, className, id, name, required, ...props }, ref) => {
    const fieldId = id ?? name ?? label.toLowerCase().replace(/\s+/g, '-');
    const hintId = `${fieldId}-hint`;
    const errorId = `${fieldId}-error`;

    return (
      <div>
        <label htmlFor={fieldId} className="block text-sm font-medium text-gray-700 mb-1">
          {label}
          {required && (
            <span className="text-red-600 ml-0.5" aria-hidden="true">
              *
            </span>
          )}
        </label>
        <select
          id={fieldId}
          name={name}
          ref={ref}
          required={required}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : hint ? hintId : undefined}
          className={cn(
            'block w-full px-3 py-2 border rounded-md shadow-sm text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 bg-white disabled:bg-gray-50 disabled:text-gray-500',
            error ? 'border-red-300 text-red-900' : 'border-gray-300 text-gray-900',
            className
          )}
          {...props}
        >
          {placeholder !== undefined && <option value="">{placeholder}</option>}
          {options.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
        {hint && !error && (
          <p id={hintId} className="mt-1 text-xs text-gray-500">
            {hint}
          </p>
        )}
        {error && (
          <p id={errorId} className="mt-1 text-xs text-red-600">
            {error}
          </p>
        )}
      </div>
    );
  }
);

SelectField.displayName = 'SelectField';
