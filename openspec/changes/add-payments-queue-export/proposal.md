# Change: Export the payments queue to Excel

## Status

**Proposed** 2026-11-04. Design agreed with a mockup. Not implemented.

## Why

Staff need the payments awaiting verification as a file they can work from and pass on without
opening each row in the portal. The queue has no export today, and the repo has no spreadsheet
code at all.

The queue table shows neither the address, mobile number nor email, and each proof takes a click
to open. The export puts those and the proof itself on one row.

## What changes

- New `GET /api/payments/export`, returning an `.xlsx` of the queue.
- New **Export to Excel** button in the "Payments Awaiting Verification" card header.
- New audit event `payments.exported`.
- New dependencies: ClosedXML 0.105.1 and PDFtoImage 4.1.0, both MIT.

## Endpoint

`GET /api/payments/export`, `members:view`. This is the permission that already lets staff see the
queue and open any proof, so the export grants no new access.

- Returns every `Submitted` payment, ignoring the queue's page size.
- Ordered oldest first by `CreatedAt`, the same as the queue.
- Includes all payment kinds: new membership, renewal, event registration, portal access.
- Content type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
- An empty queue returns a valid workbook with only the header row.

## Workbook

One sheet named `Awaiting Verification`. Row 1 is a frozen header row. Columns, in this order:

| Col | Header | Value | Cell type |
|---|---|---|---|
| A | Full Name | First, middle, last, suffix; blank parts skipped | Text |
| B | Email | `Member.User.Email` | Text |
| C | Address | `HouseNo Street, Barangay, CityMunicipality, Province ZipCode, Country`; blank parts skipped | Text, wrapped |
| D | CP Number | `Member.MobileNumber` | Text, so a leading `0` or `+` survives |
| E | Amount | `Payment.Amount` | Number, peso format |
| F | Reference | `Payment.ReferenceNo` | Text |
| G | Paid On | `Payment.PaidOn` | Date |
| H | Proof of Payment | See below | Picture or text |
| I | For | Same labels as the queue; event payments include the event title | Text, wrapped |

All member-entered values are written as cell values, never as formulas, so a value starting with
`=` cannot execute in Excel. Text starting with `=`, `+`, `-` or `@` also gets a leading apostrophe, because the cell type does not survive a re-save as CSV. Plain phone numbers are left as they are.

Every data row has the same height, tall enough for the proof picture. The picture is scaled to fit
inside column H and keeps its aspect ratio.

## Proof of Payment column

The file is the one **View proof** opens: `Payment.ProofStorageKey`, not the member's
`MemberUpload` proof. There are no links to the portal. Every proof that can be shown is embedded.

| Stored proof | Cell shows |
|---|---|
| Image | The picture, resized to at most 1000px on the long side |
| PDF | Page 1 rendered to a picture, same size limit |
| No proof attached | `No proof on file` |
| Key set, file gone from storage | `File missing – ask member to resubmit` |
| Image or PDF that can't be decoded or rendered | `Could not read file – open it in the portal` |

1000px keeps a deposit slip legible if the reader enlarges the picture, while keeping the workbook
small. Only page 1 of a multi-page PDF appears; the full PDF stays in the portal. Render resolution is lowered for oversized pages, so a tiny PDF declaring a huge page can't exhaust the server's memory.

One bad proof never fails the export. A missing native library does, with a visible error: that is a
broken deployment, and labelling every PDF unreadable would hide it.

Each original file is opened, converted and released in turn. The resized pictures stay in memory
until the workbook is saved, because ClosedXML holds them. Measured at about 1 MB of peak memory per row at larger sizes: about 120 MB at 50 rows, about 550 MB and 46 seconds at 500 rows on a desktop. That is fine for today's queue of tens of rows. If the queue ever reaches hundreds, add a row cap.

## Code structure

- `PaymentExportWorkbook` (Application): a pure renderer from rows to an xlsx stream. No database or
  storage dependency, in the same spirit as `ReceiptGenerator`.
- `PaymentProofPicture` (Application): turns a proof stream into resized JPEG bytes, or reports that
  it can't. Uses SkiaSharp for images and PDFtoImage for PDFs.
- `PaymentExportService` (Application): loads the rows, opens each proof through
  `IMemberUploadService.OpenByKeyAsync`, and calls the two above.
- `PaymentsController.Export`: calls the service, records the audit event, returns the file.

Storage keys stay inside the service. They embed the member's name and birthdate, so they must not
reach a DTO, a cell or a log line.

## Frontend

- `paymentApi.exportQueue()` fetches a blob through `apiClient`.
- `PaymentsQueueTable` shows `StandardButton` variant `on-primary` with a download icon, right of the
  card title.
- States: ready; `Exporting…` and disabled while running; disabled when `totalCount` is 0.
- The client names the download `PSMPE-Payments-Awaiting-Verification-YYYY-MM-DD.xlsx` using the
  local date. This avoids depending on `Content-Disposition` being exposed through CORS.
- A failure shows an error message in the card and leaves the queue as it was.

## Audit

The file carries personal data out of the access-controlled portal, so each export records
`payments.exported` through `IAuditLogService` with the actor, IP and row count. It stores no member
data and no per-row detail. `openspecs/system-logs.md` lists the audited events, so it is updated too.

## Decisions

- **Server-side generation.** A browser build would need a new API exposing addresses and emails,
  plus one proof download per row. CSV can't hold pictures.
- **Pictures, not links.** Requested: the file must stand alone. This means proof images leave the
  portal inside the workbook, which is why exports are audited.
- **PDFs are rendered, not linked.** Uploads accept PDF, and a reader of the file should see every
  proof the same way.
- **PDFtoImage pinned to 4.1.0.** It depends on SkiaSharp 2.88.8, the version this repo uses. Current
  releases (5.x) require SkiaSharp 4.x, which would force an upgrade across `ReceiptGenerator` and
  every upload path.

## Risks

- **Old PDFium.** 4.1.0 bundles PDFium 128. It would parse member-uploaded PDFs, which is untrusted
  input, and it won't receive security updates at this pin. Exposure is limited: files are capped at
  1 MB and rendering only runs when staff export. Moving to a current PDFium needs a SkiaSharp upgrade,
  which is a separate change.
- **Native packages on Linux.** PDFtoImage brings `SkiaSharp.NativeAssets.Linux.NoDependencies`, while
  this repo references `SkiaSharp.NativeAssets.Linux`. Both supply `libSkiaSharp`, and PDFium is a new
  native library in the container. Staging must prove the published image renders a PDF and that
  receipts and uploads still work.

## Not built

- Exporting verified or rejected payments.
- Date, status or kind filters on the export.
- Search and filter on the queue itself. It has none today, which falls short of the rule that every
  list has them; that is a separate change, and the export would then need to follow those filters.
- Later pages of a multi-page PDF.
- Links from the workbook back to the portal.
