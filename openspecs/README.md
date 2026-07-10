# OpenSpecs

Lightweight, human-readable specification documents for each API/feature area, kept next
to the code so contracts and docs don't drift apart. This is **not** a generated
artifact (that's what `/swagger` and the Swagger JSON are for) — it's a place to write
down *why* an endpoint exists, its authorization rules, and its request/response shape in
plain language before/while implementing it.

## Convention

One Markdown file per feature area, using this template:

```markdown
# <Feature name>

## Purpose
Why this exists, one or two sentences.

## Endpoints
- `METHOD /api/path` — one-line description
  - Auth: [anonymous | authenticated | policy name]
  - Request: shape
  - Response: shape

## Authorization rules
Ownership / role rules specific to this feature, in plain language.

## Open questions / TODO
Anything deliberately deferred.
```

## Index

- [auth.md](./auth.md) — registration, login, JWT issuance
- [content.md](./content.md) — CMS content CRUD and ownership rules
- [layouts.md](./layouts.md) — layouts and the system-layout protection rule
- [ai-prompt-execution.md](./ai-prompt-execution.md) — OpenAI prompt execution endpoint
- [roles.md](./roles.md) — role and permission administration

Add a new file here whenever a new feature area is added to the API.
