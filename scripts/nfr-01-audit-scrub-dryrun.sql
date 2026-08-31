-- NFR-01 remediation DRY RUN / REPORT (read-only — modifies nothing).
--
-- Run this before applying migration 20260831104242_NFR01_ScrubSensitiveAuditPayloads to see
-- how many audit.audit_events rows carry a sensitive field name or value in their
-- before_json / after_json / metadata_json payloads, plus a sample of affected event ids.
--
-- The migration itself removes prohibited keys and redacts sensitive-looking string values.
-- This script uses the same detection rules but only COUNTS and SAMPLES.

WITH flagged AS (
    SELECT
        e.id,
        e.event_id,
        e.event_type,
        e.occurred_at,
        (
            -- prohibited KEY anywhere in the payload
            EXISTS (
                SELECT 1
                FROM jsonb_each(coalesce(e.before_json,'{}') || coalesce(e.after_json,'{}') || coalesce(e.metadata_json,'{}')) kv
                WHERE lower(kv.key) IN (
                    'salary','previoussalary','currentsalary','newsalary','oldsalary','salaryamount',
                    'annualsalary','basesalary','proposedsalary','grosssalary','compensation',
                    'compensationamount','payamount','hourlyrate','dayrate','bonus','bonusamount',
                    'nationalinsurancenumber','ninumber','ni','nino','taxcode','taxidentifier','utr',
                    'bankaccountnumber','accountnumber','sortcode','iban','bankaccount','bic','swift',
                    'cardnumber','cvv','password','passwordhash','token','secret','clientsecret','apikey',
                    'refreshtoken','accesstoken','bearertoken','authorization','credentials','privatekey',
                    'connectionstring','dateofbirth','dob','personalemail','personalphone',
                    'personalphonenumber','homeaddress','medicalnote','sicknessnote','diagnosisnote',
                    'diagnosiscode')
                   OR lower(kv.key) LIKE '%nationalinsurance%'
                   OR lower(kv.key) LIKE '%bankaccount%'
                   OR lower(kv.key) LIKE '%password%'
            )
        ) AS has_prohibited_key,
        (
            -- sensitive VALUE pattern anywhere in the raw payload text
            coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'')
              ~* '[A-Z]{2}[[:space:]]?[0-9]{2}[[:space:]]?[0-9]{2}[[:space:]]?[0-9]{2}[[:space:]]?[A-D]'
            OR coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'')
              ~ '[0-9]{2}-[0-9]{2}-[0-9]{2}'
            OR coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'')
              ~ '[0-9]{12,19}'
            OR coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'')
              ~* 'bearer[[:space:]]+[A-Za-z0-9._~+/-]+'
            OR coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'')
              ~ 'eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}'
            OR coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'')
              ~ '\$2[aby]\$[0-9]{2}\$[./A-Za-z0-9]{53}'
        ) AS has_sensitive_value
    FROM audit.audit_events e
)
SELECT
    count(*) FILTER (WHERE has_prohibited_key)                       AS rows_with_prohibited_key,
    count(*) FILTER (WHERE has_sensitive_value)                      AS rows_with_sensitive_value,
    count(*) FILTER (WHERE has_prohibited_key OR has_sensitive_value) AS rows_total_affected
FROM flagged;

-- Sample of affected events (first 50):
WITH flagged AS (
    SELECT e.id, e.event_id, e.event_type, e.occurred_at,
           coalesce(e.before_json::text,'') || coalesce(e.after_json::text,'') || coalesce(e.metadata_json::text,'') AS blob
    FROM audit.audit_events e
)
SELECT event_id, event_type, occurred_at
FROM flagged
WHERE blob ~* '[A-Z]{2}[[:space:]]?[0-9]{2}[[:space:]]?[0-9]{2}[[:space:]]?[0-9]{2}[[:space:]]?[A-D]'
   OR blob ~ '[0-9]{2}-[0-9]{2}-[0-9]{2}'
   OR blob ~ '[0-9]{12,19}'
ORDER BY occurred_at DESC
LIMIT 50;
