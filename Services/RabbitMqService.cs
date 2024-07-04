using System.Text;
using System.Text.Json;
using SimBackend.Model.DTO;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SimBackend.Services
{
    public interface IRabbitMqService
    {
        void Subscribe(Action<OrderDto> handleOrder);
    }

    public class RabbitMqService : IRabbitMqService
    {
        private readonly string _hostname = "localhost";
        private readonly string _queueName = "HahnCargoSim_NewOrders";
        private readonly IConnection _connection;
        private readonly IModel _channel;

        public RabbitMqService()
        {
            var factory = new ConnectionFactory { HostName = _hostname };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_queueName, false, false, false, null);
            Console.WriteLine("RabbitMqService is initialized.");
        }

        public void Subscribe(Action<OrderDto> handleOrder)
        {
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                Console.WriteLine($"Raw message received: {json}");
                var order = JsonSerializer.Deserialize<OrderDto>(json);

                if (order == null)
                {
                    Console.WriteLine("Error: Failed to deserialize order.");
                }
                else
                {
                    handleOrder(order);
                }
            };
            _channel.BasicConsume(_queueName, true, consumer);
            Console.WriteLine("Subscribed to queue.");
        }
    }
}
