import { LuCalendarDays, LuNewspaper, LuSparkles } from 'react-icons/lu'
import { StatTile } from '../shared/StatTile'

/**
 * Placeholder preview of the future News Management module. There is no News/Announcement entity,
 * endpoint, or controller anywhere in this codebase yet - every headline below is fictional,
 * hardcoded sample content shown purely to preview the feature to prospective and current members.
 * Do not wire this to any API. Replace/delete this whole component once the real module ships.
 */

interface MockArticle {
  headline: string
  date: string
}

const MOCK_ARTICLES: MockArticle[] = [
  { headline: 'PRC Releases 2026 Master Plumber Licensure Exam Schedule', date: 'Aug 10, 2026' },
  { headline: 'Membership Renewal Deadline Extended to September 30', date: 'Aug 5, 2026' },
  { headline: 'Quezon City Chapter Elects New Set of Officers', date: 'Jul 28, 2026' },
  { headline: 'New CPD Accreditation Requirements Take Effect Next Year', date: 'Jul 15, 2026' },
]

/** Dummy dashboard card previewing the not-yet-built News Management module. */
export function NewsPreviewWidget() {
  return (
    <div className="card h-full border-2 border-dashed border-warning/40 bg-warning/5 dark:bg-warning/10">
      <div className="card-header">
        <h6 className="card-title flex items-center gap-2">
          <LuNewspaper className="size-4 shrink-0" />
          News Management
        </h6>
        <span className="inline-flex items-center gap-1 py-0.5 px-2.5 rounded-full text-xs font-semibold bg-warning/10 text-warning dark:bg-warning/15 shrink-0">
          <LuSparkles className="size-3" />
          Preview · Coming Soon
        </span>
      </div>
      <div className="card-body flex flex-col gap-4">
        <StatTile icon={LuNewspaper} label="Recent articles" value={MOCK_ARTICLES.length} accent="bg-warning/15 text-warning" />

        <ul className="flex flex-col">
          {MOCK_ARTICLES.map((article) => (
            <li
              key={article.headline}
              className="flex items-start justify-between gap-3 py-2 border-b border-dashed border-default-200 last:border-b-0"
            >
              <span className="text-sm text-default-700 font-medium">{article.headline}</span>
              <span className="flex items-center gap-1 text-xs text-default-500 shrink-0 whitespace-nowrap">
                <LuCalendarDays className="size-3 shrink-0" />
                {article.date}
              </span>
            </li>
          ))}
        </ul>

        <p className="text-xs text-default-500 rounded-lg bg-default-100 px-3 py-2">
          Sample content only - the News Management module is not yet available. Nothing above is real.
        </p>
      </div>
    </div>
  )
}
