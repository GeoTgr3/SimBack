using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimBackend.Interfaces;
using SimBackend.Model.DTO;
using static SimBackend.Model.Concepts;

namespace SimBackend.Services
{
    public class OrderSubscriberService : BackgroundService, IOrderSubscriberService
    {
        private readonly IRabbitMqService _rabbitMqService;
        private readonly HttpClient _httpClient;
        private readonly List<int> _coinUpdates = new List<int>();
        private readonly ILogger<OrderSubscriberService> _logger;
        private readonly ISimulationService _simulationService;
        private string _token;
        private SemaphoreSlim _semaphore;

        public OrderSubscriberService(
            IRabbitMqService rabbitMqService,
            ISimulationService simulationService,
            HttpClient httpClient,
            ILogger<OrderSubscriberService> logger)
        {
            _rabbitMqService = rabbitMqService;
            _httpClient = httpClient;
            _simulationService = simulationService;
            _logger = logger;
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public void SetToken(string token)
        {
            _token = token;
        }

        private async Task<HttpResponseMessage> AcceptOrder(int orderId)
        {
            _logger.LogInformation($"Accepting order {orderId}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return await _httpClient.PostAsync($"https://localhost:7115/Order/Accept?orderId={orderId}", null);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderSubscriberService is starting.");

            _rabbitMqService.Subscribe(async message =>
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Stopping token is requested. Exiting.");
                    return;
                }

                // Log the raw message received
                _logger.LogInformation($"Raw message received: {message}");

                await _semaphore.WaitAsync(stoppingToken); // Wait for the semaphore

                try
                {
                    var orderJson = JsonNode.Parse(message);
                    if (orderJson == null)
                    {
                        throw new Exception("Order deserialization resulted in null");
                    }

                    // Extract values from JSON
                    int orderId = orderJson["id"]?.GetValue<int>() ?? 0;
                    int originNodeId = orderJson["originNodeId"]?.GetValue<int>() ?? 0;
                    int targetNodeId = orderJson["targetNodeId"]?.GetValue<int>() ?? 0;
                    int load = orderJson["load"]?.GetValue<int>() ?? 0;
                    int value = orderJson["value"]?.GetValue<int>() ?? 0;
                    string deliveryDateUtc = orderJson["deliveryDateUtc"]?.GetValue<string>() ?? "";
                    string expirationDateUtc = orderJson["expirationDateUtc"]?.GetValue<string>() ?? "";

                    _logger.LogInformation($"Message received: Order ID = {orderId}, OriginNodeId = {originNodeId}, TargetNodeId = {targetNodeId}");

                    bool accepted = new Random().Next(2) == 0;
                    _logger.LogInformation($"Order {orderId} acceptance status: {accepted}");
                    if (accepted)
                    {
                        var acceptResponse = await AcceptOrder(orderId);
                        if (acceptResponse.IsSuccessStatusCode)
                        {
                            int transporterId = _simulationService.GetTransporterId();
                            if (transporterId != 0)
                            {
                                var transporterSuccess = await MoveTransporter(orderId, transporterId, originNodeId, targetNodeId);
                                if (transporterSuccess)
                                {
                                    int coins = CalculateOrderValue(value, deliveryDateUtc, expirationDateUtc);
                                    _logger.LogInformation($"Order {orderId} processed. Coins earned: {coins}");
                                    _coinUpdates.Add(coins); // Store the coin updates
                                }
                            }
                        }
                        else
                        {
                            _logger.LogError($"Order {orderId} acceptance failed with status code: {acceptResponse.StatusCode}");
                            if (acceptResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                            {
                                _logger.LogError("Unauthorized access. Please check the token.");
                                // Handle token refresh if needed
                            }
                        }
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError($"JSON deserialization error: {jsonEx.Message}");
                    _logger.LogError($"JSON payload: {message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Unexpected error during deserialization: {ex.Message}");
                }
                finally
                {
                    _semaphore.Release(); // Release the semaphore
                }
            });

            return Task.CompletedTask;
        }


        private async Task<bool> MoveTransporter(int orderId, int transporterId, int originNodeId, int targetNodeId)
        {
            var transporterResponse = await _httpClient.GetAsync($"https://localhost:7115/CargoTransporter/Get?transporterId={transporterId}");
            if (!transporterResponse.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get transporter {transporterId}. Status code: {transporterResponse.StatusCode}");
                return false;
            }

            var transporterContent = await transporterResponse.Content.ReadAsStringAsync();
            _logger.LogInformation($"Transporter response content: {transporterContent}");

            var transporter = JsonSerializer.Deserialize<CargoTransporterDto>(transporterContent);
            if (transporter == null)
            {
                _logger.LogError("Error: Transporter is null or could not be deserialized.");
                return false;
            }

            if (transporter.InTransit)
            {
                _logger.LogError($"Transporter {transporterId} is currently in transit and cannot move.");
                return false;
            }

            var currentPos = transporter.PositionNodeId;
            _logger.LogInformation($"Current position of transporter {transporterId}: {currentPos}");

            var gridObject = await GetGridData();
            if (gridObject == null)
            {
                _logger.LogError("Failed to get grid data.");
                return false;
            }

            var path = FindShortestPathByTime(gridObject, currentPos, targetNodeId);
            if (path.Count == 0)
            {
                _logger.LogError($"No path found from node {currentPos} to node {targetNodeId}");
                return false;
            }

            foreach (var nodeId in path.Skip(1))
            {
                _logger.LogInformation($"Sending move request: https://localhost:7115/CargoTransporter/Move?transporterId={transporterId}&targetNodeId={nodeId}");
                await Task.Delay(1000);

                var moveResponse = await _httpClient.PutAsync($"https://localhost:7115/CargoTransporter/Move?transporterId={transporterId}&targetNodeId={nodeId}", null);
                var moveContent = await moveResponse.Content.ReadAsStringAsync();
                _logger.LogInformation($"Moved transporter to node {nodeId}. Response: {moveResponse.StatusCode}, Content: {moveContent}");

                if (!moveResponse.IsSuccessStatusCode)
                {
                    _logger.LogError($"Failed to move transporter to node {nodeId}. Status code: {moveResponse.StatusCode}, Response content: {moveContent}");
                    return false;
                }
            }

            return true;
        }












        private List<int> FindShortestPathByTime(Grid grid, int startNodeId, int endNodeId)
        {
            var nodes = grid.Nodes.ToDictionary(n => n.Id, n => n);
            var connections = grid.Connections.GroupBy(c => c.FirstNodeId)
                                              .ToDictionary(g => g.Key, g => g.ToList());

            var times = new Dictionary<int, TimeSpan>();
            var previousNodes = new Dictionary<int, int>();
            var unvisitedNodes = new HashSet<int>();

            foreach (var node in nodes.Keys)
            {
                times[node] = TimeSpan.MaxValue;
                unvisitedNodes.Add(node);
            }

            times[startNodeId] = TimeSpan.Zero;

            while (unvisitedNodes.Count > 0)
            {
                var currentNode = unvisitedNodes.OrderBy(n => times[n]).First();
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

                        var edge = grid.Edges.FirstOrDefault(e => e.Id == connection.EdgeId);
                        if (edge == null)
                        {
                            _logger.LogWarning($"Edge with ID {connection.EdgeId} not found.");
                            continue;
                        }

                        var newTime = times[currentNode] + edge.Time;
                        if (newTime < times[neighbor])
                        {
                            times[neighbor] = newTime;
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

            if (path.First() != startNodeId || path.Last() != endNodeId)
            {
                _logger.LogError("The calculated path does not start at the start node or end at the end node.");
                return new List<int>();
            }

            _logger.LogInformation($"Calculated path: {string.Join(" -> ", path)}");
            return path;
        }




        private async Task<Grid> GetGridData()
        {
            var gridResponse = await _httpClient.GetAsync("https://localhost:7115/Grid/Get");
            if (!gridResponse.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get grid data. Status code: {gridResponse.StatusCode}");
                return null;
            }

            var gridContent = await gridResponse.Content.ReadAsStringAsync();
            JsonNode? grid;
            try
            {
                grid = JsonNode.Parse(gridContent);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing grid JSON: {ex.Message}");
                return null;
            }

            if (grid == null)
            {
                _logger.LogError("Error: Grid JSON is null.");
                return null;
            }

            var nodesJson = grid["Nodes"]?.AsArray();
            var edgesJson = grid["Edges"]?.AsArray();
            var connectionsJson = grid["Connections"]?.AsArray();

            if (nodesJson == null || edgesJson == null || connectionsJson == null)
            {
                if (nodesJson == null) _logger.LogError("Error: Missing nodes in the grid JSON.");
                if (edgesJson == null) _logger.LogError("Error: Missing edges in the grid JSON.");
                if (connectionsJson == null) _logger.LogError("Error: Missing connections in the grid JSON.");
                return null;
            }

            var gridObject = new Grid
            {
                Nodes = nodesJson.Select(node => new Node
                {
                    Id = node["Id"]?.GetValue<int>() ?? -1,
                }).Where(n => n.Id != -1).ToList(),
                Edges = edgesJson.Select(edge => new Edge
                {
                    Id = edge["Id"]?.GetValue<int>() ?? -1,
                    Cost = edge["Cost"]?.GetValue<int>() ?? -1,
                    Time = TimeSpan.Parse(edge["Time"]?.GetValue<string>() ?? "00:00:00"),
                }).Where(e => e.Id != -1 && e.Cost != -1).ToList(),
                Connections = connectionsJson.Select(conn => new Connection
                {
                    FirstNodeId = conn["FirstNodeId"]?.GetValue<int>() ?? -1,
                    SecondNodeId = conn["SecondNodeId"]?.GetValue<int>() ?? -1,
                    EdgeId = conn["EdgeId"]?.GetValue<int>() ?? -1,
                }).Where(c => c.FirstNodeId != -1 && c.SecondNodeId != -1 && c.EdgeId != -1).ToList()
            };

            _logger.LogInformation($"Deserialized Grid: Nodes={gridObject.Nodes.Count}, Edges={gridObject.Edges.Count}, Connections={gridObject.Connections.Count}");

            return gridObject;
        }






        private int CalculateOrderValue(int value, string deliveryDateUtc, string expirationDateUtc)
        {
            if (string.IsNullOrEmpty(expirationDateUtc) || string.IsNullOrEmpty(deliveryDateUtc))
            {
                _logger.LogError("Error: ExpirationDateUtc or DeliveryDateUtc is null or empty");
                return 0;
            }

            DateTime expirationDate = DateTime.Parse(expirationDateUtc);
            DateTime deliveryDate = DateTime.Parse(deliveryDateUtc);
            TimeSpan timeToDeliver = expirationDate - DateTime.UtcNow;

            if (timeToDeliver.TotalMinutes > 0)
            {
                _logger.LogInformation($"Order delivered on time. Value: {value}");
                return value;
            }
            else
            {
                // Apply a penalty for late delivery
                double penaltyPercentage = 0.5; // Example: 50% penalty
                int valueAfterPenalty = (int)(value * (1 - penaltyPercentage));
                _logger.LogInformation($"Order delivered late. Original Value: {value}, Value after penalty: {valueAfterPenalty}");
                return valueAfterPenalty;
            }
        }

        public List<int> GetCoinUpdates()
        {
            return _coinUpdates;
        }
    }
}