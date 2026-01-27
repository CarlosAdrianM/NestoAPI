using NestoAPI.Models.Facturas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// Implementación de generación de PDFs usando QuestPDF.
    /// Replica el diseño del informe RDLC Factura.rdlc
    /// </summary>
    public class GeneradorPdfFacturasQuestPdf : IGeneradorPdfFacturas
    {
        private const string URL_LOGO = "http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg";
        private byte[] _logoBytes;
        private bool _papelConMembrete;

        public ByteArrayContent GenerarPdf(List<Factura> facturas, bool papelConMembrete = false)
        {
            _papelConMembrete = papelConMembrete;
            CargarImagenes();

            var factura = facturas.FirstOrDefault();
            if (factura == null)
            {
                throw new ArgumentException("No se proporcionaron facturas para generar el PDF");
            }

            // Determinar si hay descuentos en alguna línea
            bool mostrarColumnaDescuento = factura.Lineas?.Any(l => l.Descuento != 0) ?? false;

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Element(c => ComponerCabecera(c, factura));
                    page.Content().Element(c => ComponerContenido(c, factura, mostrarColumnaDescuento));
                    page.Footer().Element(c => ComponerPie(c, factura));
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void CargarImagenes()
        {
            if (_papelConMembrete)
            {
                _logoBytes = null;
                return;
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                try
                {
                    _logoBytes = client.GetByteArrayAsync(URL_LOGO).GetAwaiter().GetResult();
                }
                catch
                {
                    _logoBytes = null;
                }
            }
        }

        private void ComponerCabecera(IContainer container, Factura factura)
        {
            var direccionEmpresa = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Empresa");
            var direccionFiscal = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Fiscal");
            var direccionEntrega = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Entrega");

            container.Column(column =>
            {
                // ========== FILA 1: Logo + Datos empresa ==========
                if (!_papelConMembrete)
                {
                    column.Item().Row(row =>
                    {
                        // Logo (izquierda) - tamaño fijo
                        if (_logoBytes != null && _logoBytes.Length > 0)
                        {
                            row.ConstantItem(100).AlignLeft().AlignTop().Image(_logoBytes);
                        }
                        else
                        {
                            row.ConstantItem(100);
                        }

                        // Datos empresa (columna derecha, centrados en esa columna)
                        if (direccionEmpresa != null)
                        {
                            row.RelativeItem().AlignCenter().Column(col =>
                            {
                                col.Item().AlignCenter().Text(direccionEmpresa.Nombre).Bold().FontSize(10);
                                col.Item().AlignCenter().Text(direccionEmpresa.Direccion).FontSize(8);
                                col.Item().AlignCenter().Text(direccionEmpresa.PoblacionCompleta).FontSize(8);
                                if (!string.IsNullOrEmpty(direccionEmpresa.Telefonos))
                                {
                                    col.Item().AlignCenter().Text($"Tel: {direccionEmpresa.Telefonos}").FontSize(8);
                                }
                                if (!string.IsNullOrEmpty(direccionEmpresa.Comentarios))
                                {
                                    col.Item().AlignCenter().Text(direccionEmpresa.Comentarios).FontSize(7);
                                }
                            });
                        }
                        else
                        {
                            row.RelativeItem();
                        }
                    });

                    column.Item().PaddingVertical(5);
                }

                // ========== FILA 2: Razón Social + Dirección Entrega ==========
                column.Item().Row(row =>
                {
                    // RAZÓN SOCIAL (izquierda)
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Column(col =>
                    {
                        col.Item().Background(Colors.Grey.Lighten3).Padding(3)
                            .Text("RAZÓN SOCIAL").Bold().FontSize(8);
                        col.Item().Padding(5).Column(innerCol =>
                        {
                            if (direccionFiscal != null)
                            {
                                innerCol.Item().Text(direccionFiscal.Nombre ?? "").FontSize(9);
                                innerCol.Item().Text(direccionFiscal.Direccion ?? "").FontSize(8);
                                innerCol.Item().Text(direccionFiscal.PoblacionCompleta ?? "").FontSize(8);
                                if (!string.IsNullOrEmpty(direccionFiscal.Telefonos))
                                {
                                    innerCol.Item().Text($"Tel: {direccionFiscal.Telefonos}").FontSize(8);
                                }
                            }
                        });
                    });

                    row.ConstantItem(10); // Espacio entre cuadros

                    // DIRECCIÓN DE ENTREGA (derecha)
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Column(col =>
                    {
                        col.Item().Background(Colors.Grey.Lighten3).Padding(3)
                            .Text("DIRECCIÓN DE ENTREGA").Bold().FontSize(8);
                        col.Item().Padding(5).Column(innerCol =>
                        {
                            if (direccionEntrega != null)
                            {
                                innerCol.Item().Text(direccionEntrega.Nombre ?? "").FontSize(9);
                                innerCol.Item().Text(direccionEntrega.Direccion ?? "").FontSize(8);
                                innerCol.Item().Text(direccionEntrega.PoblacionCompleta ?? "").FontSize(8);
                                if (!string.IsNullOrEmpty(direccionEntrega.Telefonos))
                                {
                                    innerCol.Item().Text($"Tel: {direccionEntrega.Telefonos}").FontSize(8);
                                }
                            }
                        });
                    });
                });

                column.Item().PaddingVertical(5);

                // ========== FILA 3: Cuadro FACTURA (ancho completo) ==========
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Column(col =>
                {
                    // Tipo de documento en grande (16pt como en RDLC)
                    col.Item().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter()
                        .Text(factura.TipoDocumento ?? "FACTURA").Bold().FontSize(16);

                    // Tabla con encabezados y valores debajo (como en RDLC)
                    col.Item().Padding(5).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();  // Cliente
                            columns.RelativeColumn();  // CIF/NIF
                            columns.RelativeColumn();  // Representantes
                            columns.RelativeColumn();  // Delegación
                            columns.RelativeColumn();  // Fecha
                            columns.RelativeColumn();  // Nº Factura
                        });

                        // Fila de encabezados
                        table.Cell().Padding(2).Text("Cliente").SemiBold().FontSize(8);
                        table.Cell().Padding(2).Text("CIF/NIF").SemiBold().FontSize(8);
                        table.Cell().Padding(2).Text("Representantes").SemiBold().FontSize(8);
                        table.Cell().Padding(2).Text("Delegación").SemiBold().FontSize(8);
                        table.Cell().Padding(2).Text("Fecha").SemiBold().FontSize(8);
                        table.Cell().Padding(2).Text("Nº Factura").SemiBold().FontSize(8);

                        // Fila de valores
                        table.Cell().Padding(2).Text(factura.Cliente ?? "").FontSize(8);
                        table.Cell().Padding(2).Text(factura.Nif ?? "").FontSize(8);
                        table.Cell().Padding(2).Column(repCol =>
                        {
                            if (factura.Vendedores?.Any() == true)
                            {
                                foreach (var vendedor in factura.Vendedores)
                                {
                                    repCol.Item().Text(vendedor.Nombre ?? "").FontSize(8);
                                }
                            }
                        });
                        table.Cell().Padding(2).Text(factura.Delegacion ?? "").FontSize(8);
                        table.Cell().Padding(2).Text(factura.Fecha.ToString("dd/MM/yyyy")).FontSize(8);
                        table.Cell().Padding(2).Text(factura.NumeroFactura ?? "").FontSize(8);
                    });
                });

                column.Item().PaddingVertical(3);
            });
        }

        private void ComponerContenido(IContainer container, Factura factura, bool mostrarColumnaDescuento)
        {
            container.Column(column =>
            {
                // Tabla de líneas
                column.Item().Element(c => ComponerTablaLineas(c, factura, mostrarColumnaDescuento));

                column.Item().PaddingVertical(5);

                // Totales (ancho completo)
                column.Item().Element(c => ComponerTotales(c, factura));

                column.Item().PaddingVertical(5);

                // Vencimientos (ancho completo)
                if (factura.Vencimientos?.Any() == true)
                {
                    column.Item().Element(c => ComponerVencimientos(c, factura));
                }

                // Notas al pie
                if (factura.NotasAlPie?.Any() == true)
                {
                    column.Item().PaddingTop(5).Element(c => ComponerNotas(c, factura));
                }
            });
        }

        private void ComponerTablaLineas(IContainer container, Factura factura, bool mostrarColumnaDescuento)
        {
            container.Table(table =>
            {
                // Definir columnas según RDLC: Ref., Descripción, Cant., Precio Unitario, Dtos., Importe
                if (mostrarColumnaDescuento)
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(55);   // Ref.
                        columns.RelativeColumn(3);    // Descripción
                        columns.ConstantColumn(40);   // Cant.
                        columns.ConstantColumn(60);   // Precio Unitario
                        columns.ConstantColumn(45);   // Dtos.
                        columns.ConstantColumn(65);   // Importe
                    });
                }
                else
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);   // Ref.
                        columns.RelativeColumn(3);    // Descripción
                        columns.ConstantColumn(45);   // Cant.
                        columns.ConstantColumn(65);   // Precio Unitario
                        columns.ConstantColumn(70);   // Importe
                    });
                }

                // Cabecera con nombres igual que RDLC - centrados verticalmente
                table.Header(header =>
                {
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3)
                        .AlignMiddle().Text("Ref.").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3)
                        .AlignMiddle().Text("Descripción").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3)
                        .AlignMiddle().AlignRight().Text("Cant.").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3)
                        .AlignMiddle().AlignRight().Text("Precio Unitario").Bold().FontSize(8);
                    if (mostrarColumnaDescuento)
                    {
                        header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3)
                            .AlignMiddle().AlignRight().Text("Dtos.").Bold().FontSize(8);
                    }
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3)
                        .AlignMiddle().AlignRight().Text("Importe").Bold().FontSize(8);
                });

                // Filas de líneas
                string albaranAnterior = null;
                if (factura.Lineas != null)
                {
                    foreach (var linea in factura.Lineas)
                    {
                        // Fila de albarán (si cambia) - CON línea separadora
                        if (linea.TextoAlbaran != albaranAnterior)
                        {
                            // Si no es el primer albarán, añadir línea separadora
                            if (albaranAnterior != null)
                            {
                                int colspanSep = mostrarColumnaDescuento ? 6 : 5;
                                table.Cell().ColumnSpan((uint)colspanSep)
                                    .BorderTop(1).BorderColor(Colors.Grey.Lighten1)
                                    .Height(1);
                            }

                            albaranAnterior = linea.TextoAlbaran;
                            int colspan = mostrarColumnaDescuento ? 6 : 5;
                            table.Cell().ColumnSpan((uint)colspan)
                                .PaddingVertical(3).PaddingLeft(3)
                                .Text(linea.TextoAlbaran).Bold().FontSize(8);
                        }

                        // Fila de producto - SIN líneas entre filas
                        table.Cell().Padding(2).Text(linea.Producto ?? "").FontSize(8);
                        table.Cell().Padding(2).Text(linea.DescripcionCompleta ?? "").FontSize(8);
                        table.Cell().Padding(2).AlignRight().Text(linea.Cantidad?.ToString() ?? "").FontSize(8);
                        table.Cell().Padding(2).AlignRight().Text(linea.PrecioUnitario?.ToString("N2") ?? "").FontSize(8);
                        if (mostrarColumnaDescuento)
                        {
                            table.Cell().Padding(2).AlignRight()
                                .Text(linea.Descuento != 0 ? linea.Descuento.ToString("P2") : "").FontSize(8);
                        }
                        table.Cell().Padding(2).AlignRight().Text(linea.Importe.ToString("N2")).FontSize(8);
                    }
                }
            });
        }

        private void ComponerTotales(IContainer container, Factura factura)
        {
            // Tabla de totales a ancho completo como en RDLC (sin columna TOTAL por fila)
            container.Table(table =>
            {
                // Todas las columnas del mismo tamaño
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();  // Base Imponible
                    columns.RelativeColumn();  // % IVA
                    columns.RelativeColumn();  // Importe IVA
                    columns.RelativeColumn();  // % R.E.
                    columns.RelativeColumn();  // Recargo Equiv.
                });

                // Cabecera
                table.Header(header =>
                {
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignRight().Text("Base Imponible").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignRight().Text("% IVA").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignRight().Text("Importe IVA").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignRight().Text("% R.E.").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignRight().Text("Recargo Equiv.").Bold().FontSize(8);
                });

                // Filas de totales por tipo de IVA
                if (factura.Totales != null)
                {
                    foreach (var total in factura.Totales)
                    {
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text(total.BaseImponible.ToString("N2")).FontSize(8);
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text((total.PorcentajeIVA * 100).ToString("N0") + "%").FontSize(8);
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text(total.ImporteIVA.ToString("N2")).FontSize(8);
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text(total.PorcentajeRecargoEquivalencia != 0
                                ? (total.PorcentajeRecargoEquivalencia * 100).ToString("N2") + "%" : "").FontSize(8);
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text(total.ImporteRecargoEquivalencia != 0
                                ? total.ImporteRecargoEquivalencia.ToString("N2") : "").FontSize(8);
                    }
                }

                // Fila de TOTAL (único, como en RDLC)
                table.Cell().ColumnSpan(4).Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4)
                    .Padding(4).AlignRight().Text("TOTAL:").Bold().FontSize(10);
                table.Cell().Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4)
                    .Padding(4).AlignRight().Text(factura.ImporteTotal.ToString("C2")).Bold().FontSize(10);
            });
        }

        private void ComponerVencimientos(IContainer container, Factura factura)
        {
            // Tabla de vencimientos a ancho completo como en RDLC
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(80);    // Vencimiento
                    columns.ConstantColumn(80);    // Forma Pago
                    columns.ConstantColumn(80);    // Importe
                    columns.RelativeColumn();      // IBAN
                    columns.ConstantColumn(80);    // Situación
                });

                // Cabecera - todos centrados excepto Situación a la izquierda
                table.Header(header =>
                {
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignCenter().Text("Vencimiento").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignCenter().Text("Forma Pago").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignCenter().Text("Importe").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignCenter().Text("IBAN").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten3)
                        .Padding(3).AlignMiddle().AlignLeft().Text("Situación").Bold().FontSize(8);
                });

                // Filas de vencimientos - centrados excepto Importe (derecha) y Situación (izquierda)
                foreach (var venc in factura.Vencimientos)
                {
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                        .AlignCenter().Text(venc.Vencimiento.ToString("dd/MM/yyyy")).FontSize(8);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                        .AlignCenter().Text(venc.FormaPago ?? "").FontSize(8);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                        .AlignRight().Text(venc.Importe.ToString("C2")).FontSize(8);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                        .AlignCenter().Text(venc.Iban ?? "").FontSize(8);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                        .AlignLeft().Text(venc.TextoPagado ?? "").FontSize(8);
                }
            });
        }

        private void ComponerNotas(IContainer container, Factura factura)
        {
            // Notas en negrita y tamaño normal (no grises)
            container.Column(column =>
            {
                foreach (var nota in factura.NotasAlPie)
                {
                    if (!string.IsNullOrEmpty(nota.Nota))
                    {
                        column.Item().Text(nota.Nota).Bold().FontSize(8);
                    }
                }
            });
        }

        private void ComponerPie(IContainer container, Factura factura)
        {
            container.Column(column =>
            {
                // Datos registrales
                if (!string.IsNullOrEmpty(factura.DatosRegistrales))
                {
                    column.Item().AlignCenter().Text(factura.DatosRegistrales).FontSize(6);
                }

                column.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            });
        }
    }
}
