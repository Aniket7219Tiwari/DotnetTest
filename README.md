# Northstar Store

A full-stack order and inventory sample built for the C#/.NET machine test. The API targets .NET 8 and SQL Server; the web client is React + TypeScript.

## Requirements

For running the source locally:

- Windows 10/11, macOS, or Linux
- .NET 8 SDK
- Node.js 22.12+ (or Node.js 24+) and npm
- An internet connection for the first dependency restore

The local Development setup uses SQLite and does not require SQL Server or Docker. To run against the assignment's SQL Server configuration, install SQL Server 2019 or later and use a SQL login with permission to create the database and tables. Docker Desktop is only required for the optional Compose setup below.

## Run with Docker Compose

1. Copy `.env.example` to `.env` and replace every example value with private local values. Do not commit `.env`.
2. From the repository root, run:

   ```powershell
   docker compose up --build
   ```

3. Open `http://localhost:8081` for the store and `http://localhost:5000/swagger` for Swagger. Compose waits for SQL Server health; the API applies migrations and seeds an empty database during startup.

To stop the stack, use `docker compose down`. To remove the local database volume as well, use `docker compose down -v` (this permanently deletes the Compose database).

## Run locally without Docker or SQL Server

This is the recommended setup for a first run and for trying the complete website. The API uses a local SQLite database in the Development environment. SQL Server remains available as a separate provider for the assignment and integration testing.

### 1. Install and check prerequisites

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Node.js](https://nodejs.org/) 22.12 or later. Open **two PowerShell terminals** in the project folder and check:

```powershell
dotnet --version
node --version
npm.cmd --version
```

`dotnet --version` should report `8.x`. If the `dotnet` command is not recognized after installing the SDK, close and reopen VS Code/PowerShell.

### 2. Start the API (Terminal 1)

From the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\Store.Api\Store.Api.csproj --urls http://localhost:5000
```

Wait for `Now listening on: http://localhost:5000` in this terminal. The API creates `Store.Api\store-dev.db` and its tables automatically, then seeds the initial catalog and sample users. The catalog contains 15 locally seeded sample products with INR prices; these are demo records, not live items fetched from an external retailer.

On the **first run against an empty database**, the terminal logs generated seed passwords for:

- Admin: `admin@example.com`
- Seeded customers: `customer1@example.com`, `customer2@example.com`, and `customer3@example.com`

Copy the password from the API terminal to a private password manager. The terminal prints generated passwords only when the database has no users. If you have lost that password, you can still create a normal customer account through Register; do not delete the database unless you intend to erase local orders and accounts.

### 3. Start the website (Terminal 2)

Open another PowerShell terminal at the repository root:

```powershell
Set-Location .\frontend
npm.cmd ci
$env:VITE_API_URL = "http://localhost:5000"
npm.cmd run dev -- --host 127.0.0.1
```

Wait for Vite to print `http://127.0.0.1:5173`, then open that URL in your browser. Keep both terminals open while using the app.

### 4. Try the full website flow

1. **Browse products:** open the Shop page. The API serves product records from the local SQLite database. Search, price range, stock filter, and page selection are sent to the server and reflected in the URL.
2. **Create a customer account:** choose **Sign in → Create an account**, enter your name, email, and a password of at least eight characters, then submit. Successful registration signs you in automatically. Each account created through Register receives the Customer role.
3. **Place an order:** add one or more in-stock products to the cart, open Cart, and select **Place order**. Checkout requires a signed-in Customer. The server validates current stock and price, calculates discounts, reserves inventory, and returns the order.
4. **Review or cancel an order:** open **My orders** while signed in. A Pending order can be cancelled; cancelling restores its stock. Orders and inventory state persist in `Store.Api\store-dev.db` after the API stops.
5. **Use Admin features:** sign out, sign in with `admin@example.com` and the generated Admin seed password, then open **Admin**. The page shows product management, all orders, and the top-customer report. Admins can confirm Pending orders, ship Confirmed orders, and cancel Pending or Confirmed orders.

### API and Swagger URLs

- Website: `http://127.0.0.1:5173`
- API health check: `http://localhost:5000/health`
- Swagger UI: `http://localhost:5000/swagger/index.html`
- OpenAPI JSON: `http://localhost:5000/swagger/v1/swagger.json`

Swagger’s **Authorize** button accepts an access token. First use `POST /api/auth/login` with a seeded or registered account and copy its `accessToken` from the response. Swagger sends the token in the HTTP `Authorization` header for authorized requests. Admin endpoints require an Admin token.

### Stop and restart

Stop each server using **Ctrl+C** in its own terminal. To run the app again, repeat steps 2 and 3. The SQLite database and user accounts remain between runs. If port 5000 or 5173 is already in use, stop the other process using that port before starting this project.

### Run the API with SQL Server instead

SQL Server is selected by configuration; **setting only a connection string is not sufficient** because the Development settings default to SQLite. Keep `ASPNETCORE_ENVIRONMENT=Development` for local Development behavior and override `Database__Provider` with `SqlServer` in Terminal 1. With Windows Authentication, for example:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Database__Provider = "SqlServer"
$env:ConnectionStrings__Store = "Server=localhost;Database=StoreDb;Integrated Security=True;TrustServerCertificate=True;Encrypt=False"
function New-RandomSecret([int]$ByteCount) {
    $bytes = New-Object byte[] $ByteCount
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
$env:Jwt__Key = New-RandomSecret 48
$env:Seed__AdminPassword = New-RandomSecret 24
$env:Seed__CustomerPassword = New-RandomSecret 24
Write-Host "Save this initial Admin password privately: $($env:Seed__AdminPassword)"
Write-Host "Save this initial Customer password privately: $($env:Seed__CustomerPassword)"
dotnet run --project .\Store.Api\Store.Api.csproj --urls http://localhost:5000
```

For SQL Server Authentication instead, set `ConnectionStrings__Store` to a connection string containing your SQL Server login and password. Prefer prompting for the password rather than typing it directly into a command that might be saved in shell history. Keep all secrets private and do not commit them. The generated JWT key is at least 32 bytes and generated seed passwords meet the 12-character minimum. The SQL login must be able to create the database if it does not exist and apply the EF migration.

The API applies the checked-in EF Core SQL Server migration at startup and seeds an empty database. This SQL Server database is independent of the SQLite database: changing providers does not copy or migrate the SQLite data. Existing accounts keep their existing password hashes; seed password settings do not reset existing users. The catalog data is sample seed data, not a live external product feed.

To inspect/apply migrations separately, install the EF CLI if needed and run:

```powershell
dotnet tool install --global dotnet-ef --version 8.0.11
dotnet ef migrations list --project .\Store.Api\Store.Api.csproj
dotnet ef database update --project .\Store.Api\Store.Api.csproj
```

### Running the website on a different computer

The local URLs above are for running the browser and API on the same machine. `localhost` always means “this computer”; it will not point a visitor’s browser to your API server. For a deployed website, host the API on a reachable HTTPS address, build the frontend with that API address in `VITE_API_URL`, and set the API CORS origin to the deployed website’s exact origin. Do not expose a development HTTP API or use the Development JWT key on a public network.

## Architecture

- `Store.Api/Controllers`: HTTP routes, DTO binding, authentication/authorization boundaries.
- `Store.Api/Application`: order use cases and testable order rules.
- `Store.Api/Domain`: entities and enums.
- `Store.Api/Infrastructure`: EF Core SQL Server context/migration, seed, token handling, cache, exception middleware, and pending-order worker.
- `Store.Tests`: focused xUnit rule tests and SQL Server-backed `WebApplicationFactory` integration tests.
- `frontend/src`: typed React pages, reusable navigation/guards, API client/interceptor, and cart helpers/tests.

## Inventory concurrency and idempotency

Order placement validates and combines duplicate product lines before entering a serializable SQL Server transaction. The transaction loads products in stable ID order, verifies the entire set before changing any stock, uses EF Core's SQL Server `rowversion` concurrency token for inventory updates, and inserts the order/items in that same transaction. A rowversion conflict or deadlock is translated to HTTP 409; a validation failure occurs before any write. Cancellation restores every item's stock and changes the status atomically in a transaction.

Each order stores the customer's `Idempotency-Key`; a unique `(CustomerId, IdempotencyKey)` index is the final race-safe guard. The service checks that key before and inside the transaction. Replays return the original order; they do not reserve stock a second time. The checkout client retains the same key until checkout succeeds.

Pending orders older than 30 minutes are checked once per minute by a hosted service. A batch is limited to 100 orders per pass; failed/concurrently changed rows are logged and considered again on the next pass.

## API notes

- JWT access tokens last 15 minutes. Refresh tokens are random, stored only as SHA-256 hashes, rotated on refresh, and expire after seven days.
- The login route is limited to five requests per minute per remote IP. The frontend refreshes once after a 401 and retries the original request once.
- Product catalog filters, sort, count, and pagination are applied to an EF `IQueryable`; list results are cached for two minutes and cache entries are invalidated after product create/update and every committed stock reservation/restoration.
- Discount assumption: the 5% high-subtotal discount and the fifth-order 10% discount are additive percentage points (15% when both apply), rounded to two decimal places. The fifth-order count includes every previously placed order, including cancelled orders.
- Customer-created registrations always receive the Customer role. Admins can transition Pending → Confirmed → Shipped or Pending/Confirmed → Cancelled. Customers can only cancel their own Pending orders.
- Enum values in SQL are integers: Pending=0, Confirmed=1, Shipped=2, Cancelled=3.

## Tests

Frontend:

```powershell
Set-Location frontend
npm ci
npm test
npm run build
npm run lint
```

Backend unit tests (no SQL Server required):

```powershell
dotnet test Store.Tests/Store.Tests.csproj --filter "FullyQualifiedName~OrderRulesTests"
```

To exercise the local SQLite API flow without SQL Server, run:

```powershell
dotnet test Store.Tests/Store.Tests.csproj --filter "FullyQualifiedName~LocalDevelopmentTests"
```

This test covers registration, login, product loading, order placement, idempotent replay, and cancellation/restored stock against a temporary SQLite database.

The SQL Server concurrency integration test requires an empty/disposable SQL Server database. The test bootstraps the named database (the login must be allowed to create it). Set `SQLSERVER_TEST_CONNECTION`, `Jwt__Key`, `Seed__AdminPassword`, and `Seed__CustomerPassword`, then run:

```powershell
dotnet test Store.Tests/Store.Tests.csproj
```

`OrderConcurrencyTests` starts two authenticated SQL Server-backed requests for one remaining unit and asserts one 201 and one 409. It also checks that a repeated idempotency key returns 201 twice but creates one order and decrements stock only once, and that an insufficient-stock order leaves all products unchanged. SQLite verifies the end-to-end local flow, but does not replace the required SQL Server rowversion concurrency test. The GitHub Actions workflow runs the suites with a SQL Server service container.

## Top-customer report SQL

The endpoint groups and sums on the server in a single EF query; it does not load orders into application memory or issue per-customer queries. EF Core command logging can be enabled by setting `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Information`. The following SQL was generated by EF Core 8's `ToQueryString()` for the endpoint query (the timestamp shown is a fixed test input):

```sql
DECLARE @__p_1 int = 5;
DECLARE @__since_0 datetime2 = '2026-01-01T00:00:00.0000000Z';

SELECT TOP(@__p_1) [t].[CustomerId], [u].[FullName], [u].[Email], [t].[TotalSpend]
FROM (
    SELECT [o].[CustomerId], COALESCE(SUM([o].[Total]), 0.0) AS [TotalSpend]
    FROM [Orders] AS [o]
    WHERE [o].[CreatedAt] >= @__since_0 AND [o].[Status] IN (1, 2)
    GROUP BY [o].[CustomerId]
) AS [t]
INNER JOIN [Users] AS [u] ON [t].[CustomerId] = [u].[Id]
ORDER BY [t].[TotalSpend] DESC, [u].[Id]
```

At runtime, `@__since_0` is the UTC timestamp computed from the requested `days` value. The same command is recorded in EF Core's `Microsoft.EntityFrameworkCore.Database.Command` logs. The query translation is guarded by `ReportsQueryTests`. Additional SQL Server queries for Part B are in [QUERIES.sql](./QUERIES.sql).

## Further improvements

- Add an admin user-management workflow, audit log, and revocation/session management for access tokens.
- Use a transactional outbox for order notifications and publish status changes over SignalR.
- Add API contract/OpenAPI client generation, richer validation, and additional end-to-end browser tests.
- Make discount policy/configuration explicit and add a dedicated order-count model if order-volume contention warrants it.
- Expand migration/deployment orchestration and production observability/health checks.
