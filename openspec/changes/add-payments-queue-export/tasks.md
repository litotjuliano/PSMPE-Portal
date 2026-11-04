# Tasks: Export the payments queue to Excel

## Backend
- [x] Add ClosedXML 0.105.1 and PDFtoImage 4.1.0 to `PSMPE.Portal.Application`; confirm SkiaSharp stays at 2.88.8
- [x] `PaymentProofPicture`: image and PDF page 1 to JPEG, at most 1000px on the long side; reports failure instead of throwing
- [x] `PaymentExportWorkbook`: nine columns in the agreed order, frozen header, typed cells, values never written as formulas, pictures scaled to fit column H
- [x] `PaymentExportService`: all `Submitted` payments oldest first, full name, address, email from `Member.User`, the five proof outcomes, each original file released after conversion
- [x] Register the service in `DependencyInjection`
- [x] `GET /api/payments/export` on `PaymentsController`, `members:view`, records `payments.exported` with actor, IP and row count

## Frontend
- [x] `paymentApi.exportQueue()` blob download
- [x] Export to Excel button in the `PaymentsQueueTable` header: ready, exporting, disabled on empty queue
- [x] Download named with the local date; failure shows an error in the card

## Tests
- [x] Service: only `Submitted`, oldest first, rows past the queue's page size included, all kinds included
- [x] Service: name and address skip blank parts; event payment shows the event title
- [x] Service: each proof outcome (image, PDF, none, missing file, unreadable file); one bad file doesn't fail the export
- [x] Workbook: reopen the file and assert column order, cell types, leading `0` and `+` kept, a value starting with `=` stays text, picture count
- [x] Controller: refused without `members:view`; audit event recorded; no storage key in the output
- [x] Frontend type-check and build

## Docs
- [x] `openspecs/payments.md`: endpoint and export section
- [x] `openspecs/system-logs.md`: `payments.exported` added to the audited events

## Verify on staging
- [ ] Published Linux image exports a queue containing an image proof and a PDF proof
- [ ] The file opens in Excel with pictures in the right rows
- [ ] Receipts and member uploads still work after the new native packages
- [ ] Audit entry appears on `/admin/system-logs`
