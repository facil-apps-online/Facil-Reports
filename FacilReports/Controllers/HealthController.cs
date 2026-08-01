using Microsoft.AspNetCore.Mvc;

namespace FacilReports.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            service = "FacilReports",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }
}
