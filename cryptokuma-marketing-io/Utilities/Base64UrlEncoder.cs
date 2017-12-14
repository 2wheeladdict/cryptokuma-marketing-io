using System;
using System.Collections.Generic;
using System.Text;

namespace Cryptokuma.Marketing.IO.Utilities
{
    public static class Base64UrlEncoder
    {
        static readonly char[] PADDING = { '=' };

        public static string Encode(string rawText)
        {
            return System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawText)).TrimEnd(PADDING).Replace('+', '-').Replace('/', '_');
        }

        public static string Decode(string encodedText)
        {
            string incoming = encodedText.Replace('_', '/').Replace('-', '+');
            switch (encodedText.Length % 4)
            {
                case 2:
                    incoming += "==";
                    break;
                case 3:
                    incoming += "=";
                    break;
            }
            byte[] bytes = Convert.FromBase64String(incoming);
            string originalText = Encoding.ASCII.GetString(bytes);

            return originalText;
        }
    }
}
