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

        /// <summary>Ya lo tiene la agencia: tramitado o más allá (entregado, incidentado…). Una etiqueta en curso todavía se puede parar.</summary>
        public static bool YaLoTieneLaAgencia(IEnumerable<EnvioDelPedido> envios, out int envio)
        {
            EnvioDelPedido tramitado = (envios ?? Enumerable.Empty<EnvioDelPedido>())
                .Where(e => e != null && e.Estado >= Constantes.Agencias.ESTADO_TRAMITADO && e.Estado != Constantes.Agencias.ESTADO_DEVUELTO)
                .OrderByDescending(e => e.Numero).FirstOrDefault();
            envio = tramitado?.Numero ?? 0;
            return tramitado != null;
        }

        public static ResultadoAplazamiento Evaluar(int pedido, IEnumerable<LineaAplazable> lineas, IEnumerable<EnvioDelPedido> envios, DateTime hoy)
        {
            List<LineaAplazable> cambian = (lineas ?? Enumerable.Empty<LineaAplazable>())
                .Where(l => l != null && l.Picking > 0 && l.FechaNueva.Date != l.FechaAnterior.Date).ToList();
            if (cambian.Count == 0)
            {
                return new ResultadoAplazamiento { Decision = DecisionAplazamiento.Nada };
            }
            if (YaLoTieneLaAgencia(envios, out int envio))
            {
                return new ResultadoAplazamiento { Decision = DecisionAplazamiento.Rechazar, EnvioEnAgencia = envio, Mensaje = MensajeRechazo(pedido, envio) };
            }
            var resultado = new ResultadoAplazamiento
            {
                Decision = DecisionAplazamiento.Aplazar,
                Pickings = cambian.Select(l => l.Picking).Distinct().OrderBy(p => p).ToList(),
                Etiquetas = (envios ?? Enumerable.Empty<EnvioDelPedido>())
                    .Where(e => e != null && e.Estado < Constantes.Agencias.ESTADO_TRAMITADO).Select(e => e.Numero).Distinct().OrderBy(n => n).ToList(),
                FechaNueva = cambian.Max(l => l.FechaNueva.Date)
            };
            resultado.Mensaje = AvisoUsuario(pedido, resultado, hoy);
            return resultado;
        }

        public static string MensajeRechazo(int pedido, int envio)
            => $"El pedido {pedido} ya lo tiene la agencia (envío {envio}): no llegamos a tiempo para retrasar la entrega. " +
               "La única opción es que el cliente lo rechace. Si quiere que logística intente hablar con la agencia, " +
               $"escriba a {Constantes.Correos.LOGISTICA} con el número del pedido.";

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
            return $"El pedido {pedido} ya tiene {lista} en el almacén. Se ha avisado a Almacén para que lo aparte y no salga hasta el {fecha}.{etiquetas}{futuro}";
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
                    $"No lo entreguen a la agencia: apártenlo hasta esa fecha.{etiquetas} Mientras tanto, el servidor no deja crear el envío ni el albarán.",
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
