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
