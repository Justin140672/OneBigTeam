using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <summary>
    /// NFR-01: controlled remediation of sensitive values that may already be stored in
    /// audit.audit_events before the value-level redaction guard existed.
    ///
    /// Recursively, for before_json / after_json / metadata_json:
    ///  - drops object keys whose NAME is prohibited (salary, national_insurance*, bank_account*,
    ///    password*, token, secret, iban, sort_code, ...);
    ///  - replaces string VALUES matching a sensitive pattern (NI number, IBAN, UK sort code,
    ///    12-19 digit bank/card number, "Bearer x", JWT, bcrypt hash) with '***REDACTED***'.
    ///
    /// DRY RUN / REPORT: run scripts/nfr-01-audit-scrub-dryrun.sql first — it lists affected
    /// row counts and sample event ids WITHOUT modifying any data. This migration is the apply step.
    /// audit.audit_pending_items is intentionally not touched: rows there are promoted to
    /// audit_events within seconds and any new prohibited payload is now rejected at write time.
    /// </summary>
    public partial class NFR01_ScrubSensitiveAuditPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ScrubFunctionSql);

            migrationBuilder.Sql(@"
                UPDATE audit.audit_events
                SET before_json   = audit.nfr01_scrub_jsonb(before_json),
                    after_json    = audit.nfr01_scrub_jsonb(after_json),
                    metadata_json = audit.nfr01_scrub_jsonb(metadata_json)
                WHERE before_json IS NOT NULL
                   OR after_json IS NOT NULL
                   OR metadata_json IS NOT NULL;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.nfr01_scrub_jsonb(jsonb);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.nfr01_is_prohibited_key(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.nfr01_scrub_text(text);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: the sensitive values are intentionally destroyed and audit rows are
            // append-only. No down migration.
        }

        private const string ScrubFunctionSql = @"
CREATE FUNCTION audit.nfr01_is_prohibited_key(k text) RETURNS boolean AS $$
BEGIN
    RETURN lower(k) IN (
        'salary','previoussalary','currentsalary','newsalary','oldsalary','salaryamount',
        'annualsalary','basesalary','proposedsalary','grosssalary','compensation',
        'compensationamount','payamount','hourlyrate','dayrate','bonus','bonusamount',
        'nationalinsurancenumber','ninumber','ni','nino','taxcode','taxidentifier','utr',
        'bankaccountnumber','accountnumber','sortcode','iban','bankaccount','bic','swift',
        'cardnumber','cvv','password','passwordhash','token','secret','clientsecret','apikey',
        'refreshtoken','accesstoken','bearertoken','authorization','credentials','privatekey',
        'connectionstring','dateofbirth','dob','personalemail','personalphone',
        'personalphonenumber','homeaddress','medicalnote','sicknessnote','diagnosisnote',
        'diagnosiscode'
    )
    OR lower(k) LIKE '%nationalinsurance%'
    OR lower(k) LIKE '%national_insurance%'
    OR lower(k) LIKE '%bankaccount%'
    OR lower(k) LIKE '%bank_account%'
    OR lower(k) LIKE '%password%';
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE FUNCTION audit.nfr01_scrub_text(v text) RETURNS text AS $$
DECLARE r text := v;
BEGIN
    IF r IS NULL THEN RETURN NULL; END IF;
    r := regexp_replace(r, '[A-Z]{2}[[:space:]]?[0-9]{2}[[:space:]]?[0-9]{2}[[:space:]]?[0-9]{2}[[:space:]]?[A-D]', '***REDACTED***', 'gi');
    r := regexp_replace(r, '[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}', '***REDACTED***', 'gi');
    r := regexp_replace(r, '[0-9]{2}-[0-9]{2}-[0-9]{2}', '***REDACTED***', 'g');
    r := regexp_replace(r, '[0-9]{12,19}', '***REDACTED***', 'g');
    r := regexp_replace(r, 'bearer[[:space:]]+[A-Za-z0-9._~+/-]+=*', '***REDACTED***', 'gi');
    r := regexp_replace(r, 'eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}', '***REDACTED***', 'g');
    r := regexp_replace(r, '\$2[aby]\$[0-9]{2}\$[./A-Za-z0-9]{53}', '***REDACTED***', 'g');
    RETURN r;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE FUNCTION audit.nfr01_scrub_jsonb(doc jsonb) RETURNS jsonb AS $$
DECLARE
    result jsonb;
    key text;
    val jsonb;
    elem jsonb;
BEGIN
    IF doc IS NULL THEN RETURN NULL; END IF;

    IF jsonb_typeof(doc) = 'object' THEN
        result := '{}'::jsonb;
        FOR key, val IN SELECT * FROM jsonb_each(doc) LOOP
            IF audit.nfr01_is_prohibited_key(key) THEN
                CONTINUE;
            END IF;
            result := result || jsonb_build_object(key, audit.nfr01_scrub_jsonb(val));
        END LOOP;
        RETURN result;
    ELSIF jsonb_typeof(doc) = 'array' THEN
        result := '[]'::jsonb;
        FOR elem IN SELECT * FROM jsonb_array_elements(doc) LOOP
            result := result || jsonb_build_array(audit.nfr01_scrub_jsonb(elem));
        END LOOP;
        RETURN result;
    ELSIF jsonb_typeof(doc) = 'string' THEN
        RETURN to_jsonb(audit.nfr01_scrub_text(doc #>> '{}'));
    ELSE
        RETURN doc;
    END IF;
END;
$$ LANGUAGE plpgsql IMMUTABLE;
";
    }
}
