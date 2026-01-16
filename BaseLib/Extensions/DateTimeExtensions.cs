using System;

namespace BaseLib.Extensions
{
    public static class DateTimeExtensions
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime UnixEpochLocal = UnixEpoch.ToLocalTime();

        public static long ToUnixTime(this DateTime dt)
        {
            return (long)(dt - UnixEpochLocal).TotalSeconds;
        }

        public static long ToUnixTimeUtc(this DateTime dt)
        {
            return (long)(dt.ToUniversalTime() - UnixEpoch).TotalSeconds;
        }
    }
}
