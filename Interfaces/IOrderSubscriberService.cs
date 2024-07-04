using System.Collections.Generic;
using System.Threading.Tasks;
using SimBackend.Model.DTO;

namespace SimBackend.Interfaces
{
    public interface IOrderSubscriberService
    {
        void SetToken(string token);
        List<int> GetCoinUpdates();
    }
}
