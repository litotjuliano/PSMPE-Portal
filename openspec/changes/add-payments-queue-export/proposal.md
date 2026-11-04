# Change: Export the payments queue to Excel

## Status

**Implemented** 2026-11-04. Not yet verified on staging.

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

The first sheet is named `Awaiting Verification`, followed by one sheet per payment. Row 1 of the first sheet is a frozen header row. Columns, in this order:

| Col | Header | Value | Cell type |
|---|---|---|---|
| A | Full Name | First, middle, last, suffix; blank parts skipped | Text |
| B | Email | `Member.User.Email` | Text |
| C | Address | `HouseNo Street, Barangay, CityMunicipality, Province ZipCode, Country`; blank parts skipped | Text, wrapped |
| D | CP Number | `Member.MobileNumber` | Text, so a leading `0` or `+` survives |
| E | Amount | `Payment.Amount` | Number, peso format |
| F | Reference | `Payment.ReferenceNo` | Text |
| G | Paid On | `Payment.PaidOn` | Date |
| H | Proof of Payment | See below | Link or text |
| I | For | Same labels as the queue; event payments include the event title | Text, wrapped |

All member-entered values are written as cell values, never as formulas, so a value starting with
`=` cannot execute in Excel. Text starting with `=`, `+`, `-` or `@` also gets a leading apostrophe, because the cell type does not survive a re-save as CSV. Plain phone numbers are left as they are.

Every data row has the same height. Column H shows **Proof link**, which opens the payment's sheet.
There are no pictures on this sheet, so it stays compact.

## Payment sheets

Excel can't enlarge a picture on click, so each payment also gets its own sheet with the proof
shown large. Staff click **Proof link** or the Full Name in the list to open it, and **Back to list** returns to the same
row.

- Named with the row's sequence number and the member's name, such as `1 Juan Dela Cruz`. The number
  keeps names unique when a member has two payments.
- Names are member-entered, so only letters, digits, spaces, dots and hyphens stay, cut to Excel's
  31-character limit.
- Details sit in columns A and B, with the same formula protection as the list. The proof sits to the
  right at its stored size. Without a picture, the sheet shows the same note as the list.

## Proof of Payment column

The file is the one **View proof** opens: `Payment.ProofStorageKey`, not the member's
`MemberUpload` proof. There are no links to the portal; **Proof link** only moves between sheets in the
file. Every proof that can be shown is embedded on the payment's sheet.

| Stored proof | List shows | Payment sheet shows |
|---|---|---|
| Image | `Proof link` | A smaller copy of the picture, at most 800px on the long side, JPEG quality 70 |
| PDF | `Proof link` | Page 1 rendered to a picture, same size limit |
| No proof attached | `No proof on file` | The same note |
| Key set, file gone from storage | `File missing – ask member to resubmit` | The same note |
| Image or PDF that can't be decoded or rendered | `Could not read file – open it in the portal` | The same note |

The export only reads the stored file and embeds a smaller copy, so originals never change. 800px at
quality 70 kept 10pt PDF text and small print on a clean test slip readable, while keeping the workbook small. A blurry phone photo loses more, and the original stays in the portal. Only page 1 of a multi-page PDF appears; the full PDF stays in the portal. Render resolution is lowered for oversized pages, so a tiny PDF declaring a huge page can't exhaust the server's memory.

One bad proof never fails the export. A missing native library does, with a visible error: that is a
broken deployment, and labelling every PDF unreadable would hide it.

Each original file is opened, converted and released in turn. The resized pictures stay in memory
until the workbook is saved, because ClosedXML holds them. With deliberately hard-to-compress test pictures, the test process peaked at about 155 MB at 50 rows (7 MB file) and about 615 MB at 500 rows (68 MB file) on a desktop. Real proofs are likely to compress better. That is fine for today's queue of tens of rows. The droplet is shared with production and had about 750 MB available on 2026-11-04, so add a row cap before the queue reaches hundreds.

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
