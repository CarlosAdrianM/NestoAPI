using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#266: las degradaciones del WS de GLS son transitorias (ráfagas de 15-50 min
    /// devolviendo "Servicio no disponible" en sus puntas de mañana). Una pasada masivamente
    /// Desconocida NO debe avisar a ELMAH directamente: programa UN reintento (Hangfire, 45 min)
    /// y el aviso queda para cuando el reintento también falla.
    /// </summary>
    [TestClass]
    public class SeguimientoEnviosJobsServiceTests
    {
        private const int AGENCIA_GLS = 7;

        private NVEntities _db;
        private IFabricaAgenciasRemotas _fabrica;
        private ISeguimientoAgenciaRemota _seguimiento;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            _fabrica = A.Fake<IFabricaAgenciasRemotas>();
            _seguimiento = A.Fake<ISeguimientoAgenciaRemota>();
            A.CallTo(() => _fabrica.AgenciasConSeguimiento).Returns(new[] { AGENCIA_GLS });
            A.CallTo(() => _fabrica.CrearSeguimiento(AGENCIA_GLS)).Returns(_seguimiento);
        }

        // Dos envíos en vuelo de GLS posteriores a la fecha de corte. Con 2 envíos, 2 Desconocidos
        // superan el umbral "más de la mitad" y la pasada cuenta como masivamente Desconocida.
        private void DosEnviosEnVuelo()
        {
            var envios = new[]
            {
                new EnviosAgencia { Numero = 1, Agencia = AGENCIA_GLS, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Fecha = new DateTime(2026, 7, 1), CodigoBarras = "ALB1" },
                new EnviosAgencia { Numero = 2, Agencia = AGENCIA_GLS, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Fecha = new DateTime(2026, 7, 1), CodigoBarras = "ALB2" }
            }.AsQueryable();
            DbSet<EnviosAgencia> fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o
                .Implements<IQueryable<EnviosAgencia>>()
                .Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            ConfigurarFakeDbSet(fakeEnvios, envios);
            A.CallTo(() => _db.EnviosAgencias).Returns(fakeEnvios);
        }

        private static void RespuestaSeguimiento(ISeguimientoAgenciaRemota seguimiento, EstadoEnvioSeguimiento estado, string detalle = null)
        {
            A.CallTo(() => seguimiento.ConsultarSeguimientoAsync(A<string>.Ignored))
                .Returns(Task.FromResult(new SeguimientoEnvioRemoto { Estado = estado, Detalle = detalle }));
        }

        [TestMethod]
        public async Task ActualizarSeguimientos_PasadaMasivamenteDesconocida_ProgramaReintentoEnVezDeAvisar()
        {
            DosEnviosEnVuelo();
            RespuestaSeguimiento(_seguimiento, EstadoEnvioSeguimiento.Desconocido, "Servicio no disponible en este momento");
            bool reintentoProgramado = false;
            var servicio = new SeguimientoEnviosJobsService(_db, _fabrica, programarReintento: () => reintentoProgramado = true);

            _ = await servicio.ActualizarSeguimientosAsync(new DateTime(2026, 6, 1));

            Assert.IsTrue(reintentoProgramado, "La primera pasada masivamente Desconocida debe programar el reintento");
        }

        [TestMethod]
        public async Task ActualizarSeguimientos_ElReintentoTambienDesconocido_NoVuelveAProgramar()
        {
            // En el reintento (esReintento) NUNCA se vuelve a programar otro: si sigue mal, se avisa
            // a ELMAH y la siguiente oportunidad es la pasada regular de las 2 horas.
            DosEnviosEnVuelo();
            RespuestaSeguimiento(_seguimiento, EstadoEnvioSeguimiento.Desconocido, "Servicio no disponible en este momento");
            bool reintentoProgramado = false;
            var servicio = new SeguimientoEnviosJobsService(_db, _fabrica, programarReintento: () => reintentoProgramado = true);

            _ = await servicio.ActualizarSeguimientosAsync(new DateTime(2026, 6, 1), esReintento: true);

            Assert.IsFalse(reintentoProgramado, "El reintento no debe encadenar otro reintento");
        }

        [TestMethod]
        public async Task ActualizarSeguimientos_PasadaNormal_NoProgramaReintento()
        {
            DosEnviosEnVuelo();
            RespuestaSeguimiento(_seguimiento, EstadoEnvioSeguimiento.Entregado);
            bool reintentoProgramado = false;
            var servicio = new SeguimientoEnviosJobsService(_db, _fabrica, programarReintento: () => reintentoProgramado = true);

            int actualizados = await servicio.ActualizarSeguimientosAsync(new DateTime(2026, 6, 1));

            Assert.IsFalse(reintentoProgramado, "Una pasada sin Desconocidos masivos no debe programar reintento");
            Assert.AreEqual(2, actualizados, "Los dos envíos deben pasar a Entregado");
        }

        // ===== NestoAPI#259: la etiqueta del estado (texto de la agencia) se persiste =====

        [TestMethod]
        public void AplicarSeguimiento_EstadoConDetalle_GuardaLaEtiqueta()
        {
            // Sin esto, la pestaña de Incidentados no puede decir POR QUÉ está incidentado el envío:
            // el detalle que devuelve la agencia se descartaba al persistir.
            var envio = new EnviosAgencia { Numero = 1, Estado = Constantes.Agencias.ESTADO_TRAMITADO };

            bool cambio = SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Incidentado,
                Detalle = "DISPONIBLE PARA RECOGER"
            });

            Assert.IsTrue(cambio);
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado);
            Assert.AreEqual("DISPONIBLE PARA RECOGER", envio.DetalleEstado);
        }

        [TestMethod]
        public void AplicarSeguimiento_MismoEstadoPeroOtroDetalle_CuentaComoCambio()
        {
            // Dos incidencias distintas seguidas (mismo Estado=3, otro texto): si no contara como
            // cambio, el grid se quedaría enseñando la etiqueta vieja para siempre.
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Estado = Constantes.Agencias.ESTADO_INCIDENTADO,
                DetalleEstado = "DISPONIBLE PARA RECOGER"
            };

            bool cambio = SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Incidentado,
                Detalle = "DIRECCION INCORRECTA"
            });

            Assert.IsTrue(cambio);
            Assert.AreEqual("DIRECCION INCORRECTA", envio.DetalleEstado);
        }

        [TestMethod]
        public void AplicarSeguimiento_MismoEstadoYMismoDetalle_NoCuentaComoCambio()
        {
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Estado = Constantes.Agencias.ESTADO_INCIDENTADO,
                DetalleEstado = "DISPONIBLE PARA RECOGER"
            };

            bool cambio = SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Incidentado,
                Detalle = "DISPONIBLE PARA RECOGER"
            });

            Assert.IsFalse(cambio, "Sin cambios reales no hay que marcar la entidad como modificada");
        }

        [TestMethod]
        public void AplicarSeguimiento_DetalleMasLargoQueLaColumna_LoRecorta()
        {
            // El texto lo escribe la agencia: DetalleEstado es varchar(100) y un texto más largo
            // reventaría al guardar, tumbando la pasada entera del poll (lección de Observaciones > 80).
            var envio = new EnviosAgencia { Numero = 1, Estado = Constantes.Agencias.ESTADO_TRAMITADO };

            _ = SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Incidentado,
                Detalle = new string('X', 250)
            });

            Assert.AreEqual(100, envio.DetalleEstado.Length);
        }

        [TestMethod]
        public void AplicarSeguimiento_Desconocido_NoTocaLaEtiquetaExistente()
        {
            // NestoAPI#264: Desconocido no es un estado real. Igual que no pisa el Estado, tampoco
            // puede borrar la etiqueta de la incidencia que sigue abierta.
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Estado = Constantes.Agencias.ESTADO_INCIDENTADO,
                DetalleEstado = "DISPONIBLE PARA RECOGER"
            };

            bool cambio = SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Desconocido,
                Detalle = "No se encuentra la expedición"
            });

            Assert.IsFalse(cambio);
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado);
            Assert.AreEqual("DISPONIBLE PARA RECOGER", envio.DetalleEstado);
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
