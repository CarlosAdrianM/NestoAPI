using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// Carlos (19/11/26, caso del cliente 33895): una tienda vendió a mano, en un pedido normal, la referencia del curso de un
    /// evento (90077, Masterclass Rosácea) y lo cobró en caja: factura cobrada, pero la inscripción siguió reservada y sin cupón.
    /// Decisión: NO se bloquea; el POST/PUT del pedido se guarda igual y la respuesta lleva este aviso. Las líneas de curso de la
    /// plantilla (con EventoId, NestoAPI#691) y los pedidos que crea la propia facturación del curso (#612, sin pasar por aquí) no
    /// avisan. En el PUT, solo las líneas nuevas.
    /// </summary>
    public static class AvisoCursoVendidoAMano
    {
        public const string TIPO = "CursoVendidoAMano";

        /// <summary>Un evento vivo (activo y de hoy en adelante) con la referencia de su curso.</summary>
        public class EventoConCurso
        {
            public int Id { get; set; }
            public string Titulo { get; set; }
            public DateTime Fecha { get; set; }
            public string ProductoCurso { get; set; }
        }

        /// <summary>
        /// LA REGLA (pura): las referencias de las líneas (sin EventoId) que son el curso de un evento vivo: su ProductoCurso o su
        /// referencia propia (EVENTO00123 / EVENTO00123NR). Un aviso con todas, o null.
        /// </summary>
        public static AvisoPedidoDTO Aviso(IEnumerable<LineaPedidoVentaDTO> lineas, IEnumerable<EventoConCurso> eventosVivos)
        {
            List<string> productos = Productos(lineas);
            if (!productos.Any())
            {
                return null;
            }
            List<EventoConCurso> eventos = (eventosVivos ?? Enumerable.Empty<EventoConCurso>()).Where(e => e != null).ToList();
            var mensajes = new List<string>();
            foreach (string producto in productos)
            {
                EventoConCurso evento = EventoDe(producto, eventos);
                if (evento != null)
                {
                    mensajes.Add($"La referencia {producto} es el curso del evento «{evento.Titulo?.Trim()}» ({evento.Fecha:dd/MM/yyyy}).");
                }
            }
            if (!mensajes.Any())
            {
                return null;
            }
            return new AvisoPedidoDTO
            {
                Tipo = TIPO,
                Mensaje = string.Join(" ", mensajes) + " Para que la clienta quede inscrita y reciba su cupón, inscríbala en el evento " +
                    "y cobre la señal en Cajas. El pedido se ha guardado igual."
            };
        }

        /// <summary>Las referencias (sin repetir) de las líneas de producto que no vienen de la plantilla de cursos (sin EventoId).</summary>
        public static List<string> Productos(IEnumerable<LineaPedidoVentaDTO> lineas) =>
            (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && l.EventoId == null && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                    && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .Select(l => l.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static EventoConCurso EventoDe(string producto, List<EventoConCurso> eventos)
        {
            EventoConCurso porProducto = eventos.Where(e => string.Equals(e.ProductoCurso?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Fecha).FirstOrDefault();
            if (porProducto != null)
            {
                return porProducto;
            }
            (int EventoId, ModalidadInscripcionEvento Modalidad)? propia = ReglasProductoCurso.Interpretar(producto);
            return propia.HasValue ? eventos.FirstOrDefault(e => e.Id == propia.Value.EventoId) : null;
        }

        /// <summary>
        /// El aviso del pedido grabado: lee de la base de datos solo los eventos vivos que pueden casar con sus referencias. Nunca
        /// lanza ni bloquea el pedido (si algo falla, sin aviso y ELMAH).
        /// </summary>
        public static async Task<AvisoPedidoDTO> Calcular(NVEntities db, string empresa, IEnumerable<LineaPedidoVentaDTO> lineas, DateTime hoy)
        {
            try
            {
                List<string> productos = Productos(lineas);
                if (db == null || !productos.Any())
                {
                    return null;
                }
                string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                List<int> propios = productos.Select(p => ReglasProductoCurso.Interpretar(p)).Where(i => i.HasValue).Select(i => i.Value.EventoId).ToList();
                DateTime dia = hoy.Date;
                List<EventoConCurso> eventos = (await db.Eventos
                    .Where(e => e.Activo && e.Fecha >= dia && e.Empresa == empresaLimpia
                        && ((e.ProductoCurso != null && productos.Contains(e.ProductoCurso)) || propios.Contains(e.Id)))
                    .Select(e => new { e.Id, e.Titulo, e.Fecha, e.ProductoCurso })
                    .ToListAsync().ConfigureAwait(false))
                    .Select(e => new EventoConCurso { Id = e.Id, Titulo = e.Titulo, Fecha = e.Fecha, ProductoCurso = e.ProductoCurso })
                    .ToList();
                return Aviso(lineas, eventos);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Curso vendido a mano] No se ha podido comprobar si el pedido lleva el curso de un evento: {ex.Message}", ex));
                return null;
            }
        }
    }
}
