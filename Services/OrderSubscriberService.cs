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

namespace SimBackend.Services
{
    public class OrderSubscriberService : BackgroundService, IOrderSubscriberService
    {
        private readonly IRabbitMqService _rabbitMqService;
        private readonly HttpClient _httpClient;
        private readonly List<int> _coinUpdates = new List<int>();
        private string _token;

        public OrderSubscriberService(
            IRabbitMqService rabbitMqService,
            HttpClient httpClient)
        {
            _rabbitMqService = rabbitMqService;
            _httpClient = httpClient;
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
                Console.WriteLine($"Message received: Order ID = {order.Id}, OriginNodeId = {order.OriginNodeId}, TargetNodeId = {order.TargetNodeId}");

                bool accepted = new Random().Next(2) == 0;
                Console.WriteLine($"Order {order.Id} acceptance status: {accepted}");
                if (accepted)
                {
                    await AcceptOrder(order);
                    await MoveTransporter(order);
                    int coins = CalculateOrderValue(order);
                    Console.WriteLine($"Order {order.Id} processed. Coins earned: {coins}");
                    _coinUpdates.Add(coins); // Store the coin updates
                }
            });

            return Task.CompletedTask;
        }

        private async Task AcceptOrder(OrderDto order)
        {
            Console.WriteLine($"Accepting order {order.Id}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var response = await _httpClient.PostAsync($"https://localhost:7115/Order/Accept?orderId={order.Id}", null);
            Console.WriteLine($"Order {order.Id} acceptance response: {response.StatusCode}");
        }

        private async Task MoveTransporter(OrderDto order)
        {
            Console.WriteLine($"Moving transporter for order {order.Id}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var transportersResponse = await _httpClient.GetAsync("https://localhost:7115/CargoTransporter/GetAll");
            var transporters = JsonSerializer.Deserialize<List<CargoTransporterDto>>(await transportersResponse.Content.ReadAsStringAsync());

            var transporter = transporters.FirstOrDefault(t => !t.InTransit);
            if (transporter == null)
            {
                Console.WriteLine("No available transporter found.");
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
                    Console.WriteLine($"Moved transporter {transporter.Id} to node {nodeId}. Response: {moveResponse.StatusCode}");
                }
                transporter.PositionNodeId = targetNode;
            }
        }

        private async Task<List<int>> GetPath(int startNodeId, int endNodeId)
        {
            Console.WriteLine($"Getting path from node {startNodeId} to node {endNodeId}");
            var response = await _httpClient.GetAsync($"https://localhost:7115/Grid/GetPath?startNodeId={startNodeId}&endNodeId={endNodeId}");
            var path = JsonSerializer.Deserialize<List<int>>(await response.Content.ReadAsStringAsync());
            Console.WriteLine($"Path retrieved: {string.Join(" -> ", path)}");
            return path;
        }

        private int CalculateOrderValue(OrderDto order)
        {
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
