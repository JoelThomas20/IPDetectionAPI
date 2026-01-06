var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Add CORS policy for your frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
         .WithOrigins("http://localhost:9000")      
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Enable CORS globally
app.UseCors("AllowFrontend");

// Optional: enable HTTPS redirection (safe to keep)
app.UseHttpsRedirection();

// Map controller routes
app.MapControllers();

app.Run();
