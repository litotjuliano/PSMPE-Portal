type CurrencyType = '₹' | '$' | '€';

export const currency: CurrencyType = '$';

export const currentYear = new Date().getFullYear();

export const appName = 'PSMPE Portal';

export const DEFAULT_PAGE_TITLE = 'PSMPE Portal';
export const appAuthor = 'LitXus Systems';
export const authorWebsite = '';

// Set at Docker build time from `git describe --tags --always` on the deployed commit - see
// openspec/changes/add-release-versioning/proposal.md. "dev" for a local build with no tags.
export const appVersion = import.meta.env.VITE_APP_VERSION || 'dev';

export const colorVariants = [
  'primary',
  'secondary',
  'success',
  'danger',
  'warning',
  'info',
  'dark',
  'purple',
  'pink',
  'orange',
  'light',
  'link',
];
