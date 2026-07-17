interface GaugeStatProps {
  label: string
  /** 0-100. */
  value: number
  /** Text shown under the gauge, e.g. "18 / 24 units" - defaults to "{value}%". */
  displayValue?: string
  helpText?: string
}

const RADIUS = 80
const CENTER = 100
const CIRCUMFERENCE = Math.PI * RADIUS

/** Semicircular pressure-gauge stat card - steel track, teal progress arc, steel needle. */
export const GaugeStat = ({ label, value, displayValue, helpText }: GaugeStatProps) => {
  const clamped = Math.min(100, Math.max(0, value))
  const progressLength = (clamped / 100) * CIRCUMFERENCE
  const needleAngle = (clamped / 100) * 180 - 90

  return (
    <div className="card">
      <div className="card-body p-5">
        <h6 className="font-semibold text-default-800 mb-2">{label}</h6>
        <svg viewBox="0 0 200 110" className="w-full h-auto" role="img" aria-label={`${label}: ${displayValue ?? `${clamped}%`}`}>
          <path
            d={`M ${CENTER - RADIUS} ${CENTER} A ${RADIUS} ${RADIUS} 0 0 1 ${CENTER + RADIUS} ${CENTER}`}
            fill="none"
            className="stroke-steel-200"
            strokeWidth={14}
            strokeLinecap="round"
          />
          <path
            d={`M ${CENTER - RADIUS} ${CENTER} A ${RADIUS} ${RADIUS} 0 0 1 ${CENTER + RADIUS} ${CENTER}`}
            fill="none"
            className="stroke-teal"
            strokeWidth={14}
            strokeLinecap="round"
            strokeDasharray={`${progressLength} ${CIRCUMFERENCE}`}
          />
          <line
            x1={CENTER}
            y1={CENTER}
            x2={CENTER}
            y2={CENTER - RADIUS + 16}
            className="stroke-steel-700 dark:stroke-steel-300"
            strokeWidth={3}
            strokeLinecap="round"
            transform={`rotate(${needleAngle} ${CENTER} ${CENTER})`}
          />
          <circle cx={CENTER} cy={CENTER} r={5} className="fill-steel-700 dark:fill-steel-300" />
        </svg>
        <p className="text-center text-lg font-semibold text-default-900 -mt-2">{displayValue ?? `${clamped}%`}</p>
        {helpText && <p className="text-center text-xs text-default-500 mt-1">{helpText}</p>}
      </div>
    </div>
  )
}
