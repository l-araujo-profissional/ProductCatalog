# Product Catalog API

REST API (ASP.NET Core 8, EF Core 8, SQL Server) that manages products and their stock.
See [DECISIONS.md](DECISIONS.md) for the reasoning behind the main design choices.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Docker (for SQL Server) — or any SQL Server instance (update the connection string)
- EF Core CLI tool, **only** if you change the model or want to apply migrations by hand:
  `dotnet tool install --global dotnet-ef`

## Run locally

```bash
# 1. Start SQL Server (the first start can take a little while before it accepts connections)
docker compose up -d

# 2. Run the API
dotnet run --project src/ProductCatalog.Api
```

That is all: the initial migration (`Infrastructure/Migrations/`, code-first) is already in the repository.
On startup the API creates the database if needed, applies pending migrations and inserts sample data
(only when the `Products` table is empty). This is controlled by two settings in `appsettings.json`,
both `true` by default and independent of the environment:

| Setting | Effect |
|---|---|
| `Database:MigrateOnStartup` | Apply pending EF migrations when the API starts |
| `Database:Seed` | Insert sample products if the table is empty (only runs when migrating on startup) |

Swagger UI: http://localhost:5000/swagger (served only in the `Development` environment, which is what
`dotnet run` uses through `launchSettings.json`; the descriptions come from the XML documentation comments).

The connection string in `appsettings.json` matches the credentials in `docker-compose.yml`
(development only). Override it with the `ConnectionStrings__DefaultConnection` environment variable.

### Migrations

- To apply them manually instead: set `Database:MigrateOnStartup` to `false` and run
  `dotnet ef database update --project src/ProductCatalog.Api`.
- Only when you change the model, create a new migration:
  `dotnet ef migrations add <Name> --project src/ProductCatalog.Api --output-dir Infrastructure/Migrations`
  (do **not** re-run `InitialCreate`; it already exists).

## Run the tests

```bash
dotnet test
```

The unit tests need no database (the repository is mocked). Consequently they do not exercise the SQL
Server-specific behaviour (the id sequence, the atomic stock `UPDATE`s, the `rowversion` check); see
[DECISIONS.md](DECISIONS.md) for how those were reasoned about.

## Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/products` | List all products |
| POST | `/api/products` | Create a product (201 + `Location`) |
| GET | `/api/products/{id}` | Get one product |
| PUT | `/api/products/{id}` | Replace a product, stock included (send `rowVersion` to avoid overwriting newer changes, see below) |
| DELETE | `/api/products/{id}` | Delete a product (204) |
| POST | `/api/products/{id}/decrement-stock/{quantity}` | Remove stock (409 if insufficient) |
| POST | `/api/products/{id}/add-to-stock/{quantity}` | Add stock |
| GET | `/api/products/search?name={name}` | Partial, case-insensitive name search |
| GET | `/api/products/stock-level?min={min}&max={max}` | Products with stock in `[min, max]` |

Every product in a response includes `availableStock` and a `rowVersion` token. Errors use RFC 7807
`application/problem+json` (400 validation, 404 not found, 409 conflict/insufficient stock/concurrent change,
500 unexpected).

### Prices

A price has **at most two decimal places** and lies between **0.01 and 1,000,000** (inclusive).
A value such as `19.999` is rejected with 400; it is never rounded silently. The database column is
`decimal(18,2)`, so what you send is exactly what is stored and returned. JSON numbers drop trailing zeros
(`19.90` is returned as `19.9`); it is the same value, so format it on the client when displaying.

### Updating a product (PUT) and concurrent changes

`PUT` replaces name, description, price **and stock**. Stock can also change through the two stock endpoints,
so a stale `PUT` could silently put an old stock value back. To prevent that, send the `rowVersion` from your
last read in the PUT body:

| Situation | Result |
|---|---|
| `rowVersion` sent and the product changed since (price edit, stock endpoint, another PUT) | **409**, nothing is written; re-read and retry |
| Two PUTs race, both with `rowVersion` | One succeeds, the other gets 409 |
| `rowVersion` omitted, another change lands while this request is being processed | 409 (only a window of milliseconds is covered) |
| `rowVersion` omitted, product was changed *before* this request started | **Not protected**: last write wins and stock is overwritten with the value sent |
| Product deleted between the read and the PUT | 404, or 409 if the delete happens during the request |
| Stock endpoints against each other | Safe: each is one atomic `UPDATE`; stock never goes below zero |

`rowVersion` is optional on purpose, so a plain PUT keeps working from curl or Swagger; the limitations and
alternatives are in [DECISIONS.md](DECISIONS.md).

### Example

```bash
curl -X POST http://localhost:5000/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Wireless Mouse","description":"Ergonomic","price":19.99,"stock":10}'

# Seeded ids are six digits starting at 100000; use the ids returned by GET /api/products.
curl -X POST http://localhost:5000/api/products/100001/decrement-stock/3
curl "http://localhost:5000/api/products/search?name=mouse"
curl "http://localhost:5000/api/products/stock-level?min=0&max=10"

# Safe update: read, then send the rowVersion back with the new values
curl http://localhost:5000/api/products/100001
curl -X PUT http://localhost:5000/api/products/100001 \
  -H "Content-Type: application/json" \
  -d '{"name":"Wireless Mouse","description":"Ergonomic","price":21.50,"stock":7,"rowVersion":"<value from the GET>"}'
```

## Project layout

```
src/ProductCatalog.Api
  Controllers/     HTTP layer (thin)
  Application/     Business rules (ProductService)
  Domain/          Entity, domain exceptions
  Contracts/       Request/response DTOs and validation attributes
  Infrastructure/  DbContext, repository, seeder, migrations
  Middleware/      Global exception -> ProblemDetails
tests/ProductCatalog.Tests   xUnit + NSubstitute + FluentAssertions
```
