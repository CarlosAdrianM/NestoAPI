using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Traspasos
{
    /// <summary>
    /// NestoAPI#553: estados del documento Traspaso (patrón pedido de traslado en dos pasos).
    /// Se guarda como tinyint en Traspasos.Estado (ver Scripts/Issue553_Traspasos.sql).
    /// </summary>
    public enum EstadoTraspaso : byte
    {
        Propuesto = 0,      // Calculado y guardado, todavía sin escribir en PreExtrProducto
        EnPreparacion = 1,  // Líneas en PreExtrProducto (salida y entrada), preparándose en el origen
        Enviado = 2,        // Salida contabilizada por traspaso (fase 2)
        Recibido = 3,       // Entrada contabilizada en el destino (fase 3)
        Anulado = 9
    }

    /// <summary>
    /// NestoAPI#553: cabecera del documento Traspaso. Todavía NO está mapeada en el EDMX (siguiente
    /// corte); son clases de dominio sin persistencia.
    /// </summary>
    public class Traspaso
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        /// <summary>Número del contador compartido ContadoresGlobales.TraspasoAlmacén = PreExtrProducto.NºTraspaso.</summary>
        public int NumTraspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public EstadoTraspaso Estado { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaEnvio { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public DateTime FechaModificacion { get; set; }
        public List<TraspasoLinea> Lineas { get; set; } = new List<TraspasoLinea>();

        public bool TieneDiferencias => Lineas.Any(l => l.TieneDiferencia);
    }

    /// <summary>NestoAPI#553: línea del traspaso (propuesto / preparado / enviado / recibido).</summary>
    public class TraspasoLinea
    {
        public int Id { get; set; }
        public int TraspasoId { get; set; }
        public string Producto { get; set; }
        public int CantidadPropuesta { get; set; }
        public int? CantidadPreparada { get; set; }
        public int? CantidadEnviada { get; set; }
        public int? CantidadRecibida { get; set; }
        /// <summary>Motivo de la diferencia (rotura, no encontrado, error de preparación…).</summary>
        public string Motivo { get; set; }
        public DateTime? FechaPreparacion { get; set; }
        public DateTime? FechaRecepcion { get; set; }

        /// <summary>Hay diferencia cuando lo recibido no cuadra con lo enviado (solo se sabe al recibir).</summary>
        public bool TieneDiferencia => CantidadEnviada.HasValue && CantidadRecibida.HasValue
            && CantidadEnviada.Value != CantidadRecibida.Value;
    }
}
