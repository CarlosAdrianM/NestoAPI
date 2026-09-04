using Newtonsoft.Json.Linq;

namespace NestoAPI.Models.Pagos
{
    public class RespuestaRedsys
    {
        public string Ds_Amount { get; set; }
        public string Ds_AuthorisationCode { get; set; }
        public string Ds_Currency { get; set; }
        public string Ds_Language { get; set; }
        public string Ds_MerchantCode { get; set; }
        public string Ds_MerchantData { get; set; }
        public string Ds_Order { get; set; }
        public string Ds_Response { get; set; }
        public string Ds_SecurePayment { get; set; }
        public string Ds_Terminal { get; set; }
        public string Ds_TransactionType { get; set; }
        public string Ds_UrlPago2Fases { get; set; }

        // NestoAPI#178: campos que manda Redsys cuando el pago se hizo con tokenización
        // (DS_MERCHANT_IDENTIFIER=REQUIRED). El token permite cobros posteriores sin que el
        // cliente meta la tarjeta; el PAN se queda en Redsys.

        /// <summary>El token de la tarjeta.</summary>
        public string Ds_Merchant_Identifier { get; set; }

        /// <summary>Caducidad de la tarjeta, formato AAMM (p.ej. "2712" = diciembre de 2027).</summary>
        public string Ds_ExpiryDate { get; set; }

        /// <summary>
        /// Número de tarjeta enmascarado (p.ej. "454881******04"). OJO: Redsys solo lo manda si
        /// el comercio tiene activado "recibir datos de tarjeta" (lo activa el banco, no el
        /// panel); el nuestro NO lo tiene (comprobado 01/09/26: alta 0 EUR autorizada sin él).
        /// </summary>
        public string Ds_Card_Number { get; set; }

        /// <summary>
        /// Últimos 4 dígitos. Redsys lo manda en pagos con cartera (Google Pay / Apple Pay) y
        /// su documentación dice que, si viene, es preferible a Ds_Card_Number.
        /// </summary>
        public string Ds_Card_Last4 { get; set; }

        /// <summary>Marca: 1=Visa, 2=Mastercard, 8=Amex...</summary>
        public string Ds_Card_Brand { get; set; }

        /// <summary>C = crédito, D = débito.</summary>
        public string Ds_Card_Type { get; set; }

        /// <summary>
        /// Identificador COF del pago inicial; algunos adquirentes lo exigen en los cobros MIT
        /// posteriores (DS_MERCHANT_COF_TXNID).
        /// </summary>
        public string Ds_Merchant_Cof_Txnid { get; set; }

        /// <summary>
        /// NestoAPI#181: bloque de EMV 3DS 2 (protocolVersion, threeDSServerTransID,
        /// threeDSMethodURL, acsURL, creq...). Es un objeto JSON anidado, así que se guarda como
        /// JToken; usar <c>RedsysService.Emv3DSDe()</c> para leerlo.
        /// </summary>
        public JToken Ds_EMV3DS { get; set; }

        /// <summary>
        /// El JSON decodificado tal cual llegó de Redsys (todas las claves y valores, también
        /// las que no están mapeadas arriba). Solo para el diagnóstico temporal de #445; no se
        /// serializa.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string JsonCrudo { get; set; }
    }
}
