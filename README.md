# IPDetectionApi

This repository contains a tiny ASP.NET Core Web API that detects client IP information and (by default) appends a small log entry to a file. The main endpoint is `GET /api/test/verify`.

This README explains how to run the project locally, how the current IP detection works, and gives two safe, simple examples for recording IPs when the API is hosted:
- Controller-level logging (using built-in `ILogger<T>`)
- Middleware-level logging (global request logging)

Prerequisites
- .NET 9 SDK installed: https://dotnet.microsoft.com/en-us/download
- (Optional) Docker if you want to containerize

Project layout (relevant files)
- `Program.cs` - minimal host, CORS, controllers mapped
- `Controllers/IPTestController.cs` - endpoint implementation and current file-based logging
- `IPDetectionApi.csproj` - project file

Run locally
1. Open a terminal and change into the project folder:

```bash
cd IPDetectionApi
```

2. Restore and run:

```bash
dotnet restore
dotnet run --urls "http://localhost:5000"
```

3. Call the endpoint (example using curl):

```bash
curl http://localhost:5000/api/test/verify
```

Notes about HTTPS: The `Program.cs` enables `UseHttpsRedirection()`. For local testing I recommend forcing HTTP with `--urls "http://localhost:5000"` as shown above. If you run over HTTPS, either use the URL printed to console or `curl -k https://localhost:PORT/api/test/verify`.

What the existing `IPTestController` does
- Route: `GET /api/test/verify` (controller has `[Route("api/test")]` and action `[HttpGet("verify")]`).
- It reads headers including `X-Forwarded-For`, `Host`, `X-Forwarded-Host`, `User-Agent`, and `X-Forwarded-Proto`/`X-Forwarded-Port`.
- The controller resolves client IP using `X-Forwarded-For` (first item) falling back to `HttpContext.Connection.RemoteIpAddress`.
- It writes a small line to `/home/ubuntu/ip_logs.txt` using `System.IO.File.AppendAllText` (this path is platform-specific and will need modification if hosting Windows or other locations).

Important hosting considerations
- If your app runs behind a reverse proxy (NGINX, Azure App Gateway, AWS ALB, Cloudflare) you must configure forwarded headers so `HttpContext.Connection.RemoteIpAddress` and `X-Forwarded-For` handling are correct. See the Forwarded Headers section below.
- The current controller writes to `/home/ubuntu/ip_logs.txt`. Change this to an appropriate writable path for your host (e.g. environment-based path or use a proper logging framework).

Example 1 — Replace file writes with `ILogger<T>` in the controller

1) Modify `Controllers/IPTestController.cs` to accept an `ILogger<IPTestController>` via constructor injection and use it instead of `File.AppendAllText`.

Replace the class header and fields with something like:

```csharp
using Microsoft.Extensions.Logging;

public class IPTestController : ControllerBase
{
    private readonly ILogger<IPTestController> _logger;

    public IPTestController(ILogger<IPTestController> logger)
    {
        _logger = logger;
    }

    [HttpGet("verify")]
    public IActionResult VerifyIPDetection()
    {
        var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var clientIp = xForwardedFor?.Split(',')[0]?.Trim()
                       ?? HttpContext.Connection.RemoteIpAddress?.ToString();

        var userAgent = Request.Headers["User-Agent"].FirstOrDefault();
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // Log to the app logger (configured via host)
        _logger.LogInformation("{Timestamp} | IP: {ClientIp} | UA: {UserAgent} | Path: {Path}",
            timestamp, clientIp, userAgent, Request.Path);

        return Ok(new { success = true, clientIP = clientIp, timestamp = DateTime.UtcNow });
    }
}
```

2) No additional DI registration is required for `ILogger<T>` — it is provided by the host.

Why use `ILogger`?
- It integrates with the ASP.NET Core logging infrastructure (console, file sinks, external providers).
- It is safe on all platforms and supports log levels, structured logging, and external sinks (Serilog, Seq, Elastic, etc.).

Example 2 — Global middleware that logs every request IP

Create a new middleware class, e.g. `Middlewares/RequestLoggingMiddleware.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var clientIp = xForwardedFor?.Split(',')[0]?.Trim() ?? context.Connection.RemoteIpAddress?.ToString();

        _logger.LogInformation("{Timestamp} | Request {Method} {Path} from IP {IP}",
            DateTime.UtcNow.ToString("o"), context.Request.Method, context.Request.Path, clientIp);

        await _next(context);
    }
}
```

Register the middleware in `Program.cs` before `MapControllers()` (or as early as you want to capture requests):

```csharp
app.UseMiddleware<RequestLoggingMiddleware>();
app.MapControllers();
```

Forwarded headers configuration (very important behind proxies)
1. Add this at the top of `Program.cs` (before building the app) to enable processing of `X-Forwarded-*` headers:

```csharp
using Microsoft.AspNetCore.HttpOverrides;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // If you know the proxy IP(s) add them to KnownNetworks or KnownProxies to make it secure.
    // options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
});

var app = builder.Build();
app.UseForwardedHeaders();
```

2. If you are deploying to Azure App Service, typically Azure handles forwarded headers, but it's still a good idea to enable `UseForwardedHeaders()` when behind a proxy.

Logging to file with Serilog (optional)
1. Add the `Serilog.AspNetCore` NuGet package.
2. Example minimal `Program.cs` changes for Serilog file logging:

```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/ip_detection_.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
```

Docker and publishing notes
- To publish for production (self-contained), run:

```bash
dotnet publish -c Release -o out
```

- Example `Dockerfile` (simple):

```Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish IPDetectionApi/IPDetectionApi.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "IPDetectionApi.dll"]
```

If hosting behind a load balancer or reverse-proxy in Docker/Kubernetes, ensure you enable forwarded headers and configure the proxy's IPs as known networks if possible.

Tips and troubleshooting
- If you see `127.0.0.1` or proxy IPs instead of client IPs, check that your reverse proxy forwards `X-Forwarded-For` and that `UseForwardedHeaders()` is enabled.
- On Windows hosts, change the log path from `/home/ubuntu/ip_logs.txt` to a Windows path or use `ILogger`/Serilog with a relative `logs/` folder.
- Avoid using simple file appends to system paths in production—use a proper logging provider that handles rotation and permissions.

Quick checklist to get this running and logging IPs:
1. `cd IPDetectionApi`
2. `dotnet restore`
3. Add `RequestLoggingMiddleware` or change `IPTestController` to use `ILogger<T>` as shown above.
4. If behind a proxy, add `UseForwardedHeaders()` and configure `ForwardedHeadersOptions`.
5. `dotnet run --urls "http://localhost:5000"` and test with `curl http://localhost:5000/api/test/verify`

If you want, I can:
- Make the `ILogger` substitution in `Controllers/IPTestController.cs` for you and adjust the default log path to a cross-platform location.
- Add the middleware file and register it in `Program.cs`.

-- End
