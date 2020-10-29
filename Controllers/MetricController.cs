using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.Queueing;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Controllers
{
    [ApiController]
    [Route("/")]
    public class MetricController : Controller
    {
        private readonly IXmlTaskQueue _xmlprocessor;

        private readonly ILogger<MetricController> _logger;

        public MetricController(IXmlTaskQueue xmlprocessor, ILogger<MetricController> logger)
        {
            _logger = logger;
            _xmlprocessor = xmlprocessor;
        }

        [HttpPost("send")]
        public void Post([FromBody] XDocument request)
        {
            _xmlprocessor.EnqueueXml(request);
        }

        [HttpGet("_health")]
        public ActionResult HealthCheck()
        {
            if (false)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            else
                return StatusCode(StatusCodes.Status202Accepted);
        }
    }
}
