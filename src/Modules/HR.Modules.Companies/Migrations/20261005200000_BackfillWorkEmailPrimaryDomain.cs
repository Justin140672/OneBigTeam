using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    [DbContext(typeof(CompaniesDbContext))]
    [Migration("20261005200000_BackfillWorkEmailPrimaryDomain")]
    public partial class BackfillWorkEmailPrimaryDomain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    blocked text[] := ARRAY['gmail.com', 'googlemail.com', 'hotmail.com', 'hotmail.co.uk', 'hotmail.fr', 'hotmail.de', 'hotmail.it', 'hotmail.es', 'hotmail.nl', 'hotmail.be', 'hotmail.ca', 'hotmail.com.au', 'outlook.com', 'outlook.co.uk', 'outlook.fr', 'outlook.de', 'outlook.es', 'outlook.it', 'outlook.ie', 'live.com', 'live.co.uk', 'live.fr', 'live.de', 'live.it', 'live.nl', 'live.ie', 'live.ca', 'live.com.au', 'msn.com', 'windowslive.com', 'passport.com', 'yahoo.com', 'yahoo.co.uk', 'yahoo.fr', 'yahoo.de', 'yahoo.it', 'yahoo.es', 'yahoo.ie', 'yahoo.ca', 'yahoo.com.au', 'yahoo.co.in', 'yahoo.co.jp', 'ymail.com', 'rocketmail.com', 'icloud.com', 'me.com', 'mac.com', 'aol.com', 'aol.co.uk', 'aim.com', 'proton.me', 'protonmail.com', 'protonmail.ch', 'pm.me', 'gmx.com', 'gmx.net', 'gmx.de', 'gmx.co.uk', 'gmx.at', 'gmx.ch', 'gmx.fr', 'gmx.us', 'mail.com', 'email.com', 'usa.com', 'zohomail.com', 'zoho.com', 'yandex.com', 'yandex.ru', 'ya.ru', 'mail.ru', 'inbox.ru', 'list.ru', 'bk.ru', 'qq.com', '163.com', '126.com', 'sina.com', 'naver.com', 'hanmail.net', 'daum.net', 'web.de', 't-online.de', 'freenet.de', 'libero.it', 'virgilio.it', 'laposte.net', 'orange.fr', 'wanadoo.fr', 'free.fr', 'sfr.fr', 'rediffmail.com', 'tutanota.com', 'tutanota.de', 'tuta.io', 'tuta.com', 'fastmail.com', 'fastmail.fm', 'hushmail.com', 'mailfence.com', 'posteo.de', 'posteo.net', 'runbox.com', 'inbox.com', 'lycos.com', 'hey.com', 'btinternet.com', 'btopenworld.com', 'virginmedia.com', 'ntlworld.com', 'blueyonder.co.uk', 'talktalk.net', 'mailinator.com', 'guerrillamail.com', 'guerrillamail.net', 'guerrillamail.org', 'guerrillamail.biz', 'guerrillamail.de', 'guerrillamail.info', 'guerrillamailblock.com', 'sharklasers.com', 'grr.la', 'pokemail.net', 'spam4.me', '10minutemail.com', '10minutemail.net', '20minutemail.com', 'temp-mail.org', 'temp-mail.io', 'tempmail.com', 'tempmail.net', 'tempmailo.com', 'tempail.com', 'tempr.email', 'tempinbox.com', 'mytemp.email', 'discard.email', 'discardmail.com', 'yopmail.com', 'yopmail.net', 'yopmail.fr', 'trashmail.com', 'trashmail.de', 'trashmail.net', 'throwawaymail.com', 'getnada.com', 'nada.email', 'maildrop.cc', 'mailnesia.com', 'mintemail.com', 'mohmal.com', 'dispostable.com', 'fakeinbox.com', 'emailondeck.com', 'spamgourmet.com', 'mailcatch.com', 'moakt.com', 'burnermail.io', 'getairmail.com', 'dropmail.me', 'inboxkitten.com', 'mailpoof.com', 'mail.tm', 'mail.gw', 'emailfake.com', 'jetable.org', 'mailsac.com', 'harakirimail.com', 'wegwerfmail.de'];
                BEGIN
                    DROP TABLE IF EXISTS pg_temp.work_email_domain_candidates;
                    CREATE TEMP TABLE work_email_domain_candidates (
                        company_id uuid NOT NULL,
                        tier int NOT NULL,
                        email text NOT NULL,
                        sort_ts timestamptz NULL
                    );

                    IF to_regclass('employees.employees') IS NOT NULL THEN
                        IF (SELECT count(*) FROM information_schema.columns
                            WHERE table_schema = 'employees' AND table_name = 'employees'
                              AND column_name IN ('company_id', 'work_email', 'is_initial_company_admin', 'start_date')) = 4 THEN
                            INSERT INTO work_email_domain_candidates (company_id, tier, email, sort_ts)
                            SELECT e.company_id, 1, e.work_email, e.start_date::timestamptz
                            FROM employees.employees e
                            WHERE e.is_initial_company_admin AND e.work_email IS NOT NULL;
                        END IF;

                        IF (SELECT count(*) FROM information_schema.columns
                            WHERE table_schema = 'employees' AND table_name = 'employees'
                              AND column_name IN ('company_id', 'work_email')) = 2 THEN
                            INSERT INTO work_email_domain_candidates (company_id, tier, email, sort_ts)
                            SELECT e.company_id, 3, e.work_email, NULL
                            FROM employees.employees e
                            WHERE e.work_email IS NOT NULL;
                        END IF;
                    END IF;

                    IF to_regclass('identity.user_profiles') IS NOT NULL
                       AND to_regclass('identity.user_roles') IS NOT NULL
                       AND (SELECT count(*) FROM information_schema.columns
                            WHERE table_schema = 'identity' AND table_name = 'user_profiles'
                              AND column_name IN ('id', 'company_id', 'email', 'is_active', 'created_at')) = 5
                       AND (SELECT count(*) FROM information_schema.columns
                            WHERE table_schema = 'identity' AND table_name = 'user_roles'
                              AND column_name IN ('user_id', 'role_id')) = 2 THEN
                        INSERT INTO work_email_domain_candidates (company_id, tier, email, sort_ts)
                        SELECT p.company_id, 2, p.email, p.created_at
                        FROM identity.user_profiles p
                        JOIN identity.user_roles r ON r.user_id = p.id
                        WHERE r.role_id = '00000000-0000-0000-0000-000000000006'::uuid
                          AND p.is_active
                          AND p.email IS NOT NULL;
                    END IF;

                    UPDATE companies.company_settings s
                    SET work_email_primary_domain = d.domain,
                        work_email_suggestions_enabled = TRUE
                    FROM (
                        SELECT DISTINCT ON (v.company_id) v.company_id, v.domain
                        FROM (
                            SELECT c.company_id, c.tier, c.sort_ts, c.email, c.domain,
                                   count(*) OVER (PARTITION BY c.company_id, c.tier, c.domain) AS domain_count
                            FROM (
                                SELECT k.company_id, k.tier, k.sort_ts, k.email,
                                       lower(btrim(split_part(k.email, '@', 2))) AS domain
                                FROM work_email_domain_candidates k
                                WHERE length(k.email) - length(replace(k.email, '@', '')) = 1
                                  AND split_part(k.email, '@', 1) <> ''
                            ) c
                            WHERE c.domain ~ '^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$'
                              AND length(c.domain) <= 253
                              AND NOT EXISTS (
                                  SELECT 1 FROM unnest(blocked) b
                                  WHERE c.domain = b OR c.domain LIKE '%.' || b)
                        ) v
                        ORDER BY v.company_id, v.tier,
                                 CASE WHEN v.tier = 3 THEN -v.domain_count ELSE 0 END,
                                 CASE WHEN v.tier = 3 THEN v.domain COLLATE "C" END,
                                 v.sort_ts, v.email, v.domain
                    ) d
                    WHERE s.company_id = d.company_id
                      AND (s.work_email_primary_domain IS NULL OR btrim(s.work_email_primary_domain) = '');

                    DROP TABLE pg_temp.work_email_domain_candidates;
                END
                $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
