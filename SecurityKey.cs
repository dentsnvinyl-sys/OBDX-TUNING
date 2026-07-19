namespace ODBX
{
    public static class SecurityKey
    {
        public static byte[] Calculate (byte[] seed)
        {
            ushort s = (ushort)((seed[0] << 8) | seed[1]);

            // XOR with constant
            ushort tmp = (ushort)(s ^ 0xA5A5);

            // Rotate left 3
            tmp = (ushort)((tmp << 3) | (tmp >> 13));

            // Add constant
            ushort key = (ushort)(tmp + 0x5B7D);

            // XOR final constant
            key ^= 0x3F21;

            return new byte[]
            {
                (byte)(key >> 8),
                (byte)(key & 0xFF)
            };
        }
    }
}
