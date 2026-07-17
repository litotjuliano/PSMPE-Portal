/** Faint blueprint-grid background over a primary gradient, for auth pages and empty states. */
export const BlueprintBg = () => {
  return (
    <div className="absolute inset-0 overflow-hidden bg-gradient-to-br from-primary-900 to-primary">
      <div
        aria-hidden="true"
        className="absolute inset-0"
        style={{
          backgroundImage:
            'linear-gradient(to right, rgba(255,255,255,0.1) 1px, transparent 1px), ' +
            'linear-gradient(to bottom, rgba(255,255,255,0.1) 1px, transparent 1px)',
          backgroundSize: '28px 28px',
        }}
      />
    </div>
  )
}
