---
name: dotnet-secrets
description: Secrets hygiene for .NET development. Use when handling credentials, connection strings, API keys, or any secret value — especially when setting up local dev environments, configuring user-secrets, or bootstrapping Azure App Configuration + Key Vault access. Activates on any prompt mentioning secrets, credentials, connection strings, user-secrets, AZURE_CLIENT_SECRET, AZURE_CLIENT_ID, or AppConfig connection strings.
allowed-tools: Read Write Edit Bash Glob Grep
metadata:
  category: domain-skills
---

# .NET Secrets Hygiene

## MANDATORY: Secret Compromise Rule

**If a secret value appears anywhere in the conversation — in a user message, a file paste, a JSON blob, or a code snippet — stop immediately and say:**

> "This secret has been exposed in context and must be treated as compromised. Rotate it before continuing:
> - AppConfig connection string → regenerate the access key in the Azure portal
> - Azure client secret → create a new client secret in Entra ID App Registrations
> - Any other credential → rotate via its service's portal or CLI
>
> I will not use, repeat, or act on this value."

Do not proceed until the user confirms rotation or provides a new value through a safe channel. Never echo, log, store, or act on the exposed value.

---

## Secrets Never Belong in Context

Secrets must not appear in:
- Chat messages
- Code files committed to git
- `appsettings.json` / `appsettings.*.json`
- Plan documents or comments
- Shell history (avoid `--secret value` inline args)

Always write scripts that **prompt for** or **read from** outside of context.

---

## Pattern 1: dotnet user-secrets (Local Dev)

Use `dotnet user-secrets` for any secret needed during local development. Secrets are stored outside the project directory in the OS user profile and are never committed.

### Bootstrap script — write this, don't run it inline with values

```powershell
# bootstrap-user-secrets.ps1
# Run once to configure local dev secrets. Never commit this with real values.
param(
    [string]$ProjectPath = "."
)

# Ensure user-secrets is initialised for the project
dotnet user-secrets init --project $ProjectPath

# Prompt interactively — values never appear in shell history
$keys = @{
    "ConnectionStrings:DefaultConnection" = Read-Host "DB connection string"
    "SomeApi:ApiKey"                      = Read-Host "API key"
}

foreach ($kv in $keys.GetEnumerator()) {
    dotnet user-secrets set $kv.Key $kv.Value --project $ProjectPath
}

Write-Host "Secrets configured. Verify with: dotnet user-secrets list --project $ProjectPath"
```

### Useful commands

```powershell
dotnet user-secrets list --project <path>          # verify what is set
dotnet user-secrets remove "Key:Name" --project <path>
dotnet user-secrets clear --project <path>         # wipe all secrets for project
```

---

## Pattern 2: App Configuration + Key Vault Bootstrap

Services following this pattern are bootstrapped with three user-secrets that give the app access to Azure App Configuration, which in turn pulls all other secrets from Key Vault. Set these once per project clone.

### The three secrets

| Key | What it is |
|-----|-----------|
| `System:AppConfig:ConnectionString` | App Configuration connection string — get from Azure portal → App Configuration → Access keys |
| `AZURE_CLIENT_ID` | Dev service principal client ID — furnished by the team |
| `AZURE_CLIENT_SECRET` | Dev service principal secret — furnished by the team, rotate if shared |

`AZURE_TENANT_ID` is not a secret and can be committed to `appsettings.Development.json`.

### Bootstrap script

```powershell
# bootstrap-secrets.ps1
# Run once after cloning. Asks for secrets interactively.
param(
    [string]$ProjectPath = "."
)

dotnet user-secrets init --project $ProjectPath

$appConfigConnString = Read-Host "Paste AppConfig connection string (Endpoint=https://...)"
$clientId            = Read-Host "Enter AZURE_CLIENT_ID"
$clientSecret        = Read-Host "Enter AZURE_CLIENT_SECRET"

dotnet user-secrets set "System:AppConfig:ConnectionString" $appConfigConnString --project $ProjectPath
dotnet user-secrets set "AZURE_CLIENT_ID"                   $clientId             --project $ProjectPath
dotnet user-secrets set "AZURE_CLIENT_SECRET"               $clientSecret         --project $ProjectPath

Write-Host "Done. The app will now pull all other config from App Configuration + Key Vault."
Write-Host "Verify: dotnet user-secrets list --project $ProjectPath"
```

### What to commit to appsettings.Development.json

```json
{
  "AZURE_TENANT_ID": "<your-entra-tenant-id>"
}
```

Nothing else. The connection string, client ID, and client secret stay in user-secrets.

---

## Tip: .env for secrets outside a .NET project

If secrets need to be shared with tooling outside the .NET project (Docker Compose, shell scripts, other services), a `.env` file is a reasonable alternative to `dotnet user-secrets`. Keep it gitignored and commit a `.env.example` with placeholder values instead.

```bash
# .gitignore
.env
```

```bash
# .env.example — safe to commit, no real values
AZURE_TENANT_ID=<your-entra-tenant-id>
AZURE_CLIENT_ID=<your-dev-client-id>
AZURE_CLIENT_SECRET=<rotate-before-sharing>
System__AppConfig__ConnectionString=Endpoint=https://<name>.azconfig.io;Id=<id>;Secret=<secret>
```

> Note: .NET environment variable config uses `__` (double underscore) as the key separator, not `:`. `System__AppConfig__ConnectionString` maps to `System:AppConfig:ConnectionString` in `IConfiguration`.

Copy `.env.example` to `.env` and fill in real values locally. Never commit `.env`.

---

## Pattern 3: Reading Secrets in Code

Always read secrets through `IConfiguration` or environment variables. Never accept a secret as a parameter from a caller.

```csharp
// CORRECT — read from configuration
var connString = configuration["System:AppConfig:ConnectionString"]
    ?? throw new InvalidOperationException(
        "System:AppConfig:ConnectionString is not set. Run bootstrap-csat-secrets.ps1.");

// CORRECT — DefaultAzureCredential picks up AZURE_CLIENT_ID/SECRET automatically
var credential = new DefaultAzureCredential();
var client = new ConfigurationClient(new Uri(endpoint), credential);

// WRONG — never accept a secret as a plain string parameter
public void Configure(string apiKey) { ... }

// WRONG — never hardcode
var key = "abc123secret";
```

---

## CI / Deployed Environments

In CI pipelines and deployed environments, inject secrets as environment variables or pipeline variables — never in YAML files or committed config.

```yaml
# Azure DevOps pipeline — reference secrets from variable groups, never inline
- task: DotNetCoreCLI@2
  env:
    AZURE_CLIENT_SECRET: $(AZURE_CLIENT_SECRET)   # from variable group, marked secret
    System__AppConfig__ConnectionString: $(AppConfigConnString)
```

`dotnet user-secrets` is for local development only and is ignored in published builds.
