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

## Concurrency on PUT: what is and is not protected
Stock changes through two paths: the atomic stock endpoints and `PUT`, which replaces `Stock` with an absolute value.
Review finding: `PUT` originally loaded the row and used the `rowversion` it had *just* read, so the check only
covered the milliseconds between that load and the `UPDATE`. A client that read a product (stock 10), saw a sale
bring it to 7, and then sent its stale copy would silently put the stock back to 10.

Decision: **optimistic concurrency with a token the client sends back.** Every response carries `rowVersion`
(base64 of the SQL Server `rowversion`, which changes on every update, stock endpoints included). `PUT` accepts an
optional `rowVersion`; when present, EF compares against it (`UPDATE ... WHERE Id = @id AND RowVersion = @token`)
and a mismatch becomes **409**. A malformed token is a 400 and never reaches the database.

| Situation | Protected? |
|---|---|
| PUT with `rowVersion`, product changed since the read (price, stock endpoint, other PUT) | Yes: 409 |
| Two PUTs racing, both with `rowVersion` | Yes: exactly one wins, the other gets 409 |
| PUT without `rowVersion`, change lands while the request is in flight | Yes, but only that millisecond window: 409 |
| PUT without `rowVersion`, change made before the request started | **No**: last write wins, stock included |
| Stock endpoint vs stock endpoint | Yes: single atomic `UPDATE`, no token needed (additions and removals commute) |
| Stock endpoint vs PUT | Only if the PUT carries `rowVersion` |
| PUT vs DELETE | PUT after the delete: 404. Delete during the PUT: 409 (zero rows updated raises the same exception). DELETE itself is unconditional, so it always wins |
| POST | No shared state; ids come from the sequence |

Limitations, accepted knowingly:
- **The token is optional.** Without it a PUT is last-write-wins, including stock. This keeps plain `curl`/Swagger
  calls working; a production API should make it mandatory (`If-Match` + `428 Precondition Required`).
- **The token is coarse.** Any update changes it, so a client editing only the price gets 409 after an unrelated
  sale and has to re-read. Finer granularity would need a separate token for stock or a `PATCH` that excludes it.
- **Not covered by an automated database test.** Unit tests verify that the token is decoded and passed to the
  repository, that bad tokens are rejected and that responses expose it. The `UPDATE ... WHERE RowVersion` behaviour
  itself relies on the standard EF Core/SQL Server mechanism and would need an integration test (Testcontainers).

Alternatives considered: making `PUT` ignore stock (removes the race but changes `PUT` from a full replacement);
an `ETag`/`If-Match` header instead of a body field (more idiomatic HTTP; the body field was chosen to keep it
trivial to try in Swagger and to keep the controller free of header handling).

## Validation
DataAnnotations on the request DTO, enforced automatically by `[ApiController]` (400 with `ValidationProblemDetails`).
`Price`/`Stock` are nullable in the DTO so a *missing* field is rejected instead of defaulting to 0.
Query/route rules (quantity > 0, `min <= max`, non-blank `name`) live in the service and are unit tested.
FluentValidation would be a reasonable swap if rules grew more complex.

## Price precision
One rule, applied identically at the three levels: **at most 2 decimal places, from 0.01 to 1,000,000 inclusive.**

| Level | What enforces it |
|---|---|
| API input | `[Range]` for the bounds plus `[MaxDecimalPlaces(2)]`; more decimals is a 400 |
| Database | `decimal(18,2)` and `CHECK (Price > 0)`, equivalent to `>= 0.01` at two decimals |
| Response | `Price` is the stored `decimal`; as a JSON number it has no trailing zeros (`19.90` -> `19.9`), same value |

Review finding: `19.999` used to pass validation. The database column keeps two decimals, while the `POST` response
echoed the in-memory `19.999`, so `POST` and a later `GET` could disagree (whether the provider rounds or truncates
is a detail we deliberately do not depend on). Rejecting extra decimals, instead of rounding them, means money is never
changed silently and what the client sends is exactly what is stored and returned.
The 1,000,000 ceiling is an API business rule only; the column is wider. Enforcing it in the database too would need a
new migration and was not considered worth it. The range limits are parsed with the invariant culture so machines
with a decimal comma (pt-PT) behave the same.

## Seeding
A startup seeder inserts sample rows only if the table is empty. `HasData` was avoided because it needs hard-coded IDs,
which would collide with the sequence. Migrate-on-startup (`Database:MigrateOnStartup`, on by default in `appsettings.json`, not tied to the environment) is a convenience for local runs;
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
