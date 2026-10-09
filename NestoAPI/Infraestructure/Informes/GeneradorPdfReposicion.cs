using NestoAPI.Infraestructure.Reposiciones;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>Una fila del listado impreso, ya con los textos de cada columna.</summary>
    public class FilaPdfReposicion
    {
        public string Ubicacion { get; set; }
        public string Producto { get; set; }
        public string CodigoBarras { get; set; }
        public string Descripcion { get; set; }
        public string Familia { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// Sugerencia 564 (Paloma): la reposición en papel, para prepararla (o comprobar lo que llega) a mano mientras las
    /// tiendas no tienen Ariadna. Una fila por producto con ubicación (si la hay), referencia, código de barras,
    /// descripción, cantidad y una casilla en blanco para marcarla; en el orden de <see cref="ServicioListadoReposicion.OrdenarParaRecorrer"/>.
    /// </summary>
    public class GeneradorPdfReposicion
    {
        public ByteArrayContent GenerarPdf(ListadoReposicionDTO listado)
        {
            ListadoReposicionDTO datos = listado ?? new ListadoReposicionDTO();
            List<FilaPdfReposicion> filas = Filas(datos);
            bool conUbicacion = filas.Any(f => !string.IsNullOrEmpty(f.Ubicacion));

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(1f, Unit.Centimetre);
                    page.MarginHorizontal(1f, Unit.Centimetre);
                    // Se lee de pie, recorriendo la tienda: letra como la del picking (#293)
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComponerCabecera(c, datos, filas));
                    page.Content().Element(c => ComponerTabla(c, filas, conUbicacion));
                    page.Footer().Element(ComponerPie);
                });
            });

            return new ByteArrayContent(documento.GeneratePdf());
        }

        /// <summary>Las filas en el orden del papel (el del listado, que ya viene ordenado para recorrer el almacén).</summary>
        public static List<FilaPdfReposicion> Filas(ListadoReposicionDTO listado)
        {
            return (listado?.Lineas ?? new List<LineaListadoReposicionDTO>())
                .Where(l => l != null)
                .Select(l => new FilaPdfReposicion
                {
                    Ubicacion = l.Ubicacion ?? string.Empty,
                    Producto = l.Producto?.Trim() ?? string.Empty,
                    CodigoBarras = l.CodigoBarras?.Trim() ?? string.Empty,
                    Descripcion = l.Descripcion?.Trim() ?? string.Empty,
                    Familia = l.Familia?.Trim() ?? string.Empty,
                    Cantidad = l.Cantidad
                })
                .ToList();
        }

        /// <summary>«Reposición 80893 · día 09/10/2026 · corte 09/10/2026 09:00» (o «sin número todavía» y «corte: a mano»).</summary>
        public static string LineaDatos(ListadoReposicionDTO listado)
        {
            var partes = new List<string>
            {
                listado?.NumTraspaso is int numero ? $"Traspaso {numero}" : "Sin número de traspaso todavía (se pone al terminar)"
            };
            if (listado?.Fecha is DateTime fecha)
            {
                partes.Add($"Día {fecha:dd/MM/yyyy}");
            }
            partes.Add(listado?.FechaCorte is DateTime corte ? $"Corte {corte:dd/MM/yyyy HH:mm}" : "Corte: a mano");
            return string.Join(" · ", partes);
        }

        private static void ComponerCabecera(IContainer container, ListadoReposicionDTO listado, List<FilaPdfReposicion> filas)
        {
            container.PaddingBottom(6).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text(listado.Titulo).Bold().FontSize(14);
                    row.ConstantItem(150).AlignRight().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
                });
                column.Item().Text(listado.Ruta).SemiBold().FontSize(13);
                column.Item().Text(LineaDatos(listado)).FontSize(10);
                column.Item().Text($"{filas.Count} productos · {filas.Sum(f => f.Cantidad)} unidades").FontSize(10);
            });
        }

        private static void ComponerTabla(IContainer container, List<FilaPdfReposicion> filas, bool conUbicacion)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(22);          // casilla
                    if (conUbicacion)
                    {
                        columns.ConstantColumn(62);      // Ubicación
                    }
                    columns.ConstantColumn(48);          // Ref.
                    columns.ConstantColumn(88);          // Código de barras
                    columns.RelativeColumn(3f);          // Descripción
                    columns.RelativeColumn(1.4f);        // Familia
                    columns.ConstantColumn(40);          // Cant.
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), string.Empty, alinearDerecha: false);
                    if (conUbicacion)
                    {
                        CeldaCabecera(header.Cell(), "Ubicación", alinearDerecha: false);
                    }
                    CeldaCabecera(header.Cell(), "Ref.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Código de barras", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Descripción", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Familia", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cant.", alinearDerecha: true);
                });

                foreach (FilaPdfReposicion fila in filas)
                {
                    // La casilla en blanco para marcar a mano lo que ya se ha cogido o comprobado
                    table.Cell().ShowEntire().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).AlignMiddle()
                        .Width(12).Height(12).Border(1).BorderColor(Colors.Grey.Darken2);
                    if (conUbicacion)
                    {
                        CeldaDato(table.Cell(), fila.Ubicacion, alinearDerecha: false);
                    }
                    CeldaDato(table.Cell(), fila.Producto, alinearDerecha: false);
                    CeldaDato(table.Cell(), fila.CodigoBarras, alinearDerecha: false);
                    CeldaDato(table.Cell(), fila.Descripcion, alinearDerecha: false);
                    CeldaDato(table.Cell(), fila.Familia, alinearDerecha: false, tamano: 8);
                    CeldaDato(table.Cell(), fila.Cantidad.ToString(), alinearDerecha: true, negrita: true);
                }
            });
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

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha, float tamano = 10, bool negrita = false)
        {
            // ShowEntire (#302, lección de Picking/Packing): una fila que cae en el corte de página pasa ENTERA a la
            // siguiente; partida, se repetían celdas y se contaba la unidad dos veces.
            IContainer contenido = celda.ShowEntire().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            TextSpanDescriptor descriptor = contenido.Text(texto ?? string.Empty).FontSize(tamano);
            if (negrita)
            {
                descriptor.Bold();
            }
        }

        private static void ComponerPie(IContainer container)
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
