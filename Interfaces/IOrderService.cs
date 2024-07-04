using SimBackend.Model.DTO;

namespace SimBackend.Services
{
    public interface IOrderService
    {
        void AddOrder(OrderDto order);
        IEnumerable<OrderDto> GetAllOrders();
    }
}
