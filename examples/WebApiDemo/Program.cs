using HangfireDemo.Core.Logging;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddApplicationLogging(builder.Configuration, builder.Environment.ContentRootPath, "WebApiDemo");

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
