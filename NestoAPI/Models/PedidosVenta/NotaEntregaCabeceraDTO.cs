namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#757 (corte 1 de #755): la cabecera de una nota de entrega, de GET api/NotasEntrega/{empresa}/{pedido} y de la
    /// respuesta del PUT …/Cabecera. Sin fila en NotasEntregaCabecera (las de Nesto viejo): Entrega, 1 bulto.
    /// </summary>
    public class NotaEntregaCabeceraDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>"Entrega" o "Recogida" (<see cref="TiposNotaEntrega"/>).</summary>
        public string Tipo { get; set; }
        /// <summary>Solo tiene sentido en las de Recogida; en las de Entrega los da el packing (Carlos 30/10/26).</summary>
        public short Bultos { get; set; }
        /// <summary>La factura que se devuelve (solo Recogida), opcional.</summary>
        public string FacturaOriginal { get; set; }
        /// <summary>Corte 4: el pedido de abono creado al escanear la recogida.</summary>
        public int? PedidoAbono { get; set; }
        /// <summary>NotasEntrega.Numero de sus líneas, o null si aún no se ha procesado.</summary>
        public int? NumeroNota { get; set; }
        /// <summary>Alguna línea en −2 (nota de entrega procesada).</summary>
        public bool Procesada { get; set; }
        /// <summary>El último envío del pedido (EnviosAgencia.Numero), o null.</summary>
        public int? EnvioId { get; set; }
        /// <summary>"Tramitado", "Incidentado", "Entregado" o null (sin envío o sin tramitar).</summary>
        public string EstadoEnvio { get; set; }
        /// <summary>"NE{pedido}" en las de entrega, "NR{pedido}" en las de recogida.</summary>
        public string CodigoDocumento { get; set; }
    }

    /// <summary>NestoAPI#757: el cuerpo de PUT api/NotasEntrega/{empresa}/{pedido}/Cabecera.</summary>
    public class ActualizarNotaEntregaCabeceraDTO
    {
        public string Tipo { get; set; }
        /// <summary>Recogida: obligatorio y ≥ 1 (sin él, 1). Entrega: no se valida; se guarda el valor por defecto (1).</summary>
        public short? Bultos { get; set; }
        /// <summary>Solo en Recogida. En Entrega se IGNORA (se guarda NULL), no da error.</summary>
        public string FacturaOriginal { get; set; }
    }

    public static class TiposNotaEntrega
    {
        public const string ENTREGA = "Entrega";
        public const string RECOGIDA = "Recogida";
        public const short BULTOS_POR_DEFECTO = 1;
        public const short BULTOS_MAXIMO = 999;

        /// <summary>El tipo bien escrito ("Entrega"/"Recogida") sin importar mayúsculas ni espacios; null si no es ninguno.</summary>
        public static string Normalizar(string tipo)
        {
            string t = tipo?.Trim();
            if (string.Equals(t, ENTREGA, System.StringComparison.OrdinalIgnoreCase))
            {
                return ENTREGA;
            }
            if (string.Equals(t, RECOGIDA, System.StringComparison.OrdinalIgnoreCase))
            {
                return RECOGIDA;
            }
            return null;
        }

        public static string CodigoDocumento(string tipo, int pedido) => (tipo == RECOGIDA ? "NR" : "NE") + pedido;
    }
}
