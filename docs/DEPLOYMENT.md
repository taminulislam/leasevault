# Deployment

LeaseVault runs on Azure App Service with a `staging` slot, Azure SQL, Blob Storage, Key Vault
and Application Insights. Everything is provisioned by `infra/main.bicep` and deployed by
`azure-pipelines.yml`.

## Azure resources

| Resource | Name pattern | Notes |
|----------|--------------|-------|
| Resource group | `rg-leasevault-<env>` | one per environment |
| App Service plan | `asp-leasevault-<env>` | Linux, S1 or higher (slots require Standard+) |
| Web App | `app-leasevault-<env>` | .NET 8, HTTPS only, `/health` health check, system-assigned managed identity |
| Deployment slot | `app-leasevault-<env>/staging` | own managed identity, same app settings; production swaps from here |
| Azure SQL server + DB | `sql-leasevault-<env>` / `sqldb-leasevault-<env>` | full-text catalog created by `db/fulltext.sql` |
| Storage account | `st<hash><prefix>` (max 24 chars) | blob versioning + 30-day soft delete, shared-key access disabled, container `documents` |
| Key Vault | `kv-leasevault-<env>` | soft delete + purge protection, access policies for both identities |
| Application Insights | `appi-leasevault-<env>` | workspace-based, connected via connection string |
| Log Analytics | `log-leasevault-<env>` | 30-day retention |

Provision with:

```bash
az group create -n rg-leasevault-prod -l eastus2
az deployment group create -g rg-leasevault-prod -f infra/main.bicep \
  -p namePrefix=leasevault environmentName=prod appServiceSku=S1 \
     sqlAdminLogin=lvadmin sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
     keyVaultAdminObjectId="$(az ad signed-in-user show --query id -o tsv)"
```

## Managed identity and Key Vault

The web app and the staging slot each have a **system-assigned managed identity**. No secrets are
stored in the repository or in plain app settings:

* **Key Vault** - the identities get `get`/`list` on secrets. App settings such as
  `ConnectionStrings__Default` and `AzureAd__ClientSecret` are Key Vault references
  (`@Microsoft.KeyVault(VaultName=...;SecretName=...)`), resolved by App Service at start-up.
  Secret names use `--` where the configuration key uses `:`.
* **Blob Storage** - both identities hold *Storage Blob Data Contributor* on the storage account.
  `AzureBlobDocumentStorage` authenticates with `DefaultAzureCredential`, so the same code uses the
  managed identity in Azure and the developer's `az login` / Visual Studio credentials locally.
* **Azure SQL** - the generated connection string uses `Authentication=Active Directory Default`.
  Create a contained user for the identity once:

  ```sql
  CREATE USER [app-leasevault-prod] FROM EXTERNAL PROVIDER;
  ALTER ROLE db_datareader ADD MEMBER [app-leasevault-prod];
  ALTER ROLE db_datawriter ADD MEMBER [app-leasevault-prod];
  ALTER ROLE db_ddladmin  ADD MEMBER [app-leasevault-prod]; -- EnsureCreated / full-text index
  ```

  Repeat for `app-leasevault-prod/slots/staging`.

## Entra ID single sign-on and group RBAC

1. Register an app (`LeaseVault`) with redirect URIs `https://<host>/signin-oidc` for production,
   staging and the custom domain.
2. Add the **groups** optional claim (security groups) to the ID token, or use app roles.
3. Create four security groups and put their object ids in configuration:

   ```json
   "AzureAd": {
     "TenantId": "<tenant guid>",
     "ClientId": "<app guid>",
     "Groups": { "Agents": "<guid>", "Legal": "<guid>", "Owners": "<guid>", "Admins": "<guid>" }
   }
   ```

   `EntraGroupClaimsTransformation` maps the ids to the application group names that drive the
   `RequireAgents`, `RequireLegal`, `RequireOwners`, `RequireAdmins` policies.
4. Store the client secret in Key Vault as `AzureAd--ClientSecret` (or switch to a federated
   credential / certificate and remove the secret entirely).

When `AzureAd:ClientId` is empty the app falls back to the development cookie login; never deploy
that configuration to a public environment.

## Deployment slots and swap

The pipeline deploys every `main` build to the **staging** slot (`dev` environment), runs a
`/health` smoke test, then swaps staging with production in the `prod` environment (protected by a
manual approval). Slot settings (`ASPNETCORE_ENVIRONMENT`) are marked *deployment slot setting* so
they stay with the slot during the swap; connection strings and storage URIs are shared and travel
with the code. Rolling back is another swap (see `CHANGE_MANAGEMENT.md`).

## Custom DNS

1. Add a CNAME `leasevault.contoso.com -> app-leasevault-prod.azurewebsites.net` and the
   `asuid` TXT verification record at your DNS provider.
2. Pass `customHostname=leasevault.contoso.com` to the Bicep deployment (creates the hostname
   binding) or add it in the portal.
3. Create an App Service managed certificate for the hostname and bind it (SNI SSL). Redirect URIs
   in the Entra app registration must include the custom host.

## Application settings reference

| Setting | Purpose |
|---------|---------|
| `ConnectionStrings__Default` | SQL Server connection string; empty = SQLite |
| `Storage__AccountUri` | `https://<account>.blob.core.windows.net`; empty = local disk |
| `Storage__ContainerName` | blob container (default `documents`) |
| `Storage__BlockSizeBytes` | block size for staged uploads (default 4 MiB) |
| `AzureAd__*` | Entra ID settings (see above) |
| `Approvals__StepSlaHours`, `Approvals__EscalationPollMinutes` | approval SLA and escalation cadence |
| `Approvals__DefaultAgent/Legal/Owner/FallbackOwner` | default approver assignments |
| `Reminders__LeadDays`, `Reminders__PollMinutes` | lease-expiry reminder window and cadence |
| `Retention__Enabled`, `Retention__PollHours` | retention sweep switch and cadence |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | telemetry (Serilog sink instructions in `Program.cs`) |

## Docker

```bash
docker build -t leasevault .
docker run -p 8080:8080 -v leasevault-data:/app/App_Data leasevault
```

The image runs as the non-root `app` user and keeps SQLite + local document storage under
`/app/App_Data` when no Azure services are configured.
