# Change: Consolidate Members / Membership Approvals / RMP Verifications Into One Page

## Status

**Implemented.** Design approved 2026-08-08 through collaborative brainstorming, with three scope
questions answered directly by the user. Built and verified the same day: backend build clean, 302
tests passing, frontend typecheck/lint/build clean. See `tasks.md` for what remains unverified (the
live browser pass).

## Why

The Membership nav carried three entries that were all the **same query against the same endpoint**
— `GET /api/members`, which already accepted `pendingApprovalOnly` and `pendingPrcVerificationOnly`
— rendered by three near-identical page components (65 / 73 / 57 lines, each repeating the same
loading / pagination / refetch shape) and three separate table components (207 / 113 / 156 lines of
largely duplicated markup).

The duplication cost more than tidiness: **a new applicant with an unverified RMP number sits in
both queues at once**, so clearing one person meant visiting two pages. And `/members/:id` already
performed membership approval (its title even flips to "Approval") while having no RMP action at
all — the split wasn't consistently applied even before this change.

## What Changes

One `/members` page with three tabs, replacing three nav entries:

| Tab | URL | Filter | Extra columns | Row actions |
|---|---|---|---|---|
| All Members | `/members` | none | Status, Email | Edit, Delete |
| Pending Approval | `?queue=approval` | `pendingApprovalOnly` | Applied | Approve, View |
| RMP Verification | `?queue=rmp` | `pendingPrcVerificationOnly` | Current / Pending RMP No. | Approve, Reject, View ID |

Four files deleted (~490 lines), their behaviour folded into `MembersPage` and `MembersTable`.

## The Distinction This Change Preserves

The user's framing was that all three were "the same function, to approve." Two of them are
approvals, but of **different things**, and the merged page keeps both decisions intact:

| | Membership Approval | RMP Verification |
|---|---|---|
| Decides | Admits the applicant to PSMPE | Whether a licence no./date change is genuine |
| Frequency | Once per member, ever | Every time RMP details change |
| Input | Membership ID, mandatory | None to approve; reason required to reject |
| Reject | No reject path exists | Yes — reason + `PrcVerificationHistory` audit row |
| Effects | `ApprovedAt`, receipt JPEG, approval email | Pending→current, sets `PrcIdVerified` |

**A member can be waiting on both at once** and appears in both tabs; approving one leaves the other
pending. Only the navigation and the table code merged.

## Decisions

Each resolved by the user during planning:

- **One page total**, not "Members + a merged Approvals page". Nav drops from three entries to one.
- **Actions stay inline in the row**, exactly as both queues already worked — no forcing every
  decision through the member detail page.
- **Old routes redirect** rather than 404, so bookmarks and older in-app links still land somewhere
  useful.

## Design Notes

- **Backend was almost untouched.** Both filters and all three mutation endpoints already existed.
  The only change is a `"submittedat"` arm in `GetAllAsync`'s sort switch: the approval queue has
  always shown an "Applied" column it could not sort by, and oldest-first is the natural order for a
  work queue (and is now that tab's default).
- **The active tab lives in the URL** (`?queue=`), which is what makes the redirects and the
  notification bell link work at all.
- **Tab counts** come from two `pageSize: 1` calls reading `totalCount` — no dedicated counts
  endpoint. The topbar bell already queries both queues the same way, and a one-row response is
  cheap. A failed count blanks a badge and is deliberately swallowed; it must never surface as a
  page-level error over a list that loaded fine.
- **`MembersTable` takes a `view` prop** rather than being split again. Name/Membership No./Chapter
  are shared and sortable everywhere; the tail columns and the action column are per-view. Both the
  RMP reject `ConfirmationModal` (with `reasonRequired`) and its `FilePreviewModal` for the uploaded
  RMP ID moved across intact — that document is the evidence the decision rests on.
- **"Applied" now reads `submittedAt`, not `createdAt`.** The old approvals table showed
  `createdAt`, which is when the draft row first appeared mid-wizard, not when the member applied.
  The same one-line correction was made in `NotificationsList`, which had the same bug under a
  label that literally read "submitted".

## Deviation From The Plan

The plan said to repoint the topbar's membership-application items at `?queue=approval` "for
symmetry". They already linked to `/members/:id`, the individual application — which is *more*
useful than a queue, since a reviewer sees the whole submission before deciding. Left alone. Only
the RMP items, which pointed at the now-removed `/prc-verifications`, were repointed.

## Accepted Trade-off

Collapsing three nav entries to one loses the standing visual cue that work is waiting. The topbar
notification bell still surfaces both queues with counts and names, so the cue is relocated rather
than gone. A count badge on the Members nav item would need its own fetch inside `SideNav` and was
deliberately left out of scope.
