using Microsoft.EntityFrameworkCore;
using UnitOfWorkAPI.Models.Database;
using UnitOfWorkAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("UOWDev");
builder.Services.AddDbContextFactory<UOWContext>(options =>
    options.UseSqlServer(connectionString));

// Register UnitOfWorkService as a singleton, exposed via IUnitOfWorkService
builder.Services.AddSingleton<IUnitOfWorkService, UnitOfWorkService>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
