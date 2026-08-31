# Restore Drill (NFR-04 restoration test)

Purpose: repeatedly prove that a One Big Team database backup can be restored to a working,
tenant-isolated state, and that deletion obligations can be reapplied afterwards. This is the
in-repo, no-production-access proof for NFR-04. The recurring production restore exercise
(section D) uses the identical validation step against a real backup.

Companion script: `scripts/nfr-04-restore-drill-validation.sql`.

---

## A. Local scratch-database drill (run any time, no cloud access)

Prerequisites: `psql` / `pg_restore` (PostgreSQL 15+ client), Docker or a local Postgres, and a
backup artifact — either a real `pg_dump` from Supabase _[OPERATOR to supply]_ or a dump taken from
a locally-seeded dev database.

```bash
# 1. Start a throwaway Postgres
docker run -d --name obt-restore-drill -e POSTGRES_PASSWORD=drill -p 55432:5432 postgres:16

# 2. (If you have no real dump) create one from a seeded local dev DB:
#    pg_dump --format=custom --file=obt.dump "postgresql://<dev-conn>"

# 3. Restore the dump into the scratch DB
createdb -h localhost -p 55432 -U postgres restore_drill
pg_restore -h localhost -p 55432 -U postgres -d restore_drill --no-owner --no-privileges obt.dump

# 4. Run the validation query set
psql -h localhost -p 55432 -U postgres -d restore_drill \
     -v ON_ERROR_STOP=0 -f scripts/nfr-04-restore-drill-validation.sql | tee drill-output.txt

# 5. Tear down
docker rm -f obt-restore-drill
```

Pass criteria:

- Section 1: every module schema present.
- Section 2: every `__ef_migrations_history` non-empty; latest migration id matches the release the
  backup was taken from.
- Section 3: core tables non-empty (for a production dump) / consistent with seed (for a dev dump).
- Section 4: **zero** rows with NULL `company_id` on any tenant table.
- Section 5: **zero** rows referencing a missing company.
- Section 6: document-row storage keys intact (empty-key count matches pre-backup baseline, ideally 0).
- Section 7: the deletion-obligations list is produced and matches the out-of-band register.

Any FAIL/WARNING is a drill failure — record it and raise a defect before relying on the backup.

---

## B. File + DB consistency check (when a Storage backup is also being tested) _[OPERATOR]_

1. Restore the DB as above (or into a Supabase branch).
2. Restore the private buckets from the file-backup destination into a scratch bucket set.
3. Export the object listing: `supabase storage ls --recursive <bucket>` (or `rclone lsf`) to a file.
4. For each storage-key column surfaced by validation section 6, diff the DB key set against the
   object listing:
   - keys in DB but not in storage = **orphaned references** (files lost / newer than DB target).
   - keys in storage but not in DB = **orphaned files** (acceptable; clean up or ignore).
5. Record counts in the results template. Orphaned references > 0 must be resolved per
   `backup-and-disaster-recovery.md` section 4 step 4.

---

## C. Results recording template

Copy this block into the assurance evidence store for every drill / exercise.

```
NFR-04 RESTORE DRILL RESULT
===========================
Date run:                     ____________________
Run by:                       ____________________
Drill type:                   [ ] Local scratch  [ ] Production restore exercise
Backup artifact:              ____________________ (source, timestamp T)
Backup age at test:           ______ (RPO check: within window? Y/N)
Restore duration:             ______ (RTO check: within 4h? Y/N)

Validation script results:
  1. Module schemas present........... PASS / FAIL: __________
  2. Migration history per schema..... PASS / FAIL: __________
  3. Core row counts sane............. PASS / FAIL: __________
  4. company_id NOT NULL............... PASS / FAIL: __________
  5. No cross-tenant orphans.......... PASS / FAIL: __________
  6. Document storage keys intact..... PASS / FAIL: __________
  7. Deletion obligations listed...... PASS / FAIL: __________

File/DB consistency (if tested):
  Orphaned references (DB->missing file): ______
  Orphaned files (file->no DB row):       ______
  Resolution:                              __________

Smoke tests (production exercise only):
  Health checks (DB/Supabase/Storage/Hangfire).. PASS / FAIL
  App login + view employee + open document..... PASS / FAIL
  API 200 authed / 401 anon..................... PASS / FAIL
  Admin app + deletion queue load.............. PASS / FAIL

Deletion-obligation reapplication:
  Companies with executed deletion:  ______  reapplied: ______
  Companies with pending deletion:   ______  confirmed: ______
  Cross-checked against register?    Y / N
  Second-person verification by:     ____________________

Overall result:               PASS / FAIL
Defects raised:               ____________________
Next drill due:               ____________________ (max +6 months)
```

---

## D. Recurring production restore exercise _[OPERATOR]_

- **Frequency:** at least every 6 months, and after any major schema change or hosting change.
- **Procedure:** restore the latest production backup into a fresh Supabase branch/project; run
  `scripts/nfr-04-restore-drill-validation.sql`; restore a file-backup snapshot and run section B;
  run the section 5.4 smoke tests from `backup-and-disaster-recovery.md`; complete the template in
  section C; file it in the assurance register described in `docs/compliance/data-protection-operations.md`.
- **Ownership:** the named DR owner from `backup-and-disaster-recovery.md` section 1.
- **Gate:** a skipped or failed exercise blocks the "database backup retention and restoration are
  tested" production-launch gate.
