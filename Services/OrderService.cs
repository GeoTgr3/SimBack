using System.Collections.Concurrent;
using SimBackend.Model.DTO;

namespace SimBackend.Services
{
    public class OrderService : IOrderService
    {
        private readonly ConcurrentBag<OrderDto> _orders = new ConcurrentBag<OrderDto>();

        public void AddOrder(OrderDto order)
        {
            _orders.Add(order);
        }

        public IEnumerable<OrderDto> GetAllOrders()
        {
            return _orders.ToList();
        }
    }
}
