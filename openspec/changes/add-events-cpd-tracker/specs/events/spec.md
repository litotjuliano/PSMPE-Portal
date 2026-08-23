# events Specification (Delta)

## ADDED Requirements

### Requirement: Events Are Created Without a Fixed CPD Unit Count

The system SHALL allow an Admin to create an `Event` with `CpdUnits` unset (null). Registration,
payment, attendance, and evaluation SHALL all function normally for an event whose `CpdUnits` is
still null. An Admin SHALL be able to set or correct `CpdUnits` at any time, before or after the
event's `StartsAt`/`EndsAt`.

#### Scenario: A member registers for an event with units not yet set

- **WHEN** a member registers for an event whose `CpdUnits` is null
- **THEN** the registration is created normally
- **AND** the event displays as "CPD units: TBD" until an Admin sets a value

#### Scenario: CPD units are set after the event has already happened

- **WHEN** an Admin sets `CpdUnits` on an event whose `EndsAt` is in the past
- **THEN** the update succeeds
- **AND** every existing `EventRegistration` for that event that has reached `EvaluationSubmitted`
  immediately reflects the new credit value the next time it is read

### Requirement: One Registration Per Member Per Event

The system SHALL allow at most one non-cancelled `EventRegistration` per member per event.

#### Scenario: A member cannot register twice for the same event

- **WHEN** a member who already holds a non-cancelled registration for an event attempts to
  register for it again
- **THEN** the request is refused with a clear message

### Requirement: Registration Requires Payment Verification Before Attendance Can Be Recorded

An `EventRegistration` SHALL progress through `Registered → PaymentSubmitted → PaymentVerified`
before it can be marked `Attended`. Verifying the linked `Payment` (`Kind = EventRegistration`)
SHALL be the only path from `PaymentSubmitted` to `PaymentVerified`.

#### Scenario: Attendance cannot be recorded before payment is verified

- **WHEN** an attempt is made to check in a registration that has not reached `PaymentVerified`
- **THEN** the request is refused

#### Scenario: Verifying an event payment advances the registration

- **WHEN** an Admin verifies a `Payment` whose `Kind` is `EventRegistration`
- **THEN** the linked `EventRegistration.Status` moves to `PaymentVerified`

#### Scenario: A rejected event payment can be resubmitted

- **WHEN** an Admin rejects a `Payment` linked to an `EventRegistration`
- **THEN** the registration's status reflects `Rejected`
- **AND** the member can submit a new payment proof for the same registration

### Requirement: Attendance Is Self Check-In With Admin Override

A member SHALL be able to check themselves into an event they are `PaymentVerified` for, moving
their registration to `Attended`. An Admin SHALL also be able to set or unset a registration's
`Attended` status directly, recording who performed the override.

#### Scenario: A member checks themselves in

- **WHEN** a member with a `PaymentVerified` registration performs self check-in during the event's
  attendance window
- **THEN** their registration moves to `Attended` with no admin action required

#### Scenario: An admin corrects a missed check-in

- **WHEN** an Admin marks a registrant as attended who did not self check-in
- **THEN** the registration moves to `Attended`
- **AND** the record shows the Admin as the one who set it, distinct from a self check-in

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

### Requirement: CPD Credit Is Computed, Not Stored

The CPD credit earned for a registration SHALL be derived at read time as the event's `CpdUnits`
when the registration's status is `EvaluationSubmitted` and the event's `CpdUnits` is not null, and
SHALL NOT be persisted as a separate stored value on the registration.

#### Scenario: A member's CPD total reflects only completed, credited registrations

- **WHEN** a member views their CPD credit total
- **THEN** it sums `CpdUnits` only for their registrations that are `EvaluationSubmitted` and whose
  event has a non-null `CpdUnits`
- **AND** registrations that are not yet `EvaluationSubmitted`, or whose event's `CpdUnits` is still
  null, contribute nothing to the total

### Requirement: Certificate Available Only Once Credit Is Earned

The system SHALL generate a certificate PDF for a registration only when that registration would
count toward CPD credit (`EvaluationSubmitted` and the event's `CpdUnits` is set). The certificate
SHALL be generated on request and SHALL NOT be pre-generated or cached from a prior state.

#### Scenario: Certificate request before credit is earned is refused

- **WHEN** a certificate is requested for a registration that has not reached `EvaluationSubmitted`,
  or whose event's `CpdUnits` is still null
- **THEN** the request is refused with a clear "not yet available" response, not a generated file

#### Scenario: Certificate reflects a corrected unit count

- **WHEN** a certificate is requested for a registration after its event's `CpdUnits` was corrected
  following an earlier value
- **THEN** the generated PDF shows the current `CpdUnits` value, not any value that may have been
  set previously
