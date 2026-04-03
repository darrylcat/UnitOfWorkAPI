using Microsoft.EntityFrameworkCore;
using UnitOfWorkAPI.Models.Database;
using UnitOfWorkAPI.Services;
using UnitOfWorkAPI.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("UOWDev");
builder.Services.AddDbContextFactory<UOWContext>(options =>
    options.UseSqlServer(connectionString));

// Register UnitOfWorkService as a singleton, exposed via IUnitOfWorkService
builder.Services.AddSingleton<IUnitOfWorkService, UnitOfWorkService>();
builder.Services.AddScoped<IUserDetailService, UserDetailService>();

builder.Services.AddControllers();
// Configure OpenAPI/Swagger (project-specific extension)
builder.Services.AddProjectOpenApi();

var app = builder.Build();

// Map Swagger UI unconditionally (available in all environments) at /swagger
app.MapProjectOpenApi();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
