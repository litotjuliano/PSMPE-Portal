/**
 * Mirrors CHANGELOG.md's entries in structured form for WhatsNewWidget.tsx to render - written by
 * hand in the same commit as the matching CHANGELOG.md section, not generated from it. See
 * openspec/changes/add-release-versioning/proposal.md for the versioning/release process this is
 * part of.
 */

export type ReleaseNoteChangeType = 'Added' | 'Fixed' | 'Changed';

export interface ReleaseNoteChange {
  type: ReleaseNoteChangeType;
  description: string;
}

export interface ReleaseNote {
  /** Bare vX.Y.Z, no "v" prefix and no "-rc.N" suffix - matched against appVersion after
   *  stripping any such suffix, so a staging build's "-rc.N" shows the same notes its eventual
   *  production release will. */
  version: string;
  date: string;
  changes: ReleaseNoteChange[];
}

export const releaseNotes: ReleaseNote[] = [
  {
    version: '1.5.3',
    date: '2026-11-27',
    changes: [
      {
        type: 'Changed',
        description:
          'Portal updates are now checked after they go live: if the server does not start properly, the update is undone automatically and the previous version keeps running, so a bad update no longer takes the portal offline. Nothing changes in how you use the portal.',
      },
    ],
  },
  {
    version: '1.5.2',
    date: '2026-11-27',
    changes: [
      {
        type: 'Fixed',
        description:
          'The server could fail to start, taking the whole portal offline, when an administrative account had a membership profile with payments recorded against it. Startup now leaves such a profile in place and logs a warning for an admin to review, instead of trying to delete it.',
      },
      {
        type: 'Fixed',
        description:
          'An account that is both an Admin and a Member is now treated as a real member who also has admin access. They can fill in and submit a membership application, appear in the member list once they have a profile, and keep their profile across server restarts. Only accounts with an administrative role and no Member role are treated as staff-only, with no membership profile.',
      },
    ],
  },
  {
    version: '1.5.1',
    date: '2026-11-13',
    changes: [
      {
        type: 'Fixed',
        description:
          "The Add chapter button on the Chapters page, and the Add region, province and city buttons on Locations, were white on a light background and couldn't be seen. They now show as solid buttons.",
      },
    ],
  },
  {
    version: '1.5.0',
    date: '2026-11-04',
    changes: [
      {
        type: 'Added',
        description:
          "An Export to Excel button on Members > Payments downloads every payment awaiting verification, with each member's name, email, address and mobile number, the payment details, and a Proof link that opens that payment's own sheet with the proof shown large. PDF proofs show their first page. Each export is recorded in the system logs.",
      },
    ],
  },
  {
    version: '1.4.0',
    date: '2026-10-21',
    changes: [
      {
        type: 'Added',
        description:
          "A new Chapters page (Super Admin only) to add, rename, reorder and deactivate chapters, with search and an Active/Inactive filter. A chapter that members or events still use can't be deleted, only deactivated, and renaming one updates everyone on it.",
      },
      {
        type: 'Added',
        description:
          'Events can be saved as a Draft with just a title and description. Dates, venue, capacity, fees and the rest can be filled in later; the start and end dates are only required to publish.',
      },
      {
        type: 'Changed',
        description:
          'Chapter lists on member forms, the membership application, events and event filters now come from the Chapters page instead of a fixed list.',
      },
      {
        type: 'Changed',
        description: "The Venue field on the event form is a larger multi-line box. An event's description is now required.",
      },
      {
        type: 'Fixed',
        description:
          'Saving an event with the start or end date left blank no longer freezes the form on Saving…; it now says what is missing.',
      },
    ],
  },
  {
    version: '1.3.0',
    date: '2026-10-12',
    changes: [
      {
        type: 'Added',
        description:
          'A new Locations page (Super Admin only) to add, rename and remove regions, provinces and cities/municipalities, including their zip codes, with search and region/province filters. Changes show up in every address form right away, without a software update.',
      },
      {
        type: 'Changed',
        description:
          'The address forms now read their regions, provinces and cities from the portal instead of a fixed list. Addresses members already saved are not changed.',
      },
    ],
  },
  {
    version: '1.2.1',
    date: '2026-10-12',
    changes: [
      {
        type: 'Fixed',
        description:
          'Typing a province by hand (for example zamboanga del sur or iloilo) no longer leaves the City or Municipality list empty, so Pagadian City, Iloilo City and the rest can be picked.',
      },
      {
        type: 'Fixed',
        description:
          'Zamboanga Del Sur now lists exactly 27 places - Pagadian City and its 26 municipalities - instead of also showing the independent Zamboanga City.',
      },
    ],
  },
  {
    version: '1.2.0',
    date: '2026-10-07',
    changes: [
      {
        type: 'Added',
        description:
          'A Resend to all unverified button on the Users page emails a new verification link to every unverified account at once and reports how many were sent.',
      },
      {
        type: 'Added',
        description:
          'The Users page now shows Email sent with the date once a verification email has gone out, and asks before sending another - so you do not resend to the same person twice by accident.',
      },
      {
        type: 'Changed',
        description:
          'The verification email is now a proper message with a Confirm my email button and a plain-text version, so it is less likely to land in junk. The check your email page also reminds members to look in Junk or Spam.',
      },
      {
        type: 'Changed',
        description:
          'Results of sending an email from the Users page now appear as a pop-up that disappears on its own.',
      },
      {
        type: 'Fixed',
        description: 'The Users page showed No users yet when the list failed to load; it now says what went wrong.',
      },
    ],
  },
  {
    version: '1.1.0',
    date: '2026-10-07',
    changes: [
      {
        type: 'Added',
        description:
          'Admins can resend a verification email to an unverified member from the Users page - look for the mail icon next to each unverified account.',
      },
    ],
  },
  {
    version: '1.0.2',
    date: '2026-10-07',
    changes: [
      {
        type: 'Fixed',
        description:
          'Forgot password and the admin Send password reset action now say clearly when the email could not be sent, instead of a generic error. Approving a member no longer shows an error when only the approval email fails - the approval itself had already gone through.',
      },
    ],
  },
  {
    version: '1.0.1',
    date: '2026-10-06',
    changes: [
      {
        type: 'Fixed',
        description:
          "If the verification email can't be sent when you register, you now see a clear message that your account was created and to try again shortly, instead of a generic error. Resend verification email also reports a temporary failure instead of doing nothing.",
      },
    ],
  },
  {
    version: '1.0.0',
    date: '2026-08-31',
    changes: [
      {
        type: 'Added',
        description:
          'Add Portal Access mid-cycle, without waiting for your next renewal - a standalone card on My Profile for anyone current on dues but missing the add-on.',
      },
      {
        type: 'Fixed',
        description:
          "A newly registered account with no membership application yet is now correctly restricted, instead of getting unrestricted portal access.",
      },
      {
        type: 'Fixed',
        description:
          'The sidebar no longer hides most of the menu for a restricted member, and restricted pages stay reachable - restrictions now show up as a disabled action with an explanation instead of redirecting you away.',
      },
    ],
  },
];
