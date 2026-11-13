# Design decisions

## 6-digit unique ID across multiple instances
IDs come from a **SQL Server SEQUENCE** (`START 100000`, `MAX 999999`, no cycling) used as the column default
(`NEXT VALUE FOR`). The database serialises access to the sequence, so any number of API instances can insert
concurrently with no duplicates and no retry loops, and nothing is kept in application memory.
Alternatives considered and rejected:
- *Random number + retry on unique-key violation*: works, but adds collisions as the table fills and extra round trips.
- *`MAX(Id)+1`*: racy across instances.
- *Guid/ULID*: not a 6-digit number.

Trade-offs: sequences can leave gaps (e.g. rolled-back transactions) and the space is limited to 900,000 products;
exhausting it raises an error rather than reusing IDs.

## Stock changes are atomic
`decrement-stock` / `add-to-stock` are single `UPDATE ... WHERE Stock >= @qty` statements (`ExecuteUpdateAsync`).
A read-modify-write would lose updates under concurrency. A `CHECK (Stock >= 0)` constraint is a second line of defence.
Insufficient stock returns **409 Conflict**; add-to-stock guards against `int` overflow.

## Concurrency on PUT
Products have a `rowversion` column; concurrent edits raise `DbUpdateConcurrencyException`, mapped to **409**.

## Validation
DataAnnotations on the request DTO, enforced automatically by `[ApiController]` (400 with `ValidationProblemDetails`).
`Price`/`Stock` are nullable in the DTO so a *missing* field is rejected instead of defaulting to 0.
Query/route rules (quantity > 0, `min <= max`, non-blank `name`) live in the service and are unit tested.
FluentValidation would be a reasonable swap if rules grew more complex.

## Seeding
A startup seeder inserts sample rows only if the table is empty. `HasData` was avoided because it needs hard-coded IDs,
which would collide with the sequence. Migrate-on-startup is a development convenience, switchable via configuration;
for several instances in production, run migrations as a deployment step.

## Structure
A single API project with folders per layer (controller → service → repository) instead of several assemblies:
the domain is small and extra projects would be ceremony. The repository interface keeps business rules unit-testable
without a database.

## Not included (possible next steps)
Pagination on the list endpoint, BDD tests (Reqnroll), integration tests against a real SQL Server (Testcontainers),
authentication, and a Dockerfile for the API.

## Use of AI
The solution was produced with AI assistance (Claude). I reviewed the design choices above, and the reasoning for each
is documented here so the logic can be followed independently of how the code was written.
