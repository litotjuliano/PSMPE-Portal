import { LuRocket } from 'react-icons/lu'
import { appVersion } from '../../helpers/constants'
import { releaseNotes, type ReleaseNoteChangeType } from '../../../../core/constants/releaseNotes'

const CHANGE_TYPE_STYLES: Record<ReleaseNoteChangeType, string> = {
  Added: 'bg-success/10 text-success',
  Fixed: 'bg-info/10 text-info',
  Changed: 'bg-default-200 text-default-700',
}

/** Strips a staging build's "-rc.N" suffix so e.g. "1.0.0-rc.2" still matches the "1.0.0"
 *  release-notes entry - the notes for what will become a release are written once, at the
 *  staging merge, and apply unchanged through however many rc's it takes to promote it. */
function bareVersion(version: string): string {
  return version.replace(/^v/, '').replace(/-rc\.\d+$/, '')
}

/** Dashboard card showing the current deployed version's release notes - see
 *  core/constants/releaseNotes.ts and openspec/changes/add-release-versioning/proposal.md. Renders
 *  nothing if appVersion doesn't match any entry (e.g. local dev, where it's "dev"). */
export function WhatsNewWidget() {
  const note = releaseNotes.find((entry) => entry.version === bareVersion(appVersion))
  if (!note) {
    return null
  }

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title flex items-center gap-2">
          <LuRocket className="size-4 shrink-0" />
          What's New in v{note.version}
        </h6>
      </div>
      <div className="card-body flex flex-col gap-3">
        <ul className="flex flex-col gap-2">
          {note.changes.map((change, index) => (
            <li key={index} className="flex items-start gap-2 text-sm">
              <span className={`shrink-0 mt-0.5 px-1.5 py-0.5 rounded text-xs font-medium ${CHANGE_TYPE_STYLES[change.type]}`}>
                {change.type}
              </span>
              <span className="text-default-700">{change.description}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
