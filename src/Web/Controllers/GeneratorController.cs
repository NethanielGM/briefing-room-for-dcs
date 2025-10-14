using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BriefingRoom4DCS;
using BriefingRoom4DCS.Mission;
using BriefingRoom4DCS.Template;
using System.Threading.Tasks;
using System.IO;

namespace BriefingRoom4DCS.GUI.Web.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GeneratorController : ControllerBase
    {
        private readonly ILogger<GeneratorController> _logger;

        public GeneratorController(ILogger<GeneratorController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<FileContentResult> Post(MissionTemplate template)
        {
            var briefingRoom = new BriefingRoom();
            var mission =  briefingRoom.GenerateMission(template);
            var mizBytes = await mission.SaveToMizBytes();

            if (mizBytes == null) return null; // Something went wrong during the .miz export
            return File(mizBytes, "application/octet-stream", $"{mission.Briefing.Name}.miz");
        }

        public class FromBrtRequest { public string path { get; set; } }

        [HttpPost("from-brt")]
        public async Task<FileContentResult> FromBrt([FromBody] FromBrtRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.path) || !System.IO.File.Exists(request.path))
                return null;

            var template = new MissionTemplate(request.path);
            var briefingRoom = new BriefingRoom();
            var mission = briefingRoom.GenerateMission(template);
            var mizBytes = await mission.SaveToMizBytes();
            if (mizBytes == null) return null;
            var outfile = Path.GetFileNameWithoutExtension(request.path) + ".miz";
            return File(mizBytes, "application/octet-stream", outfile);
        }
    }
}
