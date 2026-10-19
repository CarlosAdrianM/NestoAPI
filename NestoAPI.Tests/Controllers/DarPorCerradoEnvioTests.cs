using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#676 (Sugerencia 673, Aida): POST api/EnviosAgencias/{id}/DarPorCerrado. Un envío incidentado que la
    /// agencia nunca cerró se da por cerrado a mano con un motivo: pasa a su estado propio «Cerrado a mano» (5), queda
    /// en EnviosHistoria y el seguimiento automático no lo reabre ni vuelve a avisar (#666).
    /// </summary>
    [TestClass]
    public class DarPorCerradoEnvioTests
    {
        private const int AGENCIA = Constantes.Agencias.AGENCIA_CTT;

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<EnvioHistoria> fakeHistoria;
        private List<EnvioHistoria> historiaGuardada;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeHistoria = A.Fake<DbSet<EnvioHistoria>>(o => o.Implements<IQueryable<EnvioHistoria>>().Implements<IDbAsyncEnumerable<EnvioHistoria>>());
            historiaGuardada = new List<EnvioHistoria>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>._)).ReturnsLazily((EnvioHistoria h) => { historiaGuardada.Add(h); return h; });
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            controller = new EnviosAgenciasController(db)
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\aida") }, "Bearer"))
            };
        }

        private static EnviosAgencia EnvioIncidentado() => new EnviosAgencia
        {
            Numero = 251300,
            Empresa = "1  ",
            Pedido = 921456,
            Agencia = AGENCIA,
            Estado = Constantes.Agencias.ESTADO_INCIDENTADO,
            DetalleEstado = "AUSENTE. SE DEJA AVISO",
            CodigoBarras = "0082800082909720018884",
            Fecha = new DateTime(2026, 6, 18),
            Reembolso = 0,
            Usuario = @"NUEVAVISION\almacen"
        };

        private EnviosAgencia ConEnvio(EnviosAgencia envio)
        {
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        private static DarPorCerradoEnvioDTO Motivo(string motivo) => new DarPorCerradoEnvioDTO { Motivo = motivo };

        // ===== El endpoint =====

        [TestMethod]
        public async Task DarPorCerrado_Incidentado_PasaACerradoAMano()
        {
            EnviosAgencia envio = ConEnvio(EnvioIncidentado());

            var resultado = await controller.DarPorCerrado(envio.Numero, Motivo("El cliente confirma que lo recibió")) as OkNegotiatedContentResult<EnvioCerradoDTO>;

            Assert.IsNotNull(resultado, "Debe devolver 200 con el envío cerrado");
            Assert.AreEqual(Constantes.Agencias.ESTADO_CERRADO_A_MANO, envio.Estado, "Estado propio, no Entregado: la agencia no lo ha confirmado");
            Assert.AreEqual((short)5, Constantes.Agencias.ESTADO_CERRADO_A_MANO, "El siguiente libre tras DEVUELTO (4); Nesto lo pinta por número");
            Assert.AreEqual(CierreManualEnvio.DETALLE_CERRADO_A_MANO, envio.DetalleEstado);
            Assert.IsNull(envio.FechaEntrega, "No hay fecha real de entrega: no se inventa");
            Assert.AreEqual(@"NUEVAVISION\aida", envio.Usuario);
            Assert.IsTrue(CierreManualEnvio.EstaCerradoAMano(envio));
            Assert.AreEqual(251300, resultado.Content.Numero);
            Assert.AreEqual(921456, resultado.Content.Pedido);
            Assert.AreEqual(Constantes.Agencias.ESTADO_CERRADO_A_MANO, resultado.Content.Estado);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DarPorCerrado_ApuntaEnLaHistoriaEstadoEIncidenciaAnterioresConUsuarioFechaYMotivo()
        {
            EnviosAgencia envio = ConEnvio(EnvioIncidentado());
            DateTime antes = DateTime.Now;

            _ = await controller.DarPorCerrado(envio.Numero, Motivo("  La agencia no contesta; el cliente lo tiene  "));

            Assert.AreEqual(2, historiaGuardada.Count);
            EnvioHistoria estado = historiaGuardada.Single(h => h.Campo == "Estado");
            Assert.AreEqual("3", estado.ValorAnterior, "Valor ANTERIOR, como el resto de la historia del envío");
            EnvioHistoria detalle = historiaGuardada.Single(h => h.Campo == "DetalleEstado");
            Assert.AreEqual("AUSENTE. SE DEJA AVISO", detalle.ValorAnterior, "Que no se pierda qué incidencia tenía");
            foreach (EnvioHistoria fila in historiaGuardada)
            {
                Assert.AreEqual(251300, fila.NumeroEnvio);
                Assert.AreEqual(@"NUEVAVISION\aida", fila.Usuario, "El usuario lo pone el servidor desde el token");
                Assert.AreEqual("Dado por cerrado a mano: La agencia no contesta; el cliente lo tiene", fila.Observaciones);
                Assert.IsTrue(fila.FechaModificacion >= antes && fila.FechaModificacion <= DateTime.Now);
            }
        }

        [TestMethod]
        public async Task DarPorCerrado_IncidenciaMasLargaQueLaColumna_SeRecortaEnLaHistoria()
        {
            EnviosAgencia envio = EnvioIncidentado();
            envio.DetalleEstado = new string('X', 100); // DetalleEstado es varchar(100); ValorAnterior, nvarchar(50)
            ConEnvio(envio);

            _ = await controller.DarPorCerrado(envio.Numero, Motivo("Cerrado con la agencia por teléfono"));

            Assert.AreEqual(50, historiaGuardada.Single(h => h.Campo == "DetalleEstado").ValorAnterior.Length);
        }

        [TestMethod]
        public async Task DarPorCerrado_SinDetalleDeIncidencia_ValorAnteriorVacioNoNulo()
        {
            EnviosAgencia envio = EnvioIncidentado();
            envio.DetalleEstado = null;
            ConEnvio(envio);

            _ = await controller.DarPorCerrado(envio.Numero, Motivo("Entregado según el cliente"));

            Assert.AreEqual(string.Empty, historiaGuardada.Single(h => h.Campo == "DetalleEstado").ValorAnterior, "ValorAnterior es NOT NULL");
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task DarPorCerrado_SinMotivo_Devuelve400DeUstedYNoToca(string motivo)
        {
            EnviosAgencia envio = ConEnvio(EnvioIncidentado());

            var resultado = await controller.DarPorCerrado(envio.Numero, Motivo(motivo)) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(CierreManualEnvio.MENSAJE_FALTA_MOTIVO, resultado.Message);
            StringAssert.Contains(resultado.Message, "Indique");
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado);
            Assert.AreEqual(0, historiaGuardada.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DarPorCerrado_SinCuerpo_Devuelve400()
        {
            EnviosAgencia envio = ConEnvio(EnvioIncidentado());

            var resultado = await controller.DarPorCerrado(envio.Numero, null) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow((short)0, DisplayName = "En curso")]
        [DataRow((short)1, DisplayName = "Tramitado")]
        [DataRow((short)2, DisplayName = "Entregado (p. ej. ya cerrado por otra sesión)")]
        [DataRow((short)4, DisplayName = "Devuelto")]
        [DataRow((short)5, DisplayName = "Ya cerrado a mano (otra sesión)")]
        public async Task DarPorCerrado_NoIncidentado_Devuelve400DeUstedYNoToca(short estado)
        {
            EnviosAgencia envio = EnvioIncidentado();
            envio.Estado = estado;
            ConEnvio(envio);

            var resultado = await controller.DarPorCerrado(envio.Numero, Motivo("Lo que sea")) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "Solo se puede dar por cerrado un envío incidentado");
            StringAssert.Contains(resultado.Message, "921456");
            StringAssert.Contains(resultado.Message, "actualice", "De usted");
            Assert.AreEqual(estado, envio.Estado);
            Assert.AreEqual(0, historiaGuardada.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DarPorCerrado_EnvioInexistente_Devuelve404()
        {
            A.CallTo(() => fakeEnvios.FindAsync(999)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.DarPorCerrado(999, Motivo("Lo que sea"));

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public void DarPorCerrado_LlevaAuthorizeYLaRutaQueLlamaNesto()
        {
            var metodo = typeof(EnviosAgenciasController).GetMethod(nameof(EnviosAgenciasController.DarPorCerrado));
            var ruta = (System.Web.Http.RouteAttribute)Attribute.GetCustomAttribute(metodo, typeof(System.Web.Http.RouteAttribute));

            Assert.AreEqual("api/EnviosAgencias/{id:int}/DarPorCerrado", ruta.Template);
            Assert.IsNotNull(Attribute.GetCustomAttribute(metodo, typeof(System.Web.Http.AuthorizeAttribute)));
            Assert.IsNotNull(Attribute.GetCustomAttribute(metodo, typeof(System.Web.Http.HttpPostAttribute)));
        }

        // ===== El seguimiento no lo reabre ni avisa =====

        [TestMethod]
        public async Task Poll_EnvioCerradoAMano_NoLoConsultaNiLoReabreNiAvisa()
        {
            EnviosAgencia envio = EnvioIncidentado();
            _ = CierreManualEnvio.Aplicar(envio, "El cliente lo tiene", "aida", DateTime.Now);
            DbSet<EnviosAgencia> enviosPoll = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            ConfigurarFakeDbSet(enviosPoll, new[] { envio }.AsQueryable());
            NVEntities dbPoll = A.Fake<NVEntities>();
            A.CallTo(() => dbPoll.EnviosAgencias).Returns(enviosPoll);
            IFabricaAgenciasRemotas fabrica = A.Fake<IFabricaAgenciasRemotas>();
            ISeguimientoAgenciaRemota seguimiento = A.Fake<ISeguimientoAgenciaRemota>();
            A.CallTo(() => fabrica.AgenciasConSeguimiento).Returns(new[] { AGENCIA });
            A.CallTo(() => fabrica.CrearSeguimiento(AGENCIA)).Returns(seguimiento);
            // La agencia sigue diciendo que está incidentado (nunca lo cerró): justo el caso de la sugerencia.
            A.CallTo(() => seguimiento.ConsultarSeguimientoAsync(A<string>._))
                .Returns(Task.FromResult(new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Incidentado, Detalle = "AUSENTE" }));
            var avisados = new List<int>();
            var servicio = new SeguimientoEnviosJobsService(dbPoll, fabrica, avisar: _ => { },
                avisarIncidencia: e => { avisados.Add(e.Numero); return Task.CompletedTask; });

            int actualizados = await servicio.ActualizarSeguimientosAsync(new DateTime(2026, 6, 1));

            Assert.AreEqual(0, actualizados);
            A.CallTo(() => seguimiento.ConsultarSeguimientoAsync(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(Constantes.Agencias.ESTADO_CERRADO_A_MANO, envio.Estado);
            Assert.AreEqual(0, avisados.Count, "Ni aviso de #666");
        }

        [DataTestMethod]
        [DataRow(EstadoEnvioSeguimiento.Incidentado)]
        [DataRow(EstadoEnvioSeguimiento.Tramitado)]
        [DataRow(EstadoEnvioSeguimiento.EnCurso)]
        [DataRow(EstadoEnvioSeguimiento.Devuelto)]
        public void AplicarSeguimiento_CerradoAMano_LaAgenciaNoLoReabre(EstadoEnvioSeguimiento estadoAgencia)
        {
            // «Actualizar estado» a demanda sí consulta cualquier envío con albarán: tampoco por ahí se reabre
            // (y, sin pasar a incidentado, el controller no manda el aviso de #666).
            EnviosAgencia envio = EnvioIncidentado();
            _ = CierreManualEnvio.Aplicar(envio, "El cliente lo tiene", "aida", DateTime.Now);
            short estadoAnterior = envio.Estado;

            bool cambia = SeguimientoEnviosJobsService.AplicarSeguimiento(envio,
                new SeguimientoEnvioRemoto { Estado = estadoAgencia, Detalle = "AUSENTE" });

            Assert.IsFalse(cambia);
            Assert.AreEqual(Constantes.Agencias.ESTADO_CERRADO_A_MANO, envio.Estado);
            Assert.AreEqual(CierreManualEnvio.DETALLE_CERRADO_A_MANO, envio.DetalleEstado);
            Assert.IsFalse(AvisadorIncidenciasEnvio.PasaAIncidencia(estadoAnterior, envio));
        }

        [TestMethod]
        public void AplicarSeguimiento_CerradoAMano_UnaEntregaConfirmadaDespuesLoPasaAEntregadoConSuFecha()
        {
            EnviosAgencia envio = EnvioIncidentado();
            _ = CierreManualEnvio.Aplicar(envio, "El cliente lo tiene", "aida", DateTime.Now);

            bool cambia = SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Entregado,
                FechaEntrega = new DateTime(2026, 6, 20),
                Detalle = "ENTREGADO"
            });

            Assert.IsTrue(cambia);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            Assert.AreEqual(new DateTime(2026, 6, 20), envio.FechaEntrega);
            Assert.AreEqual("ENTREGADO", envio.DetalleEstado);
            Assert.IsFalse(CierreManualEnvio.EstaCerradoAMano(envio));
        }

        [TestMethod]
        public void EstaCerradoAMano_UnEntregadoPorLaAgenciaNoLoEsta()
        {
            var entregado = new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_ENTREGADO, DetalleEstado = CierreManualEnvio.DETALLE_CERRADO_A_MANO };

            Assert.IsFalse(CierreManualEnvio.EstaCerradoAMano(entregado), "Lo dice el estado, no el texto");
            Assert.IsFalse(CierreManualEnvio.EstaCerradoAMano(null));
        }

        // ===== Cómo se comporta «Cerrado a mano» en el resto de reglas =====

        [TestMethod]
        public void CerradoAMano_EstaTerminado_NoTieneEtiquetaVivaNiAlmacenCambiable()
        {
            EnviosAgencia envio = EnvioIncidentado();
            _ = CierreManualEnvio.Aplicar(envio, "El cliente lo tiene", "aida", DateTime.Now);

            Assert.IsTrue(TramitacionEnviosService.EstaEntregadoORecogido(envio), "Como un entregado: no se reenvía a la agencia");
            Assert.IsFalse(TramitacionEnviosService.TieneEtiquetaViva(envio));
            Assert.IsFalse(NestoAPI.Infraestructure.PedidosVenta.CambioAlmacenPedido.EsEtiquetaViva(envio));
            Assert.IsFalse(AvisadorIncidenciasEnvio.PasaAIncidencia(Constantes.Agencias.ESTADO_INCIDENTADO, envio), "Cerrarlo no es una incidencia nueva");
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
