using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
        /// <summary>
        /// NestoAPI#857f: corregir la fecha de un correo YA aplicado a este mismo pedido (la agencia ha dicho otra cosa: su correo o su
        /// consulta). Sin esto, un correo aplicado al mismo pedido no toca nada.
        /// </summary>
        public bool Corregir { get; set; }
        /// <summary>NestoAPI#857f: de dónde sale la fecha (<see cref="ReglaFechaRecepcionPorCorreo.Origen"/>), para el rastro.</summary>
        public string OrigenFecha { get; set; }
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
    /// <summary>
    /// NestoAPI#857g: el día que llega lo que una agencia tiene «sin noticias» (recibido + su plazo). Por defecto en horas naturales
    /// (recibido + 24/48 h); si la agencia lo pide (<see cref="IAgenciaEntrada.PlazoEnLaborables"/>: DB Schenker, 72 h = 3 laborables), en días
    /// LABORABLES de Algete: sin sábados, domingos ni <paramref name="esFestivo"/> (sin él, solo fines de semana). PURA.
    /// </summary>
    public static class PlazoAgenciaEntrada
    {
        private const int TOPE_DIAS = 400;

        public static DateTime DiaDeLlegada(DateTime recibido, int horas, bool enLaborables, Func<DateTime, bool> esFestivo = null)
        {
            if (!enLaborables)
            {
                return recibido.AddHours(horas).Date;
            }
            int dias = Math.Max(1, (int)Math.Ceiling(horas / 24.0));
            DateTime dia = recibido.Date;
            for (int i = 0; i < TOPE_DIAS && dias > 0; i++)
            {
                dia = dia.AddDays(1);
                if (dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && (esFestivo == null || !esFestivo(dia)))
                {
                    dias--;
                }
            }
            return dia;
        }

        /// <summary>Por el nombre de la agencia guardado en el correo; sin agencia conocida, <see cref="ReglaFechaRecepcionPorCorreo.HORAS_SIN_AGENCIA"/> naturales.</summary>
        public static DateTime DiaDeLlegada(DateTime recibido, string agencia, Func<DateTime, bool> esFestivo = null)
        {
            IAgenciaEntrada conocida = AgenciasEntradaRegistradas.PorNombre(agencia);
            return DiaDeLlegada(recibido, conocida?.HorasHastaEntrega ?? ReglaFechaRecepcionPorCorreo.HORAS_SIN_AGENCIA, conocida?.PlazoEnLaborables == true, esFestivo);
        }
    }

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

        /// <param name="esFestivo">NestoAPI#857g: festivos de Algete para las agencias con plazo en laborables (sin él, solo fines de semana).</param>
        public static DateTime Calcular(CorreoSeguimientoFila fila, DateTime hoy, Func<DateTime, bool> esFestivo = null)
        {
            DateTime dia = fila.EntregaEstimadaDia?.Date ?? PlazoAgenciaEntrada.DiaDeLlegada(fila.Recibido, fila.Agencia, esFestivo);
            return dia < hoy.Date ? hoy.Date : dia;
        }

        /// <summary>
        /// La fecha de un ENVÍO (correos que comparten código, <see cref="MismoEnvio"/>). Carlos 14/11/26: por cada seguimiento (bulto),
        /// manda su ÚLTIMO correo con fecha (una reprogramación corrige a la anterior; uno sin fecha no la borra) y, si alguno dice que
        /// se ha entregado, el día de la entrega (<see cref="EntregaConfirmadaCorreo"/>); entre bultos, el que llegue más tarde (el pedido
        /// no está hasta que llega el último). Un correo sin código es su propio bulto. Si ningún bulto tiene fecha, la regla con el
        /// correo más reciente. La misma para todos sus correos. Caso real 13/11/26 (Weelko): con la más tardía de todos los correos
        /// salía el 16/11 de UPS aunque luego lo reprogramó al 13/11 y entregó ese día.
        /// </summary>
        public static DateTime Calcular(IEnumerable<CorreoSeguimientoFila> envio, DateTime hoy, Func<DateTime, bool> esFestivo = null)
        {
            DateTime? dia = DiaDeLlegada(envio, esFestivo);
            return !dia.HasValue || dia.Value < hoy.Date ? hoy.Date : dia.Value;
        }

        /// <summary>
        /// NestoAPI#865: el día de llegada que dicen los correos del envío SIN el suelo de hoy (puede ser pasado: la fecha vencida
        /// recalculada lo trata entonces como «sin noticias»). Null sin correos. Las mismas reglas que <see cref="Calcular(IEnumerable{CorreoSeguimientoFila}, DateTime)"/>.
        /// </summary>
        public static DateTime? DiaDeLlegada(IEnumerable<CorreoSeguimientoFila> envio, Func<DateTime, bool> esFestivo = null)
        {
            List<CorreoSeguimientoFila> correos = (envio ?? Enumerable.Empty<CorreoSeguimientoFila>()).Where(f => f != null).ToList();
            CorreoSeguimientoFila ultimo = correos.OrderByDescending(f => f.Recibido).ThenByDescending(f => f.Id).FirstOrDefault();
            if (ultimo == null)
            {
                return null;
            }
            return LlegadaDelEnvio(correos) ?? PlazoAgenciaEntrada.DiaDeLlegada(ultimo.Recibido, ultimo.Agencia, esFestivo);
        }

        public const string ORIGEN_ENTREGADO = "Entregado";
        public const string ORIGEN_HORAS = "Horas";

        /// <summary>
        /// NestoAPI#857f: de dónde sale la fecha del envío, para el rastro: «Entregado» (algún bulto entregado manda), «Agencia»/«Consulta» (lo
        /// dice la agencia), «Proveedor» (su aviso), o «Horas» (recibido + 24/48 h). Las mismas reglas que <see cref="DiaDeLlegada"/>.
        /// </summary>
        public static string Origen(IEnumerable<CorreoSeguimientoFila> envio)
        {
            List<CorreoSeguimientoFila> correos = (envio ?? Enumerable.Empty<CorreoSeguimientoFila>()).Where(f => f != null).ToList();
            List<(DateTime Dia, string Origen)> bultos = Bultos(correos).Select(LlegadaYOrigenDelBulto).Where(b => b.HasValue).Select(b => b.Value).ToList();
            return bultos.Any() ? bultos.OrderByDescending(b => b.Dia).First().Origen : ORIGEN_HORAS;
        }

        /// <summary>El día que llega el último bulto con fecha, o null si ningún bulto la tiene. PURA.</summary>
        internal static DateTime? LlegadaDelEnvio(IEnumerable<CorreoSeguimientoFila> correos) =>
            Bultos(correos).Select(LlegadaYOrigenDelBulto).Where(d => d.HasValue).Select(d => (DateTime?)d.Value.Dia).Max();

        /// <summary>Los correos por bulto (código); un correo sin código es su propio bulto.</summary>
        private static IEnumerable<List<CorreoSeguimientoFila>> Bultos(IEnumerable<CorreoSeguimientoFila> correos)
        {
            var porBulto = new Dictionary<string, List<CorreoSeguimientoFila>>(StringComparer.OrdinalIgnoreCase);
            foreach (CorreoSeguimientoFila correo in correos.Where(f => f != null))
            {
                HashSet<string> codigos = MismoEnvio.Codigos(correo);
                if (!codigos.Any())
                {
                    _ = codigos.Add("#correo-" + correo.Id + "-" + correo.Recibido.Ticks);
                }
                foreach (string codigo in codigos)
                {
                    if (!porBulto.TryGetValue(codigo, out List<CorreoSeguimientoFila> lista))
                    {
                        porBulto[codigo] = lista = new List<CorreoSeguimientoFila>();
                    }
                    lista.Add(correo);
                }
            }
            return porBulto.Values;
        }

        /// <summary>
        /// Un bulto: entregado → el día de la entrega; si no, NestoAPI#857f, la entrega estimada del último correo DE LA AGENCIA que la dé
        /// (su correo o su consulta: <see cref="OrigenEntregaCorreo.Prioridad"/>) y, si la agencia no ha dicho ninguna, la del último que la dé
        /// (el aviso del proveedor, o correos de antes de #857f sin origen); si ninguno, null.
        /// </summary>
        private static (DateTime Dia, string Origen)? LlegadaYOrigenDelBulto(List<CorreoSeguimientoFila> correos)
        {
            CorreoSeguimientoFila entregado = correos.Where(EntregaConfirmadaCorreo.Es).OrderBy(f => f.Recibido).FirstOrDefault();
            if (entregado != null)
            {
                return (entregado.Recibido.Date, ORIGEN_ENTREGADO);
            }
            CorreoSeguimientoFila mejor = correos.Where(f => f.EntregaEstimadaDia.HasValue)
                .OrderByDescending(f => OrigenEntregaCorreo.Prioridad(f.OrigenEntrega))
                .ThenByDescending(f => f.EntregaConsultadaEl.HasValue && f.EntregaConsultadaEl > f.Recibido ? f.EntregaConsultadaEl.Value : f.Recibido)
                .ThenByDescending(f => f.Id)
                .FirstOrDefault();
            if (mejor == null)
            {
                return null;
            }
            return (mejor.EntregaEstimadaDia.Value.Date, string.IsNullOrWhiteSpace(mejor.OrigenEntrega) ? OrigenEntregaCorreo.PROVEEDOR : mejor.OrigenEntrega);
        }

        /// <summary>¿Lo bastante reciente para que el job escriba (o avise) solo?</summary>
        public static bool Reciente(DateTime recibido, DateTime ahora) => recibido >= ahora.AddDays(-DIAS_APLICACION_AUTOMATICA);
    }

    /// <summary>
    /// NestoAPI#857 (Carlos 14/11/26): ¿dice el correo que el paquete YA se ha entregado? Por el asunto o el extracto («Paquete
    /// entregado», «Tu paquete se ha entregado», «has been delivered»…). Una entrega futura («será entregado», «Entrega estimada») no
    /// cuenta. PURA.
    /// </summary>
    public static class EntregaConfirmadaCorreo
    {
        private static readonly Regex PATRON = new Regex(
            @"\b(se\s+ha\s+entregado|ha\s+sido\s+entregad[oa]|(paquete|env[ií]o|pedido|bulto)\s+entregado|has\s+been\s+delivered|was\s+delivered)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <remarks>NestoAPI#857f: también si la agencia lo ha dicho como estado (EstadoEnvio «Entregado»: su correo o su consulta).</remarks>
        public static bool Es(CorreoSeguimientoFila fila) =>
            fila != null && (string.Equals(fila.EstadoEnvio, nameof(EstadoEnvioAgencia.Entregado), StringComparison.OrdinalIgnoreCase)
                || PATRON.IsMatch(fila.Asunto ?? string.Empty) || PATRON.IsMatch(fila.Extracto ?? string.Empty));
    }

    /// <summary>NestoAPI#857 (fase 3): qué hacer con un correo según a qué pedido está ya aplicado. PURA.</summary>
    public static class ReglaAplicacionCorreo
    {
        /// <param name="corregir">NestoAPI#857f: aplicado al MISMO pedido no bloquea (se corrige su fecha).</param>
        public static EstadoAplicacionFecha? Bloqueo(string empresaAplicada, int? pedidoAplicado, string empresa, int pedido, bool reasignar, bool corregir = false)
        {
            if (!pedidoAplicado.HasValue)
            {
                return null;
            }
            if (AsignadorPedidosCompra.Clave(empresaAplicada, pedidoAplicado.Value) == AsignadorPedidosCompra.Clave(empresa, pedido))
            {
                return corregir ? (EstadoAplicacionFecha?)null : EstadoAplicacionFecha.YaAplicadoAlMismo;
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
            peticion.OrigenFecha,
            Corregida = peticion.Corregir ? true : (bool?)null,
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
