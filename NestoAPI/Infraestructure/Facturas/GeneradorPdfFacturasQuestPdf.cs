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
        private byte[] _logoBytes;
        private bool _papelConMembrete;

        public ByteArrayContent GenerarPdf(List<Factura> facturas, bool papelConMembrete = false)
        {
            _papelConMembrete = papelConMembrete;

            var factura = facturas.FirstOrDefault();
            if (factura == null)
            {
                throw new ArgumentException("No se proporcionaron facturas para generar el PDF");
            }

            // Si usa formato ticket, generar con plantilla simplificada
            if (factura.UsaFormatoTicket)
            {
                return GenerarPdfTicket(factura);
            }

            // Para formato factura estándar, validar que tenga UrlLogo (excepto si es papel con membrete)
            if (string.IsNullOrEmpty(factura.UrlLogo) && !_papelConMembrete)
            {
                throw new InvalidOperationException(
                    $"La serie '{factura.Serie}' no tiene plantilla QuestPDF configurada. " +
                    "Debe definir UrlLogo o establecer UsaFormatoTicket=true en la clase de serie.");
            }

            CargarImagenes(factura.UrlLogo);

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

        private void CargarImagenes(string urlLogo)
        {
            if (_papelConMembrete || string.IsNullOrEmpty(urlLogo))
            {
                _logoBytes = null;
                return;
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                try
                {
                    _logoBytes = client.GetByteArrayAsync(urlLogo).GetAwaiter().GetResult();
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
                // Siempre reservamos el espacio del logo para que la Razón Social quede en la misma posición
                // En papel con membrete: espacio vacío (el logo ya está preimpreso en el papel)
                // En papel blanco: imprimimos logo y datos de empresa
                column.Item().Row(row =>
                {
                    // Logo (izquierda) - tamaño igual al RDLC: 6.05cm x 3.96cm
                    // En papel membrete solo reservamos el espacio sin imprimir nada
                    if (!_papelConMembrete && _logoBytes != null && _logoBytes.Length > 0)
                    {
                        row.ConstantItem(6.05f, Unit.Centimetre).Height(3.96f, Unit.Centimetre)
                            .AlignLeft().AlignTop().Image(_logoBytes, ImageScaling.FitArea);
                    }
                    else
                    {
                        // Reservar espacio del logo (papel membrete o sin logo cargado)
                        row.ConstantItem(6.05f, Unit.Centimetre).Height(3.96f, Unit.Centimetre);
                    }

                    // Espacio vacío alineado con Razón Social (columna izquierda de FILA 2)
                    row.RelativeItem();
                    row.ConstantItem(10); // Espacio entre columnas (igual que FILA 2)

                    // Datos empresa centrados sobre Dirección de Entrega (columna derecha de FILA 2)
                    // En papel membrete no imprimimos datos de empresa (ya están en el membrete)
                    if (!_papelConMembrete && direccionEmpresa != null)
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

                // ========== Comentarios del pedido (después del cuadro FACTURA) ==========
                if (!string.IsNullOrWhiteSpace(factura.Comentarios))
                {
                    column.Item().PaddingVertical(3).PaddingHorizontal(2)
                        .Text(factura.Comentarios).Bold().FontSize(10);
                }

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

        #region Formato Ticket (Serie GB)

        /// <summary>
        /// Genera PDF en formato ticket para la serie GB.
        /// Formato simplificado sin logo, sin desglose de IVA, sin vencimientos.
        /// </summary>
        private ByteArrayContent GenerarPdfTicket(Factura factura)
        {
            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(0.5f, Unit.Centimetre);
                    page.MarginHorizontal(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Georgia"));

                    page.Header().Element(c => ComponerCabeceraTicket(c, factura));
                    page.Content().Element(c => ComponerContenidoTicket(c, factura));
                    page.Footer().Element(c => ComponerPieTicket(c, factura));
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabeceraTicket(IContainer container, Factura factura)
        {
            var direccionEntrega = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Entrega");
            string tipoDocumento = factura.ImporteTotal < 0 ? "ABONO TICKET" : "TICKET";

            container.Column(column =>
            {
                column.Item().PaddingBottom(10).Row(row =>
                {
                    // Cuadro izquierdo: TICKET + datos básicos
                    row.RelativeItem().Border(1).Column(col =>
                    {
                        col.Item().Background(Colors.Grey.Lighten2).Padding(5)
                            .Text(tipoDocumento).Bold().FontSize(16);

                        col.Item().Padding(5).Row(dataRow =>
                        {
                            dataRow.RelativeItem().Column(labelCol =>
                            {
                                labelCol.Item().Text("Cliente").FontSize(9);
                                labelCol.Item().Text(factura.Cliente ?? "").FontSize(10);
                            });
                            dataRow.RelativeItem().Column(labelCol =>
                            {
                                labelCol.Item().Text("Fecha").FontSize(9);
                                labelCol.Item().Text(factura.Fecha.ToString("dd/MM/yy")).FontSize(10);
                            });
                            dataRow.RelativeItem().Column(labelCol =>
                            {
                                labelCol.Item().Text("Nº Ticket").FontSize(9);
                                labelCol.Item().Text(factura.NumeroFactura ?? "").FontSize(10);
                            });
                        });
                    });

                    row.ConstantItem(10); // Espacio

                    // Cuadro derecho: Dirección de entrega
                    row.RelativeItem().Border(1).Column(col =>
                    {
                        col.Item().Padding(3).Text("DIRECCIÓN:").Bold().FontSize(6).FontColor(Colors.Grey.Darken1);

                        if (direccionEntrega != null)
                        {
                            col.Item().PaddingHorizontal(5).Column(innerCol =>
                            {
                                innerCol.Item().Text(direccionEntrega.Nombre ?? "").FontSize(10);
                                innerCol.Item().Text(direccionEntrega.Direccion ?? "").FontSize(10);
                                innerCol.Item().Text(direccionEntrega.PoblacionCompleta ?? "").FontSize(10);
                                if (!string.IsNullOrEmpty(direccionEntrega.Telefonos))
                                {
                                    innerCol.Item().Text(direccionEntrega.Telefonos).FontSize(10);
                                }
                                if (!string.IsNullOrEmpty(direccionEntrega.Comentarios))
                                {
                                    innerCol.Item().Text(direccionEntrega.Comentarios).FontSize(10);
                                }
                            });
                        }
                    });
                });
            });
        }

        private void ComponerContenidoTicket(IContainer container, Factura factura)
        {
            container.Column(column =>
            {
                // Tabla de líneas (sin columna Producto, sin filas de albarán)
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);    // Nombre del Producto
                        columns.ConstantColumn(50);   // Unidades
                        columns.ConstantColumn(60);   // Precio
                        columns.ConstantColumn(45);   // Desc.
                        columns.ConstantColumn(65);   // Total
                    });

                    // Cabecera
                    table.Header(header =>
                    {
                        header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten2)
                            .Padding(3).AlignMiddle().Text("Nombre del Producto").Bold().FontSize(8);
                        header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten2)
                            .Padding(3).AlignMiddle().AlignRight().Text("Unidades").Bold().FontSize(8);
                        header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten2)
                            .Padding(3).AlignMiddle().AlignRight().Text("Precio").Bold().FontSize(8);
                        header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten2)
                            .Padding(3).AlignMiddle().AlignRight().Text("Desc.").Bold().FontSize(8);
                        header.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten2)
                            .Padding(3).AlignMiddle().AlignRight().Text("Total").Bold().FontSize(8);
                    });

                    // Filas de líneas (sin separador de albarán)
                    if (factura.Lineas != null)
                    {
                        foreach (var linea in factura.Lineas)
                        {
                            // Fila de producto (sin columna de código de producto)
                            table.Cell().Padding(2).Text(linea.DescripcionCompleta ?? "").FontSize(8);
                            table.Cell().Padding(2).AlignRight().Text(linea.Cantidad?.ToString() ?? "").FontSize(8);
                            table.Cell().Padding(2).AlignRight().Text(linea.PrecioUnitario?.ToString("N2") ?? "").FontSize(8);
                            table.Cell().Padding(2).AlignRight()
                                .Text(linea.Descuento != 0 ? linea.Descuento.ToString("P2") : "").FontSize(8);
                            table.Cell().Padding(2).AlignRight().Text(linea.Importe.ToString("N2")).FontSize(8);
                        }
                    }
                });

                column.Item().PaddingVertical(10);

                // Importe Total (a la derecha)
                column.Item().AlignRight().Width(200).Column(totalCol =>
                {
                    totalCol.Item().Border(1).Background(Colors.Grey.Lighten2)
                        .Padding(5).AlignCenter()
                        .Text("Importe Total Ticket").Bold().FontSize(10);
                    totalCol.Item().Border(1).Padding(5).AlignCenter()
                        .Text(factura.ImporteTotal.ToString("N2") + " €").Bold().FontSize(14);
                });

                column.Item().PaddingVertical(10);

                // Texto de pago al contado
                column.Item().AlignCenter()
                    .Text("PAGO AL CONTADO OBLIGATORIO AL MOMENTO DE LA ENTREGA").Bold().FontSize(10);

                // Notas al pie
                if (factura.NotasAlPie?.Any() == true)
                {
                    column.Item().PaddingTop(10).Column(notasCol =>
                    {
                        foreach (var nota in factura.NotasAlPie)
                        {
                            if (!string.IsNullOrEmpty(nota.Nota))
                            {
                                notasCol.Item().Text(nota.Nota).Bold().FontSize(8);
                            }
                        }
                    });
                }
            });
        }

        private void ComponerPieTicket(IContainer container, Factura factura)
        {
            container.Column(column =>
            {
                if (!string.IsNullOrEmpty(factura.DatosRegistrales))
                {
                    column.Item().AlignCenter().Text(factura.DatosRegistrales).FontSize(8);
                }
            });
        }

        #endregion
    }
}
