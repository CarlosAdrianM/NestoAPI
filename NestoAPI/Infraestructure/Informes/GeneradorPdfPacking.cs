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
    ///   rellena en todas las filas).
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
                    page.DefaultTextStyle(x => x.FontSize(8));

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
                                    Ampliacion = PrimeroNoVacio(delPedido, l => l.Ampliacion),
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

        private void ComponerCabecera(IContainer container, int picking)
        {
            container.PaddingBottom(5).Row(row =>
            {
                row.RelativeItem().Text($"Packing List ({picking})").Bold().FontSize(14);
                row.ConstantItem(150).AlignRight().Text($"{DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerBloqueCliente(IContainer container, BloqueClientePacking bloque)
        {
            container.Column(column =>
            {
                // Cabecera del cliente (una sola vez: todos sus pedidos comparten dirección).
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(5).Column(cab =>
                {
                    cab.Item().Text(text =>
                    {
                        text.Span("Cliente: ").Bold();
                        text.Span($"{bloque.Cliente}/{bloque.Contacto}");
                    });
                    cab.Item().Text($"{bloque.Direccion} - {bloque.CodPostal} {bloque.Poblacion} - Tel. {bloque.Telefono}");
                });

                foreach (GrupoPedidoPacking pedido in bloque.Pedidos)
                {
                    column.Item().PaddingTop(6).Text(text =>
                    {
                        text.Span("Pedido: ").Bold();
                        text.Span(pedido.Numero.ToString());
                        text.Span("      Ruta: ").Bold();
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
                        column.Item().Text(pedido.ComentarioPicking).Italic();
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
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(50);   // Prov.
                    columns.ConstantColumn(55);   // Producto
                    columns.ConstantColumn(90);   // Cód. Barras
                    columns.RelativeColumn(3);    // Descripción
                    columns.ConstantColumn(45);   // Tamaño
                    columns.ConstantColumn(35);   // U.M.
                    columns.RelativeColumn(1);    // Subgrupo
                    columns.ConstantColumn(40);   // Cant.
                    columns.ConstantColumn(40);   // Cajas
                    columns.ConstantColumn(45);   // Estado
                    columns.ConstantColumn(30);   // Pas
                    columns.ConstantColumn(30);   // Fil
                    columns.ConstantColumn(30);   // Col
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
                    CeldaCabecera(header.Cell(), "Cajas", alinearDerecha: true);
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
                    CeldaDato(table.Cell(), linea.CantidadCajas.ToString(), alinearDerecha: true);
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
            contenido.Text(texto).Bold().FontSize(8);
        }

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).FontSize(8);
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
