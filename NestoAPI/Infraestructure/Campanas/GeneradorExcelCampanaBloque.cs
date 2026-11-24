using ClosedXML.Excel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NestoAPI.Infraestructure.Campanas
{
    /// <summary>
    /// El Excel (.xlsx, ClosedXML) del ensayo de una campaña en bloque (POST api/Campanas/Bloque/Excel): la lista COMPLETA, que
    /// es lo que revisa la Dirección antes de crearla. Hoja «Productos» (una fila por producto, también los que quedan fuera, con
    /// su motivo) y hoja «Resumen» (totales por familia y el total general).
    /// </summary>
    public class GeneradorExcelCampanaBloque
    {
        public const string CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        internal static readonly string[] COLUMNAS = { "Producto", "Nombre", "Familia", "Grupo", "Subgrupo", "PVP", "Descuento", "Precio resultante",
            "Audiencia", "Origen", "Estado", "Motivo", "Detalle", "Desbloquear ficha", "Condición de contado", "Fila existente", "Solo en stock" };
        internal static readonly string[] COLUMNAS_RESUMEN = { "Familia", "Productos", "Entran", "Ya estaban", "Fuera", "Solapes",
            "Fichas desbloqueadas", "Condición de contado", "Fuera por motivo" };

        public ByteArrayContent GenerarExcel(RespuestaCampanaBloqueDTO respuesta)
        {
            var contenido = new ByteArrayContent(GenerarBytes(respuesta));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = $"Campana_{NombreFichero(respuesta?.Nombre)}_{respuesta?.FechaDesde:yyyyMMdd}.xlsx"
            };
            return contenido;
        }

        internal static string NombreFichero(string nombre)
        {
            string limpio = new string((nombre ?? "campana").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return limpio.Length > 40 ? limpio.Substring(0, 40) : limpio;
        }

        public byte[] GenerarBytes(RespuestaCampanaBloqueDTO respuesta)
        {
            List<LineaCampanaBloqueDTO> lineas = respuesta?.Lineas ?? respuesta?.Muestra ?? new List<LineaCampanaBloqueDTO>();
            using (var libro = new XLWorkbook())
            {
                IXLWorksheet ws = libro.Worksheets.Add("Productos");
                Cabecera(ws, COLUMNAS);
                int fila = 2;
                foreach (LineaCampanaBloqueDTO l in lineas)
                {
                    ws.Cell(fila, 1).Value = l.Producto;
                    ws.Cell(fila, 2).Value = l.Nombre;
                    ws.Cell(fila, 3).Value = l.Familia;
                    ws.Cell(fila, 4).Value = l.Grupo;
                    ws.Cell(fila, 5).Value = l.SubGrupo;
                    if (l.PVP.HasValue)
                    {
                        ws.Cell(fila, 6).Value = l.PVP.Value;
                    }
                    ws.Cell(fila, 6).Style.NumberFormat.Format = "#,##0.00 €";
                    ws.Cell(fila, 7).Value = l.Descuento;
                    ws.Cell(fila, 7).Style.NumberFormat.Format = "0.##%";
                    if (l.PrecioResultante.HasValue)
                    {
                        ws.Cell(fila, 8).Value = l.PrecioResultante.Value;
                    }
                    ws.Cell(fila, 8).Style.NumberFormat.Format = "#,##0.00 €";
                    ws.Cell(fila, 9).Value = TextoAudiencia(l.Audiencia);
                    ws.Cell(fila, 10).Value = l.Origen;
                    ws.Cell(fila, 11).Value = TextoEstado(l.Estado);
                    ws.Cell(fila, 12).Value = l.Motivo;
                    ws.Cell(fila, 13).Value = l.Detalle;
                    ws.Cell(fila, 14).Value = l.DesbloquearFicha ? "Sí" : string.Empty;
                    ws.Cell(fila, 15).Value = l.CondicionContado ? "Sí" : string.Empty;
                    if (l.FilaExistente.HasValue)
                    {
                        ws.Cell(fila, 16).Value = l.FilaExistente.Value;
                    }
                    ws.Cell(fila, 17).Value = l.CondicionStock ? "Sí" : string.Empty;
                    fila++;
                }
                ws.SheetView.FreezeRows(1);
                if (fila > 2)
                {
                    _ = ws.Range(1, 1, fila - 1, COLUMNAS.Length).SetAutoFilter();
                }
                _ = ws.Columns(1, COLUMNAS.Length).AdjustToContents();

                IXLWorksheet resumen = libro.Worksheets.Add("Resumen");
                Cabecera(resumen, COLUMNAS_RESUMEN);
                int r = 2;
                var totales = new List<TotalesCampanaBloqueDTO>(respuesta?.PorFamilia ?? new List<TotalesCampanaBloqueDTO>());
                if (respuesta?.Total != null)
                {
                    totales.Add(respuesta.Total);
                }
                foreach (TotalesCampanaBloqueDTO t in totales)
                {
                    resumen.Cell(r, 1).Value = t.Familia ?? "TOTAL";
                    resumen.Cell(r, 2).Value = t.Productos;
                    resumen.Cell(r, 3).Value = t.Entran;
                    resumen.Cell(r, 4).Value = t.YaEstaban;
                    resumen.Cell(r, 5).Value = t.Fuera;
                    resumen.Cell(r, 6).Value = t.Solapes;
                    resumen.Cell(r, 7).Value = t.FichasDesbloqueadas;
                    resumen.Cell(r, 8).Value = t.CondicionContado;
                    resumen.Cell(r, 9).Value = string.Join(", ", (t.FueraPorMotivo ?? new Dictionary<string, int>()).Select(m => $"{m.Key}: {m.Value}"));
                    if (t.Familia == null)
                    {
                        resumen.Range(r, 1, r, COLUMNAS_RESUMEN.Length).Style.Font.Bold = true;
                    }
                    r++;
                }
                _ = resumen.Columns(1, COLUMNAS_RESUMEN.Length).AdjustToContents();

                using (var ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static void Cabecera(IXLWorksheet ws, string[] columnas)
        {
            for (int i = 0; i < columnas.Length; i++)
            {
                ws.Cell(1, i + 1).Value = columnas[i];
            }
            IXLRange cabecera = ws.Range(1, 1, 1, columnas.Length);
            cabecera.Style.Font.Bold = true;
            cabecera.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        internal static string TextoAudiencia(byte audiencia) =>
            audiencia == 1 ? "Solo profesionales" : audiencia == 2 ? "Profesionales y público" : "No va a la web";

        internal static string TextoEstado(EstadoLineaCampanaBloque estado) =>
            estado == EstadoLineaCampanaBloque.Entra ? "Entra" : estado == EstadoLineaCampanaBloque.YaEstaba ? "Ya estaba" : "Fuera";
    }
}
