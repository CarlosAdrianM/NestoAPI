using Microsoft.Reporting.WebForms;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// Implementación de generación de PDFs usando Microsoft RDLC (ReportViewer).
    /// Esta es la implementación legacy que se mantiene como fallback.
    /// </summary>
    public class GeneradorPdfFacturasRdlc : IGeneradorPdfFacturas
    {
        public ByteArrayContent GenerarPdf(List<Factura> facturas, bool papelConMembrete = false)
        {
            ReportViewer viewer = new ReportViewer();
            viewer.LocalReport.ReportPath = facturas.FirstOrDefault().RutaInforme;
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Facturas", facturas));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Direcciones", facturas.FirstOrDefault().Direcciones));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("LineasFactura", facturas.FirstOrDefault().Lineas));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Vendedores", facturas.FirstOrDefault().Vendedores));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Vencimientos", facturas.FirstOrDefault().Vencimientos));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("NotasAlPie", facturas.FirstOrDefault().NotasAlPie));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Totales", facturas.FirstOrDefault().Totales));

            viewer.LocalReport.SetParameters(new ReportParameter("PapelConMembrete", papelConMembrete.ToString()));

            viewer.LocalReport.Refresh();
            byte[] bytes = viewer.LocalReport.Render("PDF", null, out _, out _, out _, out _, out _);

            // https://stackoverflow.com/questions/29643043/crystalreports-reportdocument-memory-leak-with-database-connections
            viewer.LocalReport.Dispose();
            viewer.Dispose();

            return new ByteArrayContent(bytes);
        }
    }
}
