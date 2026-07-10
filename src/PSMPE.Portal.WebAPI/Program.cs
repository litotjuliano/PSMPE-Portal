using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Persistence.Seed;
using PSMPE.Portal.WebAPI.Extensions;
using PSMPE.Portal.WebAPI.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddPortalSwagger();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    // TODO: restrict to the deployed frontend origin(s) once known; kept open for local dev.
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (builder.Configuration.GetValue<bool>("Seed:Enabled"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

    var db = services.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    await IdentitySeeder.SeedAsync(roleManager, userManager, builder.Configuration, logger);
    await SystemConfigSeeder.SeedAsync(db, logger);
    await MemberSeeder.SeedAsync(db, userManager, logger);
}

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
