import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { LuShieldCheck } from 'react-icons/lu'
import seal from '../../assets/images/psmpe-seal.png'
import heroPhoto from '../../assets/images/auth-hero.jpg'

export interface AuthSplitLayoutProps {
  /** Heading above the form, e.g. "Welcome to PSMPE Portal". */
  heading: string
  /** Subtext under the heading. */
  subheading: string
  children: ReactNode
}

/** Split-screen shell shared by Login/Register/Forgot Password/Reset Password. */
export function AuthSplitLayout({ heading, subheading, children }: AuthSplitLayoutProps) {
  return (
    // flex (not grid+h-full) so full-height stretch is unambiguous: a grid child's `h-full`
    // only resolves against an ancestor with a *definite* height, and `.auth-viewport` only sets
    // `min-height` - grid silently collapsed to content height, leaving `min-height`'s extra
    // space as dead space below, and starving the photo panel of the height it needed to cover.
    // Flex's default `align-items: stretch` fills the container's real height with no percentage
    // math involved.
    <section className="auth-viewport relative w-full flex flex-col xl:flex-row">
      {/* Below xl the photo is a full-bleed page background rather than a side panel, so every
          tablet (iPad Mini/Air/Pro portrait 768-1024, Surface Pro 912, ...) gets the same
          single-column treatment as phones. The split only kicks in at xl (1280px), which is
          where real laptops start. */}
      <div
        className="absolute inset-0 xl:hidden bg-center bg-cover"
        style={{ backgroundImage: `url(${heroPhoto})` }}
        aria-hidden="true"
      />
      {/* Scrim: token-based so it inverts correctly in dark mode. The card below carries the
          form's own contrast; this just stops the busy blueprint from fighting the page. */}
      <div className="absolute inset-0 xl:hidden bg-body-bg/75" aria-hidden="true" />

      {/* xl+ only: the same photo as the left half of the classic split. */}
      <div
        className="hidden xl:block xl:w-1/2 min-h-0 bg-center bg-cover"
        style={{ backgroundImage: `url(${heroPhoto})` }}
      />

      {/* `grow xl:grow-0`: in the stacked flex-col direction this column has no flex-grow by
          default, so its height collapses to its content and the section's 100dvh slack ends up
          *below* it - which is why it rendered top-aligned rather than centered. Growing it
          claims that space. At xl the direction flips to row, where grow would fight the explicit
          1/2 widths, hence grow-0.
          `my-auto` (not `justify-center`) does the centering: auto margins collapse to 0 once the
          content outgrows the column, so short screens scroll from the top instead of centering
          overflow into an unreachable negative offset. */}
      <div className="relative w-full xl:w-1/2 grow xl:grow-0 min-h-0 flex flex-col items-center px-6 pt-6 pb-[calc(1.5rem+env(safe-area-inset-bottom))]">
        {/* Card only while the photo is behind the form; at xl the form sits on plain background
            as before, so every card affordance (size bump, glow, ring) is reset there.
            max-w-3xl is 1.5x max-w-lg (768px vs 512px); desktop stays at lg so the inputs don't
            stretch across half a 1440px screen. xl:px-0/py-0 rather than xl:p-0 - Tailwind sorts
            `px`/`py` after the `p` shorthand, so a shorthand reset would lose to the unprefixed
            px-6/py-8. Glow is a colour-tinted large shadow plus a hairline ring, both on primary
            tokens so they track the navy theme. */}
        <div className="max-w-3xl xl:max-w-lg w-full my-auto flex flex-col items-center text-center card px-6 py-8 md:px-10 md:py-12 shadow-2xl shadow-primary/25 ring-1 ring-primary-100 xl:bg-transparent xl:shadow-none xl:ring-0 xl:px-0 xl:py-0">
          <Link to="/" className="flex flex-col items-center gap-2 mb-4 md:mb-6">
            {/* object-contain guards the aspect ratio if the source asset is ever re-exported
                non-square; sizes step 104 -> 128 -> 152px across phone/tablet/desktop. */}
            <img
              src={seal}
              alt="PSMPE seal"
              className="size-26 md:size-32 xl:size-38 object-contain animate-auth-logo-float"
            />
            <span className="text-lg font-extrabold text-default-900 tracking-wide">PSMPE</span>
          </Link>

          <div className="mb-5 md:mb-7 animate-auth-header-rise">
            <h3 className="text-[clamp(1.25rem,2vw,1.75rem)] font-semibold text-default-900 mb-2 md:mb-3">
              {heading}
            </h3>
            <p className="text-[clamp(0.875rem,1.2vw,1rem)] font-medium text-default-500">{subheading}</p>
          </div>

          {children}

          <p className="mt-5 md:mt-7 flex items-center justify-center gap-1.5 text-[11.5px] font-medium text-default-500">
            <LuShieldCheck className="size-3.5" aria-hidden="true" />
            Encrypted &amp; verified member login
          </p>
        </div>
      </div>
    </section>
  )
}
