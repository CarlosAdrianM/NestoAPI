using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857 (fase 3): una línea en el aire del pedido de compra al que se aplica la fecha de un correo.</summary>
    public class LineaCompraCorreo
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public short Estado { get; set; }
        public bool Enviado { get; set; }
        public int Cantidad { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public string Proveedor { get; set; }
    }

    /// <summary>NestoAPI#857 (fase 3): lo que se pide al repositorio para poner la fecha de un correo en un pedido, en una transacción.</summary>
    public class PeticionAplicacionFecha
    {
        public int CorreoId { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>El día de llegada (<see cref="ReglaFechaRecepcionPorCorreo"/>).</summary>
        public DateTime Fecha { get; set; }
        public bool Reasignar { get; set; }
        public bool Automatico { get; set; }
        /// <summary>Para el correo (AplicadoPor) y Modificaciones: el usuario o «(automático)».</summary>
        public string AplicadoPor { get; set; }
        /// <summary>Para LinPedidoCmp.Usuario (varchar(30)).</summary>
        public string UsuarioLineas { get; set; }
        public string Agencia { get; set; }
        public string Codigo { get; set; }
        /// <summary>Asignado a mano: si se eligió el pedido sugerido (null = no había sugerido, o automático).</summary>
        public bool? EligioSugerido { get; set; }
    }

    public enum EstadoAplicacionFecha
    {
        /// <summary>Puesta (con <see cref="ResultadoAplicacionFecha.LineasCambiadas"/> líneas cambiadas; 0 si ya tenían esa fecha).</summary>
        Aplicada,
        /// <summary>El correo ya estaba aplicado a ese mismo pedido: no se toca nada.</summary>
        YaAplicadoAlMismo,
        /// <summary>El correo ya estaba aplicado a OTRO pedido y no se ha pedido reasignar.</summary>
        AplicadoAOtro,
        /// <summary>El pedido no tiene ninguna línea en el aire (-1/1 con Enviado).</summary>
        NoEnElAire,
        NoExisteCorreo
    }

    public class ResultadoAplicacionFecha
    {
        public EstadoAplicacionFecha Estado { get; set; }
        /// <summary>Las líneas en el aire del pedido, como estaban ANTES.</summary>
        public List<LineaCompraCorreo> LineasAntes { get; set; } = new List<LineaCompraCorreo>();
        public int LineasCambiadas { get; set; }
        public string Proveedor { get; set; }
        /// <summary>A qué pedido estaba aplicado antes el correo (al reasignar o si ya lo estaba).</summary>
        public string EmpresaAnterior { get; set; }
        public int? PedidoAnterior { get; set; }
    }

    /// <summary>
    /// NestoAPI#857 (fase 3, decisión de Carlos 12/11/26): ¿a qué pedido se puede poner la fecha SIN preguntar? Solo si la asignación es
    /// inequívoca: el correo no es una salida nuestra y tiene EXACTAMENTE un pedido candidato (su número en el texto, o el proveedor
    /// reconocido tiene un único pedido en el aire). Con 0 o con varios, queda por asignar. PURA.
    /// </summary>
    public static class ReglaAsignacionInequivoca
    {
        public static CandidatoPedidoCompra Pedido(bool? esSalidaNuestra, IEnumerable<CandidatoPedidoCompra> candidatos)
        {
            if (esSalidaNuestra == true)
            {
                return null;
            }
            List<CandidatoPedidoCompra> distintos = (candidatos ?? Enumerable.Empty<CandidatoPedidoCompra>())
                .Where(c => c != null && c.Pedido > 0)
                .GroupBy(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido))
                .Select(g => g.First())
                .ToList();
            return distintos.Count == 1 ? distintos[0] : null;
        }

        public static CandidatoPedidoCompra Pedido(CorreoSeguimientoFila fila) =>
            fila == null ? null : Pedido(fila.EsSalidaNuestra, AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos));
    }

    /// <summary>
    /// NestoAPI#857 (fase 3): la fecha de recepción que se escribe es el DÍA DE LLEGADA, no el disponible (el cálculo de la fecha de
    /// entrega al cliente ya saca lo que llega el día F en el picking del laborable siguiente). = la entrega estimada que dice el correo;
    /// si no dice ninguna, el día de (recibido + las horas de la agencia: 24 paquetería, 48 grupaje; sin agencia conocida, 48). Nunca
    /// antes de hoy. PURA.
    /// </summary>
    public static class ReglaFechaRecepcionPorCorreo
    {
        public const int HORAS_SIN_AGENCIA = AgenciaEntradaPorPatrones.HORAS_GRUPAJE;
        /// <summary>El job solo escribe con correos de los últimos días (los viejos ya no dicen nada de la llegada).</summary>
        public const int DIAS_APLICACION_AUTOMATICA = 3;

        public static DateTime Calcular(DateTime? entregaEstimadaDia, DateTime recibido, int? horasHastaEntrega, DateTime hoy)
        {
            DateTime dia = entregaEstimadaDia?.Date ?? recibido.AddHours(horasHastaEntrega ?? HORAS_SIN_AGENCIA).Date;
            return dia < hoy.Date ? hoy.Date : dia;
        }

        /// <summary>Las horas de la agencia guardada en el correo (por su nombre); null si no es una conocida.</summary>
        public static int? HorasDe(string agencia) => AgenciasEntradaRegistradas.PorNombre(agencia)?.HorasHastaEntrega;

        public static DateTime Calcular(CorreoSeguimientoFila fila, DateTime hoy) =>
            Calcular(fila.EntregaEstimadaDia, fila.Recibido, HorasDe(fila.Agencia), hoy);

        /// <summary>
        /// La fecha de un ENVÍO (correos que comparten código, <see cref="MismoEnvio"/>): la entrega estimada más tardía de sus correos (pesimista,
        /// Carlos 12/11/26); si ninguno la dice, la regla con el correo más reciente. La misma para todos sus correos.
        /// </summary>
        public static DateTime Calcular(IEnumerable<CorreoSeguimientoFila> envio, DateTime hoy)
        {
            List<CorreoSeguimientoFila> correos = (envio ?? Enumerable.Empty<CorreoSeguimientoFila>()).Where(f => f != null).ToList();
            DateTime? entrega = correos.Where(f => f.EntregaEstimadaDia.HasValue).Select(f => (DateTime?)f.EntregaEstimadaDia.Value.Date).Max();
            CorreoSeguimientoFila ultimo = correos.OrderByDescending(f => f.Recibido).FirstOrDefault();
            if (ultimo == null)
            {
                return hoy.Date;
            }
            return Calcular(entrega, ultimo.Recibido, HorasDe(ultimo.Agencia), hoy);
        }

        /// <summary>¿Lo bastante reciente para que el job escriba (o avise) solo?</summary>
        public static bool Reciente(DateTime recibido, DateTime ahora) => recibido >= ahora.AddDays(-DIAS_APLICACION_AUTOMATICA);
    }

    /// <summary>NestoAPI#857 (fase 3): qué hacer con un correo según a qué pedido está ya aplicado. PURA.</summary>
    public static class ReglaAplicacionCorreo
    {
        public static EstadoAplicacionFecha? Bloqueo(string empresaAplicada, int? pedidoAplicado, string empresa, int pedido, bool reasignar)
        {
            if (!pedidoAplicado.HasValue)
            {
                return null;
            }
            if (AsignadorPedidosCompra.Clave(empresaAplicada, pedidoAplicado.Value) == AsignadorPedidosCompra.Clave(empresa, pedido))
            {
                return EstadoAplicacionFecha.YaAplicadoAlMismo;
            }
            return reasignar ? (EstadoAplicacionFecha?)null : EstadoAplicacionFecha.AplicadoAOtro;
        }

        /// <summary>Las líneas a las que hay que cambiar la fecha (las que no tienen ya ese día).</summary>
        public static List<LineaCompraCorreo> QueCambian(IEnumerable<LineaCompraCorreo> lineas, DateTime fecha) =>
            (lineas ?? Enumerable.Empty<LineaCompraCorreo>())
                .Where(l => l != null && (!l.FechaRecepcion.HasValue || l.FechaRecepcion.Value.Date != fecha.Date))
                .ToList();
    }

    /// <summary>NestoAPI#857 (fase 3): el rastro en Modificaciones (una fila por pedido y aplicación). PURA.</summary>
    public static class RastroAplicacionFecha
    {
        public const string TABLA = "CompraFechaPorCorreo";

        public static string Anterior(PeticionAplicacionFecha peticion, IEnumerable<LineaCompraCorreo> cambian) => JsonConvert.SerializeObject(new
        {
            peticion.Empresa,
            peticion.Pedido,
            Lineas = (cambian ?? Enumerable.Empty<LineaCompraCorreo>()).Select(l => new
            {
                l.NumeroOrden,
                l.Producto,
                FechaRecepcion = l.FechaRecepcion?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            })
        });

        public static string Nuevo(PeticionAplicacionFecha peticion, int lineasCambiadas, string empresaAnterior, int? pedidoAnterior) => JsonConvert.SerializeObject(new
        {
            peticion.Empresa,
            peticion.Pedido,
            FechaRecepcion = peticion.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LineasCambiadas = lineasCambiadas,
            peticion.CorreoId,
            peticion.Agencia,
            peticion.Codigo,
            peticion.Automatico,
            peticion.AplicadoPor,
            peticion.EligioSugerido,
            ReasignadoDe = pedidoAnterior.HasValue ? AsignadorPedidosCompra.Clave(empresaAnterior, pedidoAnterior.Value) : null
        });
    }

    /// <summary>NestoAPI#857 (fase 3): el texto de la campana a Compras de un correo que se queda por asignar. PURA.</summary>
    public static class AvisoCorreoPorAsignar
    {
        public static string Titulo(CorreoSeguimientoFila fila) =>
            "Envío de un proveedor sin pedido asignado" + (string.IsNullOrWhiteSpace(fila?.Agencia) ? string.Empty : $" ({fila.Agencia.Trim()})");

        public static List<string> Lineas(CorreoSeguimientoFila fila, int candidatos)
        {
            var lineas = new List<string>();
            if (fila == null)
            {
                return lineas;
            }
            string seguimiento = string.IsNullOrWhiteSpace(fila.Codigo) ? "sin código de seguimiento" : $"seguimiento {fila.Codigo.Trim()}";
            lineas.Add($"Correo del {fila.Recibido.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} de {fila.RemitenteDireccion}, {seguimiento}.");
            if (!string.IsNullOrWhiteSpace(fila.RemitentePaquete))
            {
                lineas.Add($"Lo envía: {fila.RemitentePaquete}.");
            }
            lineas.Add(candidatos == 0
                ? "No se ha reconocido a qué pedido de compra corresponde."
                : $"Puede ser de {candidatos} pedidos de compra distintos.");
            lineas.Add("Indique de qué pedido es (o descártelo) en Correos de seguimiento por asignar.");
            return lineas;
        }

        public static Dictionary<string, string> Datos(CorreoSeguimientoFila fila, string tipo) => new Dictionary<string, string>
        {
            ["tipo"] = tipo,
            ["correo"] = fila.Id.ToString(CultureInfo.InvariantCulture),
            ["agencia"] = fila.Agencia ?? string.Empty,
            ["codigo"] = fila.Codigo ?? string.Empty
        };
    }

    /// <summary>NestoAPI#857 (fase 3): ¿es un correo por asignar (para la lista de Compras)? PURA.</summary>
    public static class CorreosPorAsignar
    {
        public static bool Es(CorreoSeguimientoFila fila) =>
            fila != null && fila.EsSalidaNuestra != true && !fila.PedidoAplicado.HasValue && fila.Descartado != true;
    }
}
