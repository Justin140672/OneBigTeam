# [P1] Reject path-traversal filenames and confine import storage to its tenant directory

## Problem and impact
The import endpoint passes the client-controlled uploaded filename directly into a local storage path. Validation only checks the extension, MIME type, size, and workbook content. A filename containing parent-directory segments can escape the generated upload directory, company directory, and storage root. File.Create then creates or truncates the resolved target under the API process's filesystem permissions.

This is not limited to Development: DataImportModule unconditionally registers LocalImportFileStorageService. The uploaded content must still be a valid allowed workbook; this finding does not claim arbitrary-content code execution. It does allow an authorised company HR user to write outside the intended company storage boundary and potentially overwrite another workbook at a known writable path.

## Evidence
- [src/Modules/HR.Modules.DataImport/Services/ImportFileValidator.cs:37](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.DataImport/Services/ImportFileValidator.cs#L37) uses Path.GetExtension without rejecting path components.
- `Features/UploadImportFile/Handler.cs:54` forwards the original FileName after workbook parsing.
- [src/Modules/HR.Modules.DataImport/Services/LocalImportFileStorageService.cs:19](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.DataImport/Services/LocalImportFileStorageService.cs#L19) interpolates that name into the key and calls File.Create at line 24.
- [src/Modules/HR.Modules.DataImport/Services/LocalImportFileStorageService.cs:58](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.DataImport/Services/LocalImportFileStorageService.cs#L58) combines paths with no canonical containment check.
- [src/Modules/HR.Modules.DataImport/DataImportModule.cs:29](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.DataImport/DataImportModule.cs#L29) registers this storage implementation in all environments.

## Reproduction / observed result
An executed probe passed `../../../review-probe.xlsx` to the production filename validator: **accepted**. It then resolved a normal `company/generated-id/filename` key through the production storage resolver: **outside the import storage root**. No file was written. A full endpoint reproduction should use a valid small XLSX workbook and a disposable target directory, never an existing real file.

## Suggested fix
Generate the physical object name entirely server-side and keep the original filename as display metadata only. Reject rooted paths, separators and traversal segments at input validation. Canonicalise and enforce containment at the storage boundary for upload, open, URL resolution, and deletion, including existing persisted keys. Review sibling Local* storage implementations for the same interpolation pattern; their deployment exposure may differ.

## Acceptance criteria
- [ ] Valid workbooks with malicious filenames cannot escape the tenant/upload directory on Windows or Linux.
- [ ] Cover forward/back slashes, parent segments, rooted paths and platform-specific path syntax.
- [ ] No out-of-root file is created, overwritten, opened or deleted; validation fails before persistence.
- [ ] Physical paths use generated names while normal download/display names remain usable.
- [ ] Existing unsafe keys are detected and quarantined or repaired safely.
- [ ] Add endpoint and storage tests using temporary fixture directories.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

