using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// Interfaz para la generación de PDFs de facturas.
    /// Permite cambiar entre implementaciones (RDLC/QuestPDF) mediante configuración.
    /// </summary>
    public interface IGeneradorPdfFacturas
    {
        /// <summary>
        /// Genera un PDF con las facturas proporcionadas.
        /// </summary>
        /// <param name="facturas">Lista de facturas a incluir en el PDF</param>
        /// <param name="papelConMembrete">Si true, el PDF se genera para papel con membrete preimpreso</param>
        /// <returns>ByteArrayContent con el PDF generado</returns>
        ByteArrayContent GenerarPdf(List<Factura> facturas, bool papelConMembrete = false);
    }
}
