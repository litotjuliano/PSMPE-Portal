# Change: Portal Access as a Recurring, Per-Payment Add-On

## Status

**Designed, not yet implemented.** Raised by the user during a payment-strategy discussion: the
association hadn't decided whether membership dues and portal (this software) access should be one
combined payment or two separate ones. Refined through extensive collaborative brainstorming across
several follow-up scenarios — mid-year mode switching, promotional pricing, paper-form intake,
admin/member data-entry mistakes, and payment reporting. Plan approved 2026-08-30; implementation not
yet started. See `tasks.md` in this folder for the breakdown.

## Why

The association's board hadn't settled on a pricing policy: some members should perhaps pay one
combined total (membership + portal), others perhaps just the base membership fee. Building an
admin-wide toggle between two global modes was the first design explored, but it created a real gap —
members mid-cycle under one policy wouldn't pick up the other policy until their own next renewal, up
to ~12 months away, with no clean way to backfill the difference.

The simpler resolution: never make it a global setting. The mandatory membership fee (plus shipping,
plus annual dues at renewal) is always required, exactly as today. Portal access is always an
optional, per-payment tick-box — available every time a payment is made, at registration and at every
renewal. Ticking it produces the "combined" total; leaving it unticked produces the "separate" total.
Both of the board's original scenarios are supported simultaneously, decided per member per payment,
with no mode to switch and therefore no gap.

## Decisions

Each resolved by the user during brainstorming:

- **No global `PaymentMode` setting.** Portal Fee is always an optional add-on, every payment,
  decided by whoever is paying (the member, or the admin recording a walk-in's actual payment) — not
  a policy switch an admin flips for everyone.
- **Portal access is recurring, not permanent.** It reflects only the member's most recently
  *verified* payment. A renewal that omits the add-on revokes it at that point.
- **Access is system-derived, never admin-overridable.** Verifying a payment is the one action that
  both approves/renews membership and grants or restricts portal access, driven entirely by what that
  payment recorded as paid. There is no separate "grant portal access" step for an admin to use
  independently of what was actually paid.
- **Fee edits, and promotions, are prospective only.** Changing a price — or a temporary promotional
  price for a specific date range — never touches an already-created `Payment`. Amounts and the
  portal-inclusion flag are captured once, at submission time.
- **Promotional pricing is a first-class, self-expiring mechanism**, not a manual "remember to change
  it back" toggle, for cases like a one-day discounted membership fee during an outreach event.
- **Paper-form/offline intake uses the existing admin-walk-in path**, one form at a time — bulk import
  is explicitly out of scope for now (confirmed acceptable at current volumes, ~100 forms).
- **Amount vs. portal-checkbox mismatches get a soft warning, not a hard block** — consistent with
  this codebase's existing stance that payment amounts are never hard-validated against configured
  fees. On the admin walk-in form specifically, the amount typed auto-drives the checkbox by default
  (typing 1,500 leaves it unticked, 2,600 ticks it), since that's the one context where the amount is
  a known, already-collected fact rather than a declared intent.
- **A simple payment report** breaks down membership-only vs. combined counts/totals and portal
  revenue collected, for a given date range.

## What Changes

- **`Payment` entity** gains `IncludesPortalAccess` (bool) and `PortalFeeAmount` (decimal) — whether
  this payment included the add-on, and the exact portal-fee amount in effect when it was created
  (captured separately from `Amount` so reporting stays accurate after later fee/promo changes).
- **`Member` entity** gains `HasPortalAccess` (bool), written exclusively by `PaymentVerification.Apply`.
- **New `FeePromotion` entity** — `FeeKey`, `PromoAmount`, `StartDate`/`EndDate` — resolved on every
  fee read as an override of the regular `SystemConfig` amount when today falls in range. No
  background job; it starts and stops by itself. Overlapping promos for the same fee are rejected.
- **`PortalFee` joins `MembershipFeeKeys`** as a fourth admin-editable fee (seeded at ₱900).
- **`PaymentVerification.Apply`** also sets `Member.HasPortalAccess = payment.IncludesPortalAccess`.
- **`MembershipAccessMiddleware`** gains a second, independent check (`PORTAL_ACCESS_REQUIRED`,
  same allowlist as the existing expired-member check) alongside the existing `MEMBERSHIP_EXPIRED`
  check.
- **New admin endpoints**: `POST/GET/DELETE /api/payments/fees/promotions`,
  `GET /api/payments/reports/summary?startDate=&endDate=`.
- **Frontend**: portal opt-in checkbox on the registration wizard and `RenewalPaymentCard`; a
  Promotions panel and a payments summary panel on the admin Payments/Fees screens; a mismatch
  caution badge in the admin payments queue; `ExpiredMembershipGate`/`AppMenu` extended to restrict
  on either expiry or lack of portal access.
- **`MembershipFeesDto.RegistrationTotal`** is replaced by four explicit totals
  (`RegistrationTotalWithoutPortal`/`WithPortal`, `RenewalTotalWithoutPortal`/`WithPortal`) — a
  deliberate breaking rename so no consumer silently keeps using a total that ignores the add-on.
- **New `PaymentKind.PortalAccessOnly`** — a standalone mid-cycle purchase of the add-on alone, for a
  member who's current on dues but never opted into portal access. `SubmitPaymentRequest` gains a
  `PortalAccessOnly` flag; `PaymentVerification.Apply` grants `HasPortalAccess` without moving
  `RenewalDueDate` the way a real `Renewal` would. Surfaced on `RenewalPaymentCard` as a compact "Add
  Portal Access" card, shown only outside the normal renewal window (where the full form's checkbox
  already covers it).

## Design Notes

- **Registration-time opt-in plumbing.** `POST /api/members/me/submit` currently takes no body; it
  gains an optional `includePortalAccess` field threaded into `EnsureRegistrationPaymentAsync`,
  rather than requiring the wizard to call `POST /api/payments/me` separately before submitting.
- **Deactivated members are excluded from the new portal check**, mirroring the existing exclusion in
  `ComputeIsExpired`/`ComputeIsInGracePeriod` — deactivation is its own axis and shouldn't be newly
  affected by this feature.
- **Middleware ordering**: a member who is both expired and lacking portal access sees
  `MEMBERSHIP_EXPIRED` — the existing check runs first, unchanged.
- **The admin-walk-in path is also the paper-form intake path.** An admin re-keys an offline
  registration through the existing member-creation screen and records the payment/portal choice at
  approval time — no applicant login required. This directly serves applicants without reliable
  internet or comfort with the portal.
- **Mismatch guarding is a UI safety net, not a backend rule** — no validation is added server-side;
  `Amount` stays exactly as advisory as it is today.
- **Rollout**: implemented on its own feature branch (other work is in progress on `develop`), merged
  to `develop`, then `staging` (triggers `deploy-staging.yml`) for testing, then `main` (triggers
  `deploy-production.yml`) once verified.

## Not Built

- **Bulk import for paper-form registrants.** One-at-a-time through the existing admin screen only;
  revisit if offline intake keeps happening at scale.
- **Line-item drill-down for the payment report.** Aggregate figures only for now.
- **Amount validation against configured fees**, as before this change — under/overpayment remains
  the admin's judgment call, informed by the new soft mismatch warning rather than a hard rule.
