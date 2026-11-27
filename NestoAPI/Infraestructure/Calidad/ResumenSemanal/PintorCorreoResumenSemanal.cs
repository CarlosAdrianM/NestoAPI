using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace NestoAPI.Infraestructure.Calidad.ResumenSemanal
{
    /// <summary>
    /// NestoAPI#914: pinta el modelo del correo semanal de calidad (puro). HTML sobrio con estilos en línea (los clientes de correo quitan los
    /// &lt;style&gt;), de una columna y como mucho 640 px para que se lea en el móvil. Las notas van con LETRA además del color: se entiende sin
    /// imágenes y sin colores.
    /// </summary>
    public static class PintorCorreoResumenSemanal
    {
        private const string FUENTE = "font-family: Arial, Helvetica, sans-serif;";
        private const string CELDA = "padding: 4px 6px; border-bottom: 1px solid #e0e0e0; vertical-align: top;";
        private const string CABECERA = "padding: 4px 6px; border-bottom: 2px solid #9e9e9e; text-align: left; font-weight: bold; background: #f5f5f5;";

        public static string Pintar(CorreoResumenSemanal correo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine($"<title>{H(correo.Asunto)}</title></head>");
            sb.AppendLine($"<body style=\"margin: 0; padding: 0; background: #ffffff; color: #212121; {FUENTE}\">");
            sb.AppendLine($"<div style=\"max-width: 640px; margin: 0 auto; padding: 12px; font-size: 14px; line-height: 1.4; {FUENTE}\">");
            sb.AppendLine($"<h1 style=\"font-size: 20px; margin: 0 0 12px 0;\">{H(correo.Asunto)}</h1>");

            Titulo(sb, "Lo que más ha cambiado");
            Lista(sb, correo.Cambios);

            Titulo(sb, "Tablero");
            if (correo.Tablero.NoDisponible != null)
            {
                Parrafo(sb, correo.Tablero.NoDisponible);
            }
            foreach (KpiCorreo k in correo.Tablero.Kpis)
            {
                PintarKpi(sb, k);
            }
            if (correo.Tablero.NoDisponible == null)
            {
                Parrafo(sb, $"Tira de las últimas 8 notas, de la más antigua a la más nueva: P = POOR, F = FAIR, G = GOOD, ★ = FANTASTIC, " +
                    $"{ReglaCorreoResumenSemanal.TIRA_SIN_NOTA} = con valor pero sin nota, {ReglaCorreoResumenSemanal.TIRA_SIN_DATO} = sin dato.", true);
            }

            Titulo(sb, "Almacén: los dos indicadores oficiales por preparador");
            if (correo.Almacen.NoDisponible != null)
            {
                Parrafo(sb, correo.Almacen.NoDisponible);
            }
            else
            {
                if (correo.Almacen.Filas.Count > 0)
                {
                    sb.AppendLine(AbrirTabla());
                    sb.AppendLine($"<tr><th style=\"{CABECERA}\">Preparador</th><th style=\"{CABECERA} text-align: right;\">Líneas</th>" +
                        $"<th style=\"{CABECERA}\">Error de envío reclamado</th><th style=\"{CABECERA}\">Falta en packing</th></tr>");
                    foreach (FilaAlmacenCorreo f in correo.Almacen.Filas)
                    {
                        string negrita = f.EsTotal ? " font-weight: bold;" : string.Empty;
                        sb.AppendLine($"<tr><td style=\"{CELDA}{negrita}\">{H(f.Preparador)}</td><td style=\"{CELDA} text-align: right;{negrita}\">{f.Lineas}</td>" +
                            $"<td style=\"{CELDA}{negrita}\">{H(f.ErrorDeEnvioReclamado)}</td><td style=\"{CELDA}{negrita}\">{H(f.FaltaEnPacking)}</td></tr>");
                    }
                    sb.AppendLine("</table>");
                }
                Lista(sb, correo.Almacen.Notas, true);
            }

            Titulo(sb, "Encuestas en sombra");
            Lista(sb, correo.Encuestas.Lineas);
            if (correo.Encuestas.NoDisponible != null)
            {
                Parrafo(sb, correo.Encuestas.NoDisponible);
            }
            else
            {
                if (correo.Encuestas.PorTema.Count > 0)
                {
                    sb.AppendLine(AbrirTabla());
                    sb.AppendLine($"<tr><th style=\"{CABECERA}\">Tema</th><th style=\"{CABECERA} text-align: right;\">Candidatas</th>" +
                        $"<th style=\"{CABECERA} text-align: right;\">Encuestas</th></tr>");
                    foreach (RecuentoCorreo r in correo.Encuestas.PorTema)
                    {
                        sb.AppendLine($"<tr><td style=\"{CELDA}\">{H(r.Que)}</td><td style=\"{CELDA} text-align: right;\">{r.Candidatas}</td>" +
                            $"<td style=\"{CELDA} text-align: right;\">{r.Encuestas}</td></tr>");
                    }
                    sb.AppendLine("</table>");
                }
                if (correo.Encuestas.PorCanal.Count > 0)
                {
                    Parrafo(sb, "Por canal: " + string.Join(" · ", correo.Encuestas.PorCanal.Select(c => $"{c.Que}: {c.Encuestas}")) + ".");
                }
            }

            Titulo(sb, "Pendiente de usted");
            Lista(sb, correo.Pendientes);

            sb.AppendLine("<hr style=\"border: 0; border-top: 1px solid #e0e0e0; margin: 16px 0 8px 0;\">");
            Lista(sb, correo.Pie, true);
            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        private static void PintarKpi(StringBuilder sb, KpiCorreo k)
        {
            string semana = k.SemanaMostrada.HasValue ? $" <span style=\"font-weight: normal; color: #616161;\">(semana del {k.SemanaMostrada.Value.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture)})</span>" : string.Empty;
            sb.AppendLine($"<h3 style=\"font-size: 15px; margin: 14px 0 4px 0;\">{H(k.Nombre)}{semana}</h3>");
            if (k.Linea != null)
            {
                Parrafo(sb, k.Linea, true);
            }
            if (k.Filas.Count == 0)
            {
                return;
            }
            sb.AppendLine(AbrirTabla());
            sb.AppendLine($"<tr><th style=\"{CABECERA}\">Dueño</th><th style=\"{CABECERA} text-align: right;\">Valor</th><th style=\"{CABECERA}\">Nota</th>" +
                $"<th style=\"{CABECERA}\">Últimas 8</th></tr>");
            foreach (FilaKpiCorreo f in k.Filas)
            {
                sb.AppendLine($"<tr><td style=\"{CELDA}\">{H(f.Dueno)}</td><td style=\"{CELDA} text-align: right; white-space: nowrap;\">{H(f.Valor)}</td>" +
                    $"<td style=\"{CELDA} white-space: nowrap;\">{H(f.Nota)}</td><td style=\"{CELDA} white-space: nowrap; font-family: Consolas, monospace;\">{TiraHtml(f.Tira)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        internal static string TiraHtml(IEnumerable<string> tira) =>
            string.Join(string.Empty, (tira ?? Enumerable.Empty<string>()).Select(Marca));

        private static string Marca(string letra)
        {
            string fondo;
            string color = "#ffffff";
            switch (letra)
            {
                case "P": fondo = "#c62828"; break;
                case "F": fondo = "#ef6c00"; break;
                case "G": fondo = "#2e7d32"; break;
                case "★": fondo = "#1565c0"; break;
                default: fondo = "#eeeeee"; color = "#616161"; break;
            }
            return $"<span style=\"display: inline-block; min-width: 14px; margin-right: 2px; padding: 0 2px; text-align: center; background: {fondo}; " +
                $"color: {color}; border-radius: 2px;\">{H(letra)}</span>";
        }

        private static string AbrirTabla() =>
            $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width: 100%; border-collapse: collapse; font-size: 13px; margin: 4px 0 8px 0; {FUENTE}\">";

        private static void Titulo(StringBuilder sb, string texto) =>
            sb.AppendLine($"<h2 style=\"font-size: 17px; margin: 20px 0 6px 0; padding-bottom: 2px; border-bottom: 1px solid #bdbdbd;\">{H(texto)}</h2>");

        private static void Parrafo(StringBuilder sb, string texto, bool pequeno = false) =>
            sb.AppendLine($"<p style=\"margin: 4px 0;{(pequeno ? " font-size: 12px; color: #616161;" : string.Empty)}\">{H(texto)}</p>");

        private static void Lista(StringBuilder sb, IEnumerable<string> lineas, bool pequeno = false)
        {
            List<string> l = (lineas ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (l.Count == 0)
            {
                return;
            }
            sb.AppendLine($"<ul style=\"margin: 4px 0; padding-left: 20px;{(pequeno ? " font-size: 12px; color: #616161;" : string.Empty)}\">");
            foreach (string x in l)
            {
                sb.AppendLine($"<li style=\"margin: 2px 0;\">{H(x)}</li>");
            }
            sb.AppendLine("</ul>");
        }

        private static string H(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
    }
}
