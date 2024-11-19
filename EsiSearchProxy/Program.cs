using EsiSearchProxy;
using EsiSearchProxy.Services;
using Microsoft.AspNetCore.HttpLogging;

var builder = WebApplication.CreateBuilder(args);

// Configure console logging
builder.Logging.AddSimpleConsole(c =>
{
    c.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
    // c.UseUtcTimestamp = true; // something to consider
});

// Add required http services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
    logging.RequestHeaders.Add("X-Proxy-Auth");
    logging.RequestHeaders.Add("X-Entity-ID");
    logging.RequestHeaders.Add("X-Token-Type");
    logging.RequestHeaders.Add("sec-ch-ua");
    logging.ResponseHeaders.Add("X-Esi-Error-Limit-Remain");
    logging.ResponseHeaders.Add("X-Esi-Error-Limit-Reset");
    logging.ResponseHeaders.Add("X-Esi-Request-Id");
    logging.MediaTypeOptions.AddText("application/javascript");
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;
});

// Setup configuration objects
var esiConfiguration = builder.Configuration.GetSection("Esi");

builder.Services.Configure<EsiConfiguration>(esiConfiguration);

// Add required esi services
builder.Services.AddScoped<EsiAuthService>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseRouting(); // Fixes a bug with SwaggerUI not working properly when a catchall route is used somewhere
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapControllers();

// Some small requests
app.MapGet("/hello", () => "Hello There");
app.MapGet("/favicon.ico", () => string.Empty);

app.Run();