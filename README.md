# AmarTools.Voting

An online voting platform built with ASP.NET Core 8 MVC, Entity Framework Core and PostgreSQL.
Organisers create voting programs, add candidates and register voters by email; voters sign in
and cast one vote; every vote is recorded in a per-program SHA-256 hash chain that the results
pages verify.

## Features

| Area | What it does |
|---|---|
| Accounts | Register, email confirmation, sign in (with lockout), forgot / reset password |
| Roles | **Voter** (every new account), **ProgramOwner** (granted by an admin on *Users*), **Admin** (seeded) |
| Programs | Create / edit / publish / delete, start and end time, optional custom link `/v/{slug}` |
| Candidates | Add / delete (frozen once voting starts; candidates with votes cannot be deleted) |
| Voters | Owner registers voters by email; the slot is linked when the voter confirms that email |
| Voting | One vote per voter, rate-limited, serializable transaction |
| Results | Live tally, chart, CSV export, hash-chain integrity check |
| Integrity | Each block hashes the vote's content and the previous block; missing blocks are detected and a background sweep re-creates them |

## Tech stack

- .NET 8, ASP.NET Core MVC + Razor Pages (Identity)
- EF Core 8 + Npgsql (PostgreSQL)
- Polly (retries), Bootstrap 5, Chart.js
- Tests: xUnit, Moq, EF Core InMemory/SQLite, Selenium WebDriver

## Project layout

```
Controllers/        Home, Voting (public), ProgramOwner, VotingAdmin
Services/           VotingService, BlockchainService, ResultsCsv, VoterLinking,
                    SecurityHeaders, Email/SmtpEmailSender, Background/ (vote block queue + sweep)
Models/, Data/      Entities and VotingDbContext
Migrations/         EF Core migrations
Areas/Identity/     Login, Register, ConfirmEmail, ForgotPassword, ResetPassword, ...
Views/              Razor views (shared layouts in Views/Shared)
Tests/              Muskan_Binoy (ProgramOwner), Shaibal (Voting), Afrin (VotingAdmin), shared tests
```

## Local setup

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download) and PostgreSQL, and create a database (e.g. `votingdb`).
2. Store the secrets for this machine (they are not in the repository):

   ```powershell
   dotnet user-secrets set "ConnectionStrings:VotingConnection" "Host=localhost;Port=5432;Database=votingdb;Username=postgres;Password=<your password>;Trust Server Certificate=true;"
   dotnet user-secrets set "Seed:AdminEmail" "admin@amartools.local"
   dotnet user-secrets set "Seed:AdminPassword" "<a strong password>"
   ```

3. Run the app (migrations are applied automatically in Development):

   ```powershell
   dotnet run --launch-profile http
   ```

   Open http://localhost:5256 and sign in with the seeded admin account.

### Email

Confirmation and password-reset emails are sent over SMTP. Without configuration the links are
**written to the console log**, which is enough for local testing. To send real email:

```powershell
dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com"        # or smtp.sendgrid.net, smtp.mailgun.org
dotnet user-secrets set "Email:Username" "you@gmail.com"         # SendGrid: "apikey"
dotnet user-secrets set "Email:Password" "<app password / API key>"
dotnet user-secrets set "Email:FromAddress" "you@gmail.com"
```

### Configuration reference

| Key | Required | Purpose |
|---|---|---|
| `ConnectionStrings:VotingConnection` (or `DATABASE_URL`) | yes | PostgreSQL connection |
| `Seed:AdminEmail`, `Seed:AdminPassword` | first run | Creates the admin account and the roles |
| `Database:ApplyMigrations` | no | Apply migrations at startup (default: on in Development) |
| `Email:SmtpHost`, `SmtpPort`, `Username`, `Password`, `FromAddress`, `EnableSsl` | no | SMTP for account emails |

In environment variables use `__` instead of `:` (e.g. `Email__SmtpHost`).

## Tests

```powershell
dotnet test Tests/AmarTools.Voting.Tests.csproj                                          # everything
dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~ProgramOwner"   # one controller
dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category=Defect"                   # defect regression tests
```

Selenium tests are skipped unless the app is running and these are set:

```powershell
$env:AMARTOOLS_BASE_URL      = "http://localhost:5256"
$env:AMARTOOLS_TEST_EMAIL    = "<ProgramOwner account>"
$env:AMARTOOLS_TEST_PASSWORD = "<password>"
$env:AMARTOOLS_HEADED        = "1"     # optional: show the browser
dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~ProgramOwnerSeleniumFlowTests"
```

Test design documents and results are in `Tests/TestResults/<member>/`
(defect report: `Tests/TestResults/Muskan_Binoy/ProgramOwnerDefectReports.md`).

## Deployment

The app ships as a Docker image (`Dockerfile`) and is described for Render in `render.yaml`
(web service + PostgreSQL, health check on `/health`). Set `Seed__AdminEmail`,
`Seed__AdminPassword` and the `Email__*` variables in the Render dashboard.

GitHub Actions (`.github/workflows/ci.yml`) builds the app and runs the test suite on every push
to `main` and on pull requests.

## Security notes

- Accounts must confirm their email before they can sign in or be linked to a voter slot.
- Anti-forgery tokens on all POSTs, rate limiting (voting, search, admin, account emails), account lockout.
- Security headers on every response (CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer-Policy, Permissions-Policy); CDN files are pinned with Subresource Integrity hashes.
- The voter's IP address is not stored with the ballot.
- Known limitation: a vote row still references the voter (`VoterId`), which enforces one vote per voter but means database administrators can see who voted for whom. See D17 in the defect report.
