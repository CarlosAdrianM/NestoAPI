using System;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): respuesta de GET api/EnviosAgencias/{id}/PruebaEntrega, la misma para todas las
    /// agencias. Siempre 200 (salvo 403 sin permiso y 404 si el envío no existe): si no hay prueba, <see cref="Disponible"/> false y
    /// el motivo en <see cref="Mensaje"/>. <see cref="Documento"/> va en base64 (Json.NET) con su <see cref="TipoDocumento"/>; si
    /// la agencia solo da un enlace, en <see cref="UrlDocumento"/>. No se guarda copia: se pide a la agencia en cada consulta.
    /// </summary>
    public class PruebaEntregaEnvioDTO
    {
        public int Numero { get; set; }
        public int Agencia { get; set; }
        public string NombreAgencia { get; set; }
        public int? Pedido { get; set; }
        public bool Disponible { get; set; }
        public string Mensaje { get; set; }
        public DateTime? FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public byte[] Documento { get; set; }
        public string TipoDocumento { get; set; }
        public string UrlDocumento { get; set; }
        /// <summary>Solo el reparto propio (como en GET api/RepartoPropio/Envios/{id}/PruebaEntrega).</summary>
        public string CobroReembolso { get; set; }
        public decimal? ImporteCobrado { get; set; }
        public string Usuario { get; set; }
    }
}
