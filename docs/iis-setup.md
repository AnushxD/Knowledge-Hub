# DocHub on IIS — org machine runbook

Setting up the whole application on a Windows machine inside the org network,
with no code changes. Follow it top to bottom the first time.

The end state is **one IIS site** serving both the API and the Angular client,
with the supporting infrastructure in Docker alongside it.

---

## What runs where

| Piece | Where it runs | Why |
|---|---|---|
| API + client | **IIS**, one site | The API serves the client from `wwwroot`, so they are same-origin and the session cookie needs no CORS |
| PostgreSQL + pgvector | **Docker Desktop** | pgvector has no supported Windows installer; the `pgvector/pgvector:pg17` image is the reliable route |
| Blob storage | **Docker Desktop** (Azurite) or real Azure | Azurite is an emulator — see the warning in step 2 |
| Ollama | **Native Windows install** | Gets the GPU if the box has one; Docker on Windows generally will not |

Nothing here is a code change. Everything is configuration.

---

## 1. Install the prerequisites

Run PowerShell **as Administrator**.

### IIS itself

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-Security -All
```

On Windows Server use Server Manager → *Add Roles and Features* → **Web Server (IIS)** instead.

### ASP.NET Core Hosting Bundle

Download the **.NET 10 Hosting Bundle** (not the SDK, not the plain runtime) from
<https://dotnet.microsoft.com/download/dotnet/10.0> and install it.

> **Order matters.** The bundle registers `AspNetCoreModuleV2` with IIS. If IIS
> is installed *after* the bundle, that registration is missing and every request
> returns **500.19**. If you hit that, re-run the bundle installer with `/repair`.

Then:

```powershell
iisreset
```

Confirm the module is present — this should list `AspNetCoreModuleV2`:

```powershell
C:\Windows\System32\inetsrv\appcmd.exe list modules
```

### Docker Desktop

Install from <https://www.docker.com/products/docker-desktop/> and set it to
**start on login** — the database has to come up before the site is useful.

### Ollama

Install from <https://ollama.com/download/windows>. It listens on
`http://localhost:11434` and runs as a background service.

### Optional: the .NET SDK

Only needed if you want to apply database migrations **on this machine**. Step 4
gives an alternative that avoids installing it.

---

## 2. Start the infrastructure

Copy `docker-compose.yml` from the repository onto the machine, then from that
folder:

```powershell
docker compose up -d --wait postgres azurite
```

Pull the two models (a few GB, once):

```powershell
ollama pull nomic-embed-text
```
```powershell
ollama pull llama3.2:3b
```

> **Azurite is an emulator.** It is fine for a pilot, and its data lives in a
> Docker volume that survives restarts — but it is not a supported production
> store. If the org has Azure, create a real Storage Account and use its
> connection string in step 3 instead. Nothing else changes; `IFileStorage`
> already speaks the real Blob API.

---

## 3. Configure the application

Configuration reaches the app as **environment variables**. The `:` in a config
key becomes `__` (two underscores).

Scoping them to the application pool is tighter than setting them machine-wide,
because a machine variable is readable by every process on the box. Create the
pool first (step 5 creates the site; do the pool now):

```powershell
C:\Windows\System32\inetsrv\appcmd.exe add apppool /name:DocHub /managedRuntimeVersion:""
```

`managedRuntimeVersion:""` is **No Managed Code** — the .NET runtime is inside
the published app, not in IIS.

Now set the variables on that pool. Adjust the values:

```powershell
$appcmd = "C:\Windows\System32\inetsrv\appcmd.exe"

function Set-PoolEnv($name, $value) {
  & $appcmd set config -section:system.applicationHost/applicationPools `
    "/+[name='DocHub'].environmentVariables.[name='$name',value='$value']" /commit:apphost
}

Set-PoolEnv "ASPNETCORE_ENVIRONMENT" "Production"
Set-PoolEnv "Database__ConnectionString" "Host=localhost;Port=5432;Database=dochub;Username=dochub;Password=dochub_local_dev"
Set-PoolEnv "FileStorage__ConnectionString" "UseDevelopmentStorage=true"
Set-PoolEnv "FileStorage__ContainerName" "documents"
Set-PoolEnv "Embeddings__BaseUrl" "http://localhost:11434"
Set-PoolEnv "Llm__BaseUrl" "http://localhost:11434"
```

Change the Postgres password from the compose default before anyone real uses
this, and update both places.

### The settings you may also want

| Variable | Notes |
|---|---|
| `Authentication__SessionHours` | Session lifetime, default 8 |
| `RateLimits__ChatRequests` | Questions per user per window, default 10 |
| `Llm__Model` | `llama3.1:8b` follows the citation format better than the 3B default, at the cost of speed |
| `KnowledgeSources__RepositoryProvider` | Leave at `none` until phase 7 lands |

### Google sign-in (optional)

```powershell
Set-PoolEnv "Authentication__Google__Enabled" "true"
Set-PoolEnv "Authentication__Google__ClientId" "…apps.googleusercontent.com"
Set-PoolEnv "Authentication__Google__ClientSecret" "…"
Set-PoolEnv "Authentication__Google__AllowedDomains__0" "your-company.com"
```

The redirect URI registered in the Google Cloud console must be
`https://<your-host>/signin-google` exactly.

> An **empty** `AllowedDomains` list admits nobody, by design. If you enable
> Google without listing a domain, the app refuses to start rather than letting
> every Google account in the world sign in.

---

## 4. Create the database schema

Provisioning is deliberately never automatic — the app will not create or
migrate a database on startup.

**Option A — SDK installed on the box:**

```powershell
dotnet ef database update --project server\src\DocHub.DataAccess --startup-project server\src\DocHub.Api
```

**Option B — no SDK on the box (preferred for a server).** On your development
machine, generate an idempotent script once:

```bash
dotnet ef migrations script --idempotent --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api --output dochub-schema.sql
```

Copy it over and apply it:

```powershell
docker compose exec -T postgres psql -U dochub -d dochub -f - < dochub-schema.sql
```

The script is safe to re-run; it applies only the migrations that are missing.

---

## 5. Deploy the site

### Get the artefact

Run the **Publish (IIS artefact)** workflow in GitHub Actions and download the
`dochub-iis-*` artefact. It contains the published API with the built Angular app
already inside `wwwroot`.

To build it locally instead:

```bash
cd client && npm ci && npm run build && cd ..
dotnet publish server/src/DocHub.Api/DocHub.Api.csproj -c Release -r win-x64 --self-contained false -o publish
mkdir -p publish/wwwroot && cp -r client/dist/client/browser/. publish/wwwroot/
```

### Put it on the machine

Extract to `C:\inetpub\dochub`.

### Create the site

```powershell
$appcmd = "C:\Windows\System32\inetsrv\appcmd.exe"
& $appcmd add site /name:DocHub /physicalPath:"C:\inetpub\dochub" /bindings:"http/*:8080:"
& $appcmd set app "DocHub/" /applicationPool:DocHub
```

### Load the user profile — do not skip this

```powershell
& $appcmd set config -section:system.applicationHost/applicationPools `
  "/[name='DocHub'].processModel.loadUserProfile:true" /commit:apphost
```

ASP.NET Core encrypts the session cookie with Data Protection keys. With no user
profile loaded, those keys are not persisted — so **every app pool recycle signs
everybody out**, and it looks like a mysterious intermittent bug rather than a
configuration problem. See "Known rough edges" below for the permanent fix.

### File permissions

```powershell
icacls "C:\inetpub\dochub" /grant "IIS AppPool\DocHub:(OI)(CI)RX"
icacls "C:\inetpub\dochub\logs" /grant "IIS AppPool\DocHub:(OI)(CI)M"
```

Read and execute on the folder; write **only** to `logs\`, and only if you turn
stdout logging on in `web.config`.

---

## 6. Provision storage and the administrator

Run these from the publish folder. They are one-shot commands against the same
binary IIS runs.

The pool environment variables from step 3 do **not** apply to a command
prompt, so set what these need in the shell first:

```powershell
cd C:\inetpub\dochub
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Database__ConnectionString = "Host=localhost;Port=5432;Database=dochub;Username=dochub;Password=dochub_local_dev"
$env:FileStorage__ConnectionString = "UseDevelopmentStorage=true"

.\DocHub.Api.exe init-storage
```

Then set the administrator password. Type it into the shell rather than storing
it anywhere:

```powershell
$env:Authentication__SeedAdminPassword = "<a real password, 12+ characters>"
.\DocHub.Api.exe seed-admin
```

That prints `Password set for dev@dochub.local (Admin).` The account is
`dev@dochub.local`. Re-running it resets the password, which is also the
recovery path if it is forgotten.

Close the shell afterwards so the password does not linger in it.

---

## 7. Start and verify

```powershell
& $appcmd start site /site.name:DocHub
```

Then, in order:

1. `http://localhost:8080/healthz` → `"status": "Healthy"`, with `postgres`,
   `blob-storage`, `embeddings` and `assistant-model` all healthy. If any is
   `Degraded`, the response names the exact command to fix it.
2. `http://localhost:8080/` → the sign-in screen.
3. Sign in as `dev@dochub.local`. You should land on the dashboard.
4. Upload a Markdown file and watch it reach **Indexed**.
5. Ask the assistant something from it and check the answer streams in word by
   word. If it appears all at once, see the buffering note below.

### Make it reachable from the network

```powershell
New-NetFirewallRule -DisplayName "DocHub" -Direction Inbound -LocalPort 8080 -Protocol TCP -Action Allow
```

For HTTPS, add a binding with the org certificate. The session cookie is
`SecurePolicy = SameAsRequest`, so it works over plain HTTP inside the network
and automatically becomes `Secure` once the site is served over HTTPS — no
config change either way.

---

## Known rough edges

**Everyone signed out after a recycle or a deploy.** Data Protection keys were
not persisted. Loading the user profile (step 5) fixes the recycle case. The
permanent fix is one line in `Program.cs`:

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\inetpub\dochub-keys"));
```

Worth doing before anyone depends on the deployment; it also survives moving the
app to a second server later.

**The assistant's answer arrives in one lump instead of streaming.** Response
buffering is on. `web.config` already sets `responseBufferLimit` to `0`; check it
survived the deploy, and that no proxy in front of IIS is buffering as well.

**A file between 25 and 28 MB is rejected with a bare 404.13.** IIS checked its
own limit first. `web.config` sets `maxAllowedContentLength` to match the app's
25 MB — again, check it survived.

**500.30 on startup.** The app threw while starting, almost always a
configuration problem. Turn on stdout logging in `web.config`
(`stdoutLogEnabled="true"`), reproduce, and read `logs\stdout_*.log`. Turn it
back off afterwards.

**Health check says the assistant model is missing.** Ollama runs in the signed-in
user's session by default. If nobody is logged in, it may not be running — set it
to start as a service, or confirm `http://localhost:11434` answers from the
server itself.

**Ingestion stops when nobody uses the app.** Hangfire runs in-process, so an
idle app pool that shuts down stops processing queued documents. Set the app
pool's *Idle Time-out* to `0` and *Start Mode* to `AlwaysRunning` if that
matters.

---

## Upgrading later

1. Download the new artefact.
2. Apply new migrations (step 4) — before swapping files, not after.
3. Stop the site, replace the folder contents, start the site.
4. Re-run `init-storage` only if the storage configuration changed. `seed-admin`
   is not needed again.
