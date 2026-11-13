# Tech debt

Known gaps that are deliberately left for later. Remove an item when it is done.

## Data location

- **Warn when a synced database is used on two devices.** A database folder in OneDrive or Dropbox can be corrupted if the app
  runs on two computers at the same time. Idea: a `wowstopwatcher.lock` file next to the database with the machine name and a
  heartbeat; at startup, warn when another computer updated it in the last few minutes. Today there is only a warning text in
  Settings › Data. Plan: `DataLocation-Plan.md` in the owner's notes.
- **Old database files after Replace or Import are never cleaned up.** The replaced file is renamed to
  `wowstopwatcher.db.replaced-<timestamp>` next to the database (besides the verified copy in `backups/`). Offer a cleanup
  in Settings › Data, or move these files into `backups/`.

## Data protection

- **macOS Keychain and Linux `secret-tool` key stores never ran on a real system.** `MacDatabaseKeyStore` and `LinuxDatabaseKeyStore`
  were written without a Mac or a Linux machine with a Secret Service (only the `secret-tool` arguments are unit tested).
  Run save, read and the recovery code fallback once on each.
- **The swap-failure rollback of Replace and Import has no test.** If swapping the database file fails midway, the previous
  database must come back; this path is not covered.
- **Recovery code attempts are not rate-limited.** The recovery code window and the Invoices PIN "forgot" flow accept unlimited
  tries (only the PIN itself locks for 30 s after 5 wrong tries).

## Invoicing

- **Word PDF conversion is untested.** `WordPdfConverter` (late-bound COM) was written without a machine that has Word.
  Run it once with Word installed; LibreOffice is the tested path.
- **PDF preview on Linux and macOS is unverified.** PDFtoImage brings SkiaSharp 4.x native assets while the Linux native asset
  resolves to 3.x. Check that the preview loads on both platforms; fallback: render the preview through LibreOffice.
- **Company color has presets only.** The custom color popup lives in the Settings feature and cannot be referenced from
  Invoicing; move it to shared Controls and use it in both.
- **Unsaved placeholder edits can be lost.** While an alias is invalid or duplicated nothing is saved; leaving the page drops
  those edits. Warn before leaving, or keep a draft.
- **No real DST test for invoice days.** `InvoiceLineBuilderTests` use a fixed-offset zone; add a case with a zone that changes
  offset inside the month.
