using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Tests.Controllers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#742 (decisiones de Carlos 29/10/26): el reembolso de las agencias con seguimiento se contabiliza al pasar el
    /// envío a ENTREGADO, no al tramitar; una sola vez; nunca los devueltos ni los incidentados; nunca otra vez los envíos
    /// tramitados antes del cambio; la 14 sigue con lo suyo; y con el interruptor apagado (o sin el script), como antes.
    /// </summary>
    [TestClass]
    public class ReembolsoAlEntregarTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 30);
        private const string USUARIO = @"NUEVAVISION\Alfredo";
        private const int CTT = 13;
        private const int SIN_SEGUIMIENTO = 11;
        private const int ASIENTO = 1250001;

        private NVEntities db;
        private IContabilidadService contabilidad;
        private IFabricaAgenciasRemotas fabrica;
        private RegistroReembolsosEnMemoria registro;
        private TramitacionEnviosService servicio;
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();
        private readonly List<ExtractoCliente> extracto = new List<ExtractoCliente>();
        private readonly List<PreContabilidad> apuntes = new List<PreContabilidad>();

        private class TransaccionDirecta : IAmbitoTransaccion
        {
            public Task<T> EjecutarAsync<T>(NVEntities db, Func<Task<T>> cuerpo) => cuerpo();
        }

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            contabilidad = A.Fake<IContabilidadService>();
            fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.AgenciasConSeguimiento).Returns(new[] { 1, 12, CTT });
            registro = new RegistroReembolsosEnMemoria { InterruptorEncendido = true };

            DbSet<EnviosAgencia> fakeEnvios = FakeDbSet(envios);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.ExtractosCliente).Returns(FakeDbSet(extracto));
            A.CallTo(() => db.EnviosHistorias).Returns(A.Fake<DbSet<EnvioHistoria>>());
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => contabilidad.CrearLineas(db, A<List<PreContabilidad>>.Ignored))
                .Invokes((NVEntities d, List<PreContabilidad> l) => apuntes.AddRange(l)).Returns(Task.FromResult(1));
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", A<string>.Ignored)).Returns(Task.FromResult(ASIENTO));

            servicio = Servicio(registro);
        }

        private TramitacionEnviosService Servicio(IRegistroReembolsosAlEntregar reg)
            => new TramitacionEnviosService(db, contabilidad, () => HOY, A.Fake<IProcedimientosExtractoCliente>(), new TransaccionDirecta(),
                fabrica, null, reg);

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).ReturnsLazily(() => new TestAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => fakeSet.Include(A<string>.Ignored)).Returns(fakeSet);
            return fakeSet;
        }

        private EnviosAgencia Envio(int agencia = CTT, decimal reembolso = 121.50M, short estado = Constantes.Agencias.ESTADO_ENTREGADO, int numero = 700001)
        {
            var envio = new EnviosAgencia
            {
                Numero = numero,
                Empresa = "1  ",
                Agencia = agencia,
                Cliente = "15191     ",
                Contacto = "0  ",
                Pedido = 922175,
                Vendedor = "NV ",
                Reembolso = reembolso,
                Estado = estado,
                Fecha = new DateTime(2026, 10, 28),
                Empresa1 = new Empresa { Número = "1  ", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
                AgenciasTransporte = new AgenciaTransporte { Numero = agencia, Nombre = "CTT", CuentaReembolsos = "55500092" }
            };
            envios.Add(envio);
            return envio;
        }

        // ===== Reglas =====

        [TestMethod]
        public void Interruptor_SoloTrueEnciende()
        {
            Assert.IsTrue(InterruptorReembolsoAlEntregar.EstaActivo("true"));
            Assert.IsTrue(InterruptorReembolsoAlEntregar.EstaActivo(" True "));
            Assert.IsFalse(InterruptorReembolsoAlEntregar.EstaActivo("false"));
            Assert.IsFalse(InterruptorReembolsoAlEntregar.EstaActivo(null));
            Assert.IsFalse(InterruptorReembolsoAlEntregar.EstaActivo("1"));
        }

        [TestMethod]
        public void Momento_ConInterruptorYSeguimiento_AlEntregar_SinAlgunoDeLosDos_AlTramitar_La14Siempre()
        {
            Assert.AreEqual(MomentoContabilizarReembolso.AlEntregar, MomentosContabilizarReembolso.De(CTT, true, true));
            Assert.AreEqual(MomentoContabilizarReembolso.AlTramitar, MomentosContabilizarReembolso.De(CTT, false, true), "Interruptor apagado");
            Assert.AreEqual(MomentoContabilizarReembolso.AlTramitar, MomentosContabilizarReembolso.De(CTT, true, false), "Sin seguimiento no pasaría nunca a Entregado");
            Assert.AreEqual(MomentoContabilizarReembolso.AlEntregar, MomentosContabilizarReembolso.De(Constantes.Agencias.AGENCIA_REPARTO_PROPIO, false, false));
        }

        // ===== Al tramitar =====

        [TestMethod]
        public async Task Tramitar_InterruptorEncendido_NoContabilizaYDejaElReembolsoPendiente()
        {
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(0, asiento);
            Assert.AreEqual(0, apuntes.Count, "Ni un apunte al tramitar");
            A.CallTo(() => contabilidad.ContabilizarDiario(A<NVEntities>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            CollectionAssert.AreEqual(new[] { envio.Numero }, registro.Pendientes().ToArray());
        }

        [TestMethod]
        public async Task Tramitar_InterruptorApagado_ContabilizaAlTramitarComoSiempre()
        {
            registro.InterruptorEncendido = false;
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(ASIENTO, asiento);
            Assert.AreEqual(121.50M, apuntes.Single().Haber);
            Assert.AreEqual("55500092", apuntes.Single().Contrapartida);
            Assert.AreEqual(0, registro.Filas.Count);
        }

        [TestMethod]
        public async Task Tramitar_SinScript_ContabilizaAlTramitarAunqueElInterruptorEsteEncendido()
        {
            registro.TablaExiste = false;
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(ASIENTO, asiento);
            Assert.AreEqual(0, registro.Filas.Count);
        }

        [TestMethod]
        public async Task Tramitar_SinRegistro_ContabilizaAlTramitar()
        {
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await Servicio(null).ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(ASIENTO, asiento);
        }

        [TestMethod]
        public async Task Tramitar_AgenciaSinSeguimiento_ContabilizaAlTramitar()
        {
            EnviosAgencia envio = Envio(agencia: SIN_SEGUIMIENTO, estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(ASIENTO, asiento, "Si nunca pasa sola a Entregado, el reembolso diferido no se contabilizaría nunca");
            Assert.AreEqual(0, registro.Filas.Count);
        }

        [TestMethod]
        public async Task Tramitar_EnvioYaTramitado_NiContabilizaNiLoDejaPendiente()
        {
            // Un envío tramitado antes del cambio (ya contabilizado al tramitar) que se vuelve a confirmar: ni otra vez al
            // tramitar ni, después, al entregar.
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_TRAMITADO, USUARIO);

            Assert.AreEqual(0, asiento);
            Assert.AreEqual(0, apuntes.Count);
            Assert.AreEqual(0, registro.Filas.Count);
        }

        [TestMethod]
        public async Task Tramitar_SinReembolso_Nada()
        {
            EnviosAgencia envio = Envio(reembolso: 0, estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(0, asiento);
            Assert.AreEqual(0, apuntes.Count);
            Assert.AreEqual(0, registro.Filas.Count);
        }

        [TestMethod]
        public async Task Tramitar_RepartoPropio_NiContabilizaNiDejaFila()
        {
            EnviosAgencia envio = Envio(agencia: Constantes.Agencias.AGENCIA_REPARTO_PROPIO, estado: Constantes.Agencias.ESTADO_TRAMITADO);

            int asiento = await servicio.ReembolsoAlTramitarAsync(envio, Constantes.Agencias.ESTADO_EN_CURSO, USUARIO);

            Assert.AreEqual(0, asiento);
            Assert.AreEqual(0, apuntes.Count);
            Assert.AreEqual(0, registro.Filas.Count, "La 14 contabiliza al entregar por su camino (caja del repartidor)");
        }

        // ===== Al entregar =====

        [TestMethod]
        public async Task Entregar_Pendiente_ContabilizaContraLaCuentaDeLaAgenciaConFechaDeHoy()
        {
            EnviosAgencia envio = Envio();
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };

            int asiento = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

            Assert.AreEqual(ASIENTO, asiento);
            PreContabilidad apunte = apuntes.Single();
            Assert.AreEqual(121.50M, apunte.Haber);
            Assert.AreEqual("55500092", apunte.Contrapartida);
            Assert.AreEqual("_Reembolso", apunte.Diario);
            Assert.AreEqual(HOY, apunte.Fecha);
            Assert.AreEqual(ASIENTO, registro.Filas[envio.Numero].Asiento);
            Assert.AreEqual(HOY, registro.Filas[envio.Numero].FechaContable);
        }

        [TestMethod]
        public async Task Entregar_Repetido_SoloUnaVez()
        {
            EnviosAgencia envio = Envio();
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };

            int primera = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);
            int segunda = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

            Assert.AreEqual(ASIENTO, primera);
            Assert.AreEqual(0, segunda);
            Assert.AreEqual(1, apuntes.Count);
            A.CallTo(() => contabilidad.ContabilizarDiario(A<NVEntities>._, A<string>._, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Entregar_OtraPasadaSeAdelanta_NoContabiliza()
        {
            EnviosAgencia envio = Envio();
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };
            registro.ReservaFalla = true;   // la leyó pendiente, pero al reservar ya no lo estaba

            int asiento = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

            Assert.AreEqual(0, asiento);
            Assert.AreEqual(0, apuntes.Count);
        }

        [TestMethod]
        public async Task Entregar_DeIncidentadoAEntregado_ContabilizaUnaVez()
        {
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_INCIDENTADO);
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };

            int incidentado = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);
            envio.Estado = Constantes.Agencias.ESTADO_ENTREGADO;
            int entregado = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);
            int otraVez = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

            Assert.AreEqual(0, incidentado);
            Assert.AreEqual(ASIENTO, entregado);
            Assert.AreEqual(0, otraVez);
            Assert.AreEqual(1, apuntes.Count);
        }

        [TestMethod]
        public async Task Entregar_DevueltoIncidentadoOCerradoAMano_NoContabilizaNunca()
        {
            foreach (short estado in new[] { Constantes.Agencias.ESTADO_DEVUELTO, Constantes.Agencias.ESTADO_INCIDENTADO,
                Constantes.Agencias.ESTADO_CERRADO_A_MANO, Constantes.Agencias.ESTADO_TRAMITADO })
            {
                envios.Clear();
                EnviosAgencia envio = Envio(estado: estado);
                registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };

                int asiento = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

                Assert.AreEqual(0, asiento, "Estado " + estado);
                Assert.IsNull(registro.Filas[envio.Numero].Asiento, "Sigue pendiente. Estado " + estado);
            }
            Assert.AreEqual(0, apuntes.Count);
        }

        [TestMethod]
        public async Task Entregar_EnvioAntiguoSinFila_NoSeContabilizaOtraVez()
        {
            // Tramitado antes del cambio: su reembolso ya se contabilizó al tramitar y no tiene fila.
            EnviosAgencia envio = Envio();

            int asiento = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

            Assert.AreEqual(0, asiento);
            Assert.AreEqual(0, apuntes.Count);
        }

        [TestMethod]
        public async Task Entregar_InterruptorApagadoDespues_LosPendientesSeContabilizanIgual()
        {
            EnviosAgencia envio = Envio();
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };
            registro.InterruptorEncendido = false;

            int asiento = await servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO);

            Assert.AreEqual(ASIENTO, asiento, "Si no, ese reembolso no se contabilizaría nunca");
        }

        [TestMethod]
        public async Task Entregar_SiFallaElAsiento_SigueSinContabilizarYLanza()
        {
            EnviosAgencia envio = Envio();
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", A<string>.Ignored)).Returns(Task.FromResult(0));

            await Assert.ThrowsExceptionAsync<Exception>(() => servicio.ContabilizarReembolsoAlEntregarAsync(envio.Numero, USUARIO));
            // En la BD la transacción deshace la reserva; aquí basta con que no se apunte asiento.
            Assert.IsFalse(registro.Filas[envio.Numero].Asiento > 0);
        }

        [TestMethod]
        public void DebeContabilizarseAlEntregar_SoloEntregadoConReembolsoSinPagarYNoLa14()
        {
            Assert.IsTrue(TramitacionEnviosService.DebeContabilizarseAlEntregar(Envio()));
            Assert.IsFalse(TramitacionEnviosService.DebeContabilizarseAlEntregar(Envio(agencia: Constantes.Agencias.AGENCIA_REPARTO_PROPIO)));
            Assert.IsFalse(TramitacionEnviosService.DebeContabilizarseAlEntregar(Envio(reembolso: 0)));
            EnviosAgencia pagado = Envio();
            pagado.FechaPagoReembolso = HOY;
            Assert.IsFalse(TramitacionEnviosService.DebeContabilizarseAlEntregar(pagado));
            Assert.IsFalse(TramitacionEnviosService.DebeContabilizarseAlEntregar(null));
        }

        // ===== Devueltos y modificaciones =====

        [TestMethod]
        public async Task QuitarReembolsoDevuelto_Pendiente_SinNingunApunte()
        {
            // Antes de #742 el devuelto se descontabilizaba (deshago en _Reembolso). Diferido, no hay nada que deshacer.
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_DEVUELTO);
            registro.Filas[envio.Numero] = new RegistroReembolsoAlEntregar { EnvioId = envio.Numero };

            var resultado = await servicio.QuitarReembolsoDevueltoAsync(envio.Numero, USUARIO);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Asiento);
            Assert.AreEqual(0M, envio.Reembolso);
            Assert.AreEqual(0, apuntes.Count);
            A.CallTo(() => contabilidad.ContabilizarDiario(A<NVEntities>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task QuitarReembolsoDevuelto_EnvioAntiguo_SeDescontabilizaComoSiempre()
        {
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_DEVUELTO);

            var resultado = await servicio.QuitarReembolsoDevueltoAsync(envio.Numero, USUARIO);

            Assert.AreEqual(ASIENTO, resultado.Asiento, "Los antiguos siguen como estaban");
            Assert.AreEqual(121.50M, apuntes.Single().Debe, "Deshago");
        }

        // ===== Pago de la agencia (#349) =====

        [TestMethod]
        public void ErrorEnviosAPagar_ReembolsoSinContabilizar_NoSePuedePagar()
        {
            EnviosAgencia envio = Envio();

            string error = TramitacionEnviosService.ErrorEnviosAPagar(new List<EnviosAgencia> { envio }, new List<int> { envio.Numero }, "1",
                new HashSet<int> { envio.Numero });

            StringAssert.Contains(error, "todavía no está contabilizado");
            Assert.IsNull(TramitacionEnviosService.ErrorEnviosAPagar(new List<EnviosAgencia> { envio }, new List<int> { envio.Numero }, "1",
                new HashSet<int>()));
        }

        // ===== Seguimiento =====

        [TestMethod]
        public async Task Seguimiento_ContabilizaTodosLosEntregadosPendientes_YUnFalloNoFrenaALosDemas()
        {
            A.CallTo(() => fabrica.AgenciasConSeguimiento).Returns(new int[0]);
            var contabilizados = new List<int>();
            var avisos = new List<Exception>();
            var job = new SeguimientoEnviosJobsService(db, fabrica, avisar: avisos.Add,
                leerReembolsosPendientesEntregados: () => Task.FromResult(new List<int> { 1, 2, 3 }),
                contabilizarReembolsoEntregado: n =>
                {
                    if (n == 2)
                    {
                        throw new InvalidOperationException("prdContabilizar falló");
                    }
                    contabilizados.Add(n);
                    return Task.FromResult(ASIENTO);
                });

            _ = await job.ActualizarSeguimientosAsync(new DateTime(2026, 6, 1));

            CollectionAssert.AreEqual(new[] { 1, 3 }, contabilizados);
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "envío 2");
        }

        [TestMethod]
        public async Task Seguimiento_SinLasFunciones_NoContabilizaNada()
        {
            A.CallTo(() => fabrica.AgenciasConSeguimiento).Returns(new int[0]);
            var job = new SeguimientoEnviosJobsService(db, fabrica, avisar: _ => Assert.Fail("No debe avisar"));

            int actualizados = await job.ActualizarSeguimientosAsync(new DateTime(2026, 6, 1));

            Assert.AreEqual(0, actualizados);
        }
    }

    /// <summary>NestoAPI#742: doble en memoria del registro de reembolsos diferidos (con su estado, para probar el «una sola vez»).</summary>
    internal class RegistroReembolsosEnMemoria : IRegistroReembolsosAlEntregar
    {
        public Dictionary<int, RegistroReembolsoAlEntregar> Filas { get; } = new Dictionary<int, RegistroReembolsoAlEntregar>();
        public bool TablaExiste { get; set; } = true;
        public bool InterruptorEncendido { get; set; }
        public bool ReservaFalla { get; set; }

        public IEnumerable<int> Pendientes() => Filas.Values.Where(f => !f.Asiento.HasValue).Select(f => f.EnvioId);

        public bool TablaDisponible() => TablaExiste;

        public bool Activo() => InterruptorEncendido && TablaExiste;

        public Task<RegistroReembolsoAlEntregar> LeerAsync(int envio)
            => Task.FromResult(Filas.TryGetValue(envio, out RegistroReembolsoAlEntregar fila) ? fila : null);

        public Task MarcarPendienteAsync(int envio, string usuario)
        {
            if (!Filas.ContainsKey(envio))
            {
                Filas[envio] = new RegistroReembolsoAlEntregar { EnvioId = envio };
            }
            return Task.FromResult(0);
        }

        public Task<bool> ReservarAsync(int envio)
        {
            if (ReservaFalla || !Filas.TryGetValue(envio, out RegistroReembolsoAlEntregar fila) || fila.Asiento.HasValue)
            {
                return Task.FromResult(false);
            }
            fila.Asiento = RegistroReembolsosAlEntregar.ASIENTO_EN_CURSO;
            return Task.FromResult(true);
        }

        public Task MarcarContabilizadoAsync(int envio, int asiento, decimal importe, DateTime fechaContable, string usuario)
        {
            Filas[envio].Asiento = asiento;
            Filas[envio].FechaContable = fechaContable;
            return Task.FromResult(0);
        }

        public Task<HashSet<int>> LeerPendientesAsync() => Task.FromResult(new HashSet<int>(Pendientes()));

        public Task<List<int>> LeerPendientesEntregadosAsync() => Task.FromResult(new List<int>());
    }
}
