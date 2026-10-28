using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>
    /// NestoAPI#698: una fila de PedidosRetenidos. Viva mientras <see cref="Liberado"/> es null. Contrato con Nesto
    /// (DetallePedidoViewModel) y NestoApp: los nombres se serializan tal cual (sin camelCase).
    /// </summary>
    public class RetencionPedidoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Texto libre: «El cliente lo quiere para noviembre», «Pendiente de pago»…</summary>
        public string Motivo { get; set; }
        /// <summary>Null = hasta nuevo aviso. Con fecha, la pasada nocturna lo libera sola ese día.</summary>
        public DateTime? HastaFecha { get; set; }
        /// <summary>CabPedidoVta.ModoServicio al retener (null si no estaba informado): «Liberar» lo devuelve tal cual.</summary>
        public byte? ModoServicioAnterior { get; set; }
        /// <summary>Quien lo retuvo (con dominio, como lo resuelve el servidor).</summary>
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? Liberado { get; set; }
        public string UsuarioLiberacion { get; set; }
        /// <summary>«Liberado a mano», «Llegó la fecha»…</summary>
        public string MotivoLiberacion { get; set; }
        /// <summary>Último recordatorio por la campana de un retenido sin fecha (la pasada nocturna).</summary>
        public DateTime? UltimoRecordatorio { get; set; }
        /// <summary>Solo en la respuesta de Retener: los pickings que ya tenía el pedido en el almacén al retenerlo (vacía si ninguno).</summary>
        public List<int> PickingsEnCurso { get; set; } = new List<int>();
        /// <summary>Solo en la respuesta de Retener: lo que hay que decirle al usuario (el pedido ya tenía picking: se ha avisado al almacén). Null si nada.</summary>
        public string Aviso { get; set; }
    }

    /// <summary>NestoAPI#698: una retención viva con el cliente y el vendedor del pedido (GET api/PedidosVenta/Retenidos).</summary>
    public class PedidoRetenidoListadoDTO : RetencionPedidoDTO
    {
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
        public string Vendedor { get; set; }
    }

    /// <summary>NestoAPI#698: cuerpo de POST api/PedidosVenta/{empresa}/{numero}/Retener.</summary>
    public class RetenerPedidoRequest
    {
        public string Motivo { get; set; }
        /// <summary>Null = hasta nuevo aviso.</summary>
        public DateTime? HastaFecha { get; set; }
        /// <summary>Respaldo cuando no hay usuario autenticado (el servidor prefiere el del token).</summary>
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#698: cuerpo (opcional) de POST api/PedidosVenta/{empresa}/{numero}/Liberar.</summary>
    public class LiberarPedidoRequest
    {
        public string Usuario { get; set; }
    }

    /// <summary>
    /// NestoAPI#698: lo que necesita <see cref="ServicioRetencionPedidos"/> de la cabecera del pedido para decidir.
    /// </summary>
    public class CabeceraPedidoRetencion
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public byte? ModoServicio { get; set; }
        public bool ServirJunto { get; set; }
        public bool NotaEntrega { get; set; }
        /// <summary>Alguna línea pendiente o en curso.</summary>
        public bool QuedaPendiente { get; set; }
        /// <summary>Líneas en curso (estado 1) que ya tienen picking: están en el almacén y no las para el modo 5.</summary>
        public int LineasEnPicking { get; set; }
        /// <summary>Los números de picking de esas líneas, separados por coma (lo da la consulta).</summary>
        public string PickingsEnCurso { get; set; }
        public List<int> Pickings()
        {
            return (PickingsEnCurso ?? string.Empty).Split(',').Select(t => int.TryParse(t.Trim(), out int n) ? n : 0).Where(n => n > 0).Distinct().OrderBy(n => n).ToList();
        }
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
        public string Vendedor { get; set; }
    }

    /// <summary>
    /// NestoAPI#698: ajustes de Web.config. Pedidos:Retenidos:DiasRecordatorio = cada cuántos días se recuerda por la campana
    /// un retenido SIN fecha (Carlos, 23/10/26: como mucho una vez a la semana → 7 por defecto). 0 o menos = sin recordatorios.
    /// </summary>
    public static class AjustesRetencion
    {
        public const string CLAVE_DIAS_RECORDATORIO = "Pedidos:Retenidos:DiasRecordatorio";
        public const int DIAS_RECORDATORIO_POR_DEFECTO = 7;

        /// <summary>Para los tests (null = el Web.config).</summary>
        internal static Func<string, string> LeerAjuste { get; set; }

        public static int DiasRecordatorio => Interpretar(Leer(CLAVE_DIAS_RECORDATORIO));

        internal static int Interpretar(string valor)
        {
            return int.TryParse(valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias) ? dias : DIAS_RECORDATORIO_POR_DEFECTO;
        }

        private static string Leer(string clave)
        {
            try
            {
                return LeerAjuste != null ? LeerAjuste(clave) : ConfigurationManager.AppSettings[clave];
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>NestoAPI#698: los textos que ve la gente (de usted), en un solo sitio.</summary>
    public static class TextosRetencion
    {
        public const string TIPO_NOTIFICACION = "PedidoRetenido";
        public const string LIBERADO_A_MANO = "Liberado a mano";
        public const string LIBERADO_POR_FECHA = "Llegó la fecha de revisión";
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static string Fecha(DateTime? fecha) => fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", castellano) : null;

        /// <summary>«Alfredo» a partir de «NUEVAVISION\Alfredo» (o lo que venga, limpio).</summary>
        public static string NombreUsuario(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return "alguien";
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 && barra < limpio.Length - 1 ? limpio.Substring(barra + 1) : limpio;
        }

        /// <summary>«Retenido por Alfredo desde el 22/10/2026 hasta el 15/11/2026: motivo» / «… hasta nuevo aviso: motivo».</summary>
        public static string Descripcion(RetencionPedidoDTO retencion, bool mayusculaInicial = true)
        {
            if (retencion == null)
            {
                return null;
            }
            string hasta = retencion.HastaFecha.HasValue ? $"hasta el {Fecha(retencion.HastaFecha)}" : "hasta nuevo aviso";
            string motivo = string.IsNullOrWhiteSpace(retencion.Motivo) ? string.Empty : $": {retencion.Motivo.Trim()}";
            return $"{(mayusculaInicial ? "Retenido" : "retenido")} por {NombreUsuario(retencion.Usuario)} desde el {Fecha(retencion.Fecha)} {hasta}{motivo}";
        }

        /// <summary>El pedido se ha liberado solo porque ha llegado su fecha de revisión.</summary>
        public static NotificacionPushDTO NotificacionLiberado(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera)
        {
            string cliente = TextoCliente(cabecera);
            return new NotificacionPushDTO
            {
                Titulo = $"El pedido {retencion.Pedido} ya no está retenido",
                Cuerpo = $"El pedido {retencion.Pedido}{cliente} estaba retenido {RetenidoPor(retencion)} hasta el {Fecha(retencion.HastaFecha)} " +
                    $"({retencion.Motivo?.Trim()}). Ha llegado la fecha y se ha liberado: vuelve a «{Constantes.Pedidos.ModosServicio.Nombre(ModoAlLiberar(retencion, cabecera))}» " +
                    "y entra en el picking. Si tiene que seguir retenido, vuelva a retenerlo desde el pedido.",
                Tipo = TIPO_NOTIFICACION,
                Datos = Datos(retencion, cabecera, "liberado")
            };
        }

        /// <summary>El recordatorio periódico de un retenido sin fecha.</summary>
        public static NotificacionPushDTO NotificacionRecordatorio(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera, DateTime hoy)
        {
            string cliente = TextoCliente(cabecera);
            int dias = Math.Max(0, (hoy.Date - retencion.Fecha.Date).Days);
            return new NotificacionPushDTO
            {
                Titulo = $"El pedido {retencion.Pedido} sigue retenido",
                Cuerpo = $"El pedido {retencion.Pedido}{cliente} lleva {dias} {(dias == 1 ? "día" : "días")} retenido {RetenidoPor(retencion)} " +
                    $"hasta nuevo aviso ({retencion.Motivo?.Trim()}). Si ya puede salir, libérelo desde el pedido; si no, no tiene que hacer nada.",
                Tipo = TIPO_NOTIFICACION,
                Datos = Datos(retencion, cabecera, "recordatorio")
            };
        }

        /// <summary>
        /// Lo que ve quien retiene un pedido que YA tiene picking: el modo 5 solo para los pickings futuros; lo que está en el
        /// almacén sigue su curso si nadie lo para. Se avisa al grupo Almacén y el servidor no deja hacer envío ni albarán.
        /// </summary>
        public static string AvisoPickingEnCurso(int pedido, List<int> pickings)
        {
            string lista = ListaPickings(pickings);
            return $"El pedido {pedido} ya tiene {lista} en el almacén. Se ha avisado a Almacén para que no lo entregue a la agencia " +
                "y el servidor no dejará hacer el envío ni el albarán mientras esté retenido, pero confírmelo con el almacén.";
        }

        /// <summary>Campana de Nesto y buzón de Ariadna al grupo Almacén: hay que parar un picking que ya está en marcha.</summary>
        public static NotificacionPushDTO NotificacionPickingEnCurso(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera, List<int> pickings)
        {
            string cliente = TextoCliente(cabecera);
            string lista = ListaPickings(pickings);
            string hasta = retencion.HastaFecha.HasValue ? $"hasta el {Fecha(retencion.HastaFecha)}" : "hasta nuevo aviso";
            return new NotificacionPushDTO
            {
                Titulo = $"NO ENTREGAR: pedido {retencion.Pedido} retenido con {lista}",
                Cuerpo = $"{NombreUsuario(retencion.Usuario)} acaba de retener el pedido {retencion.Pedido}{cliente} {hasta} ({retencion.Motivo?.Trim()}), " +
                    $"pero ya tiene {lista} en marcha. No lo entreguen a la agencia: aparten la mercancía o devuélvanla al hueco. " +
                    "Mientras esté retenido, el servidor no deja crear el envío ni el albarán.",
                Tipo = TIPO_NOTIFICACION,
                Datos = Datos(retencion, cabecera, "picking-en-curso")
            };
        }

        /// <summary>Lo que contesta el servidor cuando se intenta sacar (envío, etiqueta, albarán) un pedido retenido.</summary>
        public static string BloqueoSalida(int pedido, RetencionPedidoDTO retencion)
        {
            string detalle = retencion != null ? Descripcion(retencion, mayusculaInicial: false) : "retenido hasta nuevo aviso";
            return $"El pedido {pedido} está {detalle}. No se puede enviar ni albaranear: libérelo desde el pedido (botón «Liberar») si tiene que salir.";
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

        private static string RetenidoPor(RetencionPedidoDTO retencion) => $"por {NombreUsuario(retencion.Usuario)} desde el {Fecha(retencion.Fecha)}";

        private static string TextoCliente(CabeceraPedidoRetencion cabecera)
        {
            if (cabecera == null || string.IsNullOrWhiteSpace(cabecera.Cliente))
            {
                return string.Empty;
            }
            string nombre = cabecera.NombreCliente?.Trim();
            return string.IsNullOrEmpty(nombre) ? $" del cliente {cabecera.Cliente.Trim()}" : $" del cliente {cabecera.Cliente.Trim()} ({nombre})";
        }

        /// <summary>El modo que rige al liberar: el anterior guardado o, si no estaba informado, lo que diga ServirJunto.</summary>
        public static byte ModoAlLiberar(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera)
            => Constantes.Pedidos.ModosServicio.Efectivo(retencion?.ModoServicioAnterior, cabecera?.ServirJunto ?? false);

        private static Dictionary<string, string> Datos(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera, string accion)
        {
            return new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["accion"] = accion,
                ["empresa"] = retencion.Empresa?.Trim(),
                ["pedido"] = retencion.Pedido.ToString(CultureInfo.InvariantCulture),
                ["cliente"] = cabecera?.Cliente?.Trim()
            };
        }
    }
}
