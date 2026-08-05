# account Specification (Delta)

Throughout this document, **administrative account** means an account holding any role other than
`Member` — Super Admin, Admin, Manager, or Accounts. This matches
`MembersController.IsSystemAccountAsync`, which is the server-side authority; the web client
mirrors it rather than defining its own rule.

## ADDED Requirements

### Requirement: Self-Service Display Name

The system SHALL allow any authenticated account to change its own display name, regardless of
role. The response SHALL carry the updated account so the client can refresh cached copies without
a second request. Email SHALL be returned but SHALL NOT be editable through this endpoint.

#### Scenario: Member changes their display name

- **WHEN** an authenticated member submits a new display name to `PUT /api/account/me`
- **THEN** the stored display name is updated
- **AND** the response contains the new display name, the account email, and the account's roles

#### Scenario: Administrative account changes its display name

- **WHEN** an Admin or Super Admin submits a new display name
- **THEN** the change succeeds on the same endpoint, with no role-specific behaviour

#### Scenario: Email is not changed by this endpoint

- **WHEN** an account submits a display name change
- **THEN** its email and username are left untouched

#### Scenario: Unauthenticated callers are refused

- **WHEN** a request arrives with no valid token
- **THEN** it is rejected as unauthenticated

### Requirement: Self-Service Password Change

The system SHALL allow any authenticated account to change its own password by supplying its
current password. A successful change SHALL clear any lockout on the account. A failed attempt
SHALL NOT count toward the lockout threshold.

#### Scenario: Password is changed with the correct current password

- **WHEN** an account submits its correct current password and a compliant new password
- **THEN** the password is changed
- **AND** the account can subsequently sign in with the new password
- **AND** the account can no longer sign in with the old password

#### Scenario: Wrong current password is refused without detail

- **WHEN** an account submits an incorrect current password
- **THEN** the request is refused
- **AND** the message does not indicate whether the current password or the new password was at fault

#### Scenario: A non-compliant new password reports the policy failure

- **WHEN** an account submits a new password that violates the password policy
- **THEN** the response describes the policy failure in the same terms the reset-password flow uses

#### Scenario: Changing a password clears a lockout

- **WHEN** an account that is currently locked out changes its password successfully
- **THEN** the lockout is cleared
- **AND** the recorded failed-attempt count returns to zero

#### Scenario: Failed change attempts do not lock the account

- **WHEN** an account submits an incorrect current password more times than the lockout threshold
- **THEN** the account is not locked out
- **AND** it can still sign in with its actual password

### Requirement: Administrator-Triggered Password Reset

The system SHALL allow an account holding the user-management permission to send a password reset
email to another account. The administrator SHALL NOT learn or choose the resulting password. The
reset link SHALL be the same one the self-service forgot-password flow issues.

#### Scenario: Administrator sends a reset to a verified member

- **WHEN** an administrator triggers a reset for a member whose email is confirmed
- **THEN** a password reset email is sent to that member
- **AND** the response contains no password and no reset token

#### Scenario: The emailed link restores access, including from lockout

- **WHEN** a member is locked out after repeated failed sign-ins
- **AND** an administrator triggers a password reset
- **AND** the member completes the reset using the emailed link
- **THEN** the member can sign in with the new password
- **AND** the lockout no longer applies

#### Scenario: Unverified accounts are refused

- **WHEN** an administrator triggers a reset for an account whose email is not confirmed
- **THEN** the request is refused
- **AND** no email is sent

#### Scenario: Callers without the permission are refused

- **WHEN** an account without the user-management permission triggers a reset
- **THEN** the request is refused by the existing authorization rules

#### Scenario: A Super Admin account cannot be targeted

- **WHEN** an administrator triggers a reset against a Super Admin account
- **THEN** the request is refused

#### Scenario: Administrator sends are not blocked by the per-address email throttle

- **WHEN** an address has already exhausted its hourly self-service reset allowance
- **AND** an administrator triggers a reset for that address
- **THEN** the email is still sent

#### Scenario: Administrator resets are recorded

- **WHEN** an administrator triggers a reset
- **THEN** the acting administrator and the target account are written to the application log

### Requirement: Account Section Replaces the Membership Wizard for Administrative Accounts

The web client SHALL show an account section on the profile page for every account, and SHALL NOT
show the membership application wizard to administrative accounts.

#### Scenario: Administrative account sees only the account section

- **WHEN** an Admin or Super Admin with no membership profile opens the profile page
- **THEN** the account section is shown, offering display name, photo, and password change
- **AND** the membership application wizard is not shown

#### Scenario: Member sees both

- **WHEN** a member opens the profile page
- **THEN** the account section is shown alongside their membership profile

#### Scenario: A renamed account updates immediately in the interface

- **WHEN** an account changes its display name
- **THEN** the interface shows the new name without requiring a fresh sign-in
