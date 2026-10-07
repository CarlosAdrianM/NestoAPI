using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#553 (fase 1, lectura): la propuesta de reposición de una tienda la sigue calculando
    /// prdRellenarReposicionStock (solo calcula, con variables de tabla); aquí se valida la petición y se
    /// traduce su salida a un DTO limpio para Nesto.
    /// </summary>
    [TestClass]
    public class ServicioPropuestaReposicionTests
    {
        private readonly List<string> llamadas = new List<string>();

        private ServicioPropuestaReposicion Servicio(params FilaPropuestaReposicion[] filas)
        {
            return new ServicioPropuestaReposicion((empresa, origen, destino) =>
            {
                llamadas.Add($"{empresa}|{origen}|{destino}");
                return Task.FromResult(filas.ToList());
            });
        }

        [TestMethod]
        public async Task Propuesta_TraduceLasFilasDelProcedimiento_SinRellenoYOrdenadas()
        {
            ServicioPropuestaReposicion servicio = Servicio(
                new FilaPropuestaReposicion { Número = "41060          ", Grupo = "COS", Texto = "CREMA X                                           ", StockOrigen = 20, StockDestino = 1, CantidadMaximaDestino = 4, CantidadPendienteServirOrigen = 2, CantidadPendienteServirDestino = 0, CantidadReposicion = 3 },
                new FilaPropuestaReposicion { Número = "38272          ", Grupo = "COS", Texto = "MUESTRA HA+                                       ", StockOrigen = 50, StockDestino = 0, CantidadMaximaDestino = 5, CantidadPendienteServirOrigen = 0, CantidadPendienteServirDestino = 1, CantidadReposicion = 6 });

            List<LineaPropuestaReposicionDTO> lineas = await servicio.CalcularPropuesta("1", "alg", " rei ");

            CollectionAssert.AreEqual(new[] { "1|ALG|REI" }, llamadas, "Almacenes normalizados");
            Assert.AreEqual(2, lineas.Count);
            Assert.AreEqual("38272", lineas[0].Producto);
            Assert.AreEqual("MUESTRA HA+", lineas[0].Texto);
            Assert.AreEqual(6, lineas[0].CantidadReposicion);
            Assert.AreEqual(1, lineas[0].PendienteServirDestino);
            Assert.AreEqual("41060", lineas[1].Producto);
            Assert.AreEqual(20, lineas[1].StockOrigen);
            Assert.AreEqual(4, lineas[1].StockMaximoDestino);
        }

        [DataTestMethod]
        [DataRow("Algete", "Reina", "De Algete a Reina")]
        [DataRow("  Reina  ", "Algete", "De Reina a Algete")]
        [DataRow(null, " ", "De ALG a REI")]
        public void TituloReposicion_UsaLosNombresOElCodigo(string nombreOrigen, string nombreDestino, string esperado)
        {
            Assert.AreEqual(esperado, ServicioPropuestaReposicion.TituloReposicion("ALG", nombreOrigen, "REI", nombreDestino));
        }

        [DataTestMethod]
        [DataRow("ALG", "ALG")]
        [DataRow("ALG", "XYZ")]
        [DataRow("", "REI")]
        public async Task Propuesta_ConAlmacenesNoValidos_EsErrorDeNegocioSinLlamarAlProcedimiento(string origen, string destino)
        {
            ServicioPropuestaReposicion servicio = Servicio();

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.CalcularPropuesta("1", origen, destino));

            Assert.AreEqual(0, llamadas.Count);
        }

        [TestMethod]
        public void ErrorDelProcedimiento_ReposicionPendiente_SeConvierteEnErrorDeNegocioConSuTexto()
        {
            var ex = ServicioPropuestaReposicion.ComoErrorDeNegocio(50000, "No se puede rellenar porque hay una reposición anterior pendiente de contabilizar");

            Assert.IsInstanceOfType(ex, typeof(NestoBusinessException));
            StringAssert.Contains(ex.Message, "reposición anterior pendiente de contabilizar");
        }

        [TestMethod]
        public void ErrorDelProcedimiento_OtroErrorDeSql_NoSeConvierte()
        {
            Assert.IsNull(ServicioPropuestaReposicion.ComoErrorDeNegocio(1205, "interbloqueo"));
        }
    }
}
