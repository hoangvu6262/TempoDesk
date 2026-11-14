# SCRUM-27 Authentication rate limits and security audit design

## Goal

Reduce brute-force and abuse risk on authentication endpoints and enrich the
security audit trail without recording credentials, tokens, cookies, headers,
or other sensitive request data.

## Scope

This ticket adds:

- fixed-window rate limits for register, login, forgot-password, and refresh;
- appropriate IP and normalized-account keys;
- consistent HTTP 429 ProblemDetails and `Retry-After` responses;
- request context on every authentication audit event;
- basic configuration for rate-limit policies and audit retention;
- automated policy, recovery, audit, and redaction tests.

It does not add Redis, distributed counters, an audit-query API, an audit UI, a
retention cleanup job, CAPTCHA, account lockout changes, or rate limits for
non-authentication endpoints.

## Approved policies

| Policy | IP limit | Account limit | Window |
|---|---:|---:|---:|
| Register | 5 | none | 15 minutes |
| Login | 30 | 5 | 5 minutes |
| Forgot password | 20 | 3 | 15 minutes |
| Refresh | 30 | none | 1 minute |

Register and refresh are IP-scoped. Login and forgot-password must acquire both
the IP and account partitions. The account partition key is the lowercase
SHA-256 hash of the trimmed, normalized email; raw email is not retained in the
limiter.

Counters are process-local for this ticket. TempoDesk currently runs without
Redis, and the architecture explicitly defers distributed rate limits until
multiple API instances create a measured need.

## Request flow and response contract

Controllers acquire the required policy before invoking the authentication
service. This occurs after ASP.NET Core model binding, allowing login and
forgot-password to use the validated email without buffering or reparsing JSON
in middleware.

An allowed request continues unchanged. A denied request does not call the
authentication service and returns HTTP 429 with:

- `Content-Type: application/problem+json`;
- the existing safe ProblemDetails fields and trace ID;
- `Retry-After: <whole seconds>` using the longest blocked partition;
- no partition key, email, IP, credential, token, or internal counter value.

`Retry-After` is at least one second and is calculated consistently from the
remaining fixed window. Requests are accepted again after `TimeProvider`
advances beyond the window.

## Rate-limiter component

Add a singleton authentication limiter with an application-facing contract
that accepts a policy name, remote IP, optional normalized account identifier,
and cancellation token. It returns either allowed or a retry duration.

The implementation uses thread-safe fixed-window counters and `TimeProvider`.
Expired partitions are removed during normal acquisition so stale account keys
do not accumulate indefinitely. Configuration supplies exact limits and window
durations and is validated at startup.

The controller never passes password, raw reset token, raw refresh token,
cookie, JWT, or Authorization header to the limiter. A missing remote IP uses a
single explicit `unknown` partition so bypass is not possible.

## Audit context

The existing `security_audit_logs` table already contains `ip_address`,
`user_agent`, `trace_id`, and `metadata`. Extend the EF entity/mapping to use
these columns without a migration.

Add a scoped audit context populated by `RequestBoundaryMiddleware` after it
establishes the request activity. It contains:

- remote IP address;
- at most the first 1,024 User-Agent characters;
- the current activity trace ID.

The shared Identity DbContext enriches newly added `SecurityAuditLog` rows from
this context immediately before saving. Existing service methods keep creating
event rows with event type, user ID, outcome, and time; they do not accept HTTP
headers or request objects.

Background/non-HTTP operations have an empty context and still write valid
audit rows.

## Audit events

Preserve and enrich the events already emitted for:

- registration and email verification;
- login success/failure;
- refresh success/failure and refresh-token reuse;
- logout;
- single-session and account-wide revocation.

SCRUM-26 adds and this ticket enriches:

- `password.reset.requested`;
- `password.reset`.

Rate-limit denial writes `auth.rate_limited` with no account identifier in
metadata. The audit user ID remains null unless the authenticated actor is
already safely known. Metadata is a JSON object containing only whitelisted,
non-sensitive values such as the policy name; arbitrary request fields are
never serialized.

## Redaction boundary

Neither request logging, rate-limit state, audit columns, nor audit metadata may
contain:

- passwords;
- raw or hashed reset/refresh/email-verification tokens;
- cookie contents;
- Authorization header contents or JWTs;
- full request bodies;
- normalized/raw email partition keys.

Route templates, HTTP method, status, duration, event type, outcome, actor ID
when known, IP, bounded User-Agent, trace ID, and whitelisted policy name are
allowed.

## Configuration and retention

Add a `Security` configuration section with:

- the four approved limits and windows;
- `AuditRetentionDays = 90`, valid from 1 through 3650.

Rate limits and windows must be positive and bounded. Production and local
configuration use the approved defaults. Tests may override them with smaller
limits while preserving the same validation.

Retention is configuration-only in this ticket. No deletion job is added; a
future maintenance ticket may consume `AuditRetentionDays` when operational
retention automation is required.

## Testing

Use test-first changes to cover:

- valid defaults and rejection of zero/negative limits, windows, and retention;
- each endpoint blocking exactly after its approved threshold;
- login and forgot-password enforcing both IP and account partitions;
- distinct account hashes not sharing account quota;
- the same IP sharing IP quota across accounts;
- denied requests not reaching authentication services;
- consistent 429 ProblemDetails and integer `Retry-After`;
- automatic recovery after the fixed window using a manual time provider;
- concurrent acquisition never allowing more than the configured permit count;
- audit rows receiving IP, bounded User-Agent, and the request trace ID;
- rate-limit audit events containing only the policy name;
- audit/log scans containing no submitted password, token, cookie,
  Authorization value, JWT, email, or account partition hash;
- all existing and SCRUM-26 authentication tests continuing to pass.

## Out of scope

- Distributed rate limits or Redis.
- CAPTCHA, bot scoring, or WAF configuration.
- Audit browsing/export APIs and UI.
- Automated audit deletion.
- Changes to Identity lockout behavior.
- Rate limits outside register, login, forgot-password, and refresh.
