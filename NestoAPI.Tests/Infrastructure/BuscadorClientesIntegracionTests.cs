using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#566 contra la base de datos de verdad: la consulta que rescata a los clientes que el
    /// índice de la noche no conoce (los dados de alta después y el del número exacto). Solo lectura,
    /// solo con NESTO_TEST_BD.
    /// </summary>
    [TestClass]
    public class BuscadorClientesIntegracionTests
    {
        private const string EMPRESA = "1";

        [TestMethod]
        [TestCategory("Integracion")]
        public void Integracion_ElNumeroExactoSaleAunqueElClienteSeaAntiguo()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                // «desde» en el futuro: por fecha no entra nadie, solo el del número exacto
                List<ClienteDTO> encontrados = ClientesController
                    .ConsultaDeLosQueNoConoceElIndice(db.Clientes, EMPRESA, "15191", DateTime.Today.AddYears(1)).ToList();

                Assert.IsTrue(encontrados.Count > 0);
                Assert.IsTrue(encontrados.All(c => c.cliente == "15191"));
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public void Integracion_UnClienteDadoDeAltaDespuesDelIndice_SalePorSuNombre()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                // El último cliente dado de alta: su fecha de modificación es la del alta
                var ultimo = db.Clientes
                    .Where(c => c.Empresa == EMPRESA && c.Estado >= 0 && c.Nombre != null)
                    .OrderByDescending(c => c.Fecha_Modificación)
                    .Select(c => new { c.Nº_Cliente, c.Nombre, c.Fecha_Modificación })
                    .First();
                string palabra = ultimo.Nombre.Trim().Split(' ').OrderByDescending(p => p.Length).First();

                List<ClienteDTO> encontrados = ClientesController
                    .ConsultaDeLosQueNoConoceElIndice(db.Clientes, EMPRESA, palabra, ultimo.Fecha_Modificación.AddMinutes(-10)).ToList();

                Assert.IsTrue(encontrados.Any(c => c.cliente == ultimo.Nº_Cliente.Trim()),
                    $"El cliente {ultimo.Nº_Cliente.Trim()} tiene que salir buscando «{palabra}»");
                Assert.IsTrue(encontrados.Count < 20, "Solo los recientes, no todos los que se llaman así");
            }
        }
    }
}
