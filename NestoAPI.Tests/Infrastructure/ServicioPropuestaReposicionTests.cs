using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#553 (fase 1, lectura): la propuesta de reposición de una tienda la calcula un procedimiento (solo
    /// calcula, con variables de tabla); aquí se valida la petición y se traduce su salida a un DTO limpio para Nesto.
    /// NestoAPI#577 (corte 3a): el procedimiento es prdRellenarReposicionStock2 (sin el freno de «reposición anterior
    /// pendiente», con hora de corte para los pedidos).
    /// </summary>
    [TestClass]
    public class ServicioPropuestaReposicionTests
    {
        private readonly List<string> llamadas = new List<string>();

        private ServicioPropuestaReposicion Servicio(params FilaPropuestaReposicion[] filas)
        {
            return new ServicioPropuestaReposicion((empresa, origen, destino, corte) =>
            {
                llamadas.Add($"{empresa}|{origen}|{destino}" + (corte.HasValue ? $"|{corte:dd/MM/yyyy HH:mm}" : string.Empty));
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
        public void ErrorDelProcedimiento_UnRaiserror_SeConvierteEnErrorDeNegocioConSuTexto()
        {
            var ex = ProcedimientoPropuestaReposicion.ComoErrorDeNegocio(50000, "Un aviso del procedimiento");

            Assert.IsInstanceOfType(ex, typeof(NestoBusinessException));
            StringAssert.Contains(ex.Message, "Un aviso del procedimiento");
        }

        [TestMethod]
        public void ErrorDelProcedimiento_OtroErrorDeSql_NoSeConvierte()
        {
            Assert.IsNull(ProcedimientoPropuestaReposicion.ComoErrorDeNegocio(1205, "interbloqueo"));
        }

        // ---------------------------------------------------------------- NestoAPI#577 (corte 3a)

        [TestMethod]
        public async Task Propuesta_SinCorte_PideTodosLosPedidos_YConCorte_LoPasaAlProcedimiento()
        {
            ServicioPropuestaReposicion servicio = Servicio();

            _ = await servicio.CalcularPropuesta("1", "ALG", "ALC");
            _ = await servicio.CalcularPropuesta("1", "ALG", "ALC", new DateTime(2026, 10, 9, 10, 0, 0));

            CollectionAssert.AreEqual(new[] { "1|ALG|ALC", "1|ALG|ALC|09/10/2026 10:00" }, llamadas);
        }

        /// <summary>
        /// Regresión del 08/10/26: Alfredo no pudo calcular ALG → ALC porque la 80915 (ALG → ALC) seguía sin recibir y
        /// prdRellenarReposicionStock hacía raiserror («hay una reposición anterior pendiente de contabilizar»). La API
        /// llama ya al procedimiento nuevo, sin ese freno, por su único punto de llamada y con los cuatro parámetros tipados.
        /// </summary>
        [TestMethod]
        public void Procedimiento_EsElNuevoSinFreno_ConCorteTipadoYNuloSiNoHay()
        {
            Assert.AreEqual("prdRellenarReposicionStock2", ProcedimientoPropuestaReposicion.PROCEDIMIENTO);
            Assert.AreEqual("EXEC prdRellenarReposicionStock2 @Empresa, @AlmacenOrigen, @AlmacenDestino, @Corte", ProcedimientoPropuestaReposicion.SQL);

            SqlParameter[] sinCorte = ProcedimientoPropuestaReposicion.Parametros("1", "ALG", "ALC", null);
            SqlParameter[] conCorte = ProcedimientoPropuestaReposicion.Parametros("1", "ALG", "ALC", new DateTime(2026, 10, 9, 10, 0, 0));

            CollectionAssert.AreEqual(new[] { "@Empresa", "@AlmacenOrigen", "@AlmacenDestino", "@Corte" }, sinCorte.Select(p => p.ParameterName).ToArray());
            Assert.AreEqual(SqlDbType.Char, sinCorte[1].SqlDbType);
            Assert.AreEqual(3, sinCorte[2].Size);
            Assert.AreEqual(SqlDbType.DateTime, sinCorte[3].SqlDbType);
            Assert.AreEqual(DBNull.Value, sinCorte[3].Value);
            Assert.AreEqual(new DateTime(2026, 10, 9, 10, 0, 0), conCorte[3].Value);
        }
    }
}
