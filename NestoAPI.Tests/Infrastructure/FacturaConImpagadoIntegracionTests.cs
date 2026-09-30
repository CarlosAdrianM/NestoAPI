using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Tests.Helpers;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#572 contra la base de datos de verdad: una factura con un impagado pendiente cuyo
    /// vencimiento no coincide con el del efecto tiene que enseñarlo (decía «Pagado»). Se busca un
    /// caso vivo en los datos; si hoy no hay ninguno, la prueba no concluye. Solo lectura.
    /// </summary>
    [TestClass]
    public class FacturaConImpagadoIntegracionTests
    {
        private const string EMPRESA = "1";

        [TestMethod]
        [TestCategory("Integracion")]
        public void Integracion_FacturaConImpagadoDeOtraFechaQueElEfecto_NoDicePagado()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                // Un impagado pendiente cuyo efecto de cartera tiene otra fecha de vencimiento
                var caso = (from i in db.ExtractosCliente
                            where i.Empresa == EMPRESA && i.TipoApunte == "4" && i.ImportePdte != 0 && i.Efecto != null
                                && !i.Concepto.Contains("Gastos")
                            join e in db.ExtractosCliente
                                on new { i.Empresa, i.Número, i.Nº_Documento, i.Efecto } equals new { e.Empresa, e.Número, e.Nº_Documento, e.Efecto }
                            where e.TipoApunte == "2" && e.FechaVto != i.FechaVto
                            orderby i.Nº_Orden descending
                            select new { i.Nº_Documento, i.ImportePdte }).FirstOrDefault();
                if (caso == null)
                {
                    Assert.Inconclusive("Hoy no hay ningún impagado pendiente con otra fecha que la de su efecto.");
                }

                var gestor = new GestorFacturas(new ServicioFacturas(db));
                Factura factura = gestor.LeerFactura(EMPRESA, caso.Nº_Documento.Trim());

                Assert.IsTrue(factura.Vencimientos.Any(v => v.EsImpagado),
                    $"La factura {caso.Nº_Documento.Trim()} tiene un impagado pendiente de {caso.ImportePdte:C2}");
                Assert.IsFalse(factura.Vencimientos.All(v => v.TextoPagado == "Pagado"));
            }
        }
    }
}
