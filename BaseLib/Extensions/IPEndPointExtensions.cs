using System.Net;

namespace BaseLib.Extensions
{
    public static class IPEndPointExtensions
    {
        public static string ToIPv4String(this IPEndPoint endPoint)
        {
            if (endPoint == null)
                return string.Empty;

            return $"{endPoint.Address.MapToIPv4()}:{endPoint.Port}";
        }

        public static IPEndPoint ToIPv4EndPoint(this IPEndPoint endPoint)
        {
            if (endPoint == null)
                return null;

            return new IPEndPoint(endPoint.Address.MapToIPv4(), endPoint.Port);
        }
    }
}
