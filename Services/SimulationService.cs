using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SimBackend.Model;
using static SimBackend.Model.Concepts;

namespace SimBackend.Services
{
    public interface ISimulationService
    {
        Task StartSimulation(string token);
        Task StopSimulation(string token);
        int GetTransporterId();

    }

    public class SimulationService : ISimulationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SimulationService> _logger;
        private int _transporterId;

        public SimulationService(HttpClient httpClient, ILogger<SimulationService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

        }

        public async Task StartSimulation(string token)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Start the simulation
            await _httpClient.PostAsync("https://localhost:7115/Sim/Start", null);

            // Get the grid data
            var gridResponse = await _httpClient.GetAsync("https://localhost:7115/Grid/Get");
            var grid = JsonSerializer.Deserialize<Grid>(await gridResponse.Content.ReadAsStringAsync());

            // Buy the first transporter and place it at a random node
            var randomNodeId = grid.Nodes[new Random().Next(grid.Nodes.Count)].Id;
            var buyResponse = await _httpClient.PostAsync($"https://localhost:7115/CargoTransporter/Buy?positionNodeId={randomNodeId}", null);
            var transporterId = int.Parse(await buyResponse.Content.ReadAsStringAsync());

            // Store the transporter ID for later use
            _transporterId = transporterId;
            _logger.LogInformation($"Transporter bought with ID: {transporterId}");
            // Store the transporter ID for later use
            _logger.LogInformation($"Transporter bought with ID: {transporterId}");

            // Create initial orders
            await _httpClient.PostAsync("https://localhost:7115/Order/Create", null);

            _logger.LogInformation("Simulation started and transporter placed at random node.");
        }


        public int GetTransporterId()
        {
            return _transporterId;
        }


        public async Task StopSimulation(string token)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            await _httpClient.PostAsync("https://localhost:7115/Sim/Stop", null);
            _logger.LogInformation("Simulation stopped.");
        }
    }
}
