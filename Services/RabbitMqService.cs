using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SimBackend.Services
{
    public interface IRabbitMqService
    {
        void Subscribe(Action<string> handleOrder);
    }

    public class RabbitMqService : IRabbitMqService
    {
        private readonly string _hostname = "localhost";
        private readonly string _queueName = "HahnCargoSim_NewOrders";
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<RabbitMqService> _logger;

        public RabbitMqService(ILogger<RabbitMqService> logger)
        {
            _logger = logger;
            var factory = new ConnectionFactory { HostName = _hostname };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_queueName, false, false, false, null);
            _logger.LogInformation("RabbitMqService is initialized.");
        }

        public void Subscribe(Action<string> handleOrder)
        {
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                _logger.LogInformation($"Raw message received: {json}");
                handleOrder(json);
            };
            _channel.BasicConsume(_queueName, true, consumer);
            _logger.LogInformation("Subscribed to queue.");
        }
    }
}
