using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Genera el PDF del packing list con QuestPDF, sustituyendo el RDLC Packing.rdlc que Nesto
    /// renderizaba en local (Nesto#340). Replica la estructura del RDLC (validada contra el
    /// picking real 98900, 13/07/26):
    ///
    /// - Se agrupa por CLIENTE + dirección (salto de página solo ENTRE clientes, no por pedido:
    ///   los pedidos de un mismo cliente van seguidos en la misma página).
    /// - Dentro del cliente, un bloque por pedido CON LÍNEAS A SERVIR (cabecera Pedido/Ruta/
    ///   Usuario + tabla). Un pedido cuyas líneas son todas pendientes NO tiene bloque propio.
    /// - Al FINAL del cliente, una única sección "Productos pendientes de servir en próximos
    ///   pedidos..." con las pendientes (Tipo = "Pendientes") de TODOS sus pedidos.
    /// - Ruta/Usuario/avisos se toman del primer valor NO VACÍO de las líneas (el SP no los
    ///   rellena en todas las filas). EXCEPCIÓN (#293): Ampliacion usa el valor de la PRIMERA
    ///   fila del pedido, como el RDLC (semántica First de un textbox de grupo) — con primer-no-
    ///   vacío bastaba una fila suelta con texto para marcar 'AMPLIACIÓN PEDIDO' en pedidos que
    ///   el informe viejo no marcaba (caso real 922172).
    /// - Los bloques de cliente van ordenados por el número de pedido de su primera fila (el
    ///   RDLC ordena el grupo Cliente por Fields!Número.Value) para comparar hoja a hoja.
    /// - Anchos y fuente calcados del RDLC (detalle 10pt, Subgrupo ~6,3cm, cabeceras 14pt);
    ///   sin columna Cajas (feedback del almacén, #293).
    /// - Pie con el aviso al cliente final ("si algo no ha llegado perfecto... almacen@") igual
    ///   que el RDLC: este papel viaja DENTRO de la caja.
    /// </summary>
    public class GeneradorPdfPacking
    {
        private const string TIPO_PENDIENTES = "Pendientes";
        private const string AVISO_CLIENTE =
            "Si algo no ha llegado perfecto, por favor, envíe una foto de esta página (completa) a almacen@nuevavision.es y se lo solucionaremos. Gracias.";

        public ByteArrayContent GenerarPdf(int picking, List<PackingDTO> lineas)
        {
            List<BloqueClientePacking> bloques = Agrupar(lineas);

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComponerCabecera(c, picking));
                    page.Content().Column(column =>
                    {
                        if (!bloques.Any())
                        {
                            column.Item().PaddingTop(10).Text("No hay líneas en este picking.").Italic();
                            return;
                        }
                        for (int i = 0; i < bloques.Count; i++)
                        {
                            if (i > 0)
                            {
                                column.Item().PageBreak();
                            }
                            BloqueClientePacking bloque = bloques[i];
                            column.Item().Element(c => ComponerBloqueCliente(c, bloque));
                        }
                    });
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        /// <summary>
        /// Agrupa las líneas del SP en la estructura del informe. Interna y pura para poder
        /// testearla con los datos reales (caso picking 98900).
        /// </summary>
        internal static List<BloqueClientePacking> Agrupar(List<PackingDTO> lineas)
        {
            List<PackingDTO> datos = lineas ?? new List<PackingDTO>();

            return datos
                .GroupBy(l => new
                {
                    Cliente = l.NºCliente?.Trim(),
                    Contacto = l.Contacto?.Trim(),
                    Direccion = l.Direccion?.Trim()
                })
                // El RDLC ordena el grupo Cliente por Fields!Número.Value (= el pedido de la
                // primera fila del bloque en el orden del SP). Sin esto los bloques salen por
                // orden de aparición y no se puede comparar hoja a hoja con el informe viejo.
                .OrderBy(g => g.First().Número)
                .Select(g =>
                {
                    List<PackingDTO> delBloque = g.ToList();
                    return new BloqueClientePacking
                    {
                        Cliente = g.Key.Cliente,
                        Contacto = g.Key.Contacto,
                        Direccion = g.Key.Direccion,
                        CodPostal = PrimeroNoVacio(delBloque, l => l.CodPostal),
                        Poblacion = PrimeroNoVacio(delBloque, l => l.Poblacion),
                        Telefono = PrimeroNoVacio(delBloque, l => l.Telefono),
                        Pedidos = delBloque
                            .Where(l => l.Tipo?.Trim() != TIPO_PENDIENTES)
                            .GroupBy(l => l.Número)
                            .OrderBy(p => p.Key)
                            .Select(p =>
                            {
                                List<PackingDTO> delPedido = p.ToList();
                                return new GrupoPedidoPacking
                                {
                                    Numero = p.Key,
                                    // El SP no rellena estos campos en todas las filas: primer
                                    // valor no vacío del pedido y, si no, del bloque entero.
                                    Ruta = PrimeroNoVacio(delPedido, l => l.Ruta) ?? PrimeroNoVacio(delBloque, l => l.Ruta),
                                    Usuario = PrimeroNoVacio(delPedido, l => l.Usuario) ?? PrimeroNoVacio(delBloque, l => l.Usuario),
                                    Aviso = PrimeroNoVacio(delPedido, l => l.Aviso),
                                    // Semántica RDLC (primera fila del grupo, aunque esté vacía):
                                    // ver doc de la clase (#293, falso 'AMPLIACIÓN PEDIDO').
                                    Ampliacion = PrimeraFila(delPedido, l => l.Ampliacion),
                                    ComentarioPicking = PrimeroNoVacio(delPedido, l => l.ComentarioPicking),
                                    Lineas = delPedido
                                };
                            })
                            .ToList(),
                        Pendientes = delBloque
                            .Where(l => l.Tipo?.Trim() == TIPO_PENDIENTES)
                            .OrderBy(l => l.Número)
                            .ToList()
                    };
                })
                .ToList();
        }

        private static string PrimeroNoVacio(List<PackingDTO> lineas, Func<PackingDTO, string> selector)
        {
            return lineas.Select(selector).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        }

        // Valor de la PRIMERA fila (orden del SP), o null si está vacío: replica el textbox de
        // grupo del RDLC, que evalúa First() sobre el datasource.
        private static string PrimeraFila(List<PackingDTO> lineas, Func<PackingDTO, string> selector)
        {
            string valor = lineas.Select(selector).FirstOrDefault();
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }

        private void ComponerCabecera(IContainer container, int picking)
        {
            container.PaddingBottom(5).Row(row =>
            {
                // #315 (feedback del almacén): el título va CENTRADO. Antes salía a la izquierda,
                // justo encima del número de pedido, y se confundía el nº de picking con el de
                // pedido. Se reserva a la izquierda el mismo ancho que ocupa la fecha para que el
                // centrado sea real respecto a la página.
                row.ConstantItem(150);
                row.RelativeItem().AlignCenter().Text($"Packing List ({picking})").Bold().FontSize(14);
                row.ConstantItem(150).AlignRight().Text($"{DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        /// <summary>
        /// #315: el número de pedido ya sale arriba (14pt) en la cabecera del cliente, así que
        /// repetirlo en la línea de Ruta/Usuario sobra y confunde... salvo cuando el cliente trae
        /// VARIOS pedidos en el mismo picking: ahí es lo único que identifica a qué pedido
        /// corresponde cada tabla de líneas.
        /// </summary>
        internal static bool DebeRepetirNumeroPedido(BloqueClientePacking bloque)
        {
            return bloque?.Pedidos != null && bloque.Pedidos.Count > 1;
        }

        private void ComponerBloqueCliente(IContainer container, BloqueClientePacking bloque)
        {
            container.Column(column =>
            {
                // Cabecera del cliente (una sola vez: todos sus pedidos comparten dirección).
                // El nº de pedido va ARRIBA, junto al cliente y destacado a 14pt como en el
                // RDLC (feedback del almacén, #293).
                List<int> numerosPedido = bloque.Pedidos.Any()
                    ? bloque.Pedidos.Select(p => p.Numero).ToList()
                    : bloque.Pendientes.Select(l => l.Número).Distinct().OrderBy(n => n).ToList();
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(5).Column(cab =>
                {
                    cab.Item().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(14).Bold());
                        text.Span(numerosPedido.Count == 1 ? "Pedido: " : "Pedidos: ");
                        text.Span(string.Join(", ", numerosPedido));
                        text.Span("      Cliente: ");
                        text.Span($"{bloque.Cliente}/{bloque.Contacto}");
                    });
                    cab.Item().Text($"{bloque.Direccion} - {bloque.CodPostal} {bloque.Poblacion} - Tel. {bloque.Telefono}");
                });

                bool repetirNumeroPedido = DebeRepetirNumeroPedido(bloque);

                foreach (GrupoPedidoPacking pedido in bloque.Pedidos)
                {
                    column.Item().PaddingTop(6).Text(text =>
                    {
                        if (repetirNumeroPedido)
                        {
                            text.Span("Pedido: ").Bold().FontSize(12);
                            text.Span(pedido.Numero.ToString()).Bold().FontSize(12);
                            text.Span("      ");
                        }
                        text.Span("Ruta: ").Bold();
                        text.Span(pedido.Ruta ?? "");
                        text.Span("      Usuario: ").Bold();
                        text.Span(QuitarDominio(pedido.Usuario));
                    });
                    if (!string.IsNullOrWhiteSpace(pedido.Aviso))
                    {
                        column.Item().Text(pedido.Aviso).Bold().FontColor(Colors.Red.Darken2);
                    }
                    if (!string.IsNullOrWhiteSpace(pedido.Ampliacion))
                    {
                        column.Item().Text(pedido.Ampliacion);
                    }
                    if (!string.IsNullOrWhiteSpace(pedido.ComentarioPicking))
                    {
                        // #302 (petición del almacén): el comentario de picking destacado para que
                        // no se pase por alto al preparar (negrita 11pt sobre fondo con borde; en
                        // impresora B/N el fondo sale gris claro y sigue resaltando).
                        column.Item().PaddingTop(2).Background(Colors.Yellow.Lighten3)
                            .Border(1).BorderColor(Colors.Yellow.Darken2).Padding(3)
                            .Text(pedido.ComentarioPicking).Bold().FontSize(11);
                    }
                    column.Item().PaddingTop(2).Element(c => ComponerTablaLineas(c, pedido.Lineas));
                }

                if (bloque.Pendientes.Any())
                {
                    column.Item().PaddingTop(8).Text("Productos pendientes de servir en próximos pedidos... ").Bold().Italic();
                    column.Item().PaddingTop(2).Element(c => ComponerTablaLineas(c, bloque.Pendientes));
                }
            });
        }

        private void ComponerTablaLineas(IContainer container, List<PackingDTO> lineas)
        {
            container.Table(table =>
            {
                // Anchos proporcionales a los del RDLC (Descripción 8,5cm / Subgrupo 6,3cm), que
                // son los que evitan que los subgrupos largos partan la fila en 2-3 líneas (#293).
                // Sin columna Cajas: en el packing no aporta (feedback del almacén).
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(32);      // Prov.
                    columns.ConstantColumn(54);      // Producto
                    columns.ConstantColumn(84);      // Cód. Barras
                    columns.RelativeColumn(2.7f);    // Descripción
                    columns.ConstantColumn(42);      // Tamaño
                    columns.ConstantColumn(30);      // U.M.
                    columns.RelativeColumn(2f);      // Subgrupo
                    columns.ConstantColumn(34);      // Cant.
                    columns.ConstantColumn(40);      // Estado
                    columns.ConstantColumn(24);      // Pas
                    columns.ConstantColumn(24);      // Fil
                    columns.ConstantColumn(28);      // Col
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Prov.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Producto", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cód. Barras", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Descripción", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Tamaño", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "U.M.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Subgrupo", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cant.", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Estado", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Pas", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Fil", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Col", alinearDerecha: false);
                });

                foreach (PackingDTO linea in lineas)
                {
                    CeldaDato(table.Cell(), linea.ProveedorProducto?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.NºProducto?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.CodBarras?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Descripcion?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Tamaño?.ToString() ?? "", alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.UnidadMedida?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.NombreSubGrupo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Cantidad.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.Estado?.ToString() ?? "", alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.Pasillo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Fila?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Columna?.Trim() ?? "", alinearDerecha: false);
                }
            });
        }

        // El Usuario viene como DOMINIO\usuario; en el informe solo interesa el usuario.
        private static string QuitarDominio(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return "";
            }
            int barra = usuario.LastIndexOf('\\');
            return barra >= 0 ? usuario.Substring(barra + 1).Trim() : usuario.Trim();
        }

        private static void CeldaCabecera(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).Bold().FontSize(10);
        }

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha)
        {
            // ShowEntire (#302): sin esto, una fila con descripción de 2 líneas que cae en el
            // corte de página se PARTE, y las celdas que ya cabían (referencia, cantidad) se
            // repiten en la continuación: el operario cuenta la unidad dos veces (una por hoja)
            // y se envía de más. Con ShowEntire la fila que no cabe pasa ENTERA a la siguiente.
            IContainer contenido = celda.ShowEntire().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).FontSize(10);
        }

        private void ComponerPie(IContainer container)
        {
            // El packing viaja DENTRO de la caja: el aviso al cliente final va en todas las
            // páginas, igual que en el RDLC.
            container.Column(column =>
            {
                column.Item().AlignCenter().Text(AVISO_CLIENTE).FontSize(9).Bold();
                column.Item().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8));
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
        }
    }

    /// <summary>Bloque del packing por cliente + dirección (una página por bloque).</summary>
    internal class BloqueClientePacking
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Telefono { get; set; }
        public List<GrupoPedidoPacking> Pedidos { get; set; } = new List<GrupoPedidoPacking>();
        public List<PackingDTO> Pendientes { get; set; } = new List<PackingDTO>();
    }

    /// <summary>Pedido con líneas a servir dentro de un bloque de cliente.</summary>
    internal class GrupoPedidoPacking
    {
        public int Numero { get; set; }
        public string Ruta { get; set; }
        public string Usuario { get; set; }
        public string Aviso { get; set; }
        public string Ampliacion { get; set; }
        public string ComentarioPicking { get; set; }
        public List<PackingDTO> Lineas { get; set; } = new List<PackingDTO>();
    }
}
