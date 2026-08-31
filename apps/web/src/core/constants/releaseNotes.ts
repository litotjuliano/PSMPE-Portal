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
