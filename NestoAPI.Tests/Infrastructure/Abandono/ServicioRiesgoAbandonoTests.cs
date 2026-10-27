using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Abandono;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Abandono
{
    /// <summary>
    /// NestoAPI#714 (corte 1): la pasada nocturna (qué filas salen, episodios de alerta, instantánea semanal, best-effort por
    /// vendedor, interruptor y tabla) y la resolución de las vencidas, sin SQL.
    /// </summary>
    [TestClass]
    public class ServicioRiesgoAbandonoTests
    {
        // Martes 27/10/26 a las 05:20: la instantánea es del lunes 26
        private static readonly DateTime Ejecucion = new DateTime(2026, 10, 27, 5, 20, 0);
        private static readonly DateTime Dia = new DateTime(2026, 10, 26);

        private IRepositorioRiesgoAbandono repositorio;
        private List<Exception> errores;
        private List<FilaRiesgoAbandono> insertadas;
        private ModoAbandono modo;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioRiesgoAbandono>();
            errores = new List<Exception>();
            insertadas = new List<FilaRiesgoAbandono>();
            modo = ModoAbandono.Sombra;
            _ = A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerVendedores()).Returns(new List<string> { "DV" });
            _ = A.CallTo(() => repositorio.LeerAlertasAbiertas(A<string>._, A<string>._)).Returns(new HashSet<string>());
            _ = A.CallTo(() => repositorio.Insertar(A<List<FilaRiesgoAbandono>>._)).ReturnsLazily((List<FilaRiesgoAbandono> f) =>
            {
                insertadas.AddRange(f);
                return f.Count;
            });
        }

        private ServicioRiesgoAbandono Servicio(DateTime? ahora = null)
            => new ServicioRiesgoAbandono(repositorio, () => ahora ?? Ejecucion, () => modo, errores.Add);

        private static DatosClienteAbandono Cliente(string cliente, int cadencia, int diasSinComprar, int compras = 6)
        {
            DatosClienteAbandono datos = CalculadoraAbandonoTests.Cliente(cadencia, diasSinComprar, compras, Dia);
            datos.Cliente = cliente;
            return datos;
        }

        [TestMethod]
        public void DiaInstantanea_EsElAnterior_YLaSemanalCaeEnLunes()
        {
            Assert.AreEqual(Dia, ServicioRiesgoAbandono.DiaInstantanea(Ejecucion));
            Assert.IsTrue(ServicioRiesgoAbandono.TocaInstantaneaSemanal(Dia));
            Assert.IsFalse(ServicioRiesgoAbandono.TocaInstantaneaSemanal(Dia.AddDays(1)));
        }

        [TestMethod]
        public void PrepararFilas_SoloLosRegularesMarcadosSinAlertaAbierta_SonAlerta()
        {
            var cartera = new List<DatosClienteAbandono>
            {
                Cliente("A", 21, 52),            // regular y marcado → alerta
                Cliente("B", 21, 20),            // regular en plazo → nada entre semana
                Cliente("C", 21, 10, compras: 3), // no regular
                Cliente("D", 21, 52),            // marcado pero con alerta abierta → nada
                Cliente("E", 21, 70)             // más allá del horizonte (63): ya se fue, no es población
            };
            var abiertas = new HashSet<string> { RepositorioRiesgoAbandonoSql.Clave("D", "0") };

            List<FilaRiesgoAbandono> filas = ServicioRiesgoAbandono.PrepararFilas(cartera, Dia, semanal: false, abiertas, ModoAbandono.Sombra, out int regulares, out int marcados);

            Assert.AreEqual(3, regulares);
            Assert.AreEqual(2, marcados);
            Assert.AreEqual(1, filas.Count);
            FilaRiesgoAbandono fila = filas[0];
            Assert.AreEqual("A", fila.Cliente);
            Assert.AreEqual(TiposFilaAbandono.ALERTA, fila.Tipo);
            Assert.IsTrue(fila.Baseline);
            Assert.AreEqual(21, fila.Cadencia);
            Assert.AreEqual(52, fila.DiasSinComprar);
            Assert.AreEqual(63, fila.Horizonte);
            Assert.AreEqual(Dia.AddDays(63), fila.FechaLimite);
            Assert.AreEqual(Dia, fila.FechaInstantanea);
            Assert.AreEqual("Suele pedir cada 21 días y lleva 52 sin pedir", fila.Motivo);
            Assert.AreEqual(CalculadoraAbandono.VERSION_REGLA, fila.VersionModelo);
            Assert.AreEqual("Sombra", fila.Modo);
            Assert.IsNull(fila.Probabilidad);
            Assert.IsTrue(fila.Features.Contains("\"Version\":\"features-1\""));
            Assert.AreEqual("DV", fila.Vendedor);
            Assert.AreEqual("1", fila.Empresa);
        }

        [TestMethod]
        public void PrepararFilas_EnLunes_InstantaneaDeTodosLosRegulares_AdemasDeLasAlertas()
        {
            var cartera = new List<DatosClienteAbandono> { Cliente("A", 21, 52), Cliente("B", 21, 20), Cliente("C", 21, 10, compras: 3) };

            List<FilaRiesgoAbandono> filas = ServicioRiesgoAbandono.PrepararFilas(cartera, Dia, semanal: true, new HashSet<string>(), ModoAbandono.Sombra, out _, out _);

            Assert.AreEqual(3, filas.Count);
            Assert.AreEqual(1, filas.Count(f => f.Tipo == TiposFilaAbandono.ALERTA && f.Cliente == "A"));
            Assert.AreEqual(2, filas.Count(f => f.Tipo == TiposFilaAbandono.INSTANTANEA));
            Assert.IsTrue(filas.Single(f => f.Tipo == TiposFilaAbandono.INSTANTANEA && f.Cliente == "A").Baseline);
            Assert.IsFalse(filas.Single(f => f.Tipo == TiposFilaAbandono.INSTANTANEA && f.Cliente == "B").Baseline);
            Assert.IsTrue(filas.All(f => !string.IsNullOrEmpty(f.Features)), "las instantáneas llevan features para entrenar");
        }

        [TestMethod]
        public void EjecutarPasada_Apagado_NoTocaLaBd()
        {
            modo = ModoAbandono.Apagado;

            ResumenPasadaAbandono resumen = Servicio().EjecutarPasada();

            Assert.AreEqual(ModoAbandono.Apagado, resumen.Modo);
            A.CallTo(() => repositorio.TablaExiste()).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerVendedores()).MustNotHaveHappened();
            A.CallTo(() => repositorio.Insertar(A<List<FilaRiesgoAbandono>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EjecutarPasada_SinTabla_NoHaceNadaYLoDice()
        {
            _ = A.CallTo(() => repositorio.TablaExiste()).Returns(false);

            ResumenPasadaAbandono resumen = Servicio().EjecutarPasada();

            Assert.IsFalse(resumen.TablaExiste);
            A.CallTo(() => repositorio.LeerVendedores()).MustNotHaveHappened();
            StringAssert.Contains(RiesgoAbandonoJobsService.Texto(resumen), "Issue714_ClientesRiesgoAbandono.sql");
        }

        [TestMethod]
        public void EjecutarPasada_LeeLaCarteraDelDiaAnterior_YEscribeAlertasYSemanal()
        {
            _ = A.CallTo(() => repositorio.LeerCartera("DV", Dia)).Returns(new List<DatosClienteAbandono> { Cliente("A", 21, 52), Cliente("B", 21, 20) });

            ResumenPasadaAbandono resumen = Servicio().EjecutarPasada();

            Assert.AreEqual(Dia, resumen.FechaInstantanea);
            Assert.AreEqual(1, resumen.Vendedores);
            Assert.AreEqual(2, resumen.Cartera);
            Assert.AreEqual(2, resumen.CompradoresRegulares);
            Assert.AreEqual(1, resumen.MarcadosPorLaRegla);
            Assert.AreEqual(1, resumen.AlertasNuevas);
            Assert.AreEqual(2, resumen.InstantaneasSemanales);
            Assert.AreEqual(3, resumen.FilasInsertadas);
            Assert.AreEqual(3, insertadas.Count);
            Assert.IsTrue(insertadas.All(f => f.FechaInstantanea == Dia));
            A.CallTo(() => repositorio.LeerAlertasAbiertas("1", "DV")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, errores.Count);
            StringAssert.Contains(RiesgoAbandonoJobsService.Texto(resumen), "alertas nuevas 1");
        }

        [TestMethod]
        public void EjecutarPasada_EntreSemana_SinAlertasNuevas_NoInserta()
        {
            _ = A.CallTo(() => repositorio.LeerCartera("DV", Dia.AddDays(1))).Returns(new List<DatosClienteAbandono> { Cliente("B", 21, 20) });

            ResumenPasadaAbandono resumen = Servicio(Ejecucion.AddDays(1)).EjecutarPasada();

            Assert.AreEqual(1, resumen.CompradoresRegulares);
            Assert.AreEqual(0, resumen.FilasInsertadas);
            Assert.AreEqual(0, insertadas.Count);
        }

        [TestMethod]
        public void EjecutarPasada_UnVendedorQueFalla_VaAElmahYSigueConElSiguiente()
        {
            _ = A.CallTo(() => repositorio.LeerVendedores()).Returns(new List<string> { "ASH", "DV" });
            _ = A.CallTo(() => repositorio.LeerCartera("ASH", Dia)).Throws(new InvalidOperationException("timeout"));
            _ = A.CallTo(() => repositorio.LeerCartera("DV", Dia)).Returns(new List<DatosClienteAbandono> { Cliente("A", 21, 52) });

            ResumenPasadaAbandono resumen = Servicio().EjecutarPasada();

            Assert.AreEqual(1, resumen.VendedoresConError);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "ASH");
            Assert.AreEqual(2, resumen.FilasInsertadas, "la alerta y la instantánea semanal de DV");
            StringAssert.Contains(RiesgoAbandonoJobsService.Texto(resumen), "1 con error");
        }

        [TestMethod]
        public void ResolverVencidas_DecideYEscribeCadaFila()
        {
            _ = A.CallTo(() => repositorio.LeerPendientesResolver(Ejecucion.Date, ServicioRiesgoAbandono.MAXIMO_RESOLVER_POR_PASADA)).Returns(new List<PendienteResolverAbandono>
            {
                new PendienteResolverAbandono { Id = 1, ClienteExiste = true },
                new PendienteResolverAbandono { Id = 2, ClienteExiste = true, FechaPrimeraCompra = new DateTime(2026, 9, 3) },
                new PendienteResolverAbandono { Id = 3, ClienteExiste = true, FechaBaja = new DateTime(2026, 9, 10, 11, 0, 0), FechaPrimeraCompra = new DateTime(2026, 9, 3) },
                new PendienteResolverAbandono { Id = 4, ClienteExiste = false }
            });

            ResumenResolucionAbandono resumen = Servicio().ResolverVencidas();

            Assert.AreEqual(4, resumen.Resueltas);
            Assert.AreEqual(0, resumen.Errores);
            A.CallTo(() => repositorio.Resolver(1, ResultadoAbandono.ABANDONO, null, Ejecucion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Resolver(2, ResultadoAbandono.COMPRO, new DateTime(2026, 9, 3), Ejecucion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Resolver(3, ResultadoAbandono.BAJA, new DateTime(2026, 9, 10), Ejecucion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Resolver(4, ResultadoAbandono.DESCONOCIDO, null, Ejecucion)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resumen.PorResultado[ResultadoAbandono.BAJA]);
            StringAssert.Contains(RiesgoAbandonoJobsService.Texto(resumen), "Resueltas 4 filas");
        }

        [TestMethod]
        public void ResolverVencidas_UnaFilaQueFalla_NoParaLasDemas()
        {
            _ = A.CallTo(() => repositorio.LeerPendientesResolver(A<DateTime>._, A<int>._)).Returns(new List<PendienteResolverAbandono>
            {
                new PendienteResolverAbandono { Id = 1, ClienteExiste = true },
                new PendienteResolverAbandono { Id = 2, ClienteExiste = true }
            });
            _ = A.CallTo(() => repositorio.Resolver(1, A<string>._, A<DateTime?>._, A<DateTime>._)).Throws(new Exception("deadlock"));

            ResumenResolucionAbandono resumen = Servicio().ResolverVencidas();

            Assert.AreEqual(1, resumen.Resueltas);
            Assert.AreEqual(1, resumen.Errores);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.Resolver(2, ResultadoAbandono.ABANDONO, null, Ejecucion)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ResolverVencidas_ApagadoOSinTabla_NoHaceNada()
        {
            modo = ModoAbandono.Apagado;
            Assert.AreEqual(0, Servicio().ResolverVencidas().Resueltas);
            A.CallTo(() => repositorio.LeerPendientesResolver(A<DateTime>._, A<int>._)).MustNotHaveHappened();

            modo = ModoAbandono.Activo;
            _ = A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            Assert.IsFalse(Servicio().ResolverVencidas().TablaExiste);
            A.CallTo(() => repositorio.LeerPendientesResolver(A<DateTime>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Interruptor_TresEstados_YApagadoPorDefecto()
        {
            Assert.AreEqual(ModoAbandono.Sombra, InterruptorAbandono.Interpretar("Sombra"));
            Assert.AreEqual(ModoAbandono.Sombra, InterruptorAbandono.Interpretar(" sombra "));
            Assert.AreEqual(ModoAbandono.Activo, InterruptorAbandono.Interpretar("ACTIVO"));
            Assert.AreEqual(ModoAbandono.Apagado, InterruptorAbandono.Interpretar("Apagado"));
            Assert.AreEqual(ModoAbandono.Apagado, InterruptorAbandono.Interpretar(null));
            Assert.AreEqual(ModoAbandono.Apagado, InterruptorAbandono.Interpretar("true"));
            Assert.AreEqual("Abandono:Modo", InterruptorAbandono.CLAVE);
        }

        [TestMethod]
        public void Jobs_FueraDeHorario_DeLunesASabado_YResolverAntesQueLaPasada()
        {
            Assert.AreEqual("10 5 * * 1-6", RiesgoAbandonoJobsService.CRON_RESOLVER);
            Assert.AreEqual("20 5 * * 1-6", RiesgoAbandonoJobsService.CRON_DIARIO);
        }
    }
}
