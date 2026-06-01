using Microsoft.AspNetCore.Mvc;

namespace BillingService_API.Controllers;

[ApiController]
[Route("api/heartbeat")]
public class HeartbeatController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            service = "BillingService_API",
            timestampUtc = DateTime.UtcNow
        });
    }
}
