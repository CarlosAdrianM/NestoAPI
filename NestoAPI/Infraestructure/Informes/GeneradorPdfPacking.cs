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
    /// Genera el PDF del packing list con QuestPDF, sustituyendo el RDLC Packing.rdlc que
    /// Nesto renderizaba en local (Nesto#340). Replica la estructura del RDLC: un bloque por
    /// pedido (salto de página entre pedidos) con la cabecera del cliente (dirección, aviso,
    /// comentario de picking, ruta), la tabla de líneas a servir y, si las hay, la sección
    /// "Productos pendientes de servir en próximos pedidos..." (filas con Tipo = "Pendientes").
    /// </summary>
    public class GeneradorPdfPacking
    {
        private const string TIPO_PENDIENTES = "Pendientes";

        public ByteArrayContent GenerarPdf(int picking, List<PackingDTO> lineas)
        {
            List<PackingDTO> datos = lineas ?? new List<PackingDTO>();

            // Un grupo por pedido, conservando el orden en que las devuelve el SP.
            List<IGrouping<int, PackingDTO>> pedidos = datos.GroupBy(l => l.Número).ToList();

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
                        if (!pedidos.Any())
                        {
                            column.Item().PaddingTop(10).Text("No hay líneas en este picking.").Italic();
                            return;
                        }
                        for (int i = 0; i < pedidos.Count; i++)
                        {
                            if (i > 0)
                            {
                                column.Item().PageBreak();
                            }
                            var pedido = pedidos[i];
                            column.Item().Element(c => ComponerPedido(c, pedido.Key, pedido.ToList()));
                        }
                    });
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabecera(IContainer container, int picking)
        {
            container.PaddingBottom(5).Row(row =>
            {
                row.RelativeItem().Text($"Packing List ({picking})").Bold().FontSize(14);
                row.ConstantItem(150).AlignRight().Text($"{DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerPedido(IContainer container, int numeroPedido, List<PackingDTO> lineas)
        {
            PackingDTO cabecera = lineas.First();
            List<PackingDTO> servir = lineas.Where(l => l.Tipo?.Trim() != TIPO_PENDIENTES).ToList();
            List<PackingDTO> pendientes = lineas.Where(l => l.Tipo?.Trim() == TIPO_PENDIENTES).ToList();

            container.Column(column =>
            {
                // Cabecera del pedido: cliente, dirección y datos de entrega.
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(5).Column(cab =>
                {
                    cab.Item().Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Pedido: ").Bold();
                            text.Span(numeroPedido.ToString());
                            text.Span("      Cliente: ").Bold();
                            text.Span($"{cabecera.NºCliente?.Trim()} ({cabecera.Contacto?.Trim()})");
                        });
                        row.ConstantItem(220).AlignRight().Text(text =>
                        {
                            text.Span("Ruta: ").Bold();
                            text.Span(cabecera.Ruta?.Trim() ?? "");
                            text.Span("      Usuario: ").Bold();
                            text.Span(QuitarDominio(cabecera.Usuario));
                        });
                    });
                    cab.Item().Text($"{cabecera.Direccion?.Trim()} - {cabecera.CodPostal?.Trim()} {cabecera.Poblacion?.Trim()} - Tel. {cabecera.Telefono?.Trim()}");
                    if (!string.IsNullOrWhiteSpace(cabecera.Aviso))
                    {
                        cab.Item().Text(cabecera.Aviso.Trim()).Bold().FontColor(Colors.Red.Darken2);
                    }
                    if (!string.IsNullOrWhiteSpace(cabecera.Ampliacion))
                    {
                        cab.Item().Text(cabecera.Ampliacion.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(cabecera.ComentarioPicking))
                    {
                        cab.Item().Text(cabecera.ComentarioPicking.Trim()).Italic();
                    }
                });

                if (servir.Any())
                {
                    column.Item().PaddingTop(4).Element(c => ComponerTablaLineas(c, servir));
                }

                if (pendientes.Any())
                {
                    column.Item().PaddingTop(8).Text("Productos pendientes de servir en próximos pedidos... ").Bold().Italic();
                    column.Item().PaddingTop(2).Element(c => ComponerTablaLineas(c, pendientes));
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
            container.AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span("Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        }
    }
}
