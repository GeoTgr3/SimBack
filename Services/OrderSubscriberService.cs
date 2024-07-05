using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SimBackend.Interfaces;
using SimBackend.Model.DTO;
using SimBackend.Services;

using static SimBackend.Model.Concepts;

namespace SimBackend.Services
{
    public class OrderSubscriberService : BackgroundService, IOrderSubscriberService
    {
        private readonly IRabbitMqService _rabbitMqService;
        private readonly HttpClient _httpClient;
        private readonly List<int> _coinUpdates = new List<int>();
        private string _token;
        private readonly ISimulationService _simulationService;


        public OrderSubscriberService(
            IRabbitMqService rabbitMqService,
                        ISimulationService simulationService,

            HttpClient httpClient)
        {
            _rabbitMqService = rabbitMqService;
            _httpClient = httpClient;
            _simulationService = simulationService;

        }

        public void SetToken(string token)
        {
            _token = token;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
{
    Console.WriteLine("OrderSubscriberService is starting.");

    _rabbitMqService.Subscribe(async order =>
    {
        if (stoppingToken.IsCancellationRequested)
            return;

        Console.WriteLine($"Message received: Order ID = {order?.Id}, OriginNodeId = {order?.OriginNodeId}, TargetNodeId = {order?.TargetNodeId}");

        bool accepted = new Random().Next(2) == 0;
        Console.WriteLine($"Order {order.Id} acceptance status: {accepted}");
        if (accepted)
        {
            await AcceptOrder(order);
            int transporterId = _simulationService.GetTransporterId();
            if (transporterId != 0)
            {
                await MoveTransporter(order, transporterId);
                int coins = CalculateOrderValue(order);
                Console.WriteLine($"Order {order.Id} processed. Coins earned: {coins}");
                _coinUpdates.Add(coins); // Store the coin updates
            }
        }
    });

            return Task.CompletedTask;
        }



        private async Task AcceptOrder(OrderDto order)
        {
            if (order == null)
            {
                Console.WriteLine("Error: Order is null.");
                return;
            }

            Console.WriteLine($"Accepting order {order.Id}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var response = await _httpClient.PostAsync($"https://localhost:7115/Order/Accept?orderId={order.Id}", null);
            Console.WriteLine($"Order {order.Id} acceptance response: {response.StatusCode}");
        }

        private async Task MoveTransporter(OrderDto order, int transporterId)
        {
            if (order == null)
            {
                Console.WriteLine("Error: Order is null.");
                return;
            }

            Console.WriteLine($"Moving transporter for order {order.Id}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            // Fetch the current position of the transporter
            var transporterResponse = await _httpClient.GetAsync($"https://localhost:7115/CargoTransporter/Get?transporterId={transporterId}");
            if (!transporterResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to get transporter {transporterId}. Status code: {transporterResponse.StatusCode}");
                return;
            }

            var transporterContent = await transporterResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Transporter response content: {transporterContent}");

            // Ensure that the response content is valid JSON
            if (string.IsNullOrWhiteSpace(transporterContent))
            {
                Console.WriteLine("Error: Transporter response content is empty or invalid.");
                return;
            }

            var transporter = JsonSerializer.Deserialize<CargoTransporterDto>(transporterContent);
            if (transporter == null)
            {
                Console.WriteLine("Error: Transporter is null or could not be deserialized.");
                return;
            }

            var currentPos = transporter.PositionNodeId;
            var targetNode = order.TargetNodeId;

            if (currentPos != -1 && targetNode != -1)
            {
                var path = await GetPath(currentPos, targetNode);
                foreach (var nodeId in path)
                {
                    await Task.Delay(1000); // Simulate movement delay
                    var moveResponse = await _httpClient.PutAsync($"https://localhost:7115/CargoTransporter/Move?transporterId={transporter.Id}&targetNodeId={nodeId}", null);
                    Console.WriteLine($"Moved transporter to node {nodeId}. Response: {moveResponse.StatusCode}");
                }
            }
        }





        private List<int> Dijkstra(Grid grid, int startNodeId, int endNodeId)
        {
            var nodes = grid.Nodes.ToDictionary(n => n.Id, n => n);
            var connections = grid.Connections.GroupBy(c => c.FirstNodeId)
                                              .ToDictionary(g => g.Key, g => g.ToList());

            var distances = new Dictionary<int, int>();
            var previousNodes = new Dictionary<int, int>();
            var unvisitedNodes = new HashSet<int>();

            foreach (var node in nodes.Keys)
            {
                distances[node] = int.MaxValue;
                unvisitedNodes.Add(node);
            }

            distances[startNodeId] = 0;

            while (unvisitedNodes.Count > 0)
            {
                var currentNode = unvisitedNodes.OrderBy(n => distances[n]).First();
                unvisitedNodes.Remove(currentNode);

                if (currentNode == endNodeId)
                    break;

                if (connections.TryGetValue(currentNode, out var nodeConnections))
                {
                    foreach (var connection in nodeConnections)
                    {
                        var neighbor = connection.SecondNodeId;
                        if (!unvisitedNodes.Contains(neighbor))
                            continue;

                        var edge = grid.Edges.First(e => e.Id == connection.EdgeId);
                        var newDist = distances[currentNode] + edge.Cost;
                        if (newDist < distances[neighbor])
                        {
                            distances[neighbor] = newDist;
                            previousNodes[neighbor] = currentNode;
                        }
                    }
                }
            }

            var path = new List<int>();
            var currentNodeId = endNodeId;
            while (previousNodes.ContainsKey(currentNodeId))
            {
                path.Insert(0, currentNodeId);
                currentNodeId = previousNodes[currentNodeId];
            }
            path.Insert(0, startNodeId);

            return path;
        }

        private async Task<List<int>> GetPath(int startNodeId, int endNodeId)
        {
            var gridResponse = await _httpClient.GetAsync("https://localhost:7115/Grid/Get");
            var grid = JsonSerializer.Deserialize<Grid>(await gridResponse.Content.ReadAsStringAsync());
            return Dijkstra(grid, startNodeId, endNodeId);
        }


        private int CalculateOrderValue(OrderDto order)
        {
            if (string.IsNullOrEmpty(order.ExpirationDateUtc) || string.IsNullOrEmpty(order.DeliveryDateUtc))
            {
                Console.WriteLine("Error: ExpirationDateUtc or DeliveryDateUtc is null or empty");
                return 0;
            }

            DateTime expirationDateUtc = DateTime.Parse(order.ExpirationDateUtc);
            DateTime deliveryDateUtc = DateTime.Parse(order.DeliveryDateUtc);
            TimeSpan timeToDeliver = expirationDateUtc - DateTime.UtcNow;

            if (timeToDeliver.TotalMinutes > 0)
            {
                Console.WriteLine($"Order {order.Id} delivered on time. Value: {order.Value}");
                return order.Value;
            }
            else
            {
                // Apply a penalty for late delivery
                double penaltyPercentage = 0.5; // Example: 50% penalty
                int valueAfterPenalty = (int)(order.Value * (1 - penaltyPercentage));
                Console.WriteLine($"Order {order.Id} delivered late. Original Value: {order.Value}, Value after penalty: {valueAfterPenalty}");
                return valueAfterPenalty;
            }
        }

        public List<int> GetCoinUpdates()
        {
            return _coinUpdates;
        }
    }

   
}
