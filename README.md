# AWS CertPrep

AI-powered practice-exam app for AWS certifications (AIF-C01, CLF-C02, SAA-C03, MLA-C01).
Generate unlimited practice questions with a free-tier LLM, take practice sets or timed mock
exams, and get an ML-based readiness projection per exam domain.

**Stack:** React 19 + TypeScript (Vite) · ASP.NET Core 10 Web API · SQL Server + EF Core ·
Google Gemini or Groq (free tier) for generation · ML.NET for readiness prediction.

---

## Features

| Area | What it does |
| --- | --- |
| **Reference bank** | 74 real CLF-C02 exam-style items, extracted from `CLF-C02.pdf` and shipped with the API. Each one is classified into its official domain, given a difficulty from its shape, and tagged with the AWS services it tests. All 74 pass the app's own item-format audit, which is the evidence that the format rules match the real exam. |
| **Question generation** | Prompts a free-tier LLM with the official exam blueprint (domains + weights) **plus three real items from the reference bank as style exemplars, and the blueprint topics the bank is still thin on**, then validates every returned item (option count, exactly-one/two correct, no duplicate options, explanation present) and dedupes before saving — by normalised stem hash for an exact repeat, and by the overlap of the stem's meaningful words for a rewording of a question already in the bank. |
| **Question bank** | Browse/filter by certification, domain and difficulty; show answers; delete bad items. |
| **Practice mode** | Immediate per-question feedback with explanation. |
| **Timed mock exam** | Official question count and duration, countdown with auto-submit, feedback withheld until scored. |
| **Scoring** | Overall score vs. the real pass mark, plus per-domain accuracy breakdown. |
| **Readiness (ML.NET)** | Trains a logistic-regression model on your answer history (domain, difficulty, question type, time spent, attempt index) and projects a domain-weighted exam score with study recommendations. Falls back to shrunk observed accuracy until there are 40+ answers. |

### How the reference bank makes generation smarter

The bank is not just extra questions. Every generation call now carries four things derived
from it, which is what stops a free-tier model from drifting into textbook-flavoured items:

1. **Style exemplars.** Three real items — one of them multiple-response, drawn from the domain
   being generated for when possible — shown with their answer key and rationale, as the
   calibration target for stem length, scenario framing and how close the distractors sit.
2. **Coverage steering.** The topics the reference bank tests, ranked by how often it tests them,
   minus the ones already covered twice in your bank. Without this the model keeps returning
   items about the same handful of headline services.
3. **Phrasing rules distilled from the real items.** Business-need framing, the capitalised
   deciding qualifier (`MOST cost-effective`, `LEAST operational overhead`), and options that stay
   parallel in kind.
4. **A self-check pass.** The model is told to drop any item where a second option is defensible,
   where the key is the longest or only specific option, or where the explanation does not say
   why the strongest distractor loses.

Duplicate avoidance also prefers questions from the domain being generated for, since those are
the ones a new item is most likely to re-tread. A returned item is then dropped if it is a
rewording of one already stored — a stem hash only catches a character-for-character repeat,
and a model asked to cover the same blueprint twice rewrites rather than repeats ("Which pillar
of the Well-Architected Framework focuses on…" / "Which Well-Architected Framework pillar focuses
on…"). Both used to land in the bank, and one practice set could then draw the pair.

To rebuild the bank from a PDF (`pdftotext` comes with poppler / Xpdf):

```powershell
pdftotext -layout -enc UTF-8 CLF-C02.pdf clf.txt
python tools\extract-reference-bank.py clf.txt server\AwsCertPrep.Api\Data\ReferenceBank\CLF-C02.json
```

The extractor is a faithful extraction only — stem, options, answer key, explanation, reference
links — and prints any item it drops as malformed. Domain, difficulty and service tags are
derived at load time in `ReferenceBank.cs`, so re-running the extractor never loses classification.
Drop a `<CODE>.json` in that folder and it is picked up for that certification automatically.

The material in `CLF-C02.pdf` is third-party practice content, not official AWS exam content, and
the bank inherits that: treat the explanations as study notes, not as an answer key from AWS.

Answer keys are never sent to the browser for an unanswered question — practice feedback comes
from the answer endpoint, and full answers only ship once a session is submitted.

**UI notes**

- Theme defaults to dark; the header control cycles dark → light → follow-system and the choice
  persists per browser.
- The exam runner takes keyboard input: `A`–`E` to pick an option, `Enter` to check or advance,
  `←`/`→` to move between questions.
- Buttons only turn grey when they are genuinely disabled, so an enabled action never reads as inert.
- Confirmations are in-page dialogs, never `window.confirm`, so a timed exam is not interrupted
  by a browser modal.
- Layout is responsive down to ~390px; reading columns are capped so question text stays legible
  on wide screens.

---

## Prerequisites

- .NET SDK 10
- Node.js 20+
- SQL Server (LocalDB, Developer Edition or a container)
- Optional: a free LLM API key (see below). Without one, the app runs in `Offline` mode and
  serves questions from a built-in template bank.

## Setup

### 1. Database

The default connection string in `server/AwsCertPrep.Api/appsettings.json` points at a local
default instance:

```
Server=localhost;Database=AwsCertPrep;Trusted_Connection=True;TrustServerCertificate=True
```

For LocalDB use `Server=(localdb)\\MSSQLLocalDB;...` instead.
In Development, migrations are applied on startup; the exam blueprints, a starter question set
and the embedded reference banks are seeded in every environment (idempotently). Outside
Development, migrations are not applied automatically — see **Production deployment**.

To apply migrations manually:

```powershell
cd server\AwsCertPrep.Api
dotnet ef database update
```

### 2. AI provider (free tiers)

| Provider | Get a key | Config value |
| --- | --- | --- |
| Google Gemini | https://aistudio.google.com/apikey | `Gemini` |
| Groq | https://console.groq.com/keys | `Groq` |
| None (template bank) | — | `Offline` |

Store the key in user-secrets so it never lands in a committed file:

```powershell
cd server\AwsCertPrep.Api
dotnet user-secrets set "Ai:Provider" "Gemini"
dotnet user-secrets set "Ai:ApiKey"   "<your-key>"
```

Environment variables `GEMINI_API_KEY` / `GROQ_API_KEY` also work. `Ai:Model` is optional —
the default is `gemini-flash-latest` for Gemini and `llama-3.3-70b-versatile` for Groq.
Transient `429`/`503` responses (common on free tiers under load) are retried twice with backoff.

### 3. Run

```powershell
.\dev.ps1          # starts API + web app in separate windows
```

Or individually:

```powershell
cd server\AwsCertPrep.Api ; dotnet run --launch-profile http   # http://localhost:5176
cd client                 ; npm install ; npm run dev          # http://localhost:5173
```

Health check: <http://localhost:5176/api/health> · OpenAPI: <http://localhost:5176/openapi/v1.json>

---

## API

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/health` | Liveness: status and active AI provider. Touches nothing. |
| `GET` | `/api/health/ready` | Readiness: database reachable and the bank has servable questions |
| `GET` | `/api/certifications` | Blueprints with domain weights and bank counts |
| `GET` | `/api/certifications/{code}` | One certification |
| `POST` | `/api/questions/generate` | Generate + persist questions |
| `GET` | `/api/questions?certificationCode=…` | Browse the bank (filters: `domainId`, `difficulty`, `skip`, `take`) |
| `DELETE` | `/api/questions/{id}` | Delete an unused question — **admin key** |
| `GET` | `/api/questions/audit` | Dry run: items breaking the official format or scope |
| `POST` | `/api/questions/audit/clean` | Apply the audit (delete or retire) — **admin key** |
| `POST` | `/api/exams` | Start a session (`Practice` or `Exam`) |
| `GET` | `/api/exams/{id}` | Load a session |
| `POST` | `/api/exams/{id}/answers` | Submit one answer |
| `POST` | `/api/exams/{id}/submit` | Score the session (idempotent) |
| `GET` | `/api/exams/history` | Completed sessions |
| `GET` | `/api/insights/{code}/readiness` | ML.NET readiness projection |
| `POST` | `/api/auth/register` | Create an account, returns a token |
| `POST` | `/api/auth/login` | Exchange credentials for a token |
| `GET` | `/api/auth/me` | Who the token belongs to |
| `POST` | `/api/auth/refresh` | Renew a still-valid token |

Learners are identified by the account in their access token, sent as
`Authorization: Bearer <token>`. Every session read and write is scoped to that account, so one
learner cannot open, answer or submit another's session even holding its id — a request from the
wrong account gets the same 404 as one with an invented id.

Everything requires a token except `/api/health*`, `/api/auth/register`, `/api/auth/login`,
`/api/certifications`, and the three read-only question-bank endpoints (`GET /api/questions`,
`/api/questions/audit`, `GET /api/questions/difficulty`) — the bank is shared content, not
anyone's personal data. Generating questions does need an account, because it spends provider
quota against a per-learner limit.

Errors come back as `application/problem+json` with a `traceId` that matches the API log line.
Two endpoints are rate limited (see **Production deployment**) and two require an admin key.

Example:

```bash
curl -X POST http://localhost:5176/api/questions/generate \
  -H "Content-Type: application/json" \
  -d '{"certificationCode":"AIF-C01","difficulty":"Hard","count":5,
       "topicHint":"Bedrock guardrails and inference parameters"}'
```

---

## Project layout

```
AwsCertPrep/
├─ CLF-C02.pdf             Source material for the CLF-C02 reference bank
├─ tools/                  extract-reference-bank.py (PDF text -> reference bank JSON)
├─ server/AwsCertPrep.Api/
│  ├─ Controllers/         Certifications, Questions, Exams, Insights
│  ├─ Data/                AppDbContext, SeedData (exam blueprints), Migrations
│  │  └─ ReferenceBank/    Real exam-style items per certification, embedded at build time
│  ├─ Domain/              EF entities
│  ├─ Dtos/                Request/response contracts
│  ├─ Middleware/          Exception → ProblemDetails mapping
│  ├─ Ml/                  ReadinessService (ML.NET pipeline)
│  └─ Services/            Generation (Gemini/Groq/Offline), prompt building, reference bank,
│                          domain classification, validation, exam logic
└─ client/
   └─ src/
      ├─ api/              Typed fetch client + shared types
      ├─ components/       Small shared UI pieces
      ├─ hooks/            useCertifications
      ├─ pages/            Dashboard, Generate, Bank, Exam, Result, History
      └─ router.tsx        Minimal hash router
```

`react-router-dom` is deliberately absent: every current release sits inside an open
high-severity advisory range, and this app's five flat views need ~60 lines of routing.

---

## Production deployment

### Build and run

```powershell
dotnet publish server\AwsCertPrep.Api -c Release -o out\api
cd client ; npm ci ; npm run build        # static bundle in client\dist
```

Serve `client\dist` from any static host. Leave `VITE_API_BASE_URL` unset when the SPA and the
API share an origin (or sit behind one proxy that forwards `/api`) — the bundle then calls the
same origin it was served from. Set it only for a genuinely cross-origin API, and list that SPA
origin in `Cors:AllowedOrigins` or the browser will be refused.

### Configuration

Everything below is settable as an environment variable using `__` for `:`, e.g.
`ConnectionStrings__Default`, `Ai__ApiKey`, `RateLimits__GeneratePerHour`.

| Key | Default | Why it matters in production |
| --- | --- | --- |
| `ConnectionStrings:Default` | LocalDB-style localhost | Required. Use a least-privilege SQL login, not `sa`. |
| `Database:MigrateOnStartup` | `true` in Development, else `false` | Off by default because two instances racing one migration corrupts a schema. Run `dotnet ef database update` from the deploy pipeline; the API logs any pending migration at startup. |
| `Database:SeedOnStartup` | `true` | Idempotent blueprint + reference-bank seeding. Safe to leave on. |
| `Cors:AllowedOrigins` | the local Vite origins | Must list the deployed SPA origin. Startup warns if it still only lists localhost. |
| `Hosting:UseHttpsRedirection` | `false` in Development, else `true` | Turn off when a proxy terminates TLS without forwarding `X-Forwarded-Proto`, or you get a redirect loop. |
| `Admin:ApiKey` | unset | Gate for the destructive endpoints, *in addition to* a valid account. Unset means they stay open in Development and are **refused** everywhere else. |
| `Jwt:Key` | unset | HMAC-SHA256 signing secret, at least 32 bytes. Development mints a random one per process (so tokens die with the API); **every other environment refuses to start without it.** Use `dotnet user-secrets set Jwt:Key <secret>` locally and `Jwt__Key` from a secret store in production. |
| `Jwt:Issuer` / `Jwt:Audience` | `AwsCertPrep` / `AwsCertPrep.Spa` | Token issuer and audience claims. |
| `Jwt:AccessTokenMinutes` | `43200` (30 days) | Token lifetime in minutes, 1–525600. The SPA renews on start, every 12 hours, and on tab focus, so this is how long the app may go unopened before you are signed out. Also the window in which a token cannot be revoked. |
| `RateLimits:AuthPerFifteenMinutes` | `10` | Sign-in and registration attempts per client address. |
| `Ai:Provider` / `Ai:ApiKey` / `Ai:Model` | `Offline` | Keep the key in a secret store, never in a file or the SPA bundle. |
| `RateLimits:GeneratePerHour` | `30` | Per learner key. |
| `RateLimits:GeneratePerHourPerAddress` | `60` | Per client address. The learner key is self-asserted, so this is the limit that really caps provider spend. |
| `RateLimits:InsightsPerMinute` | `20` | Readiness trains an ML.NET model per cold call. |
| `RateLimits:GlobalPerMinute` | `300` | Catch-all per client address. |

Rejections return `429` with `Retry-After` and a `problem+json` body. Health endpoints are
exempt from every limit.

### What is hardened, and what still is not

Already handled: exam sessions are owner-scoped; timed exams expire server-side (the browser
countdown is no longer the only thing stopping late answers); destructive endpoints need a key;
generation and readiness are rate limited per authenticated account *and* per address; provider error bodies
are logged rather than returned; responses carry `nosniff`, `DENY`, `no-referrer`, a locked-down
CSP and HSTS; request bodies are capped at 128 KB; readiness results are cached against the
answer history they were computed from; logs are JSON outside Development; and startup warns
about the settings that are fine locally and wrong in production.

### Authentication

Email and password accounts via ASP.NET Core Identity, with an HS256 JWT bearer token. Passwords
are at least 10 characters with a digit and a capital; Identity locks an account for 15 minutes
after 5 failed attempts, and `/api/auth/login` and `/register` are additionally rate limited per
client address. Authorization fails closed: a fallback policy authenticates every endpoint unless
it says `[AllowAnonymous]` in so many words.

What that does **not** cover, stated plainly:

- **Registration is open.** Anyone who can reach the API can create an account and spend LLM
  quota inside the rate limits. The network perimeter still matters — put the deployment behind
  the front-door auth you already run before exposing it beyond a trusted network.
- **The token lives in `localStorage`,** so an XSS bug in the SPA is a stolen session. This is
  the accepted cost of not using an HttpOnly cookie across two origins in development.
- **Sessions are designed not to expire in normal use.** The token lasts 30 days
  (`Jwt:AccessTokenMinutes`) and the SPA renews it on every start, every 12 hours while open, and
  whenever a hidden tab becomes visible again. So opening the app at all inside the window rolls
  it forward and you stay signed in indefinitely; the only ways out are signing out, clearing site
  data, or leaving it untouched for a full 30 days.
- **Tokens cannot be revoked before they expire**, and the window above is now a month rather than
  an hour. There is no refresh-token store and nothing checks the security stamp yet, so changing
  a password does not end a session already in flight, and a token stolen through an XSS bug stays
  usable until it lapses. The token carries the stamp as an `ast` claim, which is what would make
  checking it a one-line change if that trade stops being acceptable.
- **Email is not verified and cannot be reset** — nothing here can send mail.
- There is no logout endpoint. A bearer token is stateless, so signing out is the client deleting
  it; an endpoint that accepted the call and did nothing would only imply a revocation this API
  cannot perform.

### One-off: merging the pre-authentication progress

Before accounts existed the SPA minted a per-browser key, and `localStorage` is scoped per
origin — so a Vite dev server falling back from port 5173 to 5174, or a second browser profile,
silently created a new learner and stranded the old one's progress. `client/vite.config.ts` now
sets `strictPort` so that fails loudly instead.

Two hand-run scripts in `server/AwsCertPrep.Api/Data/Scripts/` clean up after it. Both are
idempotent and deliberately **not** migrations — a migration would replay on every fresh database
and bake one developer's data into the schema history.

| Script | What it does |
| --- | --- |
| `merge-user-keys.sql` | Folds several browser keys into one. Defaults to a dry run; set `@DryRun = 0` to apply, `@PurgeFixtures = 1` to also delete leftover test keys. Folds duplicate lesson rows before re-keying, because `IX_LessonProgress_UserKey_LessonTopicId` is unique. |
| `merge-user-keys.rollback.sql` | Restores from the backup tables the merge creates. |
| `adopt-merged-progress.sql` | Re-keys the merged history onto a registered account. Run it once, after registering. |

---

## Notes and limitations

- Generated questions are study aids, not official AWS exam content. Review them before
  trusting an explanation — LLMs do get AWS service details wrong.
- Progress belongs to an account, not a browser. Registration is open and tokens cannot be
  revoked before they expire — see **Authentication** for what that does and does not protect.
- Readiness confidence is the cross-validated AUC of the trained model; with a small answer
  history it stays near 0.5–0.6, which is honest rather than flattering.
- `dotnet list package --vulnerable --include-transitive` and `npm audit` are both clean as of
  the pinned versions here. The earlier `Microsoft.OpenApi` advisory (GHSA-v5pm-xwqc-g5wc) is
  fixed in 2.12.2, which is what this project references. The OpenAPI document is only mapped in
  Development regardless, so nothing serves it in production.
