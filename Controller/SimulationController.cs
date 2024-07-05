using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimBackend.Model;
using SimBackend.Services;
using SimBackend.Interfaces;
using System.Threading.Tasks;

namespace SimBackend.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class SimulationController : ControllerBase
    {
        private readonly ISimulationService _simulationService;
        private readonly IOrderSubscriberService _orderSubscriberService;

        public SimulationController(ISimulationService simulationService, IOrderSubscriberService orderSubscriberService)
        {
            _simulationService = simulationService;
            _orderSubscriberService = orderSubscriberService;
        }

        [HttpPost]
        public async Task<IActionResult> Start([FromBody] SimulationRequestModel model)
        {
            _orderSubscriberService.SetToken(model.Token);
            await _simulationService.StartSimulation(model.Token);
            return Ok("Simulation started successfully");
        }

        [HttpPost]
        public async Task<IActionResult> Stop([FromBody] SimulationRequestModel model)
        {
            await _simulationService.StopSimulation(model.Token);
            return Ok("Simulation stopped successfully");
        }

        [HttpGet]
        public IActionResult GetCoinUpdates()
        {
            var coinUpdates = _orderSubscriberService.GetCoinUpdates();
            return Ok(coinUpdates);
        }
    }
}
