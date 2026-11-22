using System;
using System.Text;

namespace JTox
{
    internal static class Hex
    {
        public static string Encode(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        public static byte[] Decode(string s)
        {
            s = s.Trim();
            if (s.Length % 2 != 0) throw new FormatException("odd length");
            byte[] result = new byte[s.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = Uri.IsHexDigit(s[i * 2]) ? Convert.ToInt32(s[i * 2].ToString(), 16) : -1;
                int lo = Uri.IsHexDigit(s[i * 2 + 1]) ? Convert.ToInt32(s[i * 2 + 1].ToString(), 16) : -1;
                if (hi < 0 || lo < 0) throw new FormatException("not hex");
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }
    }
}
