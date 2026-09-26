using System.Text;
using System.Text.RegularExpressions;

namespace HR.Architecture.Tests;

/// <summary>
/// CodeQL alert #61 (exposure of private information): guards against Identity-module operational
/// log calls taking raw email / recipient-address values. Operational logs identify a workflow by
/// non-personal ids (administrator id, employee id, invite id, correlation id); the restricted audit
/// store — which is NOT a logger and is not scanned here — is where a business-required email lives.
///
/// <para><b>What is scanned.</b> Every <c>*.cs</c> file under <c>src/Modules/HR.Modules.Identity</c>
/// (excluding <c>bin</c>, <c>obj</c> and <c>Migrations</c>). The test reads raw source text rather
/// than reflecting over compiled types, because a log template is an argument expression, not
/// metadata. Adding Roslyn to this test project was avoided (no new dependency); instead a small
/// C#-aware lexer (<see cref="LogCallScanner"/>) blanks out comments and masks string/char literal
/// contents (regular, verbatim, interpolated incl. nested holes, and raw strings), so that
/// parentheses inside strings and commented-out code never confuse the scan.</para>
///
/// <para><b>What counts as a log call.</b> A member invocation <c>.Log(</c>, <c>.LogTrace(</c>,
/// <c>.LogDebug(</c>, <c>.LogInformation(</c>, <c>.LogWarning(</c>, <c>.LogError(</c>,
/// <c>.LogCritical(</c> or <c>.BeginScope(</c> (log scopes are emitted alongside every entry), plus
/// every <c>[LoggerMessage(...)]</c> source-generated declaration (its attribute template and the
/// parameter names of the partial method it decorates).</para>
///
/// <para><b>Rules (all case-insensitive).</b> A name is "email-like" when it is exactly
/// <c>To</c>, <c>Recipient(s)</c>, <c>Email(s)</c> or <c>EmailAddress(es)</c>, or ends with
/// <c>Email</c>, <c>Emails</c> or <c>EmailAddress(es)</c> (e.g. <c>ToEmail</c>,
/// <c>RecipientEmail</c>, <c>normalizedEmail</c>). Non-address names such as <c>EmailSent</c> or
/// <c>EmailDomains</c> are allowed. A log call is a violation when:
/// <list type="number">
/// <item><description>a message-template placeholder is email-like (<c>{Email}</c>, <c>{@To}</c>,
/// <c>{recipient:l}</c>, ...);</description></item>
/// <item><description>any argument references an email-like identifier or member
/// (<c>req.Email</c>, <c>normalizedEmail</c>, <c>administrator.Email</c>) — this catches an address
/// smuggled in under an innocuous placeholder name. Evaluated per member-access chain: an
/// Email-named segment counts anywhere (<c>req.Email.Trim()</c>), whereas the object-ish words
/// <c>To</c>/<c>Recipient(s)</c> only count as the final segment (<c>recipient.EmployeeId</c> is
/// fine);</description></item>
/// <item><description>the template is an interpolated string (<c>$"..."</c>) — templates must be
/// constant so the placeholders above stay inspectable and values stay structured;</description></item>
/// <item><description>a <c>[LoggerMessage]</c> partial method has an email-like parameter.</description></item>
/// </list></para>
///
/// <para><b>Known limits.</b> The scan is syntactic: it cannot see an address hidden inside an
/// arbitrary object's <c>ToString()</c> or inside a logged exception's message. The latter is
/// covered behaviourally by <c>IdentityOperationalLogEmailExposureTests</c> in
/// <c>HR.Modules.Identity.Tests</c> (email-keyed provider/delivery failures log the exception
/// <em>type</em> only). There is deliberately no allow-list: a genuinely required exception should
/// be discussed and, if accepted, added here with a justification comment.</para>
/// </summary>
public class IdentityOperationalLogEmailGuardTests
{
    [Fact]
    public void Identity_Operational_Log_Calls_Do_Not_Take_Email_Or_Recipient_Address_Values()
    {
        var repoRoot = FindRepoRoot();
        var identityDir = Path.Combine(repoRoot, "src", "Modules", "HR.Modules.Identity");
        Assert.True(Directory.Exists(identityDir), $"Expected Identity module source at '{identityDir}'.");

        var files = Directory.EnumerateFiles(identityDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var normalized = f.Replace('\\', '/');
                return !normalized.Contains("/bin/") && !normalized.Contains("/obj/") && !normalized.Contains("/Migrations/");
            })
            .ToList();

        var totalLogCalls = 0;
        var violations = new List<string>();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var result = LogCallScanner.Scan(File.ReadAllText(file));
            totalLogCalls += result.LogCallCount;
            violations.AddRange(result.Violations.Select(v => $"{relative}:{v}"));
        }

        // Sanity check: if this drops to zero the scanner (or the path) is broken, not the code clean.
        Assert.True(totalLogCalls >= 20,
            $"Expected to find the Identity module's log calls but found only {totalLogCalls} — the scanner is probably broken.");

        Assert.True(violations.Count == 0,
            "Identity operational log calls must not take raw email / recipient-address values (CodeQL #61). " +
            "Log a non-personal identifier (administrator/employee/invite/correlation id) instead, and never a " +
            "masked address. Business-required emails belong in the restricted audit store, not the logger:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    // Proves the scanner itself detects what it claims to, so a silent regression in the lexer
    // cannot turn the guard above into a no-op.
    [Theory]
    [InlineData("logger.LogWarning(\"Invite failed. To={Email}\", id);")]
    [InlineData("logger.LogWarning(\"Invite failed. To={to}\", id);")]
    [InlineData("_logger.LogError(ex, \"Failed {@RecipientEmail}\", x);")]
    [InlineData("logger.LogInformation(\"Sent to {EmailAddress:l}\", x);")]
    [InlineData("logger.LogInformation(\"Sent to {Recipient,10}\", x);")]
    [InlineData("logger.LogWarning(\"Failed \" +\n  \"for {EmployeeId}\", req.Email);")]
    [InlineData("logger.LogError(\"Failed for {Id}\", normalizedEmail);")]
    [InlineData("logger.LogError(\"Failed for {Id}\", req.Email.Trim().ToLowerInvariant());")]
    [InlineData("logger.LogError(\"Failed for {Id}\", recipient);")]
    [InlineData("logger.LogError($\"Failed for {id}\");")]
    [InlineData("logger.Log(LogLevel.Warning, \"x {ToEmail}\", a);")]
    [InlineData("using var s = logger.BeginScope(\"Scope {Email}\", a);")]
    [InlineData("using var s = logger.BeginScope(new Dictionary<string, object> { [\"Id\"] = administrator.Email });")]
    [InlineData("[LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = \"Failed {UserEmail}\")]\nstatic partial void Failed(ILogger logger, string userEmail);")]
    [InlineData("[LoggerMessage(Level = LogLevel.Warning, Message = \"Failed {UserId}\")]\nstatic partial void Failed(ILogger logger, Guid userId, string email);")]
    public void Scanner_Flags_Email_Like_Log_Usage(string source)
    {
        var result = LogCallScanner.Scan(source);
        Assert.NotEmpty(result.Violations);
    }

    [Theory]
    [InlineData("logger.LogInformation(\"Dispatch attempted. EmailSent={EmailSent}\", emailSent);")]
    [InlineData("logger.LogWarning(\"Rejected {RejectedCount} ({EmailDomains})\", n, string.Join(\", \", domains));")]
    [InlineData("logger.LogWarning(\"Invite failed. EmployeeId={EmployeeId} InviteId={InviteId}\", req.EmployeeId, invite.Id);")]
    [InlineData("logger.LogError(ex, \"Failed recipient {EmployeeId} in batch {BatchId}\", recipient.EmployeeId, batchId);")]
    [InlineData("// logger.LogWarning(\"old {Email}\", req.Email);\nlogger.LogWarning(\"ok {Id}\", id);")]
    [InlineData("/* logger.LogWarning(\"old {Email}\", req.Email); */ logger.LogWarning(\"ok ({ExceptionType})\", ex.GetType().FullName);")]
    [InlineData("logger.LogWarning(\"literal braces {{Email}} and paren ) in text {Id}\", id);")]
    [InlineData("var subject = $\"Hello {email}\"; logger.LogInformation(\"Sent {Id}\", id);")]
    [InlineData("[LoggerMessage(Level = LogLevel.Warning, Message = \"Failed {UserId}\")]\nstatic partial void Failed(ILogger logger, Guid userId);")]
    public void Scanner_Allows_Non_Personal_Log_Usage(string source)
    {
        var result = LogCallScanner.Scan(source);
        Assert.True(result.Violations.Count == 0, string.Join(Environment.NewLine, result.Violations));
        Assert.True(result.LogCallCount > 0);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Modules"))
                && (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root by walking up from '{AppContext.BaseDirectory}'.");
    }
}

/// <summary>
/// Syntactic scanner used by <see cref="IdentityOperationalLogEmailGuardTests"/>. See that class's
/// remarks for the rules. Works on two same-length views of the source: <c>code</c> (comments
/// blanked to spaces) and <c>masked</c> (comments blanked AND string/char literal contents replaced
/// with '_'), so character offsets line up between them.
/// </summary>
internal static class LogCallScanner
{
    internal sealed record ScanResult(int LogCallCount, IReadOnlyList<string> Violations);

    private static readonly Regex LogInvocation = new(
        @"\.\s*(?<name>Log(?:Trace|Debug|Information|Warning|Error|Critical)?|BeginScope)\s*(?:<[^()<>]*>)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex LoggerMessageAttribute = new(@"\[\s*LoggerMessage\s*\(", RegexOptions.Compiled);

    private static readonly Regex Identifier = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    // {Name}, {@Name}, {$Name}, {Name:format}, {Name,alignment} — but not escaped {{Name}}.
    private static readonly Regex Placeholder = new(
        @"(?<!\{)\{\s*[@$]?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:[,:][^{}]*)?\}(?!\})", RegexOptions.Compiled);

    internal static bool IsEmailLike(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "to" or "recipient" or "recipients" or "email" or "emails" or "emailaddress" or "emailaddresses"
            || n.EndsWith("email", StringComparison.Ordinal)
            || n.EndsWith("emails", StringComparison.Ordinal)
            || n.EndsWith("emailaddress", StringComparison.Ordinal)
            || n.EndsWith("emailaddresses", StringComparison.Ordinal);
    }

    public static ScanResult Scan(string source)
    {
        var (code, masked, literals) = Lex(source);
        var violations = new List<string>();
        var count = 0;

        foreach (Match match in LogInvocation.Matches(masked))
        {
            var open = match.Index + match.Length - 1;
            var close = FindClosingParen(masked, open);
            if (close < 0)
                continue;

            count++;
            var line = LineOf(source, match.Index);
            var callName = match.Groups["name"].Value;
            CheckArguments(code, masked, literals, open + 1, close, $"{line} ({callName})", violations);
        }

        foreach (Match match in LoggerMessageAttribute.Matches(masked))
        {
            var open = match.Index + match.Length - 1;
            var close = FindClosingParen(masked, open);
            if (close < 0)
                continue;

            count++;
            var line = LineOf(source, match.Index);
            CheckArguments(code, masked, literals, open + 1, close, $"{line} ([LoggerMessage])", violations);

            // Parameters of the decorated partial method: the first (...) after the attribute.
            var methodOpen = masked.IndexOf('(', close + 1);
            var methodClose = methodOpen < 0 ? -1 : FindClosingParen(masked, methodOpen);
            if (methodClose < 0)
                continue;

            var parameters = masked[(methodOpen + 1)..methodClose]
                .Split(',')
                .Select(p => Identifier.Matches(p).LastOrDefault()?.Value)
                .Where(p => p is not null);
            foreach (var parameter in parameters)
            {
                if (IsEmailLike(parameter!))
                    violations.Add($"{line} ([LoggerMessage]): partial method parameter '{parameter}' is an email/recipient value");
            }
        }

        return new ScanResult(count, violations);
    }

    private static void CheckArguments(
        string code, string masked, IReadOnlyList<Literal> literals, int start, int end, string location, List<string> violations)
    {
        foreach (var literal in literals.Where(l => l.Start >= start && l.End <= end))
        {
            if (literal.IsInterpolated)
            {
                violations.Add($"{location}: interpolated log template — templates must be constant");
                continue;
            }

            foreach (Match placeholder in Placeholder.Matches(literal.Content))
            {
                var name = placeholder.Groups["name"].Value;
                if (IsEmailLike(name))
                    violations.Add($"{location}: template placeholder '{{{name}}}' takes an email/recipient value");
            }
        }

        // Identifiers in the argument code (string contents are masked, so template words don't count),
        // evaluated per member-access chain. Any Email-named segment is a violation wherever it sits
        // (req.Email.Trim() still logs the address). The bare words To/Recipient(s) commonly name an
        // object rather than an address (recipient.EmployeeId is fine), so they only count as the
        // final segment of a chain.
        foreach (Match chain in MemberChain.Matches(masked[start..end]))
        {
            var segments = chain.Value.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(seg => seg.TrimEnd('?', '!').Trim())
                .ToArray();

            for (var k = 0; k < segments.Length; k++)
            {
                var segment = segments[k];
                if (!IsEmailLike(segment))
                    continue;

                var isObjectWord = segment.ToLowerInvariant() is "to" or "recipient" or "recipients";
                if (isObjectWord && k < segments.Length - 1)
                    continue;

                violations.Add($"{location}: argument references email/recipient value '{chain.Value.Trim()}'");
                break;
            }
        }
    }

    private static readonly Regex MemberChain = new(
        @"[A-Za-z_][A-Za-z0-9_]*(?:\s*[?!]?\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.Compiled);

    private static int FindClosingParen(string masked, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < masked.Length; i++)
        {
            if (masked[i] == '(')
                depth++;
            else if (masked[i] == ')' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static int LineOf(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < source.Length; i++)
        {
            if (source[i] == '\n')
                line++;
        }

        return line;
    }

    internal sealed record Literal(int Start, int End, string Content, bool IsInterpolated);

    // ---- Minimal C# lexer -------------------------------------------------------------------

    private static (string Code, string Masked, IReadOnlyList<Literal> Literals) Lex(string source)
    {
        var code = new StringBuilder(source);
        var masked = new StringBuilder(source);
        var literals = new List<Literal>();
        var i = 0;
        LexCode(source, ref i, code, masked, literals, stopAtCloseBrace: false);
        return (code.ToString(), masked.ToString(), literals);
    }

    private static void Blank(StringBuilder sb, int from, int to)
    {
        for (var k = from; k < to && k < sb.Length; k++)
        {
            if (sb[k] is not ('\r' or '\n'))
                sb[k] = ' ';
        }
    }

    private static void Mask(StringBuilder sb, int from, int to)
    {
        for (var k = from; k < to && k < sb.Length; k++)
        {
            if (sb[k] is not ('\r' or '\n'))
                sb[k] = '_';
        }
    }

    /// <summary>Lexes code until end of input (or an unmatched '}' when inside an interpolation hole).</summary>
    private static void LexCode(
        string s, ref int i, StringBuilder code, StringBuilder masked, List<Literal> literals, bool stopAtCloseBrace)
    {
        var braceDepth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                var start = i;
                while (i < s.Length && s[i] != '\n')
                    i++;
                Blank(code, start, i);
                Blank(masked, start, i);
                continue;
            }

            if (c == '/' && next == '*')
            {
                var start = i;
                var endIdx = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = endIdx < 0 ? s.Length : endIdx + 2;
                Blank(code, start, i);
                Blank(masked, start, i);
                continue;
            }

            if (c == '\'')
            {
                var start = i++;
                while (i < s.Length && s[i] != '\'')
                    i += s[i] == '\\' ? 2 : 1;
                i++;
                Mask(masked, start + 1, i - 1);
                continue;
            }

            if (c is '"' or '@' or '$')
            {
                if (TryLexString(s, ref i, code, masked, literals))
                    continue;
            }

            if (stopAtCloseBrace)
            {
                if (c == '{')
                    braceDepth++;
                else if (c == '}')
                {
                    if (braceDepth == 0)
                        return; // caller consumes the closing '}'
                    braceDepth--;
                }
            }

            i++;
        }
    }

    private static bool TryLexString(string s, ref int i, StringBuilder code, StringBuilder masked, List<Literal> literals)
    {
        var start = i;
        var p = i;
        var dollars = 0;
        var verbatim = false;
        while (p < s.Length && (s[p] == '$' || s[p] == '@'))
        {
            if (s[p] == '$') dollars++;
            else verbatim = true;
            p++;
        }

        if (p >= s.Length || s[p] != '"')
            return false; // an identifier like @class, or a stray '$'

        // Raw string literal: three or more quotes.
        var quoteRun = 0;
        while (p + quoteRun < s.Length && s[p + quoteRun] == '"')
            quoteRun++;
        if (quoteRun >= 3)
        {
            var delimiter = new string('"', quoteRun);
            var contentStart = p + quoteRun;
            var endIdx = s.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
            var contentEnd = endIdx < 0 ? s.Length : endIdx;
            i = endIdx < 0 ? s.Length : endIdx + quoteRun;
            literals.Add(new Literal(start, i, s[contentStart..contentEnd], dollars > 0));
            Mask(masked, contentStart, contentEnd);
            return true;
        }

        var interpolated = dollars > 0;
        var content = new StringBuilder();
        p++; // opening quote
        var bodyStart = p;
        while (p < s.Length)
        {
            var c = s[p];
            if (!verbatim && c == '\\')
            {
                content.Append(c);
                if (p + 1 < s.Length) content.Append(s[p + 1]);
                p += 2;
                continue;
            }

            if (c == '"')
            {
                if (verbatim && p + 1 < s.Length && s[p + 1] == '"')
                {
                    content.Append('"');
                    p += 2;
                    continue;
                }

                break;
            }

            if (interpolated && c == '{')
            {
                if (p + 1 < s.Length && s[p + 1] == '{')
                {
                    content.Append("{{");
                    p += 2;
                    continue;
                }

                // Interpolation hole: lex as code (it may contain nested strings) up to its '}'.
                p++;
                LexCode(s, ref p, code, masked, literals, stopAtCloseBrace: true);
                content.Append("{hole}");
                p++; // closing '}'
                continue;
            }

            if (!verbatim && c == '\n')
                break; // unterminated regular string; stop at end of line

            content.Append(c);
            p++;
        }

        var bodyEnd = Math.Min(p, s.Length);
        i = Math.Min(p + 1, s.Length);
        literals.Add(new Literal(start, i, content.ToString(), interpolated));
        Mask(masked, bodyStart, bodyEnd);
        return true;
    }
}
