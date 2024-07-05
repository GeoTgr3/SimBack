using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimBackend.Interfaces
{
    public interface IOrderSubscriberService
    {
        void SetToken(string token);
        List<int> GetCoinUpdates();
    }
}
