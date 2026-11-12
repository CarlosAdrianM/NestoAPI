using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#855: una recogida que pide el vendedor (NestoApp o Nesto) y confirma o rechaza el almacén. Al confirmarla nace la nota
    /// de recogida NR{PedidoNota}. Estado calculado al leer: Solicitada, Confirmada, EnReparto, Recogida, Rechazada o Anulada.
    /// </summary>
    public class RecogidaSolicitadaDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Telefono { get; set; }
        public string Ruta { get; set; }
        /// <summary>Qué hay que recoger.</summary>
        public string Texto { get; set; }
        public short Bultos { get; set; }
        public string FacturaOriginal { get; set; }
        /// <summary>«Reparto» (la furgoneta, solo ruta propia) o «Vendedor» (la recoge él al pasar por el cliente).</summary>
        public string QuienRecoge { get; set; }
        /// <summary>El vendedor del token de quien la pidió (null si no tiene).</summary>
        public string Vendedor { get; set; }
        public string UsuarioSolicita { get; set; }
        public DateTime FechaSolicitud { get; set; }
        /// <summary>Solicitada, Confirmada, EnReparto, Recogida, Rechazada o Anulada (calculado).</summary>
        public string Estado { get; set; }
        /// <summary>El pedido de la nota de recogida (al confirmar), o null.</summary>
        public int? PedidoNota { get; set; }
        /// <summary>«NR{PedidoNota}»: el código de barras del documento (el que se enseña y se lee en Algete), o null sin nota.</summary>
        public string CodigoDocumento { get; set; }
        /// <summary>«NR{PedidoNota}-1» … «NR{PedidoNota}-{Bultos}»: las etiquetas de los bultos (también valen en Algete). Vacía sin nota.</summary>
        public List<string> CodigosBultos { get; set; } = new List<string>();
        public string UsuarioResuelve { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string MotivoRechazo { get; set; }
        public string UsuarioAnula { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string MotivoAnulacion { get; set; }
        /// <summary>Cuándo se avisó de que llevaba un día laborable sin contestar (una sola vez), o null.</summary>
        public DateTime? FechaAvisoSinRespuesta { get; set; }
        /// <summary>
        /// Al anularla: lo que queda por hacer a mano (la nota sigue viva porque ya estaba en reparto o en un picking), o null si no queda
        /// nada (sin nota, o la nota se ha borrado).
        /// </summary>
        public string AvisoAnulacion { get; set; }
    }

    /// <summary>NestoAPI#855: POST api/NotasEntrega/Recogidas/Solicitudes.</summary>
    public class SolicitarRecogidaDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Texto { get; set; }
        public short? Bultos { get; set; }
        public string FacturaOriginal { get; set; }
        /// <summary>«Reparto» (por defecto) o «Vendedor».</summary>
        public string QuienRecoge { get; set; }
    }

    /// <summary>NestoAPI#855: POST api/NotasEntrega/Recogidas/Solicitudes/{id}/Confirmar (el almacén puede corregir bultos y texto).</summary>
    public class ConfirmarRecogidaSolicitadaDTO
    {
        public short? Bultos { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#855: POST api/NotasEntrega/Recogidas/Solicitudes/{id}/Rechazar.</summary>
    public class RechazarRecogidaSolicitadaDTO
    {
        public string Motivo { get; set; }
    }
}
