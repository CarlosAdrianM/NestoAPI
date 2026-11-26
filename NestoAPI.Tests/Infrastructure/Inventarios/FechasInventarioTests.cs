using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Inventarios
{
    /// <summary>
    /// NestoAPI#883 (corte 1): fecha de inventario por almacén y aviso de las reposiciones de la víspera. El caso es el inventario del sábado
    /// 21/11/26 en Algete: Reina → Algete y Alcobendas → Algete del viernes 20 seguían activas, y Algete → Reina del lunes 23 cerraba el viernes.
    /// </summary>
    [TestClass]
    public class FechasInventarioTests
    {
        private static readonly DateTime SABADO = new DateTime(2026, 11, 21);
        private static readonly DateTime MIERCOLES_8_30 = new DateTime(2026, 11, 18, 8, 30, 0);

        private static bool SinFestivos(DateTime dia, string almacen) => false;

        private static ReposicionCalendario Fila(string origen, string destino, byte dia, int horaCierre, byte antelacion, int horaLlegada = 13)
            => new ReposicionCalendario
            {
                Empresa = "1", AlmacenOrigen = origen, AlmacenDestino = destino, DiaSemana = dia, Activo = true,
                HoraCierre = TimeSpan.FromHours(horaCierre), HoraLlegadaHabitual = TimeSpan.FromHours(horaLlegada), LaborablesAntelacionCierre = antelacion
            };

        private static List<ReposicionCalendario> Calendario() => new List<ReposicionCalendario>
        {
            Fila("REI", "ALG", 5, 9, 0),
            Fila("ALC", "ALG", 5, 9, 0),
            Fila("ALG", "REI", 1, 12, 1),   // la del lunes cierra el viernes a las 12:00
            Fila("ALG", "REI", 3, 9, 0)     // la del miércoles no toca
        };

        private static CalculadoraFechaReposicion Calculadora(params ExcepcionCalendarioReposicionDTO[] excepciones)
            => new CalculadoraFechaReposicion(SinFestivos, () => excepciones.ToList());

        // ---------- LA REGLA ----------

        [TestMethod]
        public void DiaAviso_TresLaborablesAntes()
        {
            Assert.AreEqual(new DateTime(2026, 11, 18), ReglasFechasInventario.DiaAviso(SABADO, "ALG", SinFestivos), "sábado 21 → miércoles 18");
            Assert.AreEqual(new DateTime(2026, 11, 17), ReglasFechasInventario.DiaAviso(SABADO, "ALG", (d, a) => d == new DateTime(2026, 11, 19)),
                "con el jueves festivo, un día antes");
            Assert.AreEqual(new DateTime(2026, 11, 18), ReglasFechasInventario.DiaAviso(new DateTime(2026, 11, 23), "REI", SinFestivos),
                "lunes 23 → miércoles 18 (el fin de semana no cuenta)");
        }

        [TestMethod]
        public void TocaAvisar_DesdeElDiaHastaElInventarioYUnaVez()
        {
            var fecha = new FechaInventarioDTO { Almacen = "ALG", Fecha = SABADO };
            Assert.IsFalse(ReglasFechasInventario.TocaAvisar(fecha, new DateTime(2026, 11, 17), SinFestivos), "aún no");
            Assert.IsTrue(ReglasFechasInventario.TocaAvisar(fecha, MIERCOLES_8_30, SinFestivos));
            Assert.IsTrue(ReglasFechasInventario.TocaAvisar(fecha, new DateTime(2026, 11, 20), SinFestivos), "puesta tarde: el siguiente día que corra");
            Assert.IsFalse(ReglasFechasInventario.TocaAvisar(fecha, new DateTime(2026, 11, 22), SinFestivos), "el inventario ya pasó");
            fecha.AvisoEnviado = MIERCOLES_8_30;
            Assert.IsFalse(ReglasFechasInventario.TocaAvisar(fecha, new DateTime(2026, 11, 19), SinFestivos), "una vez");
        }

        [TestMethod]
        public void Afectadas_ElCasoDel21DeNoviembre()
        {
            List<ReposicionAfectadaInventarioDTO> r = ReglasFechasInventario.Afectadas(Calculadora(), Calculadora(), Calendario(), "1", "ALG", SABADO,
                null, MIERCOLES_8_30);

            Assert.AreEqual(3, r.Count);
            ReposicionAfectadaInventarioDTO sale = r.Single(x => x.Motivo == ReposicionAfectadaInventarioDTO.SALE);
            Assert.AreEqual("REI", sale.Destino);
            Assert.AreEqual(new DateTime(2026, 11, 23), sale.FechaLlegada);
            Assert.AreEqual(new DateTime(2026, 11, 20, 12, 0, 0), sale.CierraEl);
            CollectionAssert.AreEquivalent(new[] { "REI", "ALC" }, r.Where(x => x.Motivo == ReposicionAfectadaInventarioDTO.LLEGA).Select(x => x.Origen).ToArray());
            Assert.IsTrue(r.All(x => x.Estado == ReposicionAfectadaInventarioDTO.PENDIENTE && x.SePuedeQuitar));
            Assert.AreEqual("Reposición Reina → Algete, llega el viernes 20/11 (cierra el viernes 20/11 a las 9:00)",
                r.Single(x => x.Origen == "REI").Texto);
        }

        [TestMethod]
        public void Afectadas_QuitadaYMantenida()
        {
            var excepcion = new ExcepcionCalendarioReposicionDTO
            {
                Empresa = "1", Origen = "REI", Destino = "ALG", FechaLlegada = new DateTime(2026, 11, 20), Tipo = TiposExcepcionCalendarioReposicion.SIN_REPOSICION
            };
            var mantener = new DecisionViajeInventario
            {
                InventarioFechaId = 1, Origen = "ALC", Destino = "ALG", FechaLlegada = new DateTime(2026, 11, 20),
                Decision = DecisionReposicionInventarioDTO.MANTENER, Usuario = "Andre", Fecha = MIERCOLES_8_30
            };

            List<ReposicionAfectadaInventarioDTO> r = ReglasFechasInventario.Afectadas(Calculadora(excepcion), Calculadora(), Calendario(), "1", "ALG", SABADO,
                new[] { mantener }, MIERCOLES_8_30);

            Assert.AreEqual(ReposicionAfectadaInventarioDTO.QUITADA, r.Single(x => x.Origen == "REI").Estado, "se sigue enseñando, ya quitada");
            Assert.IsFalse(r.Single(x => x.Origen == "REI").SePuedeQuitar);
            ReposicionAfectadaInventarioDTO mantenida = r.Single(x => x.Origen == "ALC");
            Assert.AreEqual(ReposicionAfectadaInventarioDTO.MANTENIDA, mantenida.Estado);
            Assert.AreEqual("Andre", mantenida.DecididoPor);
            Assert.AreEqual(ReposicionAfectadaInventarioDTO.PENDIENTE, r.Single(x => x.Motivo == ReposicionAfectadaInventarioDTO.SALE).Estado);
        }

        [TestMethod]
        public void Afectadas_PasadaLaHoraDeCierre_NoSePuedeQuitar()
        {
            List<ReposicionAfectadaInventarioDTO> r = ReglasFechasInventario.Afectadas(Calculadora(), Calculadora(), Calendario(), "1", "ALG", SABADO,
                null, new DateTime(2026, 11, 20, 10, 0, 0));

            Assert.IsFalse(r.Single(x => x.Origen == "REI").SePuedeQuitar, "cerró a las 9:00");
            Assert.IsTrue(r.Single(x => x.Motivo == ReposicionAfectadaInventarioDTO.SALE).SePuedeQuitar, "cierra a las 12:00");
        }

        [TestMethod]
        public void Afectadas_InventarioDeUnaTienda()
        {
            List<ReposicionAfectadaInventarioDTO> r = ReglasFechasInventario.Afectadas(Calculadora(), Calculadora(), Calendario(), "1", "REI", SABADO,
                null, MIERCOLES_8_30);

            Assert.AreEqual(1, r.Count, "solo sale REI → ALG del viernes; la del lunes llega a Reina el 23, fuera");
            Assert.AreEqual(ReposicionAfectadaInventarioDTO.SALE, r[0].Motivo);
        }

        [TestMethod]
        public void Cuerpo_DeUsted()
        {
            var fecha = new FechaInventarioDTO { Almacen = "ALG", Fecha = SABADO };
            var pendientes = new[] { new ReposicionAfectadaInventarioDTO { Texto = "Reposición Reina → Algete, llega el viernes 20/11 (cierra el viernes 20/11 a las 9:00)" } };

            Assert.AreEqual("El sábado 21/11 hay inventario en Algete y sigue activa 1 reposición que toca su stock: " +
                "Reposición Reina → Algete, llega el viernes 20/11 (cierra el viernes 20/11 a las 9:00). " +
                "Decida en Nesto (Inventario › Fechas de inventario) si se quitan o se mantienen.", ReglasFechasInventario.Cuerpo(fecha, pendientes));
        }

        // ---------- el servicio ----------

        private IRepositorioFechasInventario repositorio;
        private IServicioExcepcionesCalendarioReposiciones excepciones;
        private IAvisadorFechasInventario avisador;
        private FechaInventarioDTO guardada;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioFechasInventario>();
            excepciones = A.Fake<IServicioExcepcionesCalendarioReposiciones>();
            avisador = A.Fake<IAvisadorFechasInventario>();
            guardada = new FechaInventarioDTO { Id = 7, Empresa = "1", Almacen = "ALG", Fecha = SABADO };
            A.CallTo(() => repositorio.TablaCreada()).Returns(true);
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).ReturnsLazily(() => Task.FromResult(Calendario()));
            A.CallTo(() => repositorio.LeerPorId(7)).ReturnsLazily(() => Task.FromResult(new FechaInventarioDTO { Id = 7, Empresa = "1", Almacen = "ALG", Fecha = SABADO }));
            A.CallTo(() => repositorio.Leer(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(new List<FechaInventarioDTO> { guardada }));
            A.CallTo(() => repositorio.LeerDecisiones(A<IEnumerable<int>>._)).Returns(Task.FromResult(new List<DecisionViajeInventario>()));
        }

        private ServicioFechasInventario Servicio(DateTime ahora)
            => new ServicioFechasInventario(repositorio, () => excepciones, avisador, SinFestivos, () => new List<ExcepcionCalendarioReposicionDTO>(), () => ahora);

        private static async Task<NestoBusinessException> Lanza(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (NestoBusinessException ex)
            {
                return ex;
            }
            Assert.Fail("Tenía que lanzar NestoBusinessException");
            return null;
        }

        [TestMethod]
        public async Task Guardar_AlmacenRaroODiaPasado_400()
        {
            NestoBusinessException raro = await Lanza(() => Servicio(MIERCOLES_8_30).Guardar(new FechaInventarioDTO { Almacen = "PRV", Fecha = SABADO }, "Carlos"));
            StringAssert.Contains(raro.Message, "ALG, REI, ALC");
            NestoBusinessException pasado = await Lanza(() => Servicio(MIERCOLES_8_30).Guardar(new FechaInventarioDTO { Almacen = "ALG", Fecha = new DateTime(2026, 11, 17) }, "Carlos"));
            StringAssert.Contains(pasado.Message, "ya ha pasado");
            A.CallTo(() => repositorio.Guardar(A<FechaInventarioDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_SinTabla_503()
        {
            A.CallTo(() => repositorio.TablaCreada()).Returns(false);
            NestoBusinessException ex = await Lanza(() => Servicio(MIERCOLES_8_30).Guardar(new FechaInventarioDTO { Almacen = "ALG", Fecha = SABADO }, "Carlos"));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        [TestMethod]
        public async Task Guardar_DevuelveLaFechaConSusReposiciones()
        {
            A.CallTo(() => repositorio.Guardar(A<FechaInventarioDTO>._, A<string>._)).Returns(Task.FromResult(7));

            FechaInventarioDTO r = await Servicio(MIERCOLES_8_30).Guardar(new FechaInventarioDTO { Almacen = " alg ", Fecha = SABADO.AddHours(10) }, @"NUEVAVISION\Carlos");

            Assert.AreEqual(3, r.Pendientes);
            Assert.AreEqual(new DateTime(2026, 11, 18), r.DiaAviso);
            A.CallTo(() => repositorio.Guardar(A<FechaInventarioDTO>.That.Matches(f => f.Almacen == "ALG" && f.Fecha == SABADO), "Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Decidir_Quitar_PoneLaExcepcionDe867YApuntaLaDecision()
        {
            _ = await Servicio(MIERCOLES_8_30).Decidir(7, new DecisionReposicionInventarioDTO
            {
                Origen = "rei", Destino = "ALG", FechaLlegada = new DateTime(2026, 11, 20), Decision = "quitar"
            }, @"NUEVAVISION\Andre");

            A.CallTo(() => excepciones.Guardar(A<ExcepcionCalendarioReposicionDTO>.That.Matches(e =>
                    e.Origen == "REI" && e.Destino == "ALG" && e.FechaLlegada == new DateTime(2026, 11, 20)
                    && e.Tipo == TiposExcepcionCalendarioReposicion.SIN_REPOSICION && e.Motivo == "Inventario de Algete del 21/11/2026"), @"NUEVAVISION\Andre"))
                .MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => repositorio.GuardarDecision(A<DecisionViajeInventario>.That.Matches(d => d.Decision == "Quitar" && d.Usuario == "Andre")))
                    .MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task Decidir_Mantener_NoTocaElCalendario()
        {
            FechaInventarioDTO r = await Servicio(MIERCOLES_8_30).Decidir(7, new DecisionReposicionInventarioDTO
            {
                Origen = "ALC", Destino = "ALG", FechaLlegada = new DateTime(2026, 11, 20), Decision = "Mantener"
            }, "Andre");

            Assert.IsNotNull(r);
            A.CallTo(() => excepciones.Guardar(A<ExcepcionCalendarioReposicionDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarDecision(A<DecisionViajeInventario>.That.Matches(d => d.Decision == "Mantener" && d.Origen == "ALC"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Decidir_ViajeQueNoTocaODecisionRara()
        {
            NestoBusinessException noToca = await Lanza(() => Servicio(MIERCOLES_8_30).Decidir(7, new DecisionReposicionInventarioDTO
            {
                Origen = "ALG", Destino = "REI", FechaLlegada = new DateTime(2026, 11, 18), Decision = "Mantener"
            }, "Andre"));
            Assert.AreEqual(HttpStatusCode.NotFound, noToca.StatusCode);

            NestoBusinessException rara = await Lanza(() => Servicio(MIERCOLES_8_30).Decidir(7, new DecisionReposicionInventarioDTO { Decision = "Quizás" }, "Andre"));
            StringAssert.Contains(rara.Message, "«Quitar» o «Mantener»");
        }

        [TestMethod]
        public async Task Avisar_ElDiaQueToca_AvisaConLasPendientesYMarca()
        {
            int avisos = await Servicio(MIERCOLES_8_30).Avisar("1");

            Assert.AreEqual(1, avisos);
            A.CallTo(() => avisador.Avisar("1", A<IEnumerable<string>>.That.Matches(a => a.Contains("REI") && a.Contains("ALC") && a.Contains("ALG")),
                A<string>.That.Contains("Algete"), A<string>.That.Contains("siguen activas 3 reposiciones"), 7)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => repositorio.MarcarAvisado(7, MIERCOLES_8_30)).MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task Avisar_AntesDeTiempo_Nada()
        {
            Assert.AreEqual(0, await Servicio(new DateTime(2026, 11, 16, 8, 30, 0)).Avisar("1"));
            A.CallTo(() => avisador.Avisar(A<string>._, A<IEnumerable<string>>._, A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.MarcarAvisado(A<int>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_TodoDecidido_MarcaSinAvisar()
        {
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).Returns(Task.FromResult(new List<ReposicionCalendario>()));

            Assert.AreEqual(0, await Servicio(MIERCOLES_8_30).Avisar("1"));
            A.CallTo(() => avisador.Avisar(A<string>._, A<IEnumerable<string>>._, A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.MarcarAvisado(7, MIERCOLES_8_30)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisar_SinTabla_NadaYNoLanza()
        {
            A.CallTo(() => repositorio.TablaCreada()).Returns(false);
            Assert.AreEqual(0, await Servicio(MIERCOLES_8_30).Avisar("1"));
        }
    }
}
