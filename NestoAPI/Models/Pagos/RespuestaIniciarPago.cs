using System;

namespace NestoAPI.Models.Pagos
{
    public class RespuestaIniciarPago
    {
        public int IdPago { get; set; }
        public string UrlRedsys { get; set; }
        public string Ds_SignatureVersion { get; set; }
        public string Ds_MerchantParameters { get; set; }
        public string Ds_Signature { get; set; }
        public Guid TokenAcceso { get; set; }
        public string UrlPaginaPago { get; set; }

        /// <summary>
        /// NestoAPI#181: página propia que autentica el cobro con EMV 3DS 2 sin enseñar la
        /// pasarela. Solo viene cuando se paga con una tarjeta guardada. Si la app la recibe,
        /// carga esta URL en el WebView en vez de enviar el formulario a Redsys; las versiones
        /// antiguas ignoran el campo y siguen funcionando como siempre.
        /// </summary>
        public string UrlPago3DS { get; set; }

        /// <summary>
        /// NestoAPI#679: avisos al generar el enlace de un pedido contra reembolso (el pedido aún no ha salido: pasarlo a
        /// tarjeta prepago; ya va en camino: hable con Administración/Logística). No bloquean: el enlace ya está creado.
        /// Los clientes antiguos ignoran el campo.
        /// </summary>
        public System.Collections.Generic.List<AvisoEnlacePago> Avisos { get; set; } = new System.Collections.Generic.List<AvisoEnlacePago>();
    }
}
