using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.Queueing;
using System.Net.Http;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Controllers
{
    [ApiController]
    [Route("/")]
    public class MetricController : Controller
    {
        private readonly IXmlTaskQueue _xmlprocessor;
        private readonly IDebugQueue _debugQueue;


        private readonly ILogger<MetricController> _logger;

        public MetricController(IXmlTaskQueue xmlprocessor, ILogger<MetricController> logger, IDebugQueue debugQueue)
        {
            _logger = logger;
            _xmlprocessor = xmlprocessor;
            _debugQueue = debugQueue;
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
        [HttpGet("paths")]
        public ActionResult ReturnPaths()
        {
            return Ok(string.Join("\r\n", _debugQueue.GetPaths()));
        }
    }
}
