using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
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
            // Sugerencia 782: hoy 1001 (dos rapports) y 1002, el lunes 1003, el día 2 1004, y hoy uno que no es de la cartera.
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Contactos(
                Hablo("1001", HOY), Hablo("1001", HOY), Hablo("1002", HOY), Hablo("1003", new DateTime(2026, 10, 5)),
                Hablo("1004", new DateTime(2026, 10, 2)), Hablo("9999", HOY)));
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

        private static Task<List<ContactoClienteDia>> Contactos(params ContactoClienteDia[] contactos) => Task.FromResult(contactos.ToList());

        private static ContactoClienteDia Hablo(string cliente, DateTime dia) => new ContactoClienteDia { Cliente = cliente, Contacto = "0", Dia = dia };

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
            Assert.AreEqual(2, respuesta.Ritmo.ContactosHoy, "clientes distintos de la cartera, no rapports");
            Assert.AreEqual(3, respuesta.Ritmo.ContactosSemana);
            Assert.AreEqual(4, respuesta.Ritmo.ContactosMes);
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

            // Los mismos tres (con su prioridad registrada); NestoAPI#677: el orden sigue a la probabilidad de esta llamada.
            CollectionAssert.AreEqual(new[] { "1003", "1001", "1002" }, segunda.Sugerencias.Select(s => s.Cliente).ToArray());
            Assert.AreEqual("Máxima", segunda.Sugerencias.Single(s => s.Cliente == "1001").Prioridad);
            Assert.AreEqual(3, sugerencias.Count);
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Leer_CambiaElTipoDeInteraccion_MismaListaDelDiaConPorcentajesYOrdenDelNuevoFiltro()
        {
            // NestoAPI#617: la lista del día es fija, pero porcentajes, motivo y orden siguen al filtro de cada llamada.
            cartera = new List<ClienteCarteraContacto> { Cliente("1001", 40, null), Cliente("1002", 6, null), Cliente("1006", 6, null) };
            A.CallTo(() => probabilidades.Leer(A<string>._, A<string>._, A<string>._)).ReturnsLazily((string v, string tipo, string grupo) =>
                Task.FromResult(tipo == "Visita"
                    ? new Dictionary<string, PrediccionContacto>
                    {
                        ["1001/0"] = new PrediccionContacto { Probabilidad = 0.8f },
                        ["1002/0"] = new PrediccionContacto { Probabilidad = 0.3f },
                        ["1006/0"] = new PrediccionContacto { Probabilidad = 0.1f }
                    }
                    : new Dictionary<string, PrediccionContacto>
                    {
                        ["1001/0"] = new PrediccionContacto { Probabilidad = 0.7f, GrupoSubgrupoMasVendido = "COSCRE" },
                        ["1002/0"] = new PrediccionContacto { Probabilidad = 0.1f },
                        ["1006/0"] = new PrediccionContacto { Probabilidad = 0.4f }
                    }));

            SugerenciasContactoDTO visita = await Servicio().Leer("MPP", "Visita", 3, "", "u");
            var filasAntes = sugerencias.Select(s => new { s.Id, s.Orden, s.Probabilidad, s.Motivo }).ToList();
            SugerenciasContactoDTO telefono = await Servicio().Leer("MPP", "Teléfono", 3, "", "u");

            CollectionAssert.AreEqual(new[] { "1001", "1002", "1006" }, visita.Sugerencias.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "1001", "1006", "1002" }, telefono.Sugerencias.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, telefono.Sugerencias.Select(s => s.Orden).ToArray());
            CollectionAssert.AreEquivalent(visita.Sugerencias.Select(s => s.SugerenciaId).ToArray(), telefono.Sugerencias.Select(s => s.SugerenciaId).ToArray());
            CollectionAssert.AreEqual(new[] { "Máxima", "Alta", "Alta" }, telefono.Sugerencias.Select(s => s.Prioridad).ToArray());
            CollectionAssert.AreEqual(new[] { 0.7f, 0.4f, 0.1f }, telefono.Sugerencias.Select(s => s.Probabilidad).ToArray());
            Assert.AreEqual("COSCRE", telefono.Sugerencias[0].GrupoSubgrupoMasVendido);
            StringAssert.Contains(visita.Sugerencias[0].Motivo, "80 %");
            StringAssert.Contains(telefono.Sugerencias[0].Motivo, "70 %");

            // La BD no cambia: ni se añade nadie ni se toca el Orden ni la Probabilidad registrados.
            Assert.AreEqual(3, sugerencias.Count);
            CollectionAssert.AreEqual(filasAntes.Select(f => f.Id + "|" + f.Orden + "|" + f.Probabilidad + "|" + f.Motivo).ToArray(),
                sugerencias.Select(s => s.Id + "|" + s.Orden + "|" + s.Probabilidad + "|" + s.Motivo).ToArray());
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
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
        public async Task Leer_UnJefeMiraLaListaDeOtroVendedor_EsLaMismaListaDelDiaYNoMarcaNadaComoAtendido()
        {
            // Nesto#521: la clave es vendedor + día. Si Alberto (ASH) abre la de David (DV) antes que él, se registra LA DE
            // DAVID (Usuario = quien la pidió) y David ve después esa misma, sin otra lista ni atendidas. A ASH no se le crea nada.
            SugerenciasContactoDTO delJefe = await Servicio().Leer("DV", "Llamada", 3, "", "NUEVAVISION\\Sancho");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Fake.ClearRecordedCalls(db);
            Fake.ClearRecordedCalls(fakeSugerencias);

            SugerenciasContactoDTO delVendedor = await Servicio().Leer("DV", "Llamada", 3, "", "NUEVAVISION\\David");
            SugerenciasContactoDTO otraVezDelJefe = await Servicio().Leer("dv", "Llamada", 3, "", "NUEVAVISION\\Sancho");

            Assert.AreEqual(3, sugerencias.Count, "una sola lista");
            Assert.IsTrue(sugerencias.All(s => s.Vendedor == "DV" && s.Usuario == "NUEVAVISION\\Sancho"), "la de David, pedida por Sancho");
            Assert.IsTrue(sugerencias.All(s => !s.Atendida && s.RapportId == null), "mirarla no la atiende");
            CollectionAssert.AreEqual(delJefe.Sugerencias.Select(s => s.SugerenciaId).ToArray(), delVendedor.Sugerencias.Select(s => s.SugerenciaId).ToArray());
            CollectionAssert.AreEqual(delJefe.Sugerencias.Select(s => s.SugerenciaId).ToArray(), otraVezDelJefe.Sugerencias.Select(s => s.SugerenciaId).ToArray());
            Assert.IsTrue(delVendedor.Sugerencias.All(s => !s.Atendida));
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            A.CallTo(() => bloqueo.Bloquear("DV", HOY)).MustHaveHappenedOnceOrMore();
            A.CallTo(() => bloqueo.Bloquear(A<string>.That.Matches(v => v != "DV"), A<DateTime>._)).MustNotHaveHappened();
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

        // ---------------- NestoAPI#593: cheque regalo sin usar en los últimos días ----------------

        private ServicioSugerenciasContacto ServicioConCheques(IChequesRegaloParaSugerencias cheques) => new ServicioSugerenciasContacto(db, repositorio,
            probabilidades, () => AHORA, (d, delegacion) => false, null, bloqueo, cheques);

        private static IChequesRegaloParaSugerencias ChequesDe(params string[] clientes)
        {
            IChequesRegaloParaSugerencias cheques = A.Fake<IChequesRegaloParaSugerencias>();
            A.CallTo(() => cheques.LeerImpulsos("MPP", HOY)).Returns(Task.FromResult(clientes.Select(c => new ChequeRegaloImpulso
            {
                Cliente = c,
                Contacto = "0",
                InicioImpulso = HOY.AddDays(-2),
                CanjeHasta = HOY.AddDays(7),
                Frase = "Tiene un cheque de 50 € sin usar hasta el 14/10"
            }).ToList()));
            return cheques;
        }

        [TestMethod]
        public async Task Leer_ClienteConChequeSinUsar_SaleElPrimeroEnMaximaConLaFraseAunqueNoLeTocara()
        {
            SugerenciasContactoDTO respuesta = await ServicioConCheques(ChequesDe("1005")).Leer("mpp", "Llamada", 3, "", "u");

            CollectionAssert.AreEqual(new[] { "1005", "1001", "1002" }, respuesta.Sugerencias.Select(s => s.Cliente).ToArray());
            SugerenciaContactoDTO conCheque = respuesta.Sugerencias[0];
            Assert.AreEqual("Máxima", conCheque.Prioridad);
            Assert.IsTrue(conCheque.TieneChequeRegalo);
            Assert.AreEqual("Tiene un cheque de 50 € sin usar hasta el 14/10", conCheque.Motivo);
            Assert.AreEqual(1, conCheque.Orden);
            Assert.AreEqual("1005", sugerencias.Single(s => s.Orden == 1).Cliente, "registrada la primera de la lista del día");
            Assert.AreEqual(2, respuesta.Ritmo.PendientesMaxima);

            // Segunda apertura: la lista del día es la misma y sigue delante, con la frase.
            SugerenciasContactoDTO segunda = await ServicioConCheques(ChequesDe("1005")).Leer("MPP", "Llamada", 3, "", "u");
            Assert.AreEqual("1005", segunda.Sugerencias[0].Cliente);
            Assert.IsTrue(segunda.Sugerencias[0].TieneChequeRegalo);
            Assert.AreEqual(3, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ClienteConChequeAlQueYaLeTocaba_DelanteDeLasOtrasMaximaYConLaFraseDelante()
        {
            SugerenciasContactoDTO respuesta = await ServicioConCheques(ChequesDe("1003")).Leer("MPP", "Llamada", 4, "", "u");

            CollectionAssert.AreEqual(new[] { "1003", "1001", "1002", "1004" }, respuesta.Sugerencias.Select(s => s.Cliente).ToArray());
            StringAssert.StartsWith(respuesta.Sugerencias[0].Motivo, "Tiene un cheque de 50 € sin usar hasta el 14/10. Ha comprado");
        }

        [TestMethod]
        public async Task Leer_FallanLosCheques_LaListaSaleIgualSinImpulso()
        {
            IChequesRegaloParaSugerencias cheques = A.Fake<IChequesRegaloParaSugerencias>();
            A.CallTo(() => cheques.LeerImpulsos(A<string>._, A<DateTime>._)).ThrowsAsync(new InvalidOperationException("sin BD"));

            SugerenciasContactoDTO respuesta = await ServicioConCheques(cheques).Leer("MPP", "Llamada", 3, "", "u");

            CollectionAssert.AreEqual(new[] { "1001", "1002", "1003" }, respuesta.Sugerencias.Select(s => s.Cliente).ToArray());
            Assert.IsFalse(respuesta.Sugerencias.Any(s => s.TieneChequeRegalo));
        }

        // ---------------- Número del día: al menos el objetivo de hoy ----------------

        /// <summary>
        /// Octubre de 2026 tiene 18 laborables del 7 al 30. Sugerencia 782 (26/10/26): el objetivo de hoy reparte las llamadas que
        /// faltan; n semanales (4 llamadas cada uno) sin contactos todavía → ObjetivoHoy = ⌈4n / 18⌉ (el objetivo del mes, n clientes).
        /// </summary>
        private void CarteraConObjetivoHoy(int clientes)
        {
            cartera = Enumerable.Range(1, clientes).Select(i => Cliente((20000 + i).ToString(), 52, null)).ToList();
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Contactos());
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyMayorQueElNumeroPedido_RegistraYDevuelveElObjetivo()
        {
            CarteraConObjetivoHoy(144); // 144 × 4 = 576 llamadas / 18 → 32

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(32, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(32, respuesta.Sugerencias.Count);
            Assert.AreEqual(32, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyMenorQueElNumeroPedido_SeQuedaElNumeroPedido()
        {
            CarteraConObjetivoHoy(45); // 45 × 4 = 180 llamadas / 18 → 10

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(10, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(20, respuesta.Sugerencias.Count);
            Assert.AreEqual(20, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyCero_SeQuedaElNumeroPedido()
        {
            // Sugerencia 782: 30 clientes de una llamada al mes, todos contactados ya este mes → objetivo cubierto.
            cartera = Enumerable.Range(1, 30).Select(i => Cliente((20000 + i).ToString(), 2, null)).ToList();
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Contactos(
                cartera.Select(c => Hablo(c.Cliente, new DateTime(2026, 10, 2))).ToArray()));

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(0, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(20, respuesta.Sugerencias.Count);
            Assert.AreEqual(20, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ObjetivoHoyPorEncimaDelMaximo_SeQuedaEnElMaximo()
        {
            CarteraConObjetivoHoy(940); // 940 × 4 = 3760 llamadas / 18 → 209

            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(209, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(ServicioSugerenciasContacto.NUMERO_MAXIMO, respuesta.Sugerencias.Count);
            Assert.AreEqual(ServicioSugerenciasContacto.NUMERO_MAXIMO, sugerencias.Count);
        }

        [TestMethod]
        public async Task Leer_ElObjetivoDeHoySubeTrasRegistrarLaLista_LaCompletaSinRepetir()
        {
            CarteraConObjetivoHoy(45); // 10: se registran las 20 pedidas
            await Servicio().Leer("MPP", "Llamada", 20, "", "u");
            Assert.AreEqual(20, sugerencias.Count);

            CarteraConObjetivoHoy(144); // 32
            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(32, respuesta.Sugerencias.Count);
            Assert.AreEqual(32, sugerencias.Count);
            Assert.AreEqual(32, sugerencias.Select(s => s.Cliente).Distinct().Count());
            CollectionAssert.AreEqual(Enumerable.Range(1, 32).ToArray(), sugerencias.Select(s => s.Orden).ToArray());
        }

        [TestMethod]
        public async Task Leer_ElObjetivoDeHoyBajaTrasRegistrarLaLista_SeSiguenDevolviendoTodasLasRegistradas()
        {
            CarteraConObjetivoHoy(144); // a las 9: 576 llamadas / 18 → 32
            await Servicio().Leer("MPP", "Llamada", 20, "", "u");
            Assert.AreEqual(32, sugerencias.Count);
            Fake.ClearRecordedCalls(fakeSugerencias);

            // A las 12 ya ha hablado una vez con 72 de los semanales: faltan 576 − 72 = 504 llamadas / 18 → 28.
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(Contactos(
                cartera.Take(72).Select(c => Hablo(c.Cliente, new DateTime(2026, 10, 2))).ToArray()));
            SugerenciasContactoDTO respuesta = await Servicio().Leer("MPP", "Llamada", 20, "", "u");

            Assert.AreEqual(28, respuesta.Ritmo.ObjetivoHoy);
            Assert.AreEqual(32, respuesta.Sugerencias.Count, "lo registrado en el día no desaparece");
            Assert.AreEqual(32, sugerencias.Count);
            A.CallTo(() => fakeSugerencias.Add(A<SugerenciaContacto>._)).MustNotHaveHappened();
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

        // ---------------- NestoAPI#620: un cliente con dos vendedores (estética y peluquería) ----------------

        /// <summary>1002/0: MPP por la ficha (estética) e IF por peluquería (VendedoresClienteGrupoProducto), con relleno de char.</summary>
        private void ClienteConDosVendedores()
        {
            // En memoria "1" ≠ "1  " (en SQL sí casan): la empresa va sin relleno; el vendedor de grupo, con él.
            var fichas = new List<Cliente> { new Cliente { Empresa = "1", Nº_Cliente = "1002", Contacto = "0", Vendedor = "MPP" } };
            var porGrupo = new List<VendedorClienteGrupoProducto>
            {
                new VendedorClienteGrupoProducto { Empresa = "1", Cliente = "1002", Contacto = "0", GrupoProducto = "PEL", Vendedor = "IF " }
            };
            DbSet<Cliente> fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            DbSet<VendedorClienteGrupoProducto> fakeGrupos = A.Fake<DbSet<VendedorClienteGrupoProducto>>(o => o
                .Implements<IQueryable<VendedorClienteGrupoProducto>>().Implements<IDbAsyncEnumerable<VendedorClienteGrupoProducto>>());
            ConfigurarFakeDbSet(fakeClientes, fichas);
            ConfigurarFakeDbSet(fakeGrupos, porGrupo);
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(fakeGrupos);
            sugerencias.Add(new SugerenciaContacto { Id = 1, Vendedor = "MPP", Cliente = "1002", Contacto = "0", Fecha = AHORA, Prioridad = "Alta", Orden = 1, Motivo = "x" });
            sugerencias.Add(new SugerenciaContacto { Id = 2, Vendedor = "IF", Cliente = "1002", Contacto = "0", Fecha = AHORA, Prioridad = "Alta", Orden = 1, Motivo = "x" });
        }

        [TestMethod]
        public async Task MarcarAtendidaPorRapport_DelVendedorDePeluqueria_SoloAtiendeLaSuya()
        {
            ClienteConDosVendedores();

            int marcadas = await new RegistroSugerenciasContacto(db).MarcarAtendidaPorRapport("1002", "0", AHORA.AddHours(1), 777, "IF ");

            Assert.AreEqual(1, marcadas);
            Assert.IsTrue(sugerencias.Single(s => s.Vendedor == "IF").Atendida);
            Assert.IsFalse(sugerencias.Single(s => s.Vendedor == "MPP").Atendida, "lo ha llamado el de peluquería: el de estética sigue teniéndolo pendiente");
        }

        [TestMethod]
        public async Task MarcarAtendidaPorRapport_SinVendedor_AtiendeLasDos()
        {
            // Alguien que no lleva al cliente (el rapport se guarda sin vendedor): se atendió al cliente, como antes de #620.
            ClienteConDosVendedores();

            int marcadas = await new RegistroSugerenciasContacto(db).MarcarAtendidaPorRapport("1002", "0", AHORA.AddHours(1), 777, null);

            Assert.AreEqual(2, marcadas);
            Assert.IsTrue(sugerencias.All(s => s.Atendida));
        }

        [TestMethod]
        public async Task ConciliarAtendidas_RapportDelVendedorDeEstetica_NoAtiendeLaDelDePeluqueria()
        {
            ClienteConDosVendedores();
            rapports.Add(new SeguimientoCliente { NºOrden = 55, Número = "1002", Contacto = "0", Fecha = AHORA.AddHours(1), Estado = 0, Vendedor = "MPP" });
            var registro = new RegistroSugerenciasContacto(db, bloqueo);

            bool cambios = await registro.ConciliarAtendidas(sugerencias, HOY);

            Assert.IsTrue(cambios);
            SugerenciaContacto deEstetica = sugerencias.Single(s => s.Vendedor == "MPP");
            Assert.IsTrue(deEstetica.Atendida);
            Assert.AreEqual(55, deEstetica.RapportId);
            Assert.IsFalse(sugerencias.Single(s => s.Vendedor == "IF").Atendida);
        }

        [TestMethod]
        public void RapportCuentaPara_SoloNoCuentaElDelOtroVendedorDelCliente()
        {
            var delCliente = new HashSet<string> { "MPP", "IF" };

            Assert.IsTrue(RegistroSugerenciasContacto.RapportCuentaPara("IF", "IF ", delCliente), "el suyo");
            Assert.IsFalse(RegistroSugerenciasContacto.RapportCuentaPara("IF", "mpp", delCliente), "el del otro vendedor del cliente");
            Assert.IsTrue(RegistroSugerenciasContacto.RapportCuentaPara("IF", null, delCliente), "sin vendedor");
            Assert.IsTrue(RegistroSugerenciasContacto.RapportCuentaPara("IF", "NV", delCliente), "el general");
            Assert.IsTrue(RegistroSugerenciasContacto.RapportCuentaPara("IF", "PA", delCliente), "alguien que ya no lo lleva");
            Assert.IsTrue(RegistroSugerenciasContacto.RapportCuentaPara("IF", "MPP", null), "sin saber quién lo lleva, como antes");
        }

        // ---------------- NestoAPI#677: orden por probabilidad y cupo de cobertura ----------------

        private ServicioSugerenciasContacto ServicioEl(DateTime ahora) => new ServicioSugerenciasContacto(db, repositorio, probabilidades, () => ahora,
            (d, delegacion) => false, null, bloqueo);

        private static ClienteCarteraContacto ClienteContactado(string id, int pedidos12, DateTime ultimoContacto) => new ClienteCarteraContacto
        {
            Cliente = id,
            Contacto = "0",
            Nombre = "Centro " + id,
            Pedidos12Meses = pedidos12,
            Pedidos24Meses = Math.Max(1, pedidos12),
            UltimoPedido = new DateTime(2026, 8, 1),
            UltimoContacto = ultimoContacto
        };

        /// <summary>Sugerencia 782: los rapports del vendedor son los de la cartera con contacto en octubre, en su día.</summary>
        private Task<List<ContactoClienteDia>> ContactadosEsteMes() => Contactos(cartera
            .Where(c => c.UltimoContacto >= new DateTime(2026, 10, 1))
            .Select(c => Hablo(c.Cliente, c.UltimoContacto.Value))
            .ToArray());

        [TestMethod]
        public async Task Leer_UltimoLaborableDelMes_EntranTodosLosCompradoresSinContactoYSeRegistraElOrigen()
        {
            // Viernes 30/10/26: tres semanales ya contactados este mes, muy probables, y 30 compradores sin contacto en octubre.
            DateTime ahora = new DateTime(2026, 10, 30, 9, 0, 0);
            cartera = new List<ClienteCarteraContacto>
            {
                ClienteContactado("s1", 52, new DateTime(2026, 10, 20)),
                ClienteContactado("s2", 52, new DateTime(2026, 10, 20)),
                ClienteContactado("s3", 52, new DateTime(2026, 10, 20))
            };
            cartera.AddRange(Enumerable.Range(1, 30).Select(i => ClienteContactado("p" + i, 2, new DateTime(2026, 9, 1).AddDays(i % 20))));
            A.CallTo(() => probabilidades.Leer(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(
                new Dictionary<string, PrediccionContacto>
                {
                    ["s1/0"] = new PrediccionContacto { Probabilidad = 0.9f },
                    ["s2/0"] = new PrediccionContacto { Probabilidad = 0.8f },
                    ["s3/0"] = new PrediccionContacto { Probabilidad = 0.7f }
                }));
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(ContactadosEsteMes());

            SugerenciasContactoDTO respuesta = await ServicioEl(ahora).Leer("MPP", "Llamada", 20, "", "u");

            // Sugerencia 782 (26/10/26): mes en clientes (3 de 33) y objetivo de hoy con las llamadas que faltan: a los 3
            // semanales contactados una vez les faltan 3 a cada uno y a los 30 sin contactar 1: 39, todas el último laborable.
            // Piden más sitios que el cupo, así que también entran los semanales (por probabilidad).
            Assert.AreEqual(3, respuesta.Ritmo.ContactosMes);
            Assert.AreEqual(33, respuesta.Ritmo.ObjetivoMes);
            Assert.AreEqual(39, respuesta.Ritmo.ObjetivoHoy, "no pasa de las llamadas que quedan");
            Assert.AreEqual(30, respuesta.Ritmo.CompradoresSinContactoMes);
            Assert.AreEqual(30, respuesta.Ritmo.CupoCoberturaHoy, "último laborable: todos los que faltan");
            Assert.IsTrue(Enumerable.Range(1, 30).All(i => respuesta.Sugerencias.Any(s => s.Cliente == "p" + i)), "aunque se pidieran 20, entran todos");
            Assert.AreEqual(33, respuesta.Sugerencias.Count, "los 30 y los 3 semanales");
            int porCupo = respuesta.Sugerencias.Count(s => s.Origen == OrigenesSugerenciaContacto.COBERTURA);
            Assert.IsTrue(porCupo > 0);
            Assert.AreEqual(porCupo, sugerencias.Count(s => s.Motivo.StartsWith(OrigenesSugerenciaContacto.PREFIJO_MOTIVO_COBERTURA)), "el origen queda en el Motivo registrado");
        }

        [TestMethod]
        public async Task Leer_MitadDeMes_ElCupoVaIntercaladoYSeMantieneEnLaSegundaLlamada()
        {
            // Jueves 15/10/26 (12 laborables por delante). 4 semanales ya contactados este mes y 24 compradores sin contacto en
            // octubre pero dentro de su cadencia (contactados del 16 al 30 de septiembre): solo entran por cupo, ⌈24 / 12⌉ = 2.
            DateTime ahora = new DateTime(2026, 10, 15, 9, 0, 0);
            cartera = Enumerable.Range(1, 4).Select(i => ClienteContactado("s" + i, 52, new DateTime(2026, 10, 5))).ToList();
            cartera.AddRange(Enumerable.Range(1, 24).Select(i => ClienteContactado("p" + i.ToString("00"), 2, new DateTime(2026, 9, 15).AddDays(i % 15 + 1))));
            var probabilidadesSemanales = new Dictionary<string, PrediccionContacto>
            {
                ["s1/0"] = new PrediccionContacto { Probabilidad = 0.9f },
                ["s2/0"] = new PrediccionContacto { Probabilidad = 0.8f },
                ["s3/0"] = new PrediccionContacto { Probabilidad = 0.7f },
                ["s4/0"] = new PrediccionContacto { Probabilidad = 0.6f }
            };
            A.CallTo(() => probabilidades.Leer(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(probabilidadesSemanales));
            A.CallTo(() => repositorio.LeerContactos(A<string>._, A<DateTime>._)).Returns(ContactadosEsteMes());

            SugerenciasContactoDTO primera = await ServicioEl(ahora).Leer("MPP", "Llamada", 5, "", "u");

            Assert.AreEqual(2, primera.Ritmo.CupoCoberturaHoy);
            CollectionAssert.AreEqual(new[] { "Probabilidad", "Cobertura", "Probabilidad", "Cobertura", "Probabilidad" }, primera.Sugerencias.Select(s => s.Origen).ToArray());
            CollectionAssert.AreEqual(new[] { "s1", "s2", "s3" }, primera.Sugerencias.Where(s => s.Origen == "Probabilidad").Select(s => s.Cliente).ToArray());
            StringAssert.StartsWith(primera.Sugerencias[1].Motivo, "Cobertura del mes: sin contacto desde el ");

            // Segunda llamada con otro filtro: s3 pasa a ser el más probable; las del cupo siguen en su sitio.
            A.CallTo(() => probabilidades.Leer(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new Dictionary<string, PrediccionContacto>
            {
                ["s1/0"] = new PrediccionContacto { Probabilidad = 0.2f },
                ["s2/0"] = new PrediccionContacto { Probabilidad = 0.1f },
                ["s3/0"] = new PrediccionContacto { Probabilidad = 0.95f }
            }));
            SugerenciasContactoDTO segunda = await ServicioEl(ahora).Leer("MPP", "Visita", 5, "", "u");

            Assert.AreEqual(5, sugerencias.Count, "no se registra nada más");
            CollectionAssert.AreEqual(new[] { "s3", primera.Sugerencias[1].Cliente, "s1", primera.Sugerencias[3].Cliente, "s2" },
                segunda.Sugerencias.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "Probabilidad", "Cobertura", "Probabilidad", "Cobertura", "Probabilidad" }, segunda.Sugerencias.Select(s => s.Origen).ToArray());
        }

        [TestMethod]
        public async Task LeerUso_PorOrigen_SugeridasYAtendidasDeCadaUno()
        {
            sugerencias.AddRange(new[]
            {
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 22, 9, 0, 0), Atendida = true, Motivo = "Compra cada 7 días y lleva 9 días sin hablar contigo" },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 22, 9, 0, 0), Atendida = false, Motivo = "Buen cliente (5 pedidos en el último año), sin ningún contacto registrado" },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 22, 9, 0, 0), Atendida = true, Motivo = "Cobertura del mes: sin ningún contacto registrado; ha comprado 1 vez en el último año" },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 22, 9, 0, 0), Atendida = false, Motivo = "Cobertura del mes: sin contacto desde el 2 de septiembre; ha comprado 2 veces en el último año" },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 22, 9, 0, 0), Atendida = false, Motivo = "Cobertura del mes: sin contacto desde el 3 de septiembre; ha comprado 2 veces en el último año" },
                new SugerenciaContacto { Vendedor = "MPP", Fecha = new DateTime(2026, 10, 22, 9, 0, 0), Atendida = true, Motivo = "Tiene un cheque de 50 € sin usar hasta el 07/11" }
            });

            List<UsoSugerenciasContactoDTO> uso = await Servicio().LeerUso(new DateTime(2026, 10, 22), new DateTime(2026, 10, 22));

            List<UsoOrigenSugerenciasContactoDTO> porOrigen = uso.Single().PorOrigen;
            CollectionAssert.AreEqual(new[] { "ChequeRegalo", "Cobertura", "Probabilidad" }, porOrigen.Select(o => o.Origen).ToArray());
            UsoOrigenSugerenciasContactoDTO cobertura = porOrigen.Single(o => o.Origen == "Cobertura");
            Assert.AreEqual(3, cobertura.Sugeridas);
            Assert.AreEqual(1, cobertura.Atendidas);
            Assert.AreEqual(0.3333, cobertura.PorcentajeAtendidas);
            UsoOrigenSugerenciasContactoDTO probabilidad = porOrigen.Single(o => o.Origen == "Probabilidad");
            Assert.AreEqual(2, probabilidad.Sugeridas);
            Assert.AreEqual(1, probabilidad.Atendidas);
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
            // Nesto#521: el permiso sobre el vendedor se prueba en ServicioVendedoresVisiblesTests; aquí se deja pasar.
            var visibles = A.Fake<NestoAPI.Infraestructure.Vendedores.IServicioVendedoresVisibles>();
            A.CallTo(() => visibles.PuedeVer(A<IPrincipal>._, A<string>._, A<string>._)).Returns(Task.FromResult(true));
            var controlador = new SugerenciasContactoController(db, Servicio(), null, visibles) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\MariaJose") };

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
