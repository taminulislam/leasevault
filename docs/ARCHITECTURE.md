# LeaseVault architecture

LeaseVault is a three-project .NET 8 solution: a Razor Pages/minimal API host, an infrastructure
library (EF Core, storage, search, hosted services) and a dependency-free core library that holds
the domain model and every business rule.

## Component diagram

```mermaid
flowchart LR
    subgraph Clients
        Browser["Browser<br/>Bootstrap 5 + jQuery + DataTables"]
        API["API clients<br/>curl / integrations"]
    end

    subgraph Web["LeaseVault.Web (ASP.NET Core 8)"]
        Pages[Razor Pages<br/>Documents, Leases, Properties,<br/>Tenants, Approvals, Search, Audit, Retention]
        Endpoints["Minimal API<br/>POST /api/documents/:id/versions<br/>GET /api/documents/:id/versions/:version<br/>POST /api/documents/:id/checkout + checkin<br/>GET /api/search"]
        Auth["Authentication<br/>Microsoft.Identity.Web (Entra ID)<br/>or dev cookie fallback"]
        Policies["Group policies<br/>Agents / Legal / Owners / Admins"]
        Health["GET /health"]
    end

    subgraph Infra["LeaseVault.Infrastructure"]
        Services[Application services<br/>DocumentService, ApprovalService,<br/>RetentionService, LeaseExpiryNotifier]
        Db[("LeaseVaultDbContext<br/>EF Core")]
        Storage{{IDocumentStorage}}
        Search{{ISearchService}}
        Email{{IEmailSender}}
        Hosted[BackgroundServices<br/>LeaseExpiryReminderService<br/>ApprovalEscalationService<br/>RetentionSweepService]
    end

    subgraph Core["LeaseVault.Core (no dependencies)"]
        Domain["Entities + rules<br/>Document check-in/out and versions<br/>ApprovalWorkflow chain<br/>RetentionCalculator<br/>Lease expiry rules"]
        Abstractions[Abstractions<br/>IDocumentStorage, ISearchService,<br/>IEmailSender, IAuditLog, IClock, ICurrentUser]
    end

    subgraph Azure
        SQL[("Azure SQL<br/>full-text catalog")]
        Blob[("Blob Storage<br/>block upload + versioning")]
        KV[Key Vault]
        AI[Application Insights]
        Entra[Entra ID groups]
    end

    subgraph Local["Local fallback"]
        SQLite[("SQLite app.db<br/>FTS5 virtual table")]
        Disk[("App_Data/storage")]
    end

    Browser --> Pages
    Browser --> Health
    Browser --> Endpoints
    API --> Endpoints
    Pages --> Auth --> Policies
    Pages --> Services
    Endpoints --> Services
    Services --> Db
    Services --> Storage
    Services --> Email
    Pages --> Search
    Endpoints --> Search
    Hosted --> Services
    Services --> Domain
    Db --> SQL
    Db --> SQLite
    Search --> SQL
    Search --> SQLite
    Storage --> Blob
    Storage --> Disk
    Auth --> Entra
    Web --> KV
    Web --> AI
```

### Layering rules

| Layer | May reference | Responsibilities |
|-------|---------------|------------------|
| `LeaseVault.Core` | nothing | Entities, enums, domain exceptions, approval chain, check-in/out, version and retention rules, abstractions |
| `LeaseVault.Infrastructure` | Core | EF Core model + seeding, `EfAuditLog`, Azure Blob / local disk storage, SQL Server / SQLite search, e-mail, application services, hosted services, DI wiring |
| `LeaseVault.Web` | Core, Infrastructure | Razor Pages, minimal API endpoints, authentication/authorization, Serilog, health checks |
| `LeaseVault.Tests` | all | Domain rule tests, infrastructure tests on SQLite in-memory, `WebApplicationFactory` integration tests |

## Provider selection

Everything environment-specific is chosen from configuration at start-up
(`ServiceCollectionExtensions.AddLeaseVaultInfrastructure`):

| Concern | Configuration | Azure implementation | Local fallback |
|---------|---------------|----------------------|----------------|
| Database | `ConnectionStrings:Default` | `UseSqlServer` (Azure SQL) | `UseSqlite("Data Source=app.db")` |
| Search | derived from provider | `SqlServerFullTextSearchService` (`CONTAINSTABLE` / `FREETEXTTABLE`) | `SqliteFtsSearchService` (FTS5 `DocumentSearch` virtual table with sync triggers; `LIKE` if FTS5 is missing) |
| Document content | `Storage:AccountUri` / `Storage:ConnectionString` | `AzureBlobDocumentStorage` (staged blocks + `CommitBlockList`, blob versioning listed via `VersionId`, `DefaultAzureCredential`) | `LocalDiskDocumentStorage` |
| Identity | `AzureAd:ClientId` | Microsoft.Identity.Web OpenID Connect + `EntraGroupClaimsTransformation` | Cookie login with `DevUserDirectory` |
| E-mail | (extension point) | SMTP/SendGrid via `IEmailSender` | `LoggingEmailSender` |

## Request flows

### Upload a version (chunked)

1. `POST /api/documents/{id}/versions` (raw body or multipart) is authorised by the
   `RequireContributor` policy.
2. `DocumentService.AddVersionAsync` loads the aggregate and calls `Document.EnsureCanAddVersion`
   *before* reading the body, so a locked or archived document fails fast.
3. The request stream is handed to `IDocumentStorage.UploadAsync`. The blob implementation stages
   4 MiB blocks and commits the block list; SHA-256 and size are computed on the fly.
4. `Document.AddVersion` applies the rules (lock owner, duplicate hash, empty content, approved ->
   draft) and appends an immutable `DocumentVersion`.
5. `EfAuditLog` appends an `AuditEntry` in the same `SaveChanges`.

### Approval chain

`ApprovalWorkflow.Start` builds the fixed Agent -> Legal -> Owner steps, activates the first and
stamps a due date from the configurable SLA. `Approve` / `Reject` / `Delegate` enforce that only the
step's assignee, delegate or escalation target may decide. `ApprovalEscalationService` runs
`ApprovalService.EscalateOverdueAsync` on an interval; an overdue step is escalated to the next
role's assignee, then the Owner, then the configured fallback owner, with a fresh SLA each time.

## Data model

```mermaid
erDiagram
    Property ||--o{ Unit : has
    Property ||--o{ Document : "scoped to"
    Unit ||--o{ Lease : "let under"
    Tenant ||--o{ Lease : signs
    Tenant ||--o{ Document : "relates to"
    Lease ||--o{ Document : attaches
    RetentionPolicy ||--o{ Document : governs
    Document ||--o{ DocumentVersion : "has versions"
    Document ||--o{ DocumentTag : tagged
    Document ||--o{ ApprovalWorkflow : "submitted via"
    ApprovalWorkflow ||--|{ ApprovalStep : "consists of"

    Property {
        int Id PK
        string Name
        string AddressLine1
        string City
        string State
        string PostalCode
        string PropertyType
    }
    Unit {
        int Id PK
        int PropertyId FK
        string UnitNumber
        int Floor
        int SquareFeet
    }
    Tenant {
        int Id PK
        string Name
        string ContactEmail
        string ContactPhone
        string Industry
    }
    Lease {
        int Id PK
        int UnitId FK
        int TenantId FK
        date StartDate
        date EndDate
        decimal MonthlyRent
        decimal SecurityDeposit
        string Status
        datetime ExpiryReminderSentUtc
    }
    Document {
        int Id PK
        string Title
        string Description
        string Category
        string Status
        int PropertyId FK
        int TenantId FK
        int LeaseId FK
        int RetentionPolicyId FK
        int CurrentVersion
        string CheckedOutBy
        datetime CheckedOutUtc
        string TagsText "full-text indexed"
        string CreatedBy
        datetime CreatedUtc
    }
    DocumentVersion {
        int Id PK
        int DocumentId FK
        int VersionNumber
        string FileName
        string ContentType
        long SizeBytes
        string StorageKey
        string Sha256
        string UploadedBy
        datetime UploadedUtc
        string Comment
    }
    DocumentTag {
        int Id PK
        int DocumentId FK
        string Value
    }
    ApprovalWorkflow {
        int Id PK
        int DocumentId FK
        string Status
        string StartedBy
        datetime StartedUtc
        datetime CompletedUtc
        int StepSlaHours
        string FallbackOwner
    }
    ApprovalStep {
        int Id PK
        int WorkflowId FK
        int Order
        string Role
        string Assignee
        string DelegatedTo
        string EscalatedTo
        string Status
        datetime DueUtc
        datetime DecidedUtc
        string DecidedBy
        string Comment
        int EscalationCount
    }
    AuditEntry {
        long Id PK
        datetime TimestampUtc
        string Actor
        string Action
        string EntityType
        string EntityId
        string Details
    }
    RetentionPolicy {
        int Id PK
        string Name
        string Category
        int RetentionYears
        string Action
        bool LegalHold
    }
```

`AuditEntry` has no relationships on purpose: it is an append-only ledger keyed by entity type and
id, and `LeaseVaultDbContext` throws if any code path tries to update or delete a row (the same
guard makes `DocumentVersion` rows immutable).

## Search index

* **SQL Server** - `db/fulltext.sql` creates `LeaseVaultCatalog` and a full-text index over
  `Documents(Title, Description, TagsText)` with automatic change tracking. Queries use
  `CONTAINSTABLE` with quoted prefix terms joined by `AND`, then relax to `FREETEXTTABLE`.
* **SQLite** - `SqliteFtsSearchService.EnsureIndexAsync` creates the `DocumentSearch` FTS5 table
  (porter + unicode61 tokenizer) and `AFTER INSERT/UPDATE/DELETE` triggers on `Documents`, then
  back-fills existing rows. Ranking uses `bm25()`. When the SQLite build lacks FTS5 the service
  reports `SqliteLike` and scans with `LIKE`.

Both providers share `SearchServiceBase`, which applies the facet filters (property, tenant, tag,
status, category), computes facet counts over the matched set and pages the results for the
DataTables grid.
