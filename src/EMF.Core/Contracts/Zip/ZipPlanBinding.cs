using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EMF.Core.Contracts.Zip;

public static class ZipPlanBinding
{
    public static string Compute(string receipt,IReadOnlyList<ZipEntryPlan> entries)=>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Version=1, Receipt=receipt, Entries=entries }))));
}
