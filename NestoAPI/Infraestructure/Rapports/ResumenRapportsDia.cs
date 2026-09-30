using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// La parte con reglas del correo diario de rapports, separada de la base de datos y de OpenAI
    /// para poder probarla.
    ///
    /// El resumen lo redacta una IA, y una IA no garantiza nada: se dejaba vendedores sin nombrar y
    /// citaba clientes sin decir de quién eran. Por eso lo que el jefe de ventas necesita SIEMPRE
    /// (quién ha trabajado, cuánto, quién no ha metido nada y qué rapports son de clientes de un
    /// telefónico) se calcula aquí y va en una cabecera fija, delante de lo que escriba la IA. Y a
    /// la IA se le da cada rapport ya con el nombre del vendedor y del cliente, agrupado por
    /// vendedor, para que no tenga que adivinar de quién habla.
    /// </summary>
    public static class ResumenRapportsDia
    {
        /// <summary>Por debajo de esto el comentario no dice nada y no se le pasa a la IA.</summary>
        public const int LONGITUD_MINIMA_COMENTARIO = 10;

        public class Rapport
        {
            public string Vendedor { get; set; }
            public string Cliente { get; set; }
            public string Contacto { get; set; }
            public string NombreCliente { get; set; }
            /// <summary>El vendedor de la ficha del cliente, que puede no ser quien mete el rapport.</summary>
            public string VendedorCliente { get; set; }
            public string Tipo { get; set; }
            public string Comentarios { get; set; }
            public bool Pedido { get; set; }
        }

        public class FichaVendedor
        {
            public string Numero { get; set; }
            public string Nombre { get; set; }
            public short Estado { get; set; }
        }

        public class ActividadVendedor
        {
            public string Vendedor { get; set; }
            public int Rapports { get; set; }
            public int Visitas { get; set; }
            public int Telefono { get; set; }
            public int WhatsApp { get; set; }
            public int ConPedido { get; set; }
            /// <summary>Rapports sin comentario útil: son los que la IA no llega a leer.</summary>
            public int SinComentario { get; set; }
        }

        public static IDictionary<string, FichaVendedor> IndexarFichas(IEnumerable<FichaVendedor> fichas)
        {
            return (fichas ?? Enumerable.Empty<FichaVendedor>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Numero))
                .GroupBy(f => f.Numero.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>«Jesús (JE)». Si no hay ficha o no tiene nombre, el código a secas.</summary>
        public static string NombreVendedor(string vendedor, IDictionary<string, FichaVendedor> fichas)
        {
            string codigo = vendedor?.Trim();
            if (string.IsNullOrEmpty(codigo))
            {
                return "(sin vendedor)";
            }
            string nombre = fichas != null && fichas.TryGetValue(codigo, out FichaVendedor ficha) ? ficha.Nombre?.Trim() : null;
            return string.IsNullOrEmpty(nombre) ? codigo : $"{nombre} ({codigo})";
        }

        /// <summary>«12345/0 NOMBRE DEL CLIENTE». Null-safe: un registro cojo no tumba el correo (NestoAPI#374).</summary>
        public static string NombreCliente(Rapport rapport)
        {
            string codigo = $"{rapport?.Cliente?.Trim()}/{rapport?.Contacto?.Trim()}";
            string nombre = rapport?.NombreCliente?.Trim();
            return string.IsNullOrEmpty(nombre) ? codigo : $"{codigo} {nombre}";
        }

        public static string TipoEnTexto(string tipo)
        {
            switch (tipo?.Trim())
            {
                case "V":
                    return "Visita";
                case "T":
                    return "Teléfono";
                case "W":
                    return "WhatsApp";
                default:
                    return "Desconocido";
            }
        }

        public static bool TieneComentarioUtil(Rapport rapport)
        {
            return rapport?.Comentarios != null && rapport.Comentarios.Length >= LONGITUD_MINIMA_COMENTARIO;
        }

        /// <summary>Una fila por vendedor con rapports ese día, ordenadas por código.</summary>
        public static List<ActividadVendedor> Actividad(IEnumerable<Rapport> rapports)
        {
            return (rapports ?? Enumerable.Empty<Rapport>())
                .GroupBy(r => r.Vendedor?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ActividadVendedor
                {
                    Vendedor = g.Key,
                    Rapports = g.Count(),
                    Visitas = g.Count(r => r.Tipo?.Trim() == "V"),
                    Telefono = g.Count(r => r.Tipo?.Trim() == "T"),
                    WhatsApp = g.Count(r => r.Tipo?.Trim() == "W"),
                    ConPedido = g.Count(r => r.Pedido),
                    SinComentario = g.Count(r => !TieneComentarioUtil(r))
                })
                .OrderBy(a => a.Vendedor, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// De los vendedores de los que se espera rapport, los que no han metido ninguno. Es lo que
        /// distingue «no ha trabajado» de «ha trabajado pero no había nada que destacar».
        /// </summary>
        public static List<string> SinRapports(IEnumerable<string> esperados, IEnumerable<Rapport> rapports)
        {
            var conRapport = new HashSet<string>(
                (rapports ?? Enumerable.Empty<Rapport>())
                    .Where(r => !string.IsNullOrWhiteSpace(r.Vendedor))
                    .Select(r => r.Vendedor.Trim()),
                StringComparer.OrdinalIgnoreCase);

            return (esperados ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(v => !conRapport.Contains(v))
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Un vendedor presencial que mete un rapport de un cliente que lleva un telefónico: es raro
        /// y el jefe de ventas quiere verlo.
        /// </summary>
        public static bool EsDeClienteDeTelefonico(Rapport rapport, IDictionary<string, FichaVendedor> fichas)
        {
            string autor = rapport?.Vendedor?.Trim();
            string delCliente = rapport?.VendedorCliente?.Trim();
            if (string.IsNullOrEmpty(autor) || string.IsNullOrEmpty(delCliente) || fichas == null ||
                string.Equals(autor, delCliente, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return fichas.TryGetValue(autor, out FichaVendedor fichaAutor) &&
                   fichas.TryGetValue(delCliente, out FichaVendedor fichaCliente) &&
                   fichaAutor.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL &&
                   fichaCliente.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO;
        }

        /// <summary>
        /// Lo que lee la IA: los rapports agrupados por vendedor, cada uno con el nombre del vendedor
        /// y del cliente. Los vendedores sin ningún comentario útil también salen, para que la IA
        /// pueda decir que no hay nada que destacar en vez de callárselos.
        /// </summary>
        public static string TextoParaIA(DateTime fecha, IEnumerable<Rapport> rapports, IDictionary<string, FichaVendedor> fichas)
        {
            List<Rapport> lista = (rapports ?? Enumerable.Empty<Rapport>()).ToList();
            var texto = new StringBuilder();
            _ = texto.Append($"Rapports del día {fecha:dd/MM/yyyy}, agrupados por vendedor.\n\n");

            foreach (var grupo in lista
                .GroupBy(r => r.Vendedor?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                string vendedor = NombreVendedor(grupo.Key, fichas);
                List<Rapport> utiles = grupo.Where(TieneComentarioUtil).ToList();
                _ = texto.Append($"=== VENDEDOR: {vendedor}. Rapports: {grupo.Count()}, de ellos con comentario: {utiles.Count} ===\n\n");

                foreach (Rapport rapport in utiles)
                {
                    _ = texto.Append($"Vendedor: {vendedor}\n");
                    _ = texto.Append($"Cliente: {NombreCliente(rapport)}\n");
                    _ = texto.Append($"Tipo: {TipoEnTexto(rapport.Tipo)}\n");
                    if (EsDeClienteDeTelefonico(rapport, fichas))
                    {
                        _ = texto.Append($"OJO: este cliente no es suyo, lo lleva {NombreVendedor(rapport.VendedorCliente, fichas)}, vendedor telefónico.\n");
                    }
                    _ = texto.Append($"Comentario: {rapport.Comentarios.Trim()}\n");
                    _ = texto.Append($"Terminó en pedido: {(rapport.Pedido ? "Sí" : "No")}\n\n");
                }
            }

            return texto.ToString();
        }

        /// <summary>
        /// La cabecera fija del correo: actividad por vendedor, quién no ha metido nada y los
        /// rapports de clientes de un telefónico. No depende de la IA.
        /// </summary>
        public static string CabeceraHtml(DateTime fecha, IEnumerable<Rapport> rapports,
            IDictionary<string, FichaVendedor> fichas, IEnumerable<string> esperados)
        {
            List<Rapport> lista = (rapports ?? Enumerable.Empty<Rapport>()).ToList();
            const string celda = "border:1px solid #ccc;padding:4px 10px;";
            const string numero = celda + "text-align:right;";

            var html = new StringBuilder();
            _ = html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;\">");
            _ = html.Append($"<h2>Actividad del día {fecha:dd/MM/yyyy}</h2>");
            _ = html.Append("<table style=\"border-collapse:collapse;\"><tr style=\"background:#eee;\">");
            foreach (string titulo in new[] { "Vendedor", "Rapports", "Visitas", "Teléfono", "WhatsApp", "Con pedido", "Sin comentario" })
            {
                _ = html.Append($"<th style=\"{celda}\">{titulo}</th>");
            }
            _ = html.Append("</tr>");
            foreach (ActividadVendedor actividad in Actividad(lista))
            {
                _ = html.Append("<tr>");
                _ = html.Append($"<td style=\"{celda}\">{Html(NombreVendedor(actividad.Vendedor, fichas))}</td>");
                foreach (int valor in new[] { actividad.Rapports, actividad.Visitas, actividad.Telefono, actividad.WhatsApp, actividad.ConPedido, actividad.SinComentario })
                {
                    _ = html.Append($"<td style=\"{numero}\">{valor}</td>");
                }
                _ = html.Append("</tr>");
            }
            _ = html.Append("</table>");

            List<string> sinRapports = SinRapports(esperados, lista);
            if (sinRapports.Any())
            {
                _ = html.Append("<p><strong>Sin ningún rapport este día:</strong> ");
                _ = html.Append(Html(string.Join(", ", sinRapports.Select(v => NombreVendedor(v, fichas)))));
                _ = html.Append(".</p>");
            }

            List<Rapport> deTelefonico = lista.Where(r => EsDeClienteDeTelefonico(r, fichas)).ToList();
            if (deTelefonico.Any())
            {
                _ = html.Append("<p><strong>Rapports de un vendedor presencial a clientes de un vendedor telefónico:</strong></p><ul>");
                foreach (Rapport rapport in deTelefonico)
                {
                    _ = html.Append("<li>");
                    _ = html.Append(Html($"{NombreVendedor(rapport.Vendedor, fichas)} → cliente {NombreCliente(rapport)}, " +
                        $"que lleva {NombreVendedor(rapport.VendedorCliente, fichas)} ({TipoEnTexto(rapport.Tipo).ToLowerInvariant()}" +
                        $"{(rapport.Pedido ? ", terminó en pedido" : string.Empty)})."));
                    _ = html.Append("</li>");
                }
                _ = html.Append("</ul>");
            }

            _ = html.Append("<hr/></div>");
            return html.ToString();
        }

        /// <summary>
        /// La IA a veces envuelve el HTML en un bloque de código (```html ... ```), que en el correo
        /// se vería como texto.
        /// </summary>
        public static string LimpiarHtmlDeIA(string respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return string.Empty;
            }
            string limpio = respuesta.Trim();
            if (limpio.StartsWith("```"))
            {
                int finPrimeraLinea = limpio.IndexOf('\n');
                limpio = finPrimeraLinea < 0 ? string.Empty : limpio.Substring(finPrimeraLinea + 1);
            }
            if (limpio.EndsWith("```"))
            {
                limpio = limpio.Substring(0, limpio.Length - 3);
            }
            return limpio.Trim();
        }

        private static string Html(string texto)
        {
            return WebUtility.HtmlEncode(texto ?? string.Empty);
        }
    }
}
