using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#181: construye y firma los Ds_MerchantParameters a mano.
    ///
    /// <para><b>Por qué no vale el helper oficial</b>: RedsysAPI.dll solo admite parámetros de
    /// tipo cadena (<c>SetParameter(clave, valor)</c>), y la entrada REST de EMV 3DS 2 exige que
    /// <c>DS_MERCHANT_EMV3DS</c> viaje como OBJETO JSON anidado
    /// (<c>"DS_MERCHANT_EMV3DS": {"threeDSInfo":"CardData"}</c>). La forma de cadena con JSON
    /// dentro es la de la entrada XML/webservice, no la de REST. Por eso aquí montamos el JSON
    /// nosotros y firmamos el resultado.</para>
    ///
    /// <para><b>La firma es la misma</b> que hace la DLL: se deriva una clave cifrando el número
    /// de pedido con 3DES-CBC (IV a ceros, relleno de ceros) con la clave del comercio, y se
    /// calcula el HMAC-SHA256 de los parámetros ya codificados en base64. Los tests comprueban
    /// que para los mismos datos sale EXACTAMENTE la misma firma que RedsysAPI.dll, que es lo
    /// que nos permite fiarnos de esta implementación en un cobro real.</para>
    /// </summary>
    internal static class FirmadorRedsys
    {
        /// <summary>
        /// El JSON de parámetros codificado en base64, tal y como viaja en Ds_MerchantParameters.
        /// </summary>
        internal static string ParametrosBase64(JObject parametros)
        {
            if (parametros == null)
            {
                throw new ArgumentNullException(nameof(parametros));
            }

            string json = parametros.ToString(Formatting.None);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Firma HMAC-SHA256 de los parámetros, con la clave derivada del número de pedido.
        /// </summary>
        internal static string Firmar(string parametrosBase64, string numeroOrden, string claveComercio)
        {
            if (string.IsNullOrWhiteSpace(parametrosBase64))
            {
                throw new ArgumentException("No hay parámetros que firmar", nameof(parametrosBase64));
            }
            if (string.IsNullOrWhiteSpace(numeroOrden))
            {
                throw new ArgumentException("La firma de Redsys se deriva del número de pedido", nameof(numeroOrden));
            }
            if (string.IsNullOrWhiteSpace(claveComercio))
            {
                throw new ArgumentException("Falta la clave secreta del comercio", nameof(claveComercio));
            }

            byte[] claveDerivada = DerivarClave(numeroOrden, Convert.FromBase64String(claveComercio));
            using (HMACSHA256 hmac = new HMACSHA256(claveDerivada))
            {
                byte[] firma = hmac.ComputeHash(Encoding.UTF8.GetBytes(parametrosBase64));
                return Convert.ToBase64String(firma);
            }
        }

        /// <summary>
        /// Cifra el número de pedido con 3DES-CBC (IV a ceros y relleno de ceros hasta múltiplo
        /// de 8). El resultado es la clave con la que se firma esa operación concreta.
        /// </summary>
        private static byte[] DerivarClave(string numeroOrden, byte[] claveComercio)
        {
            using (TripleDES tripleDes = TripleDES.Create())
            {
                tripleDes.Mode = CipherMode.CBC;
                // Redsys rellena con ceros por su cuenta, así que el proveedor no debe rellenar
                tripleDes.Padding = PaddingMode.None;
                tripleDes.Key = claveComercio;
                tripleDes.IV = new byte[8];

                byte[] datos = Encoding.UTF8.GetBytes(numeroOrden);
                int longitudBloques = ((datos.Length + 7) / 8) * 8;
                byte[] bloque = new byte[longitudBloques];
                Buffer.BlockCopy(datos, 0, bloque, 0, datos.Length);

                using (ICryptoTransform cifrador = tripleDes.CreateEncryptor())
                {
                    return cifrador.TransformFinalBlock(bloque, 0, bloque.Length);
                }
            }
        }
    }
}
