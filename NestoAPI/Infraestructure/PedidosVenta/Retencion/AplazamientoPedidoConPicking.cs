using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>Una línea con picking a la que el cliente cambia la fecha de entrega.</summary>
    public class LineaAplazable
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public int Picking { get; set; }
        public DateTime FechaAnterior { get; set; }
        public DateTime FechaNueva { get; set; }
    }

    /// <summary>Un envío del pedido (EnviosAgencia): su número y su estado.</summary>
    public class EnvioDelPedido
    {
        public int Numero { get; set; }
        public short Estado { get; set; }
        /// <summary>NestoAPI#831: la del reparto propio (14) no es «la agencia ya lo tiene» mientras esté en reparto.</summary>
        public int Agencia { get; set; }

        /// <summary>
        /// NestoAPI#831: un envío del reparto propio que aún no se ha entregado (en curso, tramitado o incidentado): al aplazar el
        /// pedido no se rechaza ni hay que anular su etiqueta; se le mueve el día de reparto (FechaEntrega).
        /// </summary>
        public bool EsRepartoPropioPorEntregar => Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
            && (Estado == Constantes.Agencias.ESTADO_EN_CURSO || Estado == Constantes.Agencias.ESTADO_TRAMITADO || Estado == Constantes.Agencias.ESTADO_INCIDENTADO);

        /// <summary>
        /// Incidencia 1328: los pickings que lleva el envío (EnviosAgenciaPickings). Null = SIN dato (envíos de antes del cambio o
        /// creados por un camino que no lo sabe). <see cref="Agencias.ReglasPickingsEnvio.SIN_MERCANCIA"/> (0) = no lleva nada del almacén.
        /// </summary>
        public List<int> Pickings { get; set; }

        /// <summary>
        /// Incidencia 1328: terminado = entregado o cerrado a mano (5, <see cref="Agencias.CierreManualEnvio"/>: un incidentado que la
        /// agencia nunca cerró y se da por terminado). Lo que lleva ya no está en el almacén, así que no puede ser lo que sigue
        /// pendiente en él.
        /// </summary>
        public bool EstaTerminado => Estado == Constantes.Agencias.ESTADO_ENTREGADO || Estado == Constantes.Agencias.ESTADO_CERRADO_A_MANO;

        /// <summary>
        /// Incidencia 1328: ¿este envío lleva (o puede llevar) las líneas de esos pickings? Con dato, solo si lleva alguno. Sin dato,
        /// como antes (puede), salvo si ya está terminado. Sin saber los pickings de las líneas (null), con dato también «puede».
        /// </summary>
        public bool PuedeLlevar(ICollection<int> pickingsDeLasLineas)
        {
            if (Pickings == null)
            {
                return !EstaTerminado;
            }
            return pickingsDeLasLineas == null || Pickings.Any(pickingsDeLasLineas.Contains);
        }
    }

    public enum DecisionAplazamiento
    {
        /// <summary>Ninguna línea con picking cambia de fecha: como siempre.</summary>
        Nada,
        /// <summary>Se aplaza: fecha nueva, aviso al usuario y campana a Almacén; el pedido no sale hasta la fecha.</summary>
        Aplazar,
        /// <summary>La agencia ya lo tiene (envío tramitado): no se puede aplazar.</summary>
        Rechazar
    }

    public class ResultadoAplazamiento
    {
        public DecisionAplazamiento Decision { get; set; }
        public List<int> Pickings { get; set; } = new List<int>();
        /// <summary>Envíos con etiqueta hecha pero sin tramitar (pendientes o en curso): el almacén tiene que anularlos.</summary>
        public List<int> Etiquetas { get; set; } = new List<int>();
        /// <summary>
        /// NestoAPI#831: los envíos del reparto propio por entregar: no se anulan; su FechaEntrega (el día de reparto) pasa a
        /// <see cref="FechaNueva"/> (<see cref="AplazamientoPedidoConPicking.MoverFechaReparto"/>).
        /// </summary>
        public List<int> EnviosRepartoPropio { get; set; } = new List<int>();
        public int EnvioEnAgencia { get; set; }
        public DateTime FechaNueva { get; set; }
        /// <summary>Lo que ve el usuario: el aviso (Aplazar) o el motivo (Rechazar).</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#722 (Carlos, 28/10/26): un vendedor pide aplazar un pedido que ya tiene picking (correo a Alfredo: «el 928083,
    /// aplaza el envío al viernes»). Hasta hoy el PUT rechazaba cambiar la fecha de una línea con picking («quite el picking
    /// primero»). Ahora, como con Retener (#698): si la agencia aún no lo tiene, se acepta la fecha nueva, se avisa al usuario y
    /// al grupo Almacén, y el servidor no deja hacer el envío, la etiqueta ni el albarán hasta esa fecha (GuardiaPedidoRetenido);
    /// si ya está tramitado en la agencia, se rechaza diciendo que no llegamos a tiempo.
    /// </summary>
    public static class AplazamientoPedidoConPicking
    {
        public const string TIPO_AVISO = "AplazadoConPicking";
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>
        /// Ya lo tiene la agencia: tramitado o más allá (entregado, incidentado…). Una etiqueta en curso todavía se puede parar. Sin
        /// saber qué pickings cambian: los envíos con dato cuentan (prudente) y los sin dato, como en <see cref="EnvioDelPedido.PuedeLlevar"/>.
        /// </summary>
        public static bool YaLoTieneLaAgencia(IEnumerable<EnvioDelPedido> envios, out int envio)
            => YaLoTieneLaAgencia(envios, null, out envio);

        /// <summary>
        /// Incidencia 1328, LA REGLA: la agencia «ya tiene» unas líneas si hay un envío del pedido tramitado o más (no devuelto, ni del
        /// reparto propio por entregar: NestoAPI#831) que las PUEDE llevar: con dato, que lleve alguno de sus pickings; sin dato,
        /// cualquiera menos el ya entregado o cerrado a mano (lo terminado no puede contener lo que sigue en el almacén).
        /// </summary>
        /// <param name="pickingsDeLasLineas">Los pickings de las líneas que cambian; null = no se saben.</param>
        public static bool YaLoTieneLaAgencia(IEnumerable<EnvioDelPedido> envios, IEnumerable<int> pickingsDeLasLineas, out int envio)
        {
            HashSet<int> pickings = pickingsDeLasLineas == null ? null : new HashSet<int>(pickingsDeLasLineas.Where(p => p > 0));
            EnvioDelPedido tramitado = (envios ?? Enumerable.Empty<EnvioDelPedido>())
                .Where(e => e != null && e.Estado >= Constantes.Agencias.ESTADO_TRAMITADO && e.Estado != Constantes.Agencias.ESTADO_DEVUELTO
                    && !e.EsRepartoPropioPorEntregar && e.PuedeLlevar(pickings))
                .OrderByDescending(e => e.Numero).FirstOrDefault();
            envio = tramitado?.Numero ?? 0;
            return tramitado != null;
        }

        /// <summary>
        /// NestoAPI#831 + incidencia 1328: los envíos del reparto propio por entregar a los que se mueve el día de reparto al aplazar:
        /// los que pueden llevar esas líneas (con dato, solo si llevan alguno de sus pickings; sin dato, todos, como antes).
        /// </summary>
        public static List<int> EnviosRepartoPropioAfectados(IEnumerable<EnvioDelPedido> envios, IEnumerable<int> pickingsDeLasLineas)
        {
            HashSet<int> pickings = pickingsDeLasLineas == null ? null : new HashSet<int>(pickingsDeLasLineas.Where(p => p > 0));
            return (envios ?? Enumerable.Empty<EnvioDelPedido>())
                .Where(e => e != null && e.EsRepartoPropioPorEntregar && e.PuedeLlevar(pickings))
                .Select(e => e.Numero).Distinct().OrderBy(n => n).ToList();
        }

        public static ResultadoAplazamiento Evaluar(int pedido, IEnumerable<LineaAplazable> lineas, IEnumerable<EnvioDelPedido> envios, DateTime hoy)
        {
            List<LineaAplazable> cambian = (lineas ?? Enumerable.Empty<LineaAplazable>())
                .Where(l => l != null && l.Picking > 0 && l.FechaNueva.Date != l.FechaAnterior.Date).ToList();
            if (cambian.Count == 0)
            {
                return new ResultadoAplazamiento { Decision = DecisionAplazamiento.Nada };
            }
            List<int> pickings = cambian.Select(l => l.Picking).Distinct().OrderBy(p => p).ToList();
            if (YaLoTieneLaAgencia(envios, pickings, out int envio))
            {
                return new ResultadoAplazamiento { Decision = DecisionAplazamiento.Rechazar, EnvioEnAgencia = envio, Mensaje = MensajeRechazo(pedido, envio) };
            }
            var conjunto = new HashSet<int>(pickings);
            var resultado = new ResultadoAplazamiento
            {
                Decision = DecisionAplazamiento.Aplazar,
                Pickings = pickings,
                // Incidencia 1328: solo las etiquetas que pueden llevar estas líneas (la de otro picking no hay que anularla)
                Etiquetas = (envios ?? Enumerable.Empty<EnvioDelPedido>())
                    .Where(e => e != null && e.Estado < Constantes.Agencias.ESTADO_TRAMITADO && !e.EsRepartoPropioPorEntregar && e.PuedeLlevar(conjunto))
                    .Select(e => e.Numero).Distinct().OrderBy(n => n).ToList(),
                // NestoAPI#831: los del reparto propio se quedan, con el día de reparto nuevo
                EnviosRepartoPropio = EnviosRepartoPropioAfectados(envios, pickings),
                FechaNueva = cambian.Max(l => l.FechaNueva.Date)
            };
            resultado.Mensaje = AvisoUsuario(pedido, resultado, hoy);
            return resultado;
        }

        /// <summary>
        /// NestoAPI#831: a los envíos del reparto propio por entregar del pedido aplazado se les pone el día de reparto nuevo
        /// (FechaEntrega = la fecha nueva, nunca antes de hoy). Los demás no se tocan. Devuelve los que han cambiado.
        /// </summary>
        public static List<int> MoverFechaReparto(IEnumerable<EnviosAgencia> envios, DateTime fechaNueva, DateTime hoy)
        {
            DateTime dia = fechaNueva.Date < hoy.Date ? hoy.Date : fechaNueva.Date;
            var movidos = new List<int>();
            foreach (EnviosAgencia envio in envios ?? Enumerable.Empty<EnviosAgencia>())
            {
                if (envio == null || !new EnvioDelPedido { Numero = envio.Numero, Estado = envio.Estado, Agencia = envio.Agencia }.EsRepartoPropioPorEntregar)
                {
                    continue;
                }
                if (envio.FechaEntrega?.Date != dia)
                {
                    envio.FechaEntrega = dia;
                    envio.FechaModificacion = DateTime.Now;
                    movidos.Add(envio.Numero);
                }
            }
            return movidos;
        }

        /// <summary>NestoAPI#722 (06/11/26): el texto de la issue; el correo a logística lo manda el botón de Nesto (AvisarLogisticaAplazamiento).</summary>
        public static string MensajeRechazo(int pedido, int envio) => AplazamientoEnvioEnAgencia.MensajeYaEnAgencia(pedido, envio);

        public static string AvisoUsuario(int pedido, ResultadoAplazamiento resultado, DateTime hoy)
        {
            string lista = ListaPickings(resultado.Pickings);
            string fecha = resultado.FechaNueva.ToString("dd/MM/yyyy", castellano);
            string etiquetas = resultado.Etiquetas.Count == 0 ? string.Empty
                : resultado.Etiquetas.Count == 1 ? $" Tiene hecha la etiqueta del envío {resultado.Etiquetas[0]}: el almacén tiene que anularla."
                : $" Tiene hechas las etiquetas de los envíos {string.Join(", ", resultado.Etiquetas)}: el almacén tiene que anularlas.";
            string futuro = resultado.FechaNueva.Date > hoy.Date
                ? $" El servidor no dejará hacer el envío ni el albarán hasta el {fecha}."
                : string.Empty;
            return $"El pedido {pedido} ya tiene {lista} en el almacén. Se ha avisado a Almacén para que lo aparte y no salga hasta el {fecha}.{etiquetas}" +
                $"{TextoRepartoPropio(resultado, fecha)}{futuro}";
        }

        /// <summary>NestoAPI#831: « La etiqueta del reparto propio (envío N) sigue valiendo: pasa al reparto del dd/MM/yyyy.»</summary>
        private static string TextoRepartoPropio(ResultadoAplazamiento resultado, string fecha)
        {
            List<int> envios = resultado.EnviosRepartoPropio ?? new List<int>();
            if (envios.Count == 0)
            {
                return string.Empty;
            }
            string cuales = envios.Count == 1 ? $"envío {envios[0]}" : $"envíos {string.Join(", ", envios)}";
            return $" La etiqueta del reparto propio ({cuales}) sigue valiendo: pasa al reparto del {fecha} y no se carga antes.";
        }

        /// <summary>Campana de Nesto y buzón de Ariadna al grupo Almacén.</summary>
        public static NotificacionPushDTO Notificacion(int pedido, string cliente, string nombreCliente, string usuario, ResultadoAplazamiento resultado)
        {
            string lista = ListaPickings(resultado.Pickings);
            string fecha = resultado.FechaNueva.ToString("dd/MM/yyyy", castellano);
            string quien = TextosRetencion.NombreUsuario(usuario);
            string textoCliente = string.IsNullOrWhiteSpace(cliente) ? string.Empty
                : string.IsNullOrWhiteSpace(nombreCliente) ? $" del cliente {cliente.Trim()}" : $" del cliente {cliente.Trim()} ({nombreCliente.Trim()})";
            string etiquetas = resultado.Etiquetas.Count == 0 ? string.Empty
                : $" Anulen {(resultado.Etiquetas.Count == 1 ? "la etiqueta del envío " : "las etiquetas de los envíos ")}{string.Join(", ", resultado.Etiquetas)}.";
            return new NotificacionPushDTO
            {
                Titulo = $"NO ENTREGAR HASTA EL {fecha}: pedido {pedido} aplazado con {lista}",
                Cuerpo = $"{quien} ha aplazado al {fecha} el pedido {pedido}{textoCliente}, que ya tiene {lista} en marcha. " +
                    $"No lo entreguen a la agencia: apártenlo hasta esa fecha.{etiquetas}{TextoRepartoPropio(resultado, fecha)} " +
                    "Mientras tanto, el servidor no deja crear el envío ni el albarán.",
                Tipo = TextosRetencion.TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TextosRetencion.TIPO_NOTIFICACION,
                    ["accion"] = "aplazado-con-picking",
                    ["pedido"] = pedido.ToString(CultureInfo.InvariantCulture),
                    ["fecha"] = resultado.FechaNueva.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }
            };
        }

        private static string ListaPickings(List<int> pickings)
        {
            List<int> lista = (pickings ?? new List<int>()).Where(p => p > 0).Distinct().OrderBy(p => p).ToList();
            if (lista.Count == 0)
            {
                return "un picking";
            }
            return lista.Count == 1 ? $"el picking {lista[0]}" : $"los pickings {string.Join(", ", lista)}";
        }
    }
}
