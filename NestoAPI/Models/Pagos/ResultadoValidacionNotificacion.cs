using System;

namespace NestoAPI.Models.Pagos
{
    public class ResultadoValidacionNotificacion
    {
        public bool FirmaValida { get; set; }
        public bool PagoAutorizado { get; set; }
        public string CodigoRespuesta { get; set; }
        public string CodigoAutorizacion { get; set; }
        public string NumeroOrden { get; set; }
        public string MensajeError { get; set; }

        // NestoAPI#178: datos de la tarjeta tokenizada, si el pago se hizo con
        // DS_MERCHANT_IDENTIFIER=REQUIRED. Con ellos ProcesarNotificacion da de alta la
        // tarjeta guardada del cliente.

        /// <summary>Token de Redsys (Ds_Merchant_Identifier). Null si no se tokenizó.</summary>
        public string TokenTarjeta { get; set; }

        /// <summary>Ds_Merchant_Cof_Txnid del pago inicial, para los cobros MIT posteriores.</summary>
        public string CofTxnId { get; set; }

        public string UltimosDigitosTarjeta { get; set; }
        public DateTime? FechaCaducidadTarjeta { get; set; }
        public string MarcaTarjeta { get; set; }

        /// <summary>C = crédito, D = débito.</summary>
        public string TipoTarjeta { get; set; }

        /// <summary>
        /// Nombres (solo nombres, sin valores) de los campos que venían en Ds_MerchantParameters.
        /// Para el diagnóstico de qué manda y qué no manda Redsys en cada terminal.
        /// </summary>
        public string CamposRecibidos { get; set; }

        public bool TieneTokenTarjeta => !string.IsNullOrWhiteSpace(TokenTarjeta);
    }
}
