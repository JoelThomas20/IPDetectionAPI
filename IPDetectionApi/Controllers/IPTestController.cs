using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Text;
using System.IO;

namespace IPDetectionApi.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class IPTestController : ControllerBase
    {
        private static readonly string LogFilePath = "/home/ubuntu/ip_logs.txt";

        [HttpGet("verify")]
        public IActionResult VerifyIPDetection()
        {
            var allHeaders = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

            var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var clientIp = xForwardedFor?.Split(',')[0]?.Trim()
                           ?? HttpContext.Connection.RemoteIpAddress?.ToString();

            var hostHeader = Request.Headers["Host"].FirstOrDefault();
            var xForwardedHost = Request.Headers["X-Forwarded-Host"].FirstOrDefault();
            var userAgent = Request.Headers["User-Agent"].FirstOrDefault();
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // ✅ Skip AWS ALB health checks (they use User-Agent: ELB-HealthChecker/2.0)
            if (userAgent != null && userAgent.Contains("ELB-HealthChecker"))
            {
                return Ok(new { success = true, message = "Health check ignored" });
            }

            var logEntry = $"{timestamp} | IP: {clientIp} | Host: {hostHeader} | XFH: {xForwardedHost} | UA: {userAgent}";

            try
            {
                System.IO.File.AppendAllText(LogFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error writing log file]: {ex.Message}");
            }

            return Ok(new
            {
                success = true,
                clientIP = clientIp,
                xForwardedForFull = xForwardedFor,
                host = hostHeader,
                xForwardedHost = xForwardedHost,
                xForwardedProto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault(),
                xForwardedPort = Request.Headers["X-Forwarded-Port"].FirstOrDefault(),
                userAgent = userAgent,
                remoteAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                allHeaders = allHeaders,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
