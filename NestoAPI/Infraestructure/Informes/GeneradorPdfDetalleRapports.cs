using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Genera el PDF del "Informe de detalle de Rapports" con QuestPDF, sustituyendo el RDLC
    /// DetalleRapports.rdlc que Nesto renderiza en local (roadmap Nesto#340, Fase 2 RDLC->QuestPDF).
    /// Conserva el contenido del RDLC: A4 apaisado, un bloque por usuario (con salto de página entre
    /// usuarios), tabla Cliente/Dirección/Tipo/Estado/Comentarios/Pedido/Hora llamada con filas en
    /// cursiva si el rapport está sin gestionar (EstadoRapport = 1) y en rojo si el cliente no tiene
    /// vendedor, y el cuadro de totales por usuario (llamadas, presenciales, telefónicas, WhatsApp,
    /// pedidos, con los pedidos por tipo entre paréntesis).
    /// </summary>
    public class GeneradorPdfDetalleRapports
    {
        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-ES");

        public ByteArrayContent GenerarPdf(DateTime fechaDesde, DateTime fechaHasta, IList<DetalleRapportsDTO> rapports)
        {
            List<DetalleRapportsDTO> datos = rapports?.ToList() ?? new List<DetalleRapportsDTO>();

            List<IGrouping<string, DetalleRapportsDTO>> grupos = datos
                .GroupBy(r => r.Usuario?.Trim() ?? string.Empty)
                .OrderBy(g => g.Key)
                .ToList();

            var documento = Document.Create(container =>
            {
                if (grupos.Count == 0)
                {
                    // Sin datos también se devuelve un PDF válido, igual que haría el RDLC.
                    container.Page(page =>
                    {
                        ConfigurarPagina(page, fechaDesde, fechaHasta);
                        page.Content().PaddingTop(10)
                            .Text("No hay rapports en el periodo indicado.").FontSize(9).Italic();
                    });
                    return;
                }

                foreach (IGrouping<string, DetalleRapportsDTO> grupo in grupos)
                {
                    List<DetalleRapportsDTO> lineas = grupo.ToList();
                    container.Page(page =>
                    {
                        ConfigurarPagina(page, fechaDesde, fechaHasta, grupo.Key);
                        page.Content().Column(column =>
                        {
                            column.Item().Element(c => ComponerTabla(c, lineas));
                            column.Item().PaddingTop(8).Element(c => ComponerTotales(c, lineas));
                        });
                    });
                }
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private static void ConfigurarPagina(PageDescriptor page, DateTime fechaDesde, DateTime fechaHasta, string usuario = null)
        {
            page.Size(PageSizes.A4.Landscape());
            page.MarginVertical(0.8f, Unit.Centimetre);
            page.MarginHorizontal(0.8f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(8));

            page.Header().Column(column =>
            {
                column.Item().AlignCenter()
                    .Text($"Informe de detalle de Rapports del {fechaDesde:dd/MM/yyyy} al {fechaHasta:dd/MM/yyyy}")
                    .Bold().FontSize(12);
                if (!string.IsNullOrEmpty(usuario))
                {
                    column.Item().Text(usuario).Bold().FontSize(12);
                }
                column.Item().PaddingBottom(4);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span("Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        }

        private static void ComponerTabla(IContainer container, List<DetalleRapportsDTO> lineas)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(45);   // Cliente
                    columns.RelativeColumn(3);    // Dirección
                    columns.ConstantColumn(32);   // Tipo
                    columns.ConstantColumn(40);   // Estado
                    columns.RelativeColumn(4);    // Comentarios
                    columns.ConstantColumn(38);   // Pedido
                    columns.ConstantColumn(80);   // Hora llamada
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Cliente");
                    CeldaCabecera(header.Cell(), "Dirección");
                    CeldaCabecera(header.Cell(), "Tipo", centrar: true);
                    CeldaCabecera(header.Cell(), "Estado", centrar: true);
                    CeldaCabecera(header.Cell(), "Comentarios");
                    CeldaCabecera(header.Cell(), "Pedido", centrar: true);
                    CeldaCabecera(header.Cell(), "Hora Llamada");
                });

                for (int i = 0; i < lineas.Count; i++)
                {
                    DetalleRapportsDTO linea = lineas[i];
                    bool zebra = i % 2 != 0;
                    bool sinGestionar = linea.EstadoRapport == 1;
                    bool sinVendedor = linea.Vendedor == null;

                    CeldaDato(table.Cell(), linea.Cliente?.Trim(), zebra, sinGestionar, sinVendedor);
                    CeldaDato(table.Cell(), FormatoDireccion(linea), zebra, sinGestionar, sinVendedor);
                    CeldaDato(table.Cell(), linea.Tipo?.Trim(), zebra, sinGestionar, sinVendedor, centrar: true);
                    CeldaDato(table.Cell(), linea.EstadoCliente?.ToString(), zebra, sinGestionar, sinVendedor, centrar: true);
                    CeldaDato(table.Cell(), linea.Comentarios?.Trim(), zebra, sinGestionar, sinVendedor);
                    CeldaDato(table.Cell(), linea.Pedido == true ? "X" : "", zebra, sinGestionar, sinVendedor, centrar: true);
                    CeldaDato(table.Cell(), linea.HoraLlamada?.ToString("dd/MM/yyyy H:mm", Cultura), zebra, sinGestionar, sinVendedor);
                }
            });
        }

        private static string FormatoDireccion(DetalleRapportsDTO linea)
        {
            if (linea.Direccion == null || linea.CodigoPostal == null || linea.Poblacion == null)
            {
                return string.Empty;
            }
            return $"{linea.Direccion.Trim()}\n{linea.CodigoPostal.Trim()}, {linea.Poblacion.Trim()}";
        }

        private static void ComponerTotales(IContainer container, List<DetalleRapportsDTO> lineas)
        {
            int totalLlamadas = lineas.Count;
            int presenciales = ContarPorTipo(lineas, "V", out int presencialesConPedido);
            int telefonicas = ContarPorTipo(lineas, "T", out int telefonicasConPedido);
            int whatsapp = ContarPorTipo(lineas, "W", out int whatsappConPedido);
            int totalPedidos = lineas.Count(l => l.Pedido == true);

            container.AlignLeft().Width(9, Unit.Centimetre).Border(1).Padding(5).Column(column =>
            {
                column.Item().Text($"Total llamadas: {totalLlamadas}").Bold();
                column.Item().Text($"Total visitas presenciales: {presenciales} ({presencialesConPedido})").Bold();
                column.Item().Text($"Total visitas telefónicas: {telefonicas} ({telefonicasConPedido})").Bold();
                column.Item().Text($"Total visitas WhatsApp: {whatsapp} ({whatsappConPedido})").Bold();
                column.Item().Text($"Total pedidos: {totalPedidos}").Bold();
            });
        }

        private static int ContarPorTipo(List<DetalleRapportsDTO> lineas, string tipo, out int conPedido)
        {
            List<DetalleRapportsDTO> delTipo = lineas.Where(l => l.Tipo?.Trim() == tipo).ToList();
            conPedido = delTipo.Count(l => l.Pedido == true);
            return delTipo.Count;
        }

        private static void CeldaCabecera(IContainer celda, string texto, bool centrar = false)
        {
            IContainer contenido = celda.Border(1).BorderColor(Colors.Grey.Lighten1)
                .Background(Colors.Grey.Lighten3).Padding(3).AlignMiddle();
            if (centrar)
            {
                contenido = contenido.AlignCenter();
            }
            contenido.Text(texto).Bold().FontSize(8);
        }

        private static void CeldaDato(IContainer celda, string texto, bool zebra, bool sinGestionar, bool sinVendedor, bool centrar = false)
        {
            IContainer contenido = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle();
            if (zebra)
            {
                contenido = contenido.Background(Colors.Grey.Lighten4);
            }
            if (centrar)
            {
                contenido = contenido.AlignCenter();
            }

            TextSpanDescriptor span = contenido.Text(texto ?? "").FontSize(8);
            if (sinGestionar)
            {
                span = span.Italic();
            }
            if (sinVendedor)
            {
                _ = span.FontColor(Colors.Red.Medium);
            }
        }
    }
}
