# events Specification (Delta)

## ADDED Requirements

### Requirement: Events Are Created Without a Fixed CPD Unit Count, Per Modality

The system SHALL allow an Admin to create an `Event` with `CpdUnitsOnsite` and `CpdUnitsOnline`
both unset (null), independently of each other. Registration, payment, attendance, and evaluation
SHALL all function normally for an event whose unit values are still null. An Admin SHALL be able to
set or correct either `CpdUnitsOnsite` or `CpdUnitsOnline` at any time, before or after the event's
`StartsAt`/`EndsAt`, without requiring the other to be set.

#### Scenario: A member registers for an event with units not yet set

- **WHEN** a member registers for an event whose `CpdUnitsOnsite` and `CpdUnitsOnline` are both null
- **THEN** the registration is created normally
- **AND** the event displays as "CPD units: TBD" for both modalities until an Admin sets a value

#### Scenario: One modality's units are set while the other remains TBD

- **WHEN** an Admin sets `CpdUnitsOnsite` on an event whose `CpdUnitsOnline` is still null
- **THEN** the update succeeds
- **AND** an Onsite registration on that event can earn credit once otherwise eligible, while an
  Online registration on the same event still shows "TBD" and earns no credit yet

#### Scenario: CPD units are set after the event has already happened

- **WHEN** an Admin sets a modality's CPD unit value on an event whose `EndsAt` is in the past
- **THEN** the update succeeds
- **AND** every existing `EventRegistration` for that modality that has reached
  `EvaluationSubmitted` immediately reflects the new credit value the next time it is read

### Requirement: Registration Fee Is Displayed Per Modality, Not System-Enforced

The system SHALL display, as the suggested amount for an `EventRegistration`, `Event.FeeOnsite` when
the registration's `Mode` is `Onsite`, or `Event.FeeOnline` when `Mode` is `Online`. The two values
SHALL be settable independently, and either MAY be 0 for a free modality. This is a *displayed
default*, not a validated charge: the member-submitted or admin-recorded payment amount is not
cross-checked against it, matching how a membership dues payment's amount is never system-validated
either — an Admin verifies the correct amount was paid as part of `Payment` verification, the same
manual check used for membership dues.

#### Scenario: Onsite and Online registrations on the same event show different suggested fees

- **WHEN** one member registers Onsite and another registers Online for the same event, and the
  event's `FeeOnsite` is 3000 while `FeeOnline` is 900
- **THEN** the Onsite member's registration form pre-fills 3000 and the Online member's pre-fills 900
- **AND** an Admin verifying either payment confirms the correct amount was paid manually, the same
  as for a membership dues payment

### Requirement: One Registration Per Member Per Event

The system SHALL allow at most one non-cancelled `EventRegistration` per member per event,
regardless of `Mode`.

#### Scenario: A member cannot register twice for the same event

- **WHEN** a member who already holds a non-cancelled registration for an event attempts to
  register for it again, even under a different `Mode`
- **THEN** the request is refused with a clear message

### Requirement: Registration Requires Payment Verification Before Attendance Can Be Recorded

An `EventRegistration` SHALL reach `PaymentVerified` — whether via a member-submitted proof payment
progressing through `Registered → PaymentSubmitted → PaymentVerified`, or via an admin-recorded cash
payment moving directly to `PaymentVerified` — before it can be marked `Attended`.

#### Scenario: Attendance cannot be recorded before payment is verified

- **WHEN** an attempt is made to record session attendance for a registration that has not reached
  `PaymentVerified`
- **THEN** the request is refused

#### Scenario: Verifying an event payment advances the registration

- **WHEN** an Admin verifies a `Payment` whose `Kind` is `EventRegistration`
- **THEN** the linked `EventRegistration.Status` moves to `PaymentVerified`

#### Scenario: A rejected event payment can be resubmitted

- **WHEN** an Admin rejects a `Payment` linked to an `EventRegistration`
- **THEN** the registration's status reflects `Rejected`
- **AND** the member can submit a new payment proof for the same registration

### Requirement: On-Site Cash Payments Can Be Recorded Directly By An Admin

The system SHALL allow an Admin to record a registration's payment as verified directly, with no
proof file, for on-site cash payers. This SHALL reach the same `PaymentVerified` state as the
member-submitted-proof path, and a registration SHALL have exactly one `Payment` regardless of which
path was used.

#### Scenario: An admin records a cash payment

- **WHEN** an Admin records a cash payment for a `Registered` registration that has not yet
  submitted payment proof
- **THEN** a `Payment` (`Kind = EventRegistration`) is created and immediately verified
- **AND** the registration's `Status` moves to `PaymentVerified`

#### Scenario: A cash payment cannot be recorded over an existing payment

- **WHEN** an Admin attempts to record a cash payment for a registration that already has a
  submitted or verified `Payment`
- **THEN** the request is refused

### Requirement: Attendance Is Recorded Per Session Via Admin Roster Reconciliation

The system SHALL track attendance per `EventSession`, not as a single whole-event flag. An Admin
SHALL be able to record, for a given `EventRegistration`, which of the event's `EventSession`s were
attended, reconciling against PSMPE's own attendance records after the event. Recording at least one
attended session for a registration SHALL move it to `Attended`.

#### Scenario: An admin reconciles roster attendance after the event

- **WHEN** an Admin records that a `PaymentVerified` registration attended one or more of the
  event's sessions
- **THEN** an `EventAttendance` row is created for each attended session, recording the Admin and
  timestamp
- **AND** the registration's `Status` moves to `Attended`

#### Scenario: A member attends only part of a multi-session event

- **WHEN** an Admin records attendance for 3 of an event's 6 sessions for a registration
- **THEN** exactly 3 `EventAttendance` rows exist for that registration
- **AND** the registration is `Attended`, with credit computed later from that 3-of-6 ratio

#### Scenario: Attendance cannot be recorded against a session from a different event

- **WHEN** an Admin attempts to record attendance against an `EventSession` that does not belong to
  the registration's `Event`
- **THEN** the request is refused

### Requirement: A Session's Venue Falls Back To The Event's Venue

`EventSession.Venue` SHALL be optional. When set, it SHALL override `Event.Venue` for display
purposes for that session alone. When null, the session SHALL display `Event.Venue`.

#### Scenario: A session overrides the event's default venue

- **WHEN** an `EventSession`'s `Venue` is set to a value different from its `Event.Venue`
- **THEN** that session displays its own `Venue`, not the event's

#### Scenario: A session with no venue override falls back to the event's venue

- **WHEN** an `EventSession`'s `Venue` is null
- **THEN** that session displays `Event.Venue`

### Requirement: Completion Requires an Attended Registration and a Submitted Evaluation

A member SHALL only be able to submit the post-event evaluation for a registration that is already
`Attended`. Submitting the evaluation SHALL move the registration to `EvaluationSubmitted`.

#### Scenario: Evaluation is blocked before attendance

- **WHEN** a member attempts to submit an evaluation for a registration that has not reached
  `Attended`
- **THEN** the request is refused

#### Scenario: A member completes an event

- **WHEN** an attended member submits the post-event evaluation form
- **THEN** their registration moves to `EvaluationSubmitted`

### Requirement: CPD Credit Is Computed, Not Stored, And Prorated By Attendance And Modality

The CPD credit earned for a registration SHALL be derived at read time as
`(sessions attended / total sessions in the event) × (Event.CpdUnitsOnsite if the registration's
Mode is Onsite, or Event.CpdUnitsOnline if Online)`, counted only when the registration's status is
`EvaluationSubmitted` and the applicable modality's unit value is not null. This value SHALL NOT be
persisted as a separate stored value on the registration.

#### Scenario: A member's CPD total reflects only completed, credited registrations

- **WHEN** a member views their CPD credit total
- **THEN** it sums the prorated credit only for their registrations that are `EvaluationSubmitted`
  and whose event has a non-null unit value for that registration's `Mode`
- **AND** registrations that are not yet `EvaluationSubmitted`, or whose event's applicable unit
  value is still null, contribute nothing to the total

#### Scenario: Partial attendance earns prorated credit

- **WHEN** an `EvaluationSubmitted` Onsite registration attended 3 of an event's 6 sessions, and the
  event's `CpdUnitsOnsite` is 8
- **THEN** the computed credit for that registration is 4 (8 × 3/6)

#### Scenario: Onsite and Online registrations on the same event earn different credit

- **WHEN** one member registered Onsite and another registered Online for the same event, both
  attended all sessions and are `EvaluationSubmitted`, and the event's `CpdUnitsOnsite` is 8 while
  `CpdUnitsOnline` is 4
- **THEN** the Onsite member's computed credit is 8 and the Online member's is 4

### Requirement: Certificate Available Only Once Credit Is Earned

The system SHALL generate a certificate PDF for a registration only when that registration would
count toward CPD credit (`EvaluationSubmitted` and the applicable modality's unit value is set). The
certificate SHALL list the sessions the registrant attended and the prorated CPD units earned. The
certificate SHALL be generated on request and SHALL NOT be pre-generated or cached from a prior
state.

#### Scenario: Certificate request before credit is earned is refused

- **WHEN** a certificate is requested for a registration that has not reached `EvaluationSubmitted`,
  or whose applicable modality's unit value is still null
- **THEN** the request is refused with a clear "not yet available" response, not a generated file

#### Scenario: Certificate reflects a corrected unit count

- **WHEN** a certificate is requested for a registration after its event's applicable unit value was
  corrected following an earlier value
- **THEN** the generated PDF shows the current, recomputed credit value, not any value that may have
  been correct previously

#### Scenario: Certificate lists only attended sessions

- **WHEN** a certificate is requested for a registration that attended 3 of an event's 6 sessions
- **THEN** the generated PDF lists exactly those 3 sessions, not the other 3
