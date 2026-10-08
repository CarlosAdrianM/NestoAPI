using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#603 (corte 1): el servicio (registro del día, atendidas, uso) y el controlador.</summary>
    [TestClass]
    public class ServicioSugerenciasContactoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 7, 9, 30, 0);
        private static readonly DateTime HOY = AHORA.Date;

        private NVEntities db;
        private DbSet<SugerenciaContacto> fakeSugerencias;
        private DbSet<SeguimientoCliente> fakeRapports;
        private List<SugerenciaContacto> sugerencias;
        private List<SeguimientoCliente> rapports;
        private IRepositorioCarteraContacto repositorio;
        private IProbabilidadesContacto probabilidades;
        private List<ClienteCarteraContacto> cartera;
        private string delegacionConsultada;
        private IBloqueoSugerenciasDelDia bloqueo;
        private IBloqueoDelDia bloqueoAbierto;
        private Action alBloquear;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            sugerencias = new List<SugerenciaContacto>();
            rapports = new List<SeguimientoCliente>();
            fakeSugerencias = A.Fake<DbSet<SugerenciaContacto>>(o => o
                .Implements<IQueryable<SugerenciaContacto>>()
                .Implements<IDbAsyncEnumerable<SugerenciaContacto>>());
            fakeRapports = A.Fake<DbSet<SeguimientoCliente>>(o => o
                .Implements<IQueryable<SeguimientoCliente>>()
                .Implements<IDbAsyncEnumerable<SeguimientoCliente>>());
            ConfigurarFakeDbSet(fakeSugerencias, sugerencias);
            ConfigurarFakeDbSet(fakeRapports, rapports);
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).ReturnsLazily((SugerenciaContacto s) =>
            {
                s.Id = sugerencias.Count + 100;
                sugerencias.Add(s);
                return s;
            });
            A.CallTo(() => db.SugerenciasContacto).Returns(fakeSugerencias);
            A.CallTo(() => db.SeguimientosClientes).Returns(fakeRapports);

            alBloquear = null;
            bloqueoAbierto = A.Fake<IBloqueoDelDia>();
            bloqueo = A.Fake<IBloqueoSugerenciasDelDia>();
            A.CallTo(() => bloqueo.Bloquear(A<string>._, A<DateTime>._)).ReturnsLazily(() =>
            {
                alBloquear?.Invoke();
                return Task.FromResult(bloqueoAbierto);
            });

            cartera = new List<ClienteCarteraContacto>
            {
                Cliente("1001", 40, null),   // Alta (o Máxima con probabilidad)
                Cliente("1002", 6, null),    // Alta
                Cliente("1003", 2, null),    // Media
                Cliente("1004", 0, null, pedidos24: 2), // Baja
                Cliente("1005", 6, 3)        // dentro de su cadencia: no sale
            };
            repositorio = A.Fake<IRepositorioCarteraContacto>();
            A.CallTo(() => repositorio.LeerCartera(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(cartera.Select(Copia).ToList()));
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new ContactosVendedor { Hoy = 2, Semana = 5, Mes = 10 }));
            A.CallTo(() => repositorio.LeerDelegacion(A<string>._)).Returns(Task.FromResult("REI"));
            probabilidades = A.Fake<IProbabilidadesContacto>();
            A.CallTo(() => probabilidades.Leer(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(
                new Dictionary<string, PrediccionContacto>
                {
                    ["1001/0"] = new PrediccionContacto { Probabilidad = 0.8f, GrupoSubgrupoMasVendido = "COSCRE" },
                    ["1002/0"] = new PrediccionContacto { Probabilidad = 0.2f }
                }));
        }

        private static ClienteCarteraContacto Cliente(string id, int pedidos12, int? diasContacto, int pedidos24 = -1) => new ClienteCarteraContacto
        {
            Cliente = id,
            Contacto = "0",
            Nombre = "Centro " + id,
            Telefono = "91000" + id,
            Pedidos12Meses = pedidos12,
            Pedidos24Meses = pedidos24 >= 0 ? pedidos24 : Math.Max(1, pedidos12),
            UltimoPedido = HOY.AddDays(-40),
            UltimoContacto = diasContacto.HasValue ? HOY.AddDays(-diasContacto.Value) : (DateTime?)null
        };

        private static ClienteCarteraContacto Copia(ClienteCarteraContacto c) => (ClienteCarteraContacto)typeof(object)
            .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, null);

        private ServicioSugerenciasContacto Servicio() => new ServicioSugerenciasContacto(db, repositorio, probabilidades, () => AHORA,
            (d, delegacion) => { delegacionConsultada = delegacion; return false; }, null, bloqueo);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        // ---------------- Servicio: lista y registro ----------------

        [TestMethod]
        public async Task Leer_PrimeraVezDelDia_RegistraLasSugerenciasEnOrden()
        {
            SugerenciasContactoDTO respuesta = await Servicio().Leer("mpp", "Llamada", 3, "", "NUEVAVISION\\MariaJose");

            Assert.AreEqual("MPP", respuesta.Vendedor);
            CollectionAssert.AreEqual(new[] { "1001", "1002", "1003" }, respuesta.Sugerencias.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "Máxima", "Alta", "Media" }, respuesta.Sugerencias.Select(s => s.Prioridad).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, respuesta.Sugerencias.Select(s => s.Orden).ToArray());
            Assert.AreEqual(0.8f, respuesta.Sugerencias[0].Probabilidad);
            Assert.AreEqual("COSCRE", respuesta.Sugerencias[0].GrupoSubgrupoMasVendido);
            Assert.AreEqual("910001001", respuesta.Sugerencias[0].Telefono);
            Assert.IsTrue(respuesta.Sugerencias.All(s => s.SugerenciaId >= 100 && !s.Atendida));

            Assert.AreEqual(3, sugerencias.Count);
            Assert.IsTrue(sugerencias.All(s => s.Vendedor == "MPP" && s.Usuario == "NUEVAVISION\\MariaJose" && s.Fecha == AHORA && !string.IsNullOrEmpty(s.Motivo)));
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();

            // Ritmo en vivo: los pendientes son los cuatro a los que les toca, aunque se devuelvan tres.
            Assert.AreEqual(1, respuesta.Ritmo.PendientesMaxima);
            Assert.AreEqual(1, respuesta.Ritmo.PendientesAlta);
            Assert.AreEqual(1, respuesta.Ritmo.PendientesMedia);
            Assert.AreEqual(1, respuesta.Ritmo.PendientesBaja);
            Assert.AreEqual(2, respuesta.Ritmo.ContactosHoy);
            Assert.AreEqual("REI", delegacionConsultada, "festivos de la delegación del vendedor");
        }

        [TestMethod]
        public async Task Leer_SegundaVezDelDia_NoDuplicaYDevuelveLasMismas()
        {
            await Servicio().Leer("MPP", "Llamada", 3, "", "u");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Fake.ClearRecordedCalls(db);
            Fake.ClearRecordedCalls(fakeSugerencias);

            // Entre medias el modelo cambia de opinión: la lista del día se mantiene.
            A.CallTo(() => probabilidades.Leer(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(
                new Dictionary<string, PrediccionContacto> { ["1003/0"] = new PrediccionContacto { Probabilidad = 0.99f } }));

            SugerenciasContactoDTO segunda = await Servicio().Leer("MPP", "Llamada", 3, "", "u");

            CollectionAssert.AreEqual(new[] { "1001", "1002", "1003" }, segunda.Sugerencias.Select(s => s.Cliente).ToArray());
            Assert.AreEqual("Máxima", segunda.Sugerencias[0].Prioridad);
            Assert.AreEqual(3, sugerencias.Count);
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Leer_SegundaVezConRapportDeHoy_LaMarcaAtendidaYDesapareceDeLaListaViva()
        {
            await Servicio().Leer("MPP", "Llamada", 3, "", "u");
            rapports.Add(new SeguimientoCliente { NºOrden = 555, Número = "1002", Contacto = "0", Fecha = AHORA.AddHours(1), Estado = 0 });
            rapports.Add(new SeguimientoCliente { NºOrden = 556, Número = "1003", Contacto = "0", Fecha = HOY.AddDays(-1), Estado = 0 });
            cartera.Single(c => c.Cliente == "1002").UltimoContacto = AHORA.AddHours(1);

            SugerenciasContactoDTO segunda = await Servicio().Leer("MPP", "Llamada", 3, "", "u");

            SugerenciaContactoDTO atendida = segunda.Sugerencias.Single(s => s.Cliente == "1002");
            Assert.IsTrue(atendida.Atendida);
            Assert.AreEqual(0, atendida.DiasDesdeUltimoContacto, "los datos del cliente vienen frescos");
            Assert.IsFalse(segunda.Sugerencias.Single(s => s.Cliente == "1003").Atendida, "el rapport de ayer no cuenta");
            SugerenciaContacto fila = sugerencias.Single(s => s.Cliente == "1002");
            Assert.AreEqual(555, fila.RapportId);
            Assert.AreEqual(AHORA.AddHours(1), fila.FechaAtendida);
            Assert.AreEqual(0, segunda.Ritmo.PendientesAlta, "ya no le toca: no cuenta como pendiente");
        }

        [TestMethod]
        public async Task Leer_PidenMasQueLasRegistradas_AnadeSoloLasQueFaltanSinRepetir()
        {
            await Servicio().Leer("MPP", "Llamada", 2, "", "u");
            Assert.AreEqual(2, sugerencias.Count);

            SugerenciasContactoDTO mas = await Servicio().Leer("MPP", "Llamada", 4, "", "u");

            CollectionAssert.AreEqual(new[] { "1001", "1002", "1003", "1004" }, mas.Sugerencias.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, sugerencias.Select(s => s.Orden).ToArray());
            Assert.AreEqual(4, sugerencias.Select(s => s.Cliente).Distinct().Count());
        }

        [TestMethod]
        public async Task Leer_LasDeOtroDiaUOtroVendedor_NoCuentan()
        {
            sugerencias.Add(new SugerenciaContacto { Id = 1, Vendedor = "MPP", Cliente = "1004", Contacto = "0", Fecha = HOY.AddDays(-1), Prioridad = "Baja", Orden = 1, Motivo = "x" });
            sugerencias.Add(new SugerenciaContacto { Id = 2, Vendedor = "LHY", Cliente = "1004", Contacto = "0", Fecha = AHORA, Prioridad = "Baja", Orden = 1, Motivo = "x" });

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 2, "", "u");

            CollectionAssert.AreEqual(new[] { "1001", "1002" }, respuesta.Sugerencias.Select(s => s.Cliente).ToArray());
            Assert.AreEqual(4, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_DosAperturasALaVez_LaSegundaEncuentraLaListaDentroDelBloqueoYNoDuplica()
        {
            // NestoAPI#603 (07/10/26): Nesto abre Rapports con dos llamadas casi a la vez. Las dos leen la lista vacía;
            // mientras esta espera el bloqueo, la otra registra la lista entera. Dentro del bloqueo hay que volver a leer.
            alBloquear = () =>
            {
                if (!sugerencias.Any())
                {
                    sugerencias.Add(new SugerenciaContacto { Id = 1, Vendedor = "MPP", Cliente = "1001", Contacto = "0", Fecha = AHORA, Prioridad = "Máxima", Orden = 1, Motivo = "x" });
                    sugerencias.Add(new SugerenciaContacto { Id = 2, Vendedor = "MPP", Cliente = "1002", Contacto = "0", Fecha = AHORA, Prioridad = "Alta", Orden = 2, Motivo = "x" });
                    sugerencias.Add(new SugerenciaContacto { Id = 3, Vendedor = "MPP", Cliente = "1003", Contacto = "0", Fecha = AHORA, Prioridad = "Media", Orden = 3, Motivo = "x" });
                }
            };

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 3, "", "u");

            Assert.AreEqual(3, sugerencias.Count, "una sola lista en la tabla");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, respuesta.Sugerencias.Select(s => s.SugerenciaId).ToArray());
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).MustNotHaveHappened();
            A.CallTo(() => bloqueo.Bloquear("MPP", HOY)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Leer_PrimeraVezDelDia_RegistraDentroDelBloqueoYLoConfirma()
        {
            await Servicio().Leer("MPP", "Llamada", 3, "", "u");

            Assert.AreEqual(3, sugerencias.Count);
            A.CallTo(() => bloqueo.Bloquear("MPP", HOY)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => bloqueoAbierto.Confirmar()).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => bloqueoAbierto.Dispose()).MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task Leer_ConDuplicadosHistoricos_DevuelveCadaClienteUnaVezConLaFilaDeMenorId()
        {
            // Lo que quedó en la tabla el 07/10/26 antes del fix: la lista entera dos veces, con el mismo Orden.
            int id = 0;
            foreach (int vuelta in new[] { 1, 2 })
            {
                int orden = 0;
                foreach (string cliente in new[] { "1001", "1002", "1003" })
                {
                    sugerencias.Add(new SugerenciaContacto { Id = ++id, Vendedor = "MPP", Cliente = cliente, Contacto = "0", Fecha = AHORA, Prioridad = "Alta", Orden = ++orden, Motivo = "x" });
                }
            }

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 3, "", "u");

            CollectionAssert.AreEqual(new[] { "1001", "1002", "1003" }, respuesta.Sugerencias.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, respuesta.Sugerencias.Select(s => s.SugerenciaId).ToArray());
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).MustNotHaveHappened();
        }

        // ---------------- Número del día: al menos el objetivo de hoy ----------------

        /// <summary>
        /// Octubre de 2026 tiene 18 laborables del 7 al 30: n clientes que compran cada semana (52 pedidos, cadencia 7)
        /// suman 3 contactos al mes cada uno, sin contactos todavía → ObjetivoHoy = ⌈3n / 18⌉.
        /// </summary>
        private void CarteraConObjetivoHoy(int clientes)
        {
            cartera = Enumerable.Range(1, clientes).Select(i => Cliente((20000 + i).ToString(), 52, null)).ToList();
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new ContactosVendedor()));
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyMayorQueElNumeroPedido_RegistraYDevuelveElObjetivo()
        {
            CarteraConObjetivoHoy(187); // 561 / 18 → 32

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(32, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(32, respuesta.Sugerencias.Count);
            Assert.AreEqual(32, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyMenorQueElNumeroPedido_SeQuedaElNumeroPedido()
        {
            CarteraConObjetivoHoy(55); // 165 / 18 → 10

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(10, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(20, respuesta.Sugerencias.Count);
            Assert.AreEqual(20, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyCero_SeQuedaElNumeroPedido()
        {
            CarteraConObjetivoHoy(30);
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new ContactosVendedor { Mes = 500 }));

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(0, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(20, respuesta.Sugerencias.Count);
            Assert.AreEqual(20, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyPorEncimaDelMaximo_SeQuedaEnElMaximo()
        {
            CarteraConObjetivoHoy(1250); // 3750 / 18 → 209

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(209, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(ServicioSugerenciasContacto.NUMERO_MAXIMO, respuesta.Sugerencias.Count);
            Assert.AreEqual(ServicioSugerenciasContacto.NUMERO_MAXIMO, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ElObjetivoDeHoySubeTrasRegistrarLaLista_LaCompletaSinRepetir()
        {
            CarteraConObjetivoHoy(55); // 10: se registran las 20 pedidas
            await Servicio().Leer("MPP", "Llamada", 20, "", "u");
            Assert.AreEqual(20, sugerencias.Count);

            CarteraConObjetivoHoy(187); // 32
            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(32, respuesta.Sugerencias.Count);
            Assert.AreEqual(32, sugerencias.Count);
            Assert.AreEqual(32, sugerencias.Select(s => s.Cliente).Distinct().Count());
            CollectionAssert.AreEqual(Enumerable.Range(1, 32).ToArray(), sugerencias.Select(s => s.Orden).ToArray());
        }

        [TestMethod]
        public async Task Leer_SinVendedor_ArgumentException()
        {
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Servicio().Leer(" ", "Llamada", 20, "", "u"));
        }

        // ---------------- Rapport → atendida ----------------

        [TestMethod]
        public async Task MarcarAtendidaPorRapport_SugerenciaDelMismoDia_AtendidaConElRapport()
        {
            sugerencias.Add(new SugerenciaContacto { Id = 1, Vendedor = "MPP", Cliente = "1002", Contacto = "0", Fecha = AHORA, Prioridad = "Alta", Orden = 1, Motivo = "x" });
            sugerencias.Add(new SugerenciaContacto { Id = 2, Vendedor = "MPP", Cliente = "1002", Contacto = "0", Fecha = HOY.AddDays(-1), Prioridad = "Alta", Orden = 1, Motivo = "x" });
            sugerencias.Add(new SugerenciaContacto { Id = 3, Vendedor = "MPP", Cliente = "1002", Contacto = "1", Fecha = AHORA, Prioridad = "Alta", Orden = 2, Motivo = "x" });
            DateTime fechaRapport = AHORA.AddHours(2);

            int marcadas = await new RegistroSugerenciasContacto(db).MarcarAtendidaPorRapport("1002      ", "0  ", fechaRapport, 777);

            Assert.AreEqual(1, marcadas);
            Assert.IsTrue(sugerencias[0].Atendida);
            Assert.AreEqual(777, sugerencias[0].RapportId);
            Assert.AreEqual(fechaRapport, sugerencias[0].FechaAtendida);
            Assert.IsFalse(sugerencias[1].Atendida, "otro día");
            Assert.IsFalse(sugerencias[2].Atendida, "otro contacto");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task MarcarAtendidaPorRapport_SinSugerencia_NoGuardaNada()
        {
            int marcadas = await new RegistroSugerenciasContacto(db).MarcarAtendidaPorRapport("1002", "0", AHORA, 777);

            Assert.AreEqual(0, marcadas);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        // ---------------- Uso ----------------

        [TestMethod]
        public async Task LeerUso_PorVendedor_DiasSugeridasAtendidasYRapports()
        {
            sugerencias.AddRange(new[]
            {
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 5, 9, 0, 0), Atendida = true },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 5, 9, 0, 0), Atendida = false },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 6, 9, 0, 0), Atendida = true },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 6, 9, 0, 0), Atendida = true },
                new SugerenciaContacto { Vendedor = "PA", Fecha = new DateTime(2026, 10, 6, 10, 0, 0), Atendida = false },
                new SugerenciaContacto { Vendedor = "PA", Fecha = new DateTime(2026, 9, 30, 10, 0, 0), Atendida = true } // fuera del periodo
            });
            rapports.AddRange(new[]
            {
                new SeguimientoCliente { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 5, 12, 0, 0), Estado = 0 },
                new SeguimientoCliente { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 6, 12, 0, 0), Estado = 1 },
                new SeguimientoCliente { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 6, 13, 0, 0), Estado = 2 }, // gestión administrativa
                new SeguimientoCliente { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 6, 14, 0, 0), Estado = 0 },
                new SeguimientoCliente { Vendedor = "LHY", Fecha = new DateTime(2026, 10, 6, 14, 0, 0), Estado = 0 }
            });

            List<UsoSugerenciasContactoDTO> uso = await Servicio().LeerUso(new DateTime(2026, 10, 1), new DateTime(2026, 10, 6));

            Assert.AreEqual(2, uso.Count);
            UsoSugerenciasContactoDTO mpp = uso.Single(u => u.Vendedor == "MPP");
            Assert.AreEqual(2, mpp.DiasConSugerencias);
            Assert.AreEqual(4, mpp.Sugeridas);
            Assert.AreEqual(3, mpp.Atendidas);
            Assert.AreEqual(0.75, mpp.PorcentajeAtendidas);
            Assert.AreEqual(3, mpp.RapportsTotales);
            UsoSugerenciasContactoDTO pa = uso.Single(u => u.Vendedor == "PA");
            Assert.AreEqual(1, pa.Sugeridas);
            Assert.AreEqual(0, pa.Atendidas);
            Assert.AreEqual(0, pa.RapportsTotales);
        }

        // ---------------- Caché del modelo ----------------

        [TestMethod]
        public void ProbabilidadesModelo_LaConsultaSeCacheaPorVendedorYDia_ElTipoYElGrupoSoloEnLaPrediccion()
        {
            var manana = new DateTime(2026, 10, 7, 10, 0, 0);
            var tarde = new DateTime(2026, 10, 7, 16, 0, 0);
            Assert.AreEqual(ProbabilidadesContactoModelo.ClaveCacheConsulta("mpp", manana), ProbabilidadesContactoModelo.ClaveCacheConsulta("MPP ", tarde),
                "el tipo de interacción y la hora ya no cambian la consulta");
            Assert.AreNotEqual(ProbabilidadesContactoModelo.ClaveCacheConsulta("MPP", manana), ProbabilidadesContactoModelo.ClaveCacheConsulta("MPP", manana.AddDays(1)));
            Assert.AreEqual(ProbabilidadesContactoModelo.ClaveCache("MPP", "", "", manana), ProbabilidadesContactoModelo.ClaveCache("MPP", "Teléfono", "", manana),
                "vacío y Teléfono son Llamada");
            Assert.AreNotEqual(ProbabilidadesContactoModelo.ClaveCache("MPP", "Llamada", "", manana), ProbabilidadesContactoModelo.ClaveCache("MPP", "Visita", "", manana));
            Assert.AreNotEqual(ProbabilidadesContactoModelo.ClaveCache("MPP", "Llamada", "COSCRE", manana), ProbabilidadesContactoModelo.ClaveCache("MPP", "Llamada", "PELTIN", manana));
            Assert.AreNotEqual(ProbabilidadesContactoModelo.ClaveCache("MPP", "Llamada", "", manana), ProbabilidadesContactoModelo.ClaveCache("MPP", "Llamada", "", tarde),
                "EsPorLaTarde es feature: por la tarde se vuelve a puntuar");
        }

        // ---------------- Controlador ----------------

        [TestMethod]
        public async Task Controlador_SinVendedor_400()
        {
            var servicio = A.Fake<IServicioSugerenciasContacto>();
            var controlador = new SugerenciasContactoController(db, servicio) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\MariaJose") };

            Assert.IsInstanceOfType(await controlador.GetSugerenciasContacto(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await controlador.GetSugerenciasContacto("  "), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.Leer(A<string>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_ConVendedor_200ConLaFormaDelContrato()
        {
            var controlador = new SugerenciasContactoController(db, Servicio()) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\MariaJose") };

            var resultado = await controlador.GetSugerenciasContacto("MPP", "Llamada", 2) as OkNegotiatedContentResult<SugerenciasContactoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("MPP", resultado.Content.Vendedor);
            Assert.AreEqual(AHORA, resultado.Content.Fecha);
            Assert.IsNotNull(resultado.Content.Ritmo);
            Assert.IsFalse(string.IsNullOrEmpty(resultado.Content.Ritmo.Frase));
            Assert.AreEqual(2, resultado.Content.Sugerencias.Count);
            Assert.AreEqual("NUEVAVISION\\MariaJose", sugerencias[0].Usuario, "usuario del Identity");

            // Los nombres que Nesto ya lee de ClienteProbabilidadVenta (sin distinguir mayúsculas) siguen en el JSON.
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(resultado.Content.Sugerencias[0]);
            foreach (string campo in new[] { "\"Cliente\"", "\"Contacto\"", "\"Nombre\"", "\"Direccion\"", "\"Poblacion\"", "\"Telefono\"",
                "\"Probabilidad\"", "\"DiasDesdeUltimoPedido\"", "\"DiasDesdeUltimaInteraccion\"", "\"Prioridad\"", "\"Motivo\"", "\"SugerenciaId\"" })
            {
                StringAssert.Contains(json, campo);
            }
        }

        [TestMethod]
        public async Task Controlador_UsoSinPermiso_403()
        {
            var servicio = A.Fake<IServicioSugerenciasContacto>();
            var controlador = new SugerenciasContactoController(db, servicio) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\MariaJose", "Ventas") };

            var resultado = await controlador.GetUso() as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => servicio.LeerUso(A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_UsoDireccion_200()
        {
            var servicio = A.Fake<IServicioSugerenciasContacto>();
            A.CallTo(() => servicio.LeerUso(A<DateTime>._, A<DateTime>._)).Returns(Task.FromResult(new List<UsoSugerenciasContactoDTO> { new UsoSugerenciasContactoDTO { Vendedor = "MPP" } }));
            var controlador = new SugerenciasContactoController(db, servicio) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\Carlos", "Dirección") };

            var resultado = await controlador.GetUso(new DateTime(2026, 10, 1), new DateTime(2026, 10, 7)) as OkNegotiatedContentResult<List<UsoSugerenciasContactoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("MPP", resultado.Content.Single().Vendedor);
            A.CallTo(() => servicio.LeerUso(new DateTime(2026, 10, 1), new DateTime(2026, 10, 7))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_UsoHastaAnteriorADesde_400()
        {
            var controlador = new SugerenciasContactoController(db, A.Fake<IServicioSugerenciasContacto>()) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\Carlos", "Informática") };

            Assert.IsInstanceOfType(await controlador.GetUso(new DateTime(2026, 10, 7), new DateTime(2026, 10, 1)), typeof(BadRequestErrorMessageResult));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> datos) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
        }
    }
}
