# Product Catalog API

REST API (ASP.NET Core 8, EF Core 8, SQL Server) that manages products and their stock.
See [DECISIONS.md](DECISIONS.md) for the reasoning behind the main design choices.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Docker (for SQL Server) — or any SQL Server instance (update the connection string)
- EF Core CLI tool: `dotnet tool install --global dotnet-ef`

## Run locally

```bash
# 1. Start SQL Server
docker compose up -d

# 2. Create the initial migration (code-first). Only needed once; commit the generated Migrations folder.
dotnet ef migrations add InitialCreate \
  --project src/ProductCatalog.Api \
  --output-dir Infrastructure/Migrations

# 3. Run the API (applies migrations and seeds sample data on startup in Development)
dotnet run --project src/ProductCatalog.Api
```

Swagger UI: http://localhost:5000/swagger (the descriptions shown there come from the XML documentation comments in the code)

To apply migrations manually instead: `dotnet ef database update --project src/ProductCatalog.Api`
(and set `Database:MigrateOnStartup` to `false`).

## Run the tests

```bash
dotnet test
```

The unit tests need no database.

## Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/products` | List all products |
| POST | `/api/products` | Create a product (201 + `Location`) |
| GET | `/api/products/{id}` | Get one product |
| PUT | `/api/products/{id}` | Replace a product |
| DELETE | `/api/products/{id}` | Delete a product (204) |
| POST | `/api/products/{id}/decrement-stock/{quantity}` | Remove stock (409 if insufficient) |
| POST | `/api/products/{id}/add-to-stock/{quantity}` | Add stock |
| GET | `/api/products/search?name={name}` | Partial, case-insensitive name search |
| GET | `/api/products/stock-level?min={min}&max={max}` | Products with stock in `[min, max]` |

Every product in a response includes `availableStock`. Errors use RFC 7807 `application/problem+json`
(400 validation, 404 not found, 409 conflict/insufficient stock, 500 unexpected).

### Example

```bash
curl -X POST http://localhost:5000/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Wireless Mouse","description":"Ergonomic","price":19.99,"stock":10}'

curl -X POST http://localhost:5000/api/products/100001/decrement-stock/3
curl "http://localhost:5000/api/products/search?name=mouse"
curl "http://localhost:5000/api/products/stock-level?min=0&max=10"
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
