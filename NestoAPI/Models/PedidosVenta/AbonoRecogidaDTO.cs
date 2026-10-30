using System;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#760 (corte 4 de #755): GET api/NotasEntrega/Recogidas/{codigo}. Lo que Nesto enseña antes de crear el abono de una
    /// recogida (cliente, texto, bultos, quién la entregó y cuándo).
    /// </summary>
    public class RecogidaDTO
    {
        public string Empresa { get; set; }
        /// <summary>El pedido de la nota de recogida (CabPedidoVta.Número).</summary>
        public int PedidoNota { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Las líneas de texto de la nota, juntas.</summary>
        public string TextoNota { get; set; }
        public short Bultos { get; set; }
        /// <summary>La factura que se devuelve, si la nota la tiene (opcional): con ella, la rectificativa queda vinculada.</summary>
        public string FacturaOriginal { get; set; }
        /// <summary>"Tramitado", "Incidentado", "Entregado" (= recogido) o null (sin envío).</summary>
        public string EstadoEnvio { get; set; }
        /// <summary>Cuándo la recogió el repartidor (prueba de entrega o FechaEntrega del envío), o null.</summary>
        public DateTime? FechaRecogida { get; set; }
        /// <summary>
        /// Quién la entregó al repartidor (prueba de entrega). Es un dato personal: solo para Almacén, Administración y Dirección
        /// (como la prueba de entrega de api/RepartoPropio); a Informática le llega null.
        /// </summary>
        public string Receptor { get; set; }
        /// <summary>El pedido de abono ya creado para esta recogida, o null.</summary>
        public int? PedidoAbono { get; set; }
    }

    /// <summary>NestoAPI#760: el cuerpo de POST api/NotasEntrega/Recogidas/Abono.</summary>
    public class CrearAbonoRecogidaDTO
    {
        /// <summary>«NR929554-1» (la etiqueta del bulto) o «NR929554» (el documento).</summary>
        public string Codigo { get; set; }
        /// <summary>Crear el abono aunque el repartidor no la haya marcado como recogida (Nesto lo pregunta tras el 409).</summary>
        public bool Forzar { get; set; }
    }

    /// <summary>NestoAPI#760: la respuesta de POST api/NotasEntrega/Recogidas/Abono (201 si se crea, 200 si ya existía).</summary>
    public class AbonoRecogidaDTO
    {
        public string Empresa { get; set; }
        /// <summary>El pedido de abono (sin líneas al crearlo: Nesto las va añadiendo con cantidad −1).</summary>
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        /// <summary>La serie rectificativa (RV para una nota NV).</summary>
        public string Serie { get; set; }
        public int PedidoNota { get; set; }
        public string FacturaOriginal { get; set; }
        /// <summary>El texto de la nota, para el aviso fijo de la cabecera en Nesto.</summary>
        public string TextoNota { get; set; }
        /// <summary>True si el abono ya estaba creado (otro bulto de la misma recogida o una segunda lectura).</summary>
        public bool YaExistia { get; set; }
    }
}
