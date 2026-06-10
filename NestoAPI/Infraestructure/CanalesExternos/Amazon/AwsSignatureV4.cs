using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: firma AWS Signature V4 para llamar a Amazon SQS por HTTP sin SDK.
    /// Port directo del script PowerShell ya validado contra la cola real (10/06/2026).
    /// Solo firma los headers host;x-amz-date (NO content-type) para evitar mismatches
    /// con el Content-Type que añade HttpClient.
    /// </summary>
    public static class AwsSignatureV4
    {
        public struct FirmaResultado
        {
            public string AmzDate { get; set; }
            public string Authorization { get; set; }
        }

        /// <summary>
        /// Firma una petición POST cuyo cuerpo (form-urlencoded) es el payload.
        /// </summary>
        public static FirmaResultado FirmarPost(
            string accessKey, string secretKey, string region, string service,
            string host, string canonicalUri, string body, DateTime utcNow)
        {
            string amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            string canonicalHeaders = "host:" + host + "\n" + "x-amz-date:" + amzDate + "\n";
            string signedHeaders = "host;x-amz-date";
            string payloadHash = Sha256Hex(body);

            // POST \n uri \n (query vacío) \n canonicalHeaders \n signedHeaders \n payloadHash
            string canonicalRequest =
                "POST\n" + canonicalUri + "\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;

            const string algorithm = "AWS4-HMAC-SHA256";
            string credentialScope = dateStamp + "/" + region + "/" + service + "/aws4_request";
            string stringToSign =
                algorithm + "\n" + amzDate + "\n" + credentialScope + "\n" + Sha256Hex(canonicalRequest);

            byte[] kSecret = Encoding.UTF8.GetBytes("AWS4" + secretKey);
            byte[] kDate = HmacSha256(kSecret, dateStamp);
            byte[] kRegion = HmacSha256(kDate, region);
            byte[] kService = HmacSha256(kRegion, service);
            byte[] kSigning = HmacSha256(kService, "aws4_request");
            string signature = ToHex(HmacSha256(kSigning, stringToSign));

            string authorization = algorithm + " Credential=" + accessKey + "/" + credentialScope +
                ", SignedHeaders=" + signedHeaders + ", Signature=" + signature;

            return new FirmaResultado { AmzDate = amzDate, Authorization = authorization };
        }

        private static byte[] HmacSha256(byte[] key, string data)
        {
            using (HMACSHA256 h = new HMACSHA256(key))
            {
                return h.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        private static string Sha256Hex(string data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(data)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                _ = sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
