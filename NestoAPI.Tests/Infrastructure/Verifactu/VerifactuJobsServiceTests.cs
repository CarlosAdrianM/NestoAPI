using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#329: job de estados y reintentos de Verifactu. Consulta estados pendientes,
    /// reintenta las no declaradas (solo series que tramitan y desde la fecha de arranque de
    /// la sombra — NUNCA el histórico) y marca fichas con NIF rechazado.
    /// </summary>
    [TestClass]
    public class VerifactuJobsServiceTests
    {
        private NVEntities db;
        private DbSet<CabFacturaVta> fakeFacturas;
        private IServicioVerifactu servicioVerifactu;
        private IServicioValidacionNif validacionNif;
        private IServicioCorreoElectronico correo;
        private List<CabFacturaVta> reenviadas;
        private VerifactuResponse respuestaReenvio;
        private VerifactuJobsService job;
        private DateTime fechaInicioOriginal;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o => o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            servicioVerifactu = A.Fake<IServicioVerifactu>();
            A.CallTo(() => servicioVerifactu.EstaHabilitado).Returns(true);
            validacionNif = A.Fake<IServicioValidacionNif>();
            correo = A.Fake<IServicioCorreoElectronico>();
            reenviadas = new List<CabFacturaVta>();
            respuestaReenvio = new VerifactuResponse { Exitoso = true, Uuid = "uuid-nuevo" };
            job = new VerifactuJobsService(db, servicioVerifactu, validacionNif, correo,
                f => { reenviadas.Add(f); return Task.FromResult(respuestaReenvio); });
            fechaInicioOriginal = VerifactuJobsService.FechaInicioDeclaracion;
            VerifactuJobsService.FechaInicioDeclaracion = new DateTime(2026, 7, 20);
        }

        [TestCleanup]
        public void Cleanup()
        {
            VerifactuJobsService.FechaInicioDeclaracion = fechaInicioOriginal;
        }

        private void ConFacturas(params CabFacturaVta[] facturas)
        {
            var data = facturas.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<CabFacturaVta>)fakeFacturas).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<CabFacturaVta>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).Provider)
                .Returns(new TestDbAsyncQueryProvider<CabFacturaVta>(data.Provider));
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private static CabFacturaVta Factura(string numero, string serie = "NV", string uuid = null,
            string estado = null, DateTime? fecha = null, string cliente = "30676")
        {
            return new CabFacturaVta
            {
                Empresa = "1",
                Número = numero,
                Serie = serie,
                Nº_Cliente = cliente,
                Fecha = fecha ?? new DateTime(2026, 7, 21),
                VerifactuUUID = uuid,
                VerifactuEstado = estado
            };
        }

        [TestMethod]
        public async Task ActualizarEstados_PendienteConVeredicto_ActualizaYPersiste()
        {
            var factura = Factura("NV2612451", uuid: "uuid-1", estado: "Pendiente");
            ConFacturas(factura);
            _ = A.CallTo(() => servicioVerifactu.ConsultarEstadoAsync("uuid-1"))
                .Returns(new VerifactuResponse { Exitoso = true, Estado = "Correcto" });

            var resumen = new ResumenJobVerifactu();
            await job.ActualizarEstadosPendientes(resumen);

            Assert.AreEqual("Correcto", factura.VerifactuEstado);
            Assert.AreEqual(1, resumen.EstadosActualizados);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task ActualizarEstados_RechazoPorNif_MarcaLaFichaComoIncorrecta()
        {
            var factura = Factura("NV2612489", uuid: "uuid-2", estado: "Pendiente");
            ConFacturas(factura);
            _ = A.CallTo(() => servicioVerifactu.ConsultarEstadoAsync("uuid-2"))
                .Returns(new VerifactuResponse
                {
                    Exitoso = true,
                    Estado = "Incorrecto",
                    MensajeError = "El NIF/NOMBRE (90021192/ANA...) del destinatario no se encuentra registrado"
                });

            var resumen = new ResumenJobVerifactu();
            await job.ActualizarEstadosPendientes(resumen);

            Assert.AreEqual(1, resumen.Rechazadas.Count);
            A.CallTo(() => validacionNif.MarcarIncorrecto("30676", A<string>.That.Contains("RECHAZO VERIFACTU"), "VerifactuJob"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reintentos_SoloSeriesQueTramitanYDesdeLaFechaDeArranque()
        {
            // La guarda más importante del job: jamás declarar el histórico ni series que no tramitan
            var candidata = Factura("NV2612489", fecha: new DateTime(2026, 7, 21));
            var historica = Factura("NV2500000", fecha: new DateTime(2026, 1, 15));
            var serieNoTramita = Factura("GB1234567", serie: "GB", fecha: new DateTime(2026, 7, 21));
            var yaDeclarada = Factura("NV2612451", uuid: "uuid-1", fecha: new DateTime(2026, 7, 21));
            ConFacturas(candidata, historica, serieNoTramita, yaDeclarada);

            var resumen = new ResumenJobVerifactu();
            await job.ReintentarNoDeclaradas(resumen);

            Assert.AreEqual(1, reenviadas.Count, "Solo la candidata (NV, sin UUID, post-arranque)");
            Assert.AreEqual("NV2612489", reenviadas.Single().Número);
            Assert.AreEqual(1, resumen.Declaradas);
        }

        [TestMethod]
        public async Task Reintentos_RechazoPorNif_MarcaLaFichaYLoAcumulaParaElResumen()
        {
            ConFacturas(Factura("NV2612489"));
            respuestaReenvio = new VerifactuResponse
            {
                Exitoso = false,
                MensajeError = "El NIF/NOMBRE (90021192/ANA...) del destinatario no se encuentra registrado"
            };

            var resumen = new ResumenJobVerifactu();
            await job.ReintentarNoDeclaradas(resumen);

            Assert.AreEqual(0, resumen.Declaradas);
            Assert.AreEqual(1, resumen.SinDeclarar.Count);
            A.CallTo(() => validacionNif.MarcarIncorrecto("30676", A<string>.Ignored, "VerifactuJob"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarPasada_ConLaSombraApagada_NoHaceNada()
        {
            A.CallTo(() => servicioVerifactu.EstaHabilitado).Returns(false);
            ConFacturas(Factura("NV2612489"));

            var resumen = await job.ProcesarPasada();

            Assert.AreEqual(0, reenviadas.Count);
            Assert.AreEqual(0, resumen.EstadosActualizados);
            A.CallTo(() => servicioVerifactu.ConsultarEstadoAsync(A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EsRechazoPorNif_DetectaElCasoRealYNoOtrosErrores()
        {
            Assert.IsTrue(VerifactuJobsService.EsRechazoPorNif(
                "El NIF/NOMBRE (90021192/ANA ISABEL) del destinatario no se encuentra registrado"));
            Assert.IsFalse(VerifactuJobsService.EsRechazoPorNif("Error de estructura en el XML"));
            Assert.IsFalse(VerifactuJobsService.EsRechazoPorNif(null));
        }

        [TestMethod]
        public void EsEstadoDeRechazo_IncorrectoYRechazadaSi_CorrectoYPendienteNo()
        {
            Assert.IsTrue(VerifactuJobsService.EsEstadoDeRechazo("Incorrecto"));
            Assert.IsTrue(VerifactuJobsService.EsEstadoDeRechazo("Rechazada"));
            Assert.IsFalse(VerifactuJobsService.EsEstadoDeRechazo("Correcto"));
            Assert.IsFalse(VerifactuJobsService.EsEstadoDeRechazo("AceptadoConErrores"));
            Assert.IsFalse(VerifactuJobsService.EsEstadoDeRechazo("Pendiente"));
        }
    }
}
