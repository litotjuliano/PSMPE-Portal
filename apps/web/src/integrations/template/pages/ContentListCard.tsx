import { Link } from 'react-router-dom'
import { LuPlus, LuSquarePen, LuTrash2 } from 'react-icons/lu'
import { ContentStatus, type ContentItem } from '../../../core/types/content'

interface ContentListCardProps {
  items: ContentItem[]
  onDelete: (id: string) => void
}

const statusLabels: Record<number, string> = {
  [ContentStatus.Draft]: 'Draft',
  [ContentStatus.Published]: 'Published',
  [ContentStatus.Archived]: 'Archived',
}

const statusClasses: Record<number, string> = {
  [ContentStatus.Draft]: 'bg-default-150 text-default-600',
  [ContentStatus.Published]: 'bg-success/10 text-success',
  [ContentStatus.Archived]: 'bg-warning/10 text-warning',
}

export const ContentListCard = ({ items, onDelete }: ContentListCardProps) => {
  return (
    <div className="card">
      <div className="card-header flex justify-between items-center">
        <h6 className="card-title">Content</h6>
        <Link to="/content/new" className="btn btn-sm bg-primary text-white">
          <LuPlus className="size-4 me-1" />
          New content
        </Link>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-500 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Title</th>
                    <th className="px-3.5 py-3 text-start">Status</th>
                    <th className="px-3.5 py-3 text-start">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {items.map((item) => (
                    <tr key={item.id} className="text-default-800 font-normal text-sm">
                      <td className="px-3.5 py-2.5 font-medium">{item.title}</td>
                      <td className="px-3.5 py-2.5">
                        <span className={`inline-flex items-center py-0.5 px-2.5 rounded text-xs font-medium ${statusClasses[item.status]}`}>
                          {statusLabels[item.status]}
                        </span>
                      </td>
                      <td className="px-3.5 py-2.5">
                        <div className="flex items-center gap-1.5">
                          <Link
                            to={`/content/${item.id}`}
                            className="btn btn-icon size-8 hover:bg-default-150 rounded-full text-default-500"
                            aria-label="Edit"
                          >
                            <LuSquarePen className="size-4" />
                          </Link>
                          <button
                            onClick={() => onDelete(item.id)}
                            className="btn btn-icon size-8 hover:bg-danger/10 hover:text-danger rounded-full text-default-500"
                            aria-label="Delete"
                          >
                            <LuTrash2 className="size-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {items.length === 0 && (
                    <tr>
                      <td colSpan={3} className="py-6 px-3.5 text-center text-default-500">
                        No content yet.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
