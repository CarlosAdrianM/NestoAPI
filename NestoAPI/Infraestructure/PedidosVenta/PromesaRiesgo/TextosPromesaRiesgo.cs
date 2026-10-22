using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Lo que hace falta de un riesgo para avisar (la fila más el cliente y sus comerciales).</summary>
    public class AvisoPromesaRiesgo
    {
        public RiesgoPromesa Riesgo { get; set; }
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
        /// <summary>Nombres de los comerciales del pedido (para el correo).</summary>
        public string Comerciales { get; set; }
        /// <summary>True si es un riesgo que ya se avisó y ha empeorado.</summary>
        public bool Empeora { get; set; }
    }

    /// <summary>
    /// NestoAPI#693: los textos, puros y de usted. El «por qué» sale del desglose del motor de #606
    /// (<see cref="FechaEntregaAgenciaDTO.Desglose"/>, las mismas cuentas que el correo del pedido) más la causa del evento
    /// («Reina mandó 1 de 3 en la reposición 81094»).
    /// </summary>
    public static class TextosPromesaRiesgo
    {
        public const string TIPO_NOTIFICACION = "PromesaEnRiesgo";
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>«jueves 22/10/2026» o «sin fecha».</summary>
        public static string Fecha(DateTime? fecha) => fecha.HasValue ? GestorPresupuestos.DiaCorreo(fecha.Value.Date) : "sin fecha";

        /// <summary>Las partidas que no llegan a la prometida (sin fecha, o que salen después).</summary>
        public static List<PartidaFechaEntregaAgenciaDTO> PartidasQueFallan(DateTime prometida, FechaEntregaAgenciaDTO calculo)
        {
            return (calculo?.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => p != null && (!p.PuedeSalirEl.HasValue || p.PuedeSalirEl.Value.Date > prometida.Date))
                .ToList();
        }

        /// <summary>
        /// Qué productos fallan y por qué, una frase por producto: «42485 Parches (3 uds.): Reina mandó 1 de 3 en la reposición
        /// 81094; 2 en Reina: llegan con la reposición y pueden salir el viernes 23/10/2026.». Si ninguna partida falla por sí
        /// sola (el cliente cierra ese día, «Tras reponer de tiendas» espera a todo…), el motivo del motor.
        /// </summary>
        /// <param name="necesidades">Producto → unidades pendientes en el pedido (para «1 de 3»).</param>
        /// <param name="nombres">Producto → descripción (opcional).</param>
        public static string Motivo(int pedido, DateTime prometida, FechaEntregaAgenciaDTO calculo, IDictionary<string, CausaPromesaRiesgo> causas,
            IDictionary<string, int> necesidades, IDictionary<string, string> nombres)
        {
            List<PartidaFechaEntregaAgenciaDTO> fallan = PartidasQueFallan(prometida, calculo);
            if (!fallan.Any())
            {
                string motivo = calculo?.Motivo?.Trim();
                return string.IsNullOrEmpty(motivo) ? "El cálculo de la fecha de entrega a la agencia ya no llega a la prometida." : motivo;
            }
            var frases = new List<string>();
            foreach (IGrouping<string, PartidaFechaEntregaAgenciaDTO> producto in fallan.GroupBy(p => p.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                int necesita = necesidades != null && necesidades.TryGetValue(producto.Key, out int n) ? n : producto.Sum(p => p.Unidades);
                string nombre = nombres != null && nombres.TryGetValue(producto.Key, out string descripcion) && !string.IsNullOrWhiteSpace(descripcion)
                    ? $"{producto.Key} {descripcion.Trim()}"
                    : producto.Key;
                var partes = new List<string>();
                if (causas != null && causas.TryGetValue(producto.Key, out CausaPromesaRiesgo causa))
                {
                    string texto = TextoCausa(causa, necesita, pedido);
                    if (!string.IsNullOrWhiteSpace(texto))
                    {
                        partes.Add(texto);
                    }
                }
                partes.AddRange(producto.Select(FrasePartida));
                frases.Add($"{nombre} ({Unidades(necesita)}): {string.Join("; ", partes)}.");
            }
            return string.Join(" ", frases);
        }

        /// <summary>«Reina mandó 1 de 3 en la reposición 81094». Null si la causa no dice nada de este pedido.</summary>
        public static string TextoCausa(CausaPromesaRiesgo causa, int necesita, int pedido)
        {
            if (causa == null)
            {
                return null;
            }
            if (causa.Mandadas.HasValue && !string.IsNullOrWhiteSpace(causa.Tienda))
            {
                string tienda = CalculadoraFechaEntregaAgencia.NombreAlmacen(causa.Tienda.Trim());
                string reposicion = causa.Traspaso.HasValue ? $" en la reposición {causa.Traspaso.Value}" : " en la reposición";
                return causa.Mandadas.Value < necesita
                    ? $"{tienda} mandó {causa.Mandadas.Value} de {necesita}{reposicion}"
                    : $"{tienda} mandó {causa.Mandadas.Value}{reposicion}";
            }
            if (causa.PedidoOrigen.HasValue)
            {
                return causa.PedidoOrigen.Value == pedido
                    ? "se ha modificado el pedido"
                    : $"también lo lleva el pedido {causa.PedidoOrigen.Value}, grabado o modificado después";
            }
            return string.IsNullOrWhiteSpace(causa.Texto) ? null : causa.Texto.Trim();
        }

        /// <summary>Lo mismo que dice el motor de cada partida, en una frase corta.</summary>
        internal static string FrasePartida(PartidaFechaEntregaAgenciaDTO p)
        {
            bool varias = p.Unidades != 1;
            string n = varias ? "n" : string.Empty;
            string saleEl = p.PuedeSalirEl.HasValue ? $"puede{n} salir el {Fecha(p.PuedeSalirEl)}" : "sin fecha";
            switch (p.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    string tienda = CalculadoraFechaEntregaAgencia.NombreAlmacen(p.Tienda?.Trim());
                    return p.PuedeSalirEl.HasValue
                        ? $"{p.Unidades} en {tienda}: llega{n} con la reposición y {saleEl}"
                        : $"{p.Unidades} en {tienda}: sin fecha ({p.MotivoSinFecha?.Trim() ?? "sin calendario de reposición"})";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return $"{p.Unidades} ya viene{n} de camino desde una tienda: {saleEl}";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    string pedidoProveedor = p.PedidoProveedor.HasValue ? $"del pedido a proveedor {p.PedidoProveedor.Value}" : "de un pedido a proveedor";
                    string prevista = p.FechaPrevistaProveedor.HasValue ? $", prevista el {Fecha(p.FechaPrevistaProveedor)}" : " sin fecha prevista";
                    string vencida = p.LlegadaSupuesta.HasValue ? " (ya pasó y no ha llegado)" : string.Empty;
                    return $"{p.Unidades} {pedidoProveedor}{prevista}{vencida}: {saleEl}";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK:
                    return $"{p.Unidades} sin fecha: {(string.IsNullOrWhiteSpace(p.MotivoSinFecha) ? "no hay stock ni fecha de llegada" : p.MotivoSinFecha.Trim())}";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT:
                    return $"{p.Unidades} desmontando el kit {p.Kit?.Trim()} en Algete: {saleEl}";
                default:
                    return $"{p.Unidades} con stock en Algete: {saleEl}";
            }
        }

        private static string Unidades(int unidades) => $"{unidades} {(unidades == 1 ? "ud." : "uds.")}";

        public static string TextoCliente(string cliente, string nombre)
        {
            string numero = cliente?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? numero : $"{numero} - {nombre.Trim()}";
        }

        /// <summary>«se queda sin fecha» / «se retrasa al viernes 23/10/2026».</summary>
        public static string QuePasa(DateTime? nueva) => nueva.HasValue ? $"se retrasa al {Fecha(nueva)}" : "se queda sin fecha";

        public static NotificacionPushDTO Notificacion(AvisoPromesaRiesgo aviso)
        {
            RiesgoPromesa r = aviso.Riesgo;
            string cliente = TextoCliente(aviso.Cliente, aviso.NombreCliente);
            string ahora = r.FechaNueva.HasValue ? $"ahora sale el {Fecha(r.FechaNueva)}" : "ahora se queda sin fecha";
            string empeora = aviso.Empeora ? " (ya se le avisó y ha empeorado)" : string.Empty;
            return new NotificacionPushDTO
            {
                Titulo = $"El pedido {r.Pedido} {QuePasa(r.FechaNueva)}",
                Cuerpo = $"El pedido {r.Pedido} del cliente {cliente} tenía la entrega a la agencia prometida para el " +
                    $"{Fecha(r.FechaPrometida)} y {ahora}{empeora}. {r.Motivo} Revise el pedido y, si hace falta, avise al cliente.",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["empresa"] = r.Empresa?.Trim(),
                    ["pedido"] = r.Pedido.ToString(CultureInfo.InvariantCulture),
                    ["cliente"] = aviso.Cliente?.Trim()
                }
            };
        }

        /// <summary>El correo resumen: una tabla con los pedidos, del que antes se va a la agencia al que más tarde.</summary>
        public static string CuerpoCorreo(IEnumerable<AvisoPromesaRiesgo> avisos, bool sombra)
        {
            List<AvisoPromesaRiesgo> lista = (avisos ?? Enumerable.Empty<AvisoPromesaRiesgo>())
                .OrderBy(a => a.Riesgo.FechaPrometida).ThenBy(a => a.Riesgo.Pedido).ToList();
            var s = new StringBuilder();
            _ = s.Append("<p>");
            if (sombra)
            {
                _ = s.Append("<b>MODO SOMBRA</b> (Pedidos:AvisoPromesaRiesgo = Sombra): esto NO le ha llegado a nadie más; es lo que se habría avisado.<br>");
            }
            _ = s.Append(lista.Count == 1
                ? "Este pedido ya no llega a la fecha de entrega a la agencia que se le prometió al cliente:"
                : $"Estos {lista.Count} pedidos ya no llegan a la fecha de entrega a la agencia que se le prometió al cliente:");
            _ = s.Append("</p>");
            _ = s.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse; font-family:Arial; font-size:12px\">");
            _ = s.Append("<tr style=\"background-color:#eeeeee\"><th>Pedido</th><th>Cliente</th><th>Comercial</th><th>Prometida</th><th>Ahora</th><th>Qué falla y por qué</th></tr>");
            foreach (AvisoPromesaRiesgo a in lista)
            {
                RiesgoPromesa r = a.Riesgo;
                string estilo = r.FechaNueva.HasValue ? string.Empty : " style=\"color:red\"";
                _ = s.Append("<tr>")
                    .Append($"<td>{r.Pedido}</td>")
                    .Append($"<td>{Html(TextoCliente(a.Cliente, a.NombreCliente))}</td>")
                    .Append($"<td>{Html(a.Comerciales)}</td>")
                    .Append($"<td>{Html(Fecha(r.FechaPrometida))}</td>")
                    .Append($"<td{estilo}><b>{Html(Fecha(r.FechaNueva))}</b>{(a.Empeora ? " (ha empeorado)" : string.Empty)}</td>")
                    .Append($"<td>{Html(r.Motivo)}</td>")
                    .Append("</tr>");
            }
            _ = s.Append("</table>");
            _ = s.Append("<p>Revise cada pedido y, si hace falta, avise al cliente o cambie lo que convenga (modo de servir, reposición puntual, " +
                "sustituir el producto o renegociar la fecha). No se le volverá a avisar de estos pedidos salvo que empeoren.</p>");
            return s.ToString();
        }

        public static string AsuntoCorreo(int pedidos, bool sombra) =>
            (sombra ? "[Sombra] " : string.Empty) + (pedidos == 1
                ? "Un pedido ya no llega a la fecha prometida"
                : $"{pedidos} pedidos ya no llegan a la fecha prometida");

        /// <summary>Solo &amp; &lt; &gt; y comillas: las tildes se quedan como están (como GestorPresupuestos).</summary>
        private static string Html(string texto) => (texto ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
