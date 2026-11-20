using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857g: una fila de dbo.ComprasConfirmacionesProveedor (Scripts/Issue857g_ComprasConfirmacionesProveedor.sql, fuera del EDMX): la
    /// confirmación de un pedido que mandó el proveedor al recibirlo, con nuestros pedidos candidatos (JSON como en los correos).
    /// </summary>
    public class ConfirmacionPedidoProveedorFila
    {
        public int Id { get; set; }
        public string MensajeId { get; set; }
        public DateTime Recibido { get; set; }
        public string RemitenteDireccion { get; set; }
        public string Formato { get; set; }
        public string PedidoProveedor { get; set; }
        public string ReferenciaCliente { get; set; }
        public string Agencia { get; set; }
        public string ProveedorPropuesto { get; set; }
        public string PedidosCandidatos { get; set; }
    }

    /// <summary>
    /// NestoAPI#857g: de qué pedido nuestro es una CONFIRMACIÓN del proveedor. Llega al poco de mandarle el pedido, así que, de los candidatos
    /// del asignador (sus pedidos en el aire), por este orden: (1) el que tiene el número de la referencia que le dimos; (2) si solo hay uno,
    /// ese; (3) si solo uno se le mandó en las <see cref="HORAS_ENVIO"/> horas antes de la confirmación, ese; (4) si no, todos (no se elige a
    /// ciegas). PURA.
    /// </summary>
    public static class ReglaPedidoConfirmado
    {
        public const int HORAS_ENVIO = 48;

        public static List<CandidatoPedidoCompra> Elegir(IEnumerable<CandidatoPedidoCompra> candidatos, IEnumerable<PedidoCompraEnAire> enAire,
            string referenciaCliente, DateTime recibido)
        {
            List<CandidatoPedidoCompra> lista = (candidatos ?? Enumerable.Empty<CandidatoPedidoCompra>()).Where(c => c != null && c.Pedido > 0)
                .GroupBy(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)).Select(g => g.First()).ToList();
            if (int.TryParse(referenciaCliente?.Trim(), out int numero))
            {
                List<CandidatoPedidoCompra> conReferencia = lista.Where(c => c.Pedido == numero).ToList();
                if (conReferencia.Count == 1)
                {
                    return conReferencia;
                }
            }
            if (lista.Count <= 1)
            {
                return lista;
            }
            var pedidos = (enAire ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null)
                .GroupBy(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)).ToDictionary(g => g.Key, g => g.First());
            List<CandidatoPedidoCompra> recientes = lista.Where(c => pedidos.TryGetValue(AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido), out PedidoCompraEnAire p)
                && p.FechaEnvio.HasValue && p.FechaEnvio.Value <= recibido && p.FechaEnvio.Value >= recibido.AddHours(-HORAS_ENVIO)).ToList();
            return recientes.Count == 1 ? recientes : lista;
        }
    }

    /// <summary>
    /// NestoAPI#857g: el enlace pedido del proveedor ↔ nuestro pedido. Un correo de un envío que trae la referencia del remitente («Ref.:
    /// SO252231WKOUT77626», MBE) se asigna a los candidatos de la confirmación de ese pedido del proveedor («SO252231») que sigan en el aire
    /// (motivo <see cref="AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR"/>). Manda sobre todo menos sobre nuestro número en el texto y el PDF. PURA.
    /// </summary>
    public static class ReglaPedidoProveedor
    {
        public const int LONGITUD_MINIMA = 4;
        /// <summary>Hasta cuánto atrás se mira una confirmación (un pedido puede tardar en salir).</summary>
        public const int DIAS_CONFIRMACIONES = 120;

        /// <summary>
        /// ¿Es la referencia la de ese pedido? Igual, o empieza por él y lo que sigue no es una cifra («SO252231WKOUT77626» es del SO252231;
        /// «SO2522310» no). Sin mayúsculas ni espacios.
        /// </summary>
        public static bool Casa(string referencia, string pedidoProveedor)
        {
            string r = Limpio(referencia);
            string p = Limpio(pedidoProveedor);
            if (r.Length < LONGITUD_MINIMA || p.Length < LONGITUD_MINIMA || !r.StartsWith(p, StringComparison.Ordinal))
            {
                return false;
            }
            return r.Length == p.Length || !char.IsDigit(r[p.Length]);
        }

        public static AsignacionPedidoCompra Completar(AsignacionPedidoCompra asignacion, IEnumerable<string> referencias,
            IEnumerable<ConfirmacionPedidoProveedorFila> confirmaciones, IEnumerable<PedidoCompraEnAire> enAire)
        {
            List<string> refs = (referencias ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (!refs.Any() || confirmaciones == null)
            {
                return asignacion;
            }
            if ((asignacion?.Candidatos ?? new List<CandidatoPedidoCompra>()).Any(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_NUMERO
                || c.Motivo == AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO))
            {
                return asignacion;
            }
            var enElAire = (enAire ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null)
                .GroupBy(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)).ToDictionary(g => g.Key, g => g.First());
            List<CandidatoPedidoCompra> candidatos = confirmaciones
                .Where(c => c != null && refs.Any(r => Casa(r, c.PedidoProveedor)))
                .SelectMany(c => AsignadorPedidosCompra.Deserializar(c.PedidosCandidatos))
                .Where(c => enElAire.ContainsKey(AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)))
                .GroupBy(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)).Select(g => g.First())
                .Select(c => new CandidatoPedidoCompra
                {
                    Empresa = c.Empresa?.Trim(), Pedido = c.Pedido, Proveedor = c.Proveedor?.Trim(), Motivo = AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR
                })
                .ToList();
            if (!candidatos.Any())
            {
                return asignacion;
            }
            List<string> proveedores = candidatos.Select(c => c.Proveedor).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new AsignacionPedidoCompra { ProveedorPropuesto = proveedores.Count == 1 ? proveedores[0] : null, Candidatos = candidatos };
        }

        /// <summary>La confirmación llega al poco de hacerse la compra: hasta estos minutos después (y 5 antes, por los relojes).</summary>
        public const int MINUTOS_TRAS_LA_COMPRA = 60;

        /// <summary>
        /// 20/11/26 (Weelko, «Albarán WK/OUT/78316»): el aviso de salida no trae el pedido del proveedor (SO…) ni el nuestro, pero sí cuándo se
        /// hizo la compra. Las confirmaciones del MISMO remitente recibidas justo después de ese momento son las de ese pedido: sus candidatos
        /// que sigan en el aire (motivo <see cref="AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR"/>). No pisa nuestro número ni el PDF. PURA.
        /// </summary>
        public static AsignacionPedidoCompra CompletarPorFechaCompra(AsignacionPedidoCompra asignacion, DateTime? fechaCompra, string remitente,
            IEnumerable<ConfirmacionPedidoProveedorFila> confirmaciones, IEnumerable<PedidoCompraEnAire> enAire)
        {
            if (!fechaCompra.HasValue || string.IsNullOrWhiteSpace(remitente) || confirmaciones == null)
            {
                return asignacion;
            }
            if ((asignacion?.Candidatos ?? new List<CandidatoPedidoCompra>()).Any(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_NUMERO
                || c.Motivo == AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO || c.Motivo == AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR))
            {
                return asignacion;
            }
            DateTime desde = fechaCompra.Value.AddMinutes(-5);
            DateTime hasta = fechaCompra.Value.AddMinutes(MINUTOS_TRAS_LA_COMPRA);
            var enElAire = (enAire ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null)
                .GroupBy(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)).ToDictionary(g => g.Key, g => g.First());
            List<CandidatoPedidoCompra> candidatos = confirmaciones
                .Where(c => c != null && string.Equals(c.RemitenteDireccion?.Trim(), remitente.Trim(), StringComparison.OrdinalIgnoreCase)
                    && c.Recibido >= desde && c.Recibido <= hasta)
                .SelectMany(c => AsignadorPedidosCompra.Deserializar(c.PedidosCandidatos))
                .Where(c => enElAire.ContainsKey(AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)))
                .GroupBy(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)).Select(g => g.First())
                .Select(c => new CandidatoPedidoCompra
                {
                    Empresa = c.Empresa?.Trim(), Pedido = c.Pedido, Proveedor = c.Proveedor?.Trim(), Motivo = AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR
                })
                .ToList();
            if (!candidatos.Any())
            {
                return asignacion;
            }
            List<string> proveedores = candidatos.Select(c => c.Proveedor).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new AsignacionPedidoCompra { ProveedorPropuesto = proveedores.Count == 1 ? proveedores[0] : null, Candidatos = candidatos };
        }

        private static string Limpio(string texto) => (texto ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }
}
