using NestoAPI.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Net.Http;

namespace NestoAPI.Infraestructure
{
    public class GeneradorPdfModelo347
    {
        private const string URL_LOGO = "http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg";
        private const string URL_PLAYSTORE = "https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas";
        private const string URL_QR_API = "https://api.qrserver.com/v1/create-qr-code/?size=100x100&data=";

        private byte[] _logoBytes;
        private byte[] _qrBytes;

        public byte[] Generar(Mod347DTO datos, Empresa empresaDeclarante, int anno)
        {
            // Cargar imágenes
            CargarImagenes();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.2f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9.5f));

                    page.Header().Element(c => ComponerCabecera(c, empresaDeclarante, anno));
                    page.Content().Element(c => ComponerContenido(c, datos, empresaDeclarante, anno));
                    page.Footer().Element(ComponerPie);
                });
            });

            return documento.GeneratePdf();
        }

        private void CargarImagenes()
        {
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

                try
                {
                    var qrUrl = URL_QR_API + Uri.EscapeDataString(URL_PLAYSTORE);
                    _qrBytes = client.GetByteArrayAsync(qrUrl).GetAwaiter().GetResult();
                }
                catch
                {
                    _qrBytes = null;
                }
            }
        }

        private void ComponerCabecera(IContainer container, Empresa empresa, int anno)
        {
            container.Column(column =>
            {
                // Logo y título en la misma fila
                column.Item().Row(row =>
                {
                    // Logo a la izquierda
                    if (_logoBytes != null && _logoBytes.Length > 0)
                    {
                        row.ConstantItem(100).AlignLeft().Image(_logoBytes);
                    }
                    else
                    {
                        row.ConstantItem(100);
                    }

                    // Título centrado
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignCenter().Text("CERTIFICADO").Bold().FontSize(16);
                        col.Item().AlignCenter().PaddingTop(3).Text("Declaración Anual de Operaciones").FontSize(11);
                        col.Item().AlignCenter().Text("con Terceras Personas").FontSize(11);
                        col.Item().AlignCenter().PaddingTop(2).Text($"MODELO 347 - EJERCICIO {anno}").Bold().FontSize(12);
                    });

                    // Espacio a la derecha para equilibrar
                    row.ConstantItem(100);
                });

                column.Item().PaddingVertical(6);

                // Línea separadora
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);

                column.Item().PaddingVertical(3);
            });
        }

        private void ComponerContenido(IContainer container, Mod347DTO datos, Empresa empresa, int anno)
        {
            container.Column(column =>
            {
                // Datos del declarante
                column.Item().Element(c => ComponerDatosDeclarante(c, empresa));

                column.Item().PaddingVertical(8);

                // Datos del cliente declarado
                column.Item().Element(c => ComponerDatosCliente(c, datos));

                column.Item().PaddingVertical(8);

                // Tabla de importes por trimestre
                column.Item().Element(c => ComponerTablaImportes(c, datos, anno));

                column.Item().PaddingVertical(10);

                // Texto legal
                column.Item().Element(c => ComponerTextoLegal(c, empresa, anno));

                column.Item().PaddingVertical(10);

                // Promoción de la app
                column.Item().Element(ComponerPromocionApp);
            });
        }

        private void ComponerDatosDeclarante(IContainer container, Empresa empresa)
        {
            container.Border(1).BorderColor(Colors.Grey.Medium).Column(column =>
            {
                // Cabecera del bloque
                column.Item().Background(Colors.Blue.Lighten4).Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("DATOS DEL DECLARANTE").Bold().FontSize(10);
                });

                // Contenido
                column.Item().Padding(8).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Razón Social: ").SemiBold();
                            text.Span(empresa.Nombre?.Trim() ?? "");
                        });
                    });

                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("NIF: ").SemiBold();
                            text.Span(empresa.NIF?.Trim() ?? "");
                        });
                    });

                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Domicilio: ").SemiBold();
                            text.Span($"{empresa.Dirección?.Trim() ?? ""} {empresa.Dirección2?.Trim() ?? ""}".Trim());
                        });
                    });

                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("C.P. y Población: ").SemiBold();
                            text.Span($"{empresa.CodPostal?.Trim() ?? ""} {empresa.Población?.Trim() ?? ""}");
                        });
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Provincia: ").SemiBold();
                            text.Span(empresa.Provincia?.Trim() ?? "");
                        });
                    });
                });
            });
        }

        private void ComponerDatosCliente(IContainer container, Mod347DTO datos)
        {
            container.Border(1).BorderColor(Colors.Grey.Medium).Column(column =>
            {
                // Cabecera del bloque
                column.Item().Background(Colors.Green.Lighten4).Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("DATOS DEL CLIENTE DECLARADO").Bold().FontSize(10);
                });

                // Contenido
                column.Item().Padding(8).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Nombre/Razón Social: ").SemiBold();
                            text.Span(datos.nombre?.Trim() ?? "");
                        });
                    });

                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("NIF/CIF: ").SemiBold();
                            text.Span(datos.cifNif?.Trim() ?? "");
                        });
                    });

                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Domicilio: ").SemiBold();
                            text.Span(datos.direccion?.Trim() ?? "");
                        });
                    });

                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Código Postal: ").SemiBold();
                            text.Span(datos.codigoPostal?.Trim() ?? "");
                        });
                    });
                });
            });
        }

        private void ComponerTablaImportes(IContainer container, Mod347DTO datos, int anno)
        {
            container.Column(column =>
            {
                column.Item().Text("IMPORTE DE LAS OPERACIONES").Bold().FontSize(10);
                column.Item().PaddingTop(5);

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                    });

                    // Cabecera
                    table.Header(header =>
                    {
                        header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(5)
                            .Text("PERÍODO").Bold();
                        header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(5)
                            .AlignRight().Text("IMPORTE (€)").Bold();
                    });

                    // Trimestres
                    string[] trimestres = { "1er Trimestre", "2º Trimestre", "3er Trimestre", "4º Trimestre" };
                    for (int i = 0; i < 4; i++)
                    {
                        table.Cell().Border(1).BorderColor(Colors.Grey.Medium).Padding(5).Text($"{trimestres[i]} {anno}");
                        table.Cell().Border(1).BorderColor(Colors.Grey.Medium).Padding(5).AlignRight().Text(datos.trimestre[i].ToString("N2"));
                    }

                    // Total
                    table.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Blue.Lighten5).Padding(5)
                        .Text("TOTAL ANUAL").Bold();
                    table.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Blue.Lighten5).Padding(5)
                        .AlignRight().Text(datos.total.ToString("N2")).Bold().FontSize(11);
                });
            });
        }

        private void ComponerTextoLegal(IContainer container, Empresa empresa, int anno)
        {
            container.Column(column =>
            {
                column.Item().Text(text =>
                {
                    text.Span($"{empresa.Nombre?.Trim() ?? ""}, con NIF {empresa.NIF?.Trim() ?? ""}, ");
                    text.Span("CERTIFICA").Bold();
                    text.Span(" que el importe total de las operaciones realizadas con el cliente ");
                    text.Span("arriba identificado durante el ejercicio ");
                    text.Span($"{anno}").Bold();
                    text.Span(", a efectos de la declaración anual ");
                    text.Span("de operaciones con terceras personas (Modelo 347), asciende a las cantidades ");
                    text.Span("indicadas en el presente documento.");
                });

                column.Item().PaddingTop(6);

                column.Item().Text(text =>
                {
                    text.Span("Este certificado se expide a petición del interesado y para que surta los efectos ");
                    text.Span("oportunos ante la Agencia Estatal de Administración Tributaria.");
                });

                column.Item().PaddingTop(10);

                column.Item().AlignRight().Text(text =>
                {
                    text.Span($"En {empresa.Población?.Trim() ?? ""}, a {DateTime.Now:d 'de' MMMM 'de' yyyy}").FontSize(9);
                });
            });
        }

        private void ComponerPromocionApp(IContainer container)
        {
            container.Border(1).BorderColor(Colors.Orange.Medium).Background(Colors.Orange.Lighten5).Padding(8).Row(row =>
            {
                // QR a la izquierda
                if (_qrBytes != null && _qrBytes.Length > 0)
                {
                    row.ConstantItem(65).AlignMiddle().Image(_qrBytes);
                    row.ConstantItem(10); // Espacio
                }

                // Texto promocional
                row.RelativeItem().AlignMiddle().Column(col =>
                {
                    col.Item().Text("¡Descarga nuestra app!").Bold().FontSize(10);
                    col.Item().PaddingTop(2).Text(text =>
                    {
                        text.Span("Accede a tus certificados, información de productos, vídeoprotocolos y mucho más desde tu móvil. ");
                        text.Span("Disponible en Google Play Store.");
                    });
                    col.Item().PaddingTop(3).Text(URL_PLAYSTORE).FontSize(7).FontColor(Colors.Blue.Medium);
                });
            });
        }

        private void ComponerPie(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                column.Item().PaddingTop(5).AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
        }
    }
}
