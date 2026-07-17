import { LuCheck } from 'react-icons/lu'

interface PipeStepperProps {
  steps: string[]
  step: number
  /** Furthest step reached this session - steps up to and including this are "completed" and
   *  clickable; steps beyond it are "future" and disabled. */
  maxStepReached: number
  onStepClick: (step: number) => void
  /** True while a stepper-click (or other save) is in flight - disables navigation. */
  navigating: boolean
}

/** Waterworks-themed wizard stepper - a pipe run connecting each step's "fitting" node. */
export const PipeStepper = ({ steps, step, maxStepReached, onStepClick, navigating }: PipeStepperProps) => {
  return (
    <div className="flex items-start mb-6">
      {steps.map((label, i) => {
        const isCurrent = i === step
        const isCompleted = i <= maxStepReached && !isCurrent
        const isFuture = i > maxStepReached
        const connectorFilled = i < maxStepReached

        return (
          <div key={label} className="flex items-start flex-1 last:flex-none">
            {/* Button and connector each center within an identical fixed-height (size-9) box,
                so their midlines always line up regardless of label height/wrapping - avoids a
                brittle margin-top guess. */}
            <div className="flex flex-col items-center gap-2">
              <div className="size-9 flex items-center justify-center">
                <button
                  type="button"
                  onClick={() => onStepClick(i)}
                  disabled={isCurrent || isFuture || navigating}
                  aria-current={isCurrent ? 'step' : undefined}
                  aria-label={`Step ${i + 1}: ${label}${isCompleted ? ' (completed - click to edit)' : isFuture ? ' (not yet reached)' : ''}`}
                  className={`shrink-0 size-9 rounded-full flex items-center justify-center text-sm font-semibold border-2 transition ${
                    isCurrent
                      ? 'bg-primary border-primary-800 text-white ring-[5px] ring-primary-100 cursor-default'
                      : isCompleted
                        ? 'bg-copper border-copper-dark text-white cursor-pointer hover:scale-[1.08] disabled:cursor-not-allowed disabled:opacity-60'
                        : 'bg-white border-steel-200 text-steel-400 cursor-not-allowed'
                  }`}
                >
                  {isCompleted ? <LuCheck className="size-4" /> : i + 1}
                </button>
              </div>
              <span
                className={`text-xs whitespace-nowrap text-center ${
                  isCurrent
                    ? 'text-primary dark:text-primary-100 font-medium'
                    : isCompleted
                      ? 'text-copper-dark dark:text-copper-light'
                      : 'text-steel-400'
                }`}
              >
                {label}
              </span>
            </div>

            {i < steps.length - 1 && (
              <div className="relative flex-1 size-9 flex items-center mx-1 shrink-0">
                <div className="relative w-full h-2.5 rounded-full overflow-hidden">
                  {connectorFilled ? (
                    <>
                      <div className="absolute inset-0 rounded-full bg-gradient-to-b from-[#D08A45] via-copper to-copper-dark shadow-[inset_0_1px_0_rgba(255,255,255,0.5),inset_0_-1px_0_rgba(0,0,0,0.35)]" />
                      {/* Joint fitting - a small darker collar where this pipe segment begins. */}
                      <div className="absolute left-0 inset-y-0 w-1.5 bg-copper-dark rounded-l-full" />
                    </>
                  ) : (
                    <div className="absolute inset-0 rounded-full bg-steel-200 shadow-[inset_0_1px_2px_rgba(0,0,0,0.15)]" />
                  )}
                </div>
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
