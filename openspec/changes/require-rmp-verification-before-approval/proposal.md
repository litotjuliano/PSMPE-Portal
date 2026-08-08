# Change: Require RMP Verification Before Membership Approval

## Status

**Implemented.** Reported by the user from the live UI, analysed against live data, designed through
collaborative brainstorming with three scope questions answered directly. Built and verified the
same day: backend build clean, 307 tests passing, frontend typecheck/lint/build clean. See
`tasks.md` for what remains unverified (the live browser pass).

## Why

A member could be **admitted to PSMPE while their RMP licence had never been checked**.
`ApproveAsync` validated `ApprovedAt`, and the Membership ID's length and uniqueness — nothing else.

Spotted because Test 123 appeared in both the Pending Approval and RMP Verification tabs and could
be approved from the first while still pending in the second. Checking the live data showed it was
already routine, not hypothetical:

| Member | Approved | RMP licence | Verified |
|---|---|---|---|
| Juan Dela Cruz `000003` | yes | 0123456 | **yes** |
| Juan Cruz `000007` | yes | 0123456 | **no** |
| Maria Fernandez `000004` | yes | (pending 0789012) | **no** |
| Pedro Bautista `000005` | yes | — | **no** |
| Juan Dela Cruz `000008` | yes | — | **no** |

**4 of 5 approved members held unverified licences.** The licence is the eligibility criterion for
membership, and approving assigns a control number, generates a receipt and emails it — expensive to
unwind if the licence later fails to check out.

## The trap a naive fix would have hit

`GetAllAsync`'s verification filter only matches members with a licence number, current or pending.
`CreateAsync` did not require one and marked admin-created profiles submitted immediately. A member
with no licence therefore never entered the verification queue — under a plain "must be verified to
approve" rule they would have been **permanently unapprovable**: unable to be verified, unable to be
approved. Rows `000005` and `000008` are exactly that shape.

## What Changes

1. **`ApproveAsync` refuses an unverified member.** Placed deliberately *after* the existing
   already-approved short-circuit, so the 4 pre-existing approvals stand and repeat calls on them
   still succeed. The rule is forward-only.
2. **`prcLicenseNo` becomes required at admin create**, matching what `SubmitMyProfileAsync` already
   demands of self-service applicants. Closes the deadlock at source.
3. **`ApproveApplicationWizard` replaces `ApproveMembershipModal`** — a three-step flow so the gate
   doesn't become a round trip between two tabs.

## Decisions

Each resolved by the user during planning:

- **Server gate *and* wizard**, not one or the other. The server is the guarantee; the wizard is what
  keeps it from being friction.
- **The 4 existing approved-but-unverified members are left alone.** Nothing already issued is
  retracted.
- **Require a licence at admin create** rather than explaining the deadlock away with a different
  error message.

## Design Notes

- **The wizard skips step 1 for an already-verified member**, landing on Membership ID. Without that,
  every approval would write a redundant `PrcVerificationHistory` row asserting a verification that
  had already happened.
- **Rejecting at step 1 ends the flow.** The application stays unapproved and the member stays in the
  RMP Verification queue with the reason attached — rejection is not a path to approval.
- **Nothing was rebuilt.** The wizard composes `PipeStepper`, `FilePreviewModal`,
  `ConfirmationModal` (`reasonRequired`), and the Membership ID field lifted whole from the deleted
  modal — including its debounced availability check, its out-of-order response guard, and the
  behaviour of staying open on a duplicate (now returning to step 2, where the field is).
- **`MemberFormPage` now keeps the loaded `Member`** alongside its form state. The wizard needs the
  real record — licence, pending licence, verification flag — none of which `MemberFormState` holds.
- **The standalone RMP Verification tab stays.** It still serves the other case: an already-approved
  member changing their licence, where no membership decision is involved.

## Test Impact

The gate broke **13 existing tests** on the first run, which is the gate proving it works. A
`VerifyRmpAsync` helper now precedes each approval so the tests read as intent rather than ceremony.

One test needed more than a helper. `GetAll_WithPendingPrcVerificationOnly_...` deliberately built a
submitted member with no licence via the admin Create path — a path that now correctly refuses it.
That shape is no longer constructible through any API, but **legacy rows still exist**, so the filter
must still exclude them; the test now seeds the row straight into the context, with a comment saying
why it bypasses the API.

Four new cases: approving unverified is rejected and leaves no trace, verify→approve succeeds,
create without a licence fails without persisting (three blank variants).

## Not in this change

The info-icon tooltips on the two queue tabs, planned earlier and still pending. Worth doing after
this, since the wizard changes what the Pending Approval tab's explanation should say.
