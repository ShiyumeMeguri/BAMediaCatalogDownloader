using K4os.Hash.xxHash;
using System.Text;

namespace GameMainConfigEncryption
{
    public static class EncryptionUtils
    {
        public static string NewEncryptString(string value, byte[] key)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 8)
            {
                return value;
            }
            byte[] raw = Encoding.Unicode.GetBytes(value);
            byte[] xored = EncryptionUtils.XOR(raw, key);
            return Convert.ToBase64String(xored);
        }

        public static byte[] CreateKey(string name)
        {
            uint seed = XXH32.DigestOf(Encoding.UTF8.GetBytes(name));
            var twister = new MersenneTwister(seed);
            return twister.NextBytes(8);
        }

        public static byte[] XOR(byte[] value, byte[] key)
        {
            int keyLen = key.Length;
            return value.Select((b, i) => (byte)(b ^ key[i % keyLen])).ToArray();
        }

        public static string ConvertString(string value, byte[] key)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            try
            {
                // 先 Base64 解码
                byte[] raw = Convert.FromBase64String(value);
                // XOR
                byte[] xored = XOR(raw, key);
                // 按 UTF-16 (LE) 解码
                string decoded = Encoding.Unicode.GetString(xored);
                return decoded;
            }
            catch
            {
                return value;
            }
        }
    }
}
