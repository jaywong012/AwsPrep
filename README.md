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
| **Question generation** | Prompts a free-tier LLM with the official exam blueprint (domains + weights) **plus three real items from the reference bank as style exemplars, and the blueprint topics the bank is still thin on**, then validates every returned item (option count, exactly-one/two correct, no duplicate options, explanation present) and dedupes by normalised stem hash before saving. |
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
the ones a new item is most likely to re-tread.

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

Learners are identified by the `X-User-Key` header (the SPA generates and stores one per
browser). Swap this for the authenticated subject when you add auth. Every session read and
write is scoped to that key, so one learner cannot open, answer or submit another's session even
holding its id — a request with the wrong key gets the same 404 as one with an invented id.

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
| `Admin:ApiKey` | unset | Gate for the destructive endpoints. Unset means they stay open in Development and are **refused** everywhere else. |
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
generation and readiness are rate limited per learner *and* per address; provider error bodies
are logged rather than returned; responses carry `nosniff`, `DENY`, `no-referrer`, a locked-down
CSP and HSTS; request bodies are capped at 128 KB; readiness results are cached against the
answer history they were computed from; logs are JSON outside Development; and startup warns
about the settings that are fine locally and wrong in production.

Still open, by design: **there is no authentication.** `X-User-Key` separates learners from each
other, not attackers from data — anyone who can reach the API can browse the bank, start
sessions and spend LLM quota inside the rate limits. Put the deployment behind whatever
front-door auth you already run (an identity-aware proxy, an ingress with OIDC, Entra ID app
proxy) before exposing it beyond a trusted network, and replace `X-User-Key` with the
authenticated subject when you do.

---

## Notes and limitations

- Generated questions are study aids, not official AWS exam content. Review them before
  trusting an explanation — LLMs do get AWS service details wrong.
- No authentication yet; `X-User-Key` identifies a browser, and progress is per browser. See
  **Production deployment** for what that does and does not protect.
- Readiness confidence is the cross-validated AUC of the trained model; with a small answer
  history it stays near 0.5–0.6, which is honest rather than flattering.
- `Microsoft.OpenApi` reports advisory GHSA-v5pm-xwqc-g5wc. It only affects apps that *parse*
  untrusted OpenAPI documents; this project only emits one. Remove `AddOpenApi()`/`MapOpenApi()`
  if you need a clean audit.
