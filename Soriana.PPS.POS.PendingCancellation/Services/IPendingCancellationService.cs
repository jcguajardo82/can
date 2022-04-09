using System;
using System.Threading.Tasks;

namespace Soriana.PPS.POS.PendingCancellation.Services
{
    public interface IPendingCancellationService
    {
        Task<string> PendingCancellation(string OrderID);
    }
}
