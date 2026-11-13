using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ProductCatalog.Api.Application;
using ProductCatalog.Api.Infrastructure;
using ProductCatalog.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence ---------------------------------------------------------------------------
// EF Core context using SQL Server; the connection string lives in appsettings.json
// (override it with the ConnectionStrings__DefaultConnection environment variable).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- Application services ------------------------------------------------------------------
// Scoped = one instance per HTTP request, which matches the DbContext lifetime.
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductService, ProductService>();

// --- Web API -------------------------------------------------------------------------------
builder.Services.AddControllers();

// Error responses use the standard RFC 7807 ProblemDetails format.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Swagger/OpenAPI, including the XML documentation comments from the code.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});

var app = builder.Build();

// --- HTTP pipeline -------------------------------------------------------------------------
// Must be registered first so it can catch exceptions thrown by everything after it.
app.UseExceptionHandler();

// Swagger UI is only exposed in Development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// --- Database initialisation ---------------------------------------------------------------
// Convenience for local development. In a multi-instance production deployment, run migrations
// as a separate deployment step (e.g. `dotnet ef database update` or a migration bundle) and
// set Database:MigrateOnStartup=false.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Creates the database if needed and applies any pending EF migrations.
    await db.Database.MigrateAsync();

    // Optional sample data (only inserted when the table is empty).
    if (app.Configuration.GetValue<bool>("Database:Seed"))
        await DbSeeder.SeedAsync(db);
}

app.Run();

// Makes the auto-generated Program class visible to integration tests (WebApplicationFactory<Program>).
public partial class Program;
