interface SearchableSelectProps {
  id: string
  value: string
  onChange: (next: string) => void
  options: string[]
  placeholder?: string
  required?: boolean
  disabled?: boolean
}

/**
 * A text box that suggests from a list as you type, via a native `<datalist>`.
 *
 * Used for the address cascade, where a plain `<select>` meant scrolling ~90 provinces or several
 * dozen cities with no way to jump. `<datalist>` gets type-to-filter, keyboard navigation and
 * screen-reader support for free, with no dependency and no custom focus/outside-click handling to
 * get wrong.
 *
 * The deliberate trade-off is that it does not constrain input: anything typed is kept, matching
 * the free-text-allowed decision already made for these fields (see openspecs/members.md, "Address
 * entry"). A value outside the list is a real address the dataset is missing, not an error - the
 * server stores plain strings either way.
 */
export const SearchableSelect = ({
  id,
  value,
  onChange,
  options,
  placeholder,
  required = false,
  disabled = false,
}: SearchableSelectProps) => {
  const listId = `${id}-options`

  return (
    <>
      <input
        id={id}
        className="form-input"
        // Plain text, and no explicit role - `input[list]` already carries combobox semantics, and
        // restating the role without the aria-expanded state it implies reads worse to a screen
        // reader than the native pairing does.
        type="text"
        list={listId}
        autoComplete="off"
        required={required}
        disabled={disabled}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <datalist id={listId}>
        {options.map((option) => (
          <option key={option} value={option} />
        ))}
      </datalist>
    </>
  )
}
