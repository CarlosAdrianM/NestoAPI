using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.IndicadoresStock;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.IndicadoresStock
{
    /// <summary>NestoAPI#629: el job mensual y la reconstrucción (idempotencia, orden, de noche, errores a ELMAH sin tirar el resto).</summary>
    [TestClass]
    public class IndicadoresStockJobsServiceTests
    {
        private static readonly DateTime SEPTIEMBRE = new DateTime(2026, 9, 1);
        private static readonly DateTime NOCHE = new DateTime(2026, 10, 15, 23, 0, 0);
        private RepositorioIndicadoresStockEnMemoria repo;
        private List<Exception> errores;
        private List<(int meses, DateTime desde, DateTime cuando)> programados;

        [TestInitialize]
        public void Inicializar()
        {
            repo = new RepositorioIndicadoresStockEnMemoria();
            repo.Productos.Add(new ProductoEnMemoria { Producto = "A", PrecioMedio = 2m, Grupo = "ACC", SubGrupo = "002" });
            repo.Extracto.Add(new MovimientoExtracto { Producto = "A", Fecha = new DateTime(2020, 1, 1), Cantidad = 10 });
            repo.Extracto.Add(new MovimientoExtracto { Producto = "A", Fecha = new DateTime(2026, 9, 10), Cantidad = 5, Almacen = "REI" });
            errores = new List<Exception>();
            programados = new List<(int, DateTime, DateTime)>();
        }

        private IndicadoresStockJobsService Servicio(params DateTime[] momentos)
        {
            var cola = new Queue<DateTime>(momentos.Length == 0 ? new[] { NOCHE } : momentos);
            DateTime ultimo = cola.Peek();
            return new IndicadoresStockJobsService(repo,
                () => cola.Count > 0 ? (ultimo = cola.Dequeue()) : ultimo,
                ex => errores.Add(ex),
                (meses, desde, cuando) => programados.Add((meses, desde, cuando)));
        }

        [TestMethod]
        public void RellenarMes_DosVeces_NoDuplica()
        {
            IndicadoresStockJobsService servicio = Servicio();

            Assert.AreEqual(ResultadoMesIndicadoresStock.Completo, servicio.RellenarMes(SEPTIEMBRE).Result);
            int filas = repo.Tabla.Count;
            Assert.AreEqual(ResultadoMesIndicadoresStock.Completo, servicio.RellenarMes(SEPTIEMBRE.AddDays(14)).Result);

            Assert.AreEqual(3, filas, "TOTAL, ALG y REI");
            Assert.AreEqual(filas, repo.Tabla.Count);
            Assert.IsTrue(repo.Tabla.All(f => f.Mes == SEPTIEMBRE));
            Assert.AreEqual(30m, repo.Tabla.Single(f => f.Almacen == FilaIndicadoresStock.TOTAL).ValorStock);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void RellenarMes_SoloTocaSuMes()
        {
            IndicadoresStockJobsService servicio = Servicio();
            _ = servicio.RellenarMes(SEPTIEMBRE.AddMonths(-1)).Result;
            int deAgosto = repo.Tabla.Count;

            _ = servicio.RellenarMes(SEPTIEMBRE).Result;
            _ = servicio.RellenarMes(SEPTIEMBRE).Result;

            Assert.AreEqual(deAgosto, repo.Tabla.Count(f => f.Mes == SEPTIEMBRE.AddMonths(-1)));
        }

        [TestMethod]
        public void RellenarMes_SiFallaUnaConsulta_GuardaLoDemasYLoApuntaEnElmah()
        {
            repo.FalloServicio = mes => new TimeoutException("Tiempo de espera agotado");

            ResultadoMesIndicadoresStock resultado = Servicio().RellenarMes(SEPTIEMBRE).Result;

            Assert.AreEqual(ResultadoMesIndicadoresStock.Parcial, resultado);
            FilaIndicadoresStock total = repo.Tabla.Single(f => f.Almacen == FilaIndicadoresStock.TOTAL);
            Assert.AreEqual(30m, total.ValorStock);
            Assert.IsNull(total.DiasServirMedia);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "2026-09");
            StringAssert.Contains(errores[0].Message, "días en servir");
        }

        [TestMethod]
        public void RellenarMes_SinTabla_NoCalculaYAvisa()
        {
            repo.TablaExiste = false;

            Assert.AreEqual(ResultadoMesIndicadoresStock.Error, Servicio().RellenarMes(SEPTIEMBRE).Result);
            Assert.AreEqual(0, repo.MesesGuardados.Count);
            StringAssert.Contains(errores.Single().Message, "Issue629_IndicadoresStock.sql");
        }

        [TestMethod]
        public void Reconstruir_DeNoche_ElMasRecientePrimero()
        {
            List<DateTime> procesados = Servicio().ProcesarReconstruccion(3, SEPTIEMBRE).Result;

            CollectionAssert.AreEqual(new[] { SEPTIEMBRE, SEPTIEMBRE.AddMonths(-1), SEPTIEMBRE.AddMonths(-2) }, procesados);
            CollectionAssert.AreEqual(procesados, repo.MesesGuardados);
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void Reconstruir_RelanzadaNoDuplica()
        {
            _ = Servicio().ProcesarReconstruccion(3, SEPTIEMBRE).Result;
            int filas = repo.Tabla.Count;

            _ = Servicio().ProcesarReconstruccion(3, SEPTIEMBRE).Result;

            Assert.AreEqual(filas, repo.Tabla.Count);
        }

        [TestMethod]
        public void Reconstruir_DeDia_NoHaceNadaYSeProgramaParaLaNoche()
        {
            DateTime manana = new DateTime(2026, 10, 15, 10, 0, 0);

            List<DateTime> procesados = Servicio(manana).ProcesarReconstruccion(36, SEPTIEMBRE).Result;

            Assert.AreEqual(0, procesados.Count);
            Assert.AreEqual((36, SEPTIEMBRE, new DateTime(2026, 10, 15, 22, 0, 0)), programados.Single());
        }

        [TestMethod]
        public void Reconstruir_SiSeHaceDeDiaAMitad_ProgramaLosQueFaltan()
        {
            DateTime madrugada = new DateTime(2026, 10, 16, 6, 30, 0);
            DateTime dia = new DateTime(2026, 10, 16, 7, 0, 0);

            List<DateTime> procesados = Servicio(madrugada, madrugada, dia).ProcesarReconstruccion(5, SEPTIEMBRE).Result;

            Assert.AreEqual(2, procesados.Count);
            Assert.AreEqual((3, SEPTIEMBRE.AddMonths(-2), new DateTime(2026, 10, 16, 22, 0, 0)), programados.Single());
        }

        [TestMethod]
        public void Reconstruir_SiUnMesFalla_SigueConLosDemas()
        {
            repo.FalloGuardar = mes => mes == SEPTIEMBRE.AddMonths(-1) ? new InvalidOperationException("bloqueo") : null;

            List<DateTime> procesados = Servicio().ProcesarReconstruccion(3, SEPTIEMBRE).Result;

            Assert.AreEqual(3, procesados.Count);
            CollectionAssert.AreEqual(new[] { SEPTIEMBRE, SEPTIEMBRE.AddMonths(-2) }, repo.MesesGuardados);
            StringAssert.Contains(errores.Single().Message, "2026-08");
        }

        [TestMethod]
        public void Reconstruir_SinTabla_ParaEnElPrimerMes()
        {
            repo.TablaExiste = false;

            List<DateTime> procesados = Servicio().ProcesarReconstruccion(36, SEPTIEMBRE).Result;

            Assert.AreEqual(1, procesados.Count);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void TocaMensual_ElDia1_ODia2SiEl1EsDomingo()
        {
            Assert.IsTrue(IndicadoresStockJobsService.TocaMensual(new DateTime(2026, 10, 1, 3, 30, 0)));   // jueves
            Assert.IsFalse(IndicadoresStockJobsService.TocaMensual(new DateTime(2026, 10, 2, 3, 30, 0)));  // viernes
            Assert.IsFalse(IndicadoresStockJobsService.TocaMensual(new DateTime(2026, 11, 1, 3, 30, 0)));  // domingo
            Assert.IsTrue(IndicadoresStockJobsService.TocaMensual(new DateTime(2026, 11, 2, 3, 30, 0)));   // lunes tras domingo 1
        }

        [TestMethod]
        public void Mensual_ElDia1DeMadrugada_RellenaElMesAnterior()
        {
            ResultadoMesIndicadoresStock? resultado = Servicio(new DateTime(2026, 10, 1, 3, 30, 0)).ProcesarMensual().Result;

            Assert.AreEqual(ResultadoMesIndicadoresStock.Completo, resultado);
            CollectionAssert.AreEqual(new[] { SEPTIEMBRE }, repo.MesesGuardados);
        }

        [TestMethod]
        public void Mensual_SiArrancaDeDia_LoDejaParaLaNoche()
        {
            ResultadoMesIndicadoresStock? resultado = Servicio(new DateTime(2026, 10, 1, 9, 0, 0)).ProcesarMensual().Result;

            Assert.IsNull(resultado);
            Assert.AreEqual(0, repo.MesesGuardados.Count);
            Assert.AreEqual((1, SEPTIEMBRE, new DateTime(2026, 10, 1, 22, 0, 0)), programados.Single());
        }

        [TestMethod]
        public void Mensual_SiNoToca_NoHaceNada()
        {
            Assert.IsNull(Servicio(new DateTime(2026, 10, 2, 3, 30, 0)).ProcesarMensual().Result);
            Assert.AreEqual(0, repo.MesesGuardados.Count);
            Assert.AreEqual(0, programados.Count);
        }
    }
}
