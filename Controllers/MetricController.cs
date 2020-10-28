using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.Processing;

namespace MetricReceiver.Controllers
{
    [ApiController]
    [Route("/")]
    public class MetricController : Controller
    {
        private readonly XmlProcessor _xmlprocessor;

        private readonly ILogger<MetricController> _logger;

        public MetricController (XmlProcessor xmlprocessor, ILogger<MetricController> logger)
        {
            _logger = logger;
            _xmlprocessor = xmlprocessor;
        }

        [HttpPost("send")]
        public void Post([FromBody] XDocument request)
        {
            _xmlprocessor.EnqueueTask(request);
        }

        [HttpGet("_health")]
        public ActionResult HealthCheck(){
            if (false)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            else
                return StatusCode(StatusCodes.Status202Accepted);            
        }
    }
}
