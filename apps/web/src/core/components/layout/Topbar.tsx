import { Menu, MenuButton, MenuItem, MenuItems } from '@headlessui/react'
import { ChevronDownIcon } from '@heroicons/react/20/solid'
import { useAuth } from '../../auth/useAuth'

export function Topbar() {
  const { user, logout } = useAuth()

  return (
    <header className="flex h-14 items-center justify-end border-b border-gray-200 bg-white px-6">
      <Menu as="div" className="relative">
        <MenuButton className="flex items-center gap-1 text-sm font-medium text-gray-700">
          {user?.displayName}
          <ChevronDownIcon className="h-4 w-4" />
        </MenuButton>
        <MenuItems className="absolute right-0 mt-2 w-40 origin-top-right rounded-md bg-white py-1 shadow-lg ring-1 ring-black/5 focus:outline-none">
          <MenuItem>
            <button
              onClick={logout}
              className="block w-full px-4 py-2 text-left text-sm text-gray-700 data-[focus]:bg-gray-100"
            >
              Sign out
            </button>
          </MenuItem>
        </MenuItems>
      </Menu>
    </header>
  )
}
