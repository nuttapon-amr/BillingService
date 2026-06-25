using BillingService_API.Services;
using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

AppContext.SetSwitch("System.Drawing.EnableUnixSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BillingService API",
        Version = "v1",
        Description = "API for billing documents (Receipt/Tax Invoice)."
    });
});

var connectionString = builder.Configuration.GetConnectionString("BillingService")
                      ?? builder.Configuration["ConnectionStrings:BillingService"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'ConnectionStrings:BillingService' is not configured.");
}

builder.Services.AddDbContext<BillingServiceContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

builder.Services.AddScoped<IDocumentNumberService, DocumentNumberService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "BillingService API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
