using System;

namespace NestoAPI.Models.Pagos
{
    /// <summary>Cuerpo de POST api/Pagos/{id}/Anular (Administración, Dirección e Informática).</summary>
    public class SolicitudAnularEnlacePago
    {
        /// <summary>Obligatorio (hasta 200 caracteres): por qué se anula, p. ej. «Pedido ya cobrado por reembolso».</summary>
        public string Motivo { get; set; }
    }

    /// <summary>Respuesta de POST api/Pagos/{id}/Anular.</summary>
    public class RespuestaAnularEnlacePago
    {
        public int Id { get; set; }
        /// <summary>Siempre «Cancelado».</summary>
        public string Estado { get; set; }
        public DateTime FechaActualizacion { get; set; }
        /// <summary>Para enseñárselo al usuario tal cual.</summary>
        public string Mensaje { get; set; }
    }
}
