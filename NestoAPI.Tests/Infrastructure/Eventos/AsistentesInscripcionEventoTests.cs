using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#811 (Reina, sugerencia 1006; Carlos 04/11/26): número de asistentes por inscripción. La clienta 41286/0 compró
    /// dos kits del Lifting Coreano (pedido 929697) y vienen dos empleadas: una inscripción de 2 asistentes que ocupa 2 plazas y
    /// que necesita 2 unidades compradas. Sin el campo (o sin el script) todo sigue valiendo 1.
    /// </summary>
    [TestClass]
    public class AsistentesInscripcionEventoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 4, 10, 0, 0);
        private const int EVENTO_KIT = 4;
        private const int EVENTO_PEQUENO = 6;
        private const int PEDIDO = 929697;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<Modificacion> modificaciones;
        private IRepositorioProductosRequeridosEventos requeridos;
        private IRepositorioAsistentesEventos repositorio;
        private Dictionary<int, int> guardados;
        private Dictionary<string, int> unidades;
        private int siguienteId;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO_KIT, Empresa = "1", Titulo = "Formación por compra Lifting Coreano 19/11/2026", Fecha = new DateTime(2026, 11, 19),
                    ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "ALC", Plazas = 18, Usuario = "Paloma", FechaModificacion = HOY },
                new Evento { Id = EVENTO_PEQUENO, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 11, 20),
                    ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "ALC", Plazas = 3, Usuario = "Paloma", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            modificaciones = new List<Modificacion>();
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "41286", Contacto = "0  ", Nombre = "CENTRO KIT", ClientePrincipal = true, Vendedor = "JE ", Teléfono = "915555555" },
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "LUNA", ClientePrincipal = true, Vendedor = "JE ", Teléfono = "915555555" },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0  ", Nombre = "OTRA", ClientePrincipal = true, Vendedor = "NV ", Teléfono = "915555555" }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            DbSet<EventoInscripcion> fakeInscripciones = Crear(inscripciones);
            A.CallTo(() => fakeInscripciones.Add(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                i.Id = siguienteId++;
                inscripciones.Add(i);
                return i;
            });
            A.CallTo(() => fakeInscripciones.Remove(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                _ = inscripciones.Remove(i);
                return i;
            });
            A.CallTo(() => db.EventosInscripciones).Returns(fakeInscripciones);
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));
            A.CallTo(() => db.PersonasContactoClientes).Returns(Crear(new List<PersonaContactoCliente>()));
            DbSet<Modificacion> fakeModificaciones = Crear(modificaciones);
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>._)).ReturnsLazily((Modificacion m) => { modificaciones.Add(m); return m; });
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);

            requeridos = A.Fake<IRepositorioProductosRequeridosEventos>();
            A.CallTo(() => requeridos.Leer(A<IReadOnlyCollection<int>>._)).ReturnsLazily((IReadOnlyCollection<int> ids) =>
                Task.FromResult(ids.Contains(EVENTO_KIT)
                    ? new Dictionary<int, List<ProductoRequeridoLeido>>
                    {
                        [EVENTO_KIT] = new List<ProductoRequeridoLeido>
                        {
                            new ProductoRequeridoLeido { EventoId = EVENTO_KIT, Producto = "45363", Nombre = "LIFTING COREANO KIT", FechaCreacion = new DateTime(2026, 10, 20, 12, 0, 0) }
                        }
                    }
                    : new Dictionary<int, List<ProductoRequeridoLeido>>()));
            A.CallTo(() => requeridos.EventosQueExigenTodos(A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult(new HashSet<int>()));
            A.CallTo(() => requeridos.BuscarJustificante("1", "41286", A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .Returns(Task.FromResult(new JustificanteCompraEvento { Pedido = PEDIDO, Producto = "45363", Estado = Constantes.EstadosLineaVenta.FACTURA }));
            A.CallTo(() => requeridos.PonerJustificante(A<int>._, A<int>._)).Returns(Task.FromResult(true));
            A.CallTo(() => requeridos.LeerJustificantes(A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult(new Dictionary<int, int>()));
            unidades = new Dictionary<string, int> { ["45363"] = 2 };
            A.CallTo(() => requeridos.UnidadesCompradas(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .ReturnsLazily(() => Task.FromResult(new Dictionary<string, int>(unidades)));

            guardados = new Dictionary<int, int>();
            repositorio = A.Fake<IRepositorioAsistentesEventos>();
            A.CallTo(() => repositorio.Disponible()).Returns(true);
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).ReturnsLazily((IReadOnlyCollection<int> ids) =>
                Task.FromResult(guardados.Where(g => ids.Contains(g.Key) && g.Value > 1).ToDictionary(g => g.Key, g => g.Value)));
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).ReturnsLazily((int id, int n) =>
            {
                guardados[id] = n;
                return Task.FromResult(true);
            });
        }

        private ServicioInscripcionesEventos Servicio(bool conRepositorio = true) =>
            new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => null, A.Fake<IProcesadorInscripcionPagada>())
            {
                ProductosRequeridos = requeridos,
                AsistentesInscripciones = conRepositorio ? repositorio : null
            };

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal ADMINISTRACION = Usuario("NUEVAVISION\\Aida", Constantes.GruposSeguridad.ADMINISTRACION);
        private static readonly IPrincipal DIRECCION = Usuario("NUEVAVISION\\Manuel", Constantes.GruposSeguridad.DIRECCION);

        private EventoInscripcion Inscrita(int id, string cliente, int eventoId, int asistentes = 1, EstadoInscripcionEvento estado = EstadoInscripcionEvento.Inscrita,
            string respuesta = null)
        {
            var i = new EventoInscripcion
            {
                Id = id, EventoId = eventoId, Empresa = "1", Cliente = cliente, Contacto = "0  ", Origen = "Nesto",
                Estado = estado.ToString(), Modalidad = "Gratuita", AsistenciaRespuesta = respuesta,
                UsuarioCreacion = "NUEVAVISION\\Jesus", FechaCreacion = HOY.AddDays(-1), Usuario = "NUEVAVISION\\Jesus", FechaModificacion = HOY.AddDays(-1)
            };
            inscripciones.Add(i);
            if (asistentes != 1)
            {
                guardados[id] = asistentes;
            }
            return i;
        }

        // ---------------- Reglas puras ----------------

        [TestMethod]
        public void Normalizar_SinDecirNada1_YMenosDe1Es400()
        {
            Assert.AreEqual(1, ReglasAsistentesEvento.Normalizar(null));
            Assert.AreEqual(2, ReglasAsistentesEvento.Normalizar(2));
            _ = Assert.ThrowsException<NestoBusinessException>(() => ReglasAsistentesEvento.Normalizar(0));
            _ = Assert.ThrowsException<NestoBusinessException>(() => ReglasAsistentesEvento.Normalizar(-3));
        }

        [TestMethod]
        public void PlazasOcupadas_SumaLosAsistentesDeLasQueOcupanPlaza()
        {
            var lista = new List<EventoInscripcion>
            {
                new EventoInscripcion { Id = 1, Estado = "Inscrita" },
                new EventoInscripcion { Id = 2, Estado = "Reservada" },
                new EventoInscripcion { Id = 3, Estado = "Anulada" },
                new EventoInscripcion { Id = 4, Estado = "Pagada", AsistenciaRespuesta = RespuestaAsistenciaEvento.NoPodraIr.ToString() },
                new EventoInscripcion { Id = 5, Estado = "Pagada" }
            };
            var asistentes = new Dictionary<int, int> { [1] = 2, [3] = 5, [4] = 4 };

            // 2 (Id 1) + 1 (Id 2) + 1 (Id 5); la anulada y la de «No podré ir» no ocupan
            Assert.AreEqual(4, ReglasAsistentesEvento.PlazasOcupadas(lista, asistentes));
            Assert.AreEqual(3, ReglasAsistentesEvento.PlazasOcupadas(lista, null), "Sin la columna, 1 por inscripción");
        }

        [TestMethod]
        public void Caben_CuentaLosAsistentesNuevos()
        {
            var evento = new Evento { Plazas = 18 };
            Assert.IsTrue(ReglasAsistentesEvento.Caben(evento, 16, 2));
            Assert.IsFalse(ReglasAsistentesEvento.Caben(evento, 17, 2));
            Assert.IsTrue(ReglasAsistentesEvento.Caben(new Evento(), 1000, 50), "Sin plazas, siempre");
        }

        [TestMethod]
        public void UnidadesQueCuentan_AlgunoSuma_TodosElQueMenosTiene()
        {
            var lista = new List<ProductoRequeridoLeido>
            {
                new ProductoRequeridoLeido { Producto = "A" },
                new ProductoRequeridoLeido { Producto = "B" }
            };
            var compradas = new Dictionary<string, int> { ["a"] = 3, ["B"] = 1, ["OTRO"] = 9 };

            Assert.AreEqual(4, ReglasAsistentesEvento.UnidadesQueCuentan(lista, compradas, todos: false));
            Assert.AreEqual(1, ReglasAsistentesEvento.UnidadesQueCuentan(lista, compradas, todos: true));
            Assert.AreEqual(0, ReglasAsistentesEvento.UnidadesQueCuentan(lista, new Dictionary<string, int> { ["A"] = 3 }, todos: true));
        }

        [TestMethod]
        public void MensajeFaltanUnidades_DiceLoCompradoYLosAsistentes()
        {
            string mensaje = ReglasAsistentesEvento.MensajeFaltanUnidades("Formación por compra Lifting Coreano", "41286", "0",
                new List<ProductoRequeridoLeido> { new ProductoRequeridoLeido { Producto = "45363", Nombre = "LIFTING COREANO KIT" } },
                todos: false, compradas: 1, asistentes: 2, desde: new DateTime(2026, 8, 21));

            Assert.AreEqual("El cliente 41286/0 ha comprado 1 unidad de 45363 (LIFTING COREANO KIT) desde el 21/08/2026 y quiere inscribir " +
                "2 asistentes en «Formación por compra Lifting Coreano»: hace falta una unidad comprada por asistente.", mensaje);
        }

        // ---------------- Inscribir ----------------

        [TestMethod]
        public async Task Inscribir_CasoReal_DosKitsDosAsistentes_InscritaConDosYSeGuarda()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_KIT,
                new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto", Asistentes = 2 }, VENDEDOR);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.AreEqual(2, dto.Asistentes);
            Assert.AreEqual(PEDIDO, dto.PedidoJustificante);
            A.CallTo(() => repositorio.Poner(dto.Id, 2)).MustHaveHappenedOnceExactly();
            A.CallTo(() => requeridos.UnidadesCompradas("1", "41286",
                A<IReadOnlyCollection<string>>.That.Matches(p => p.Count == 1 && p.Contains("45363")), new DateTime(2026, 8, 21)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Inscribir_UnKitDosAsistentes_400ClaroYNoSeInscribe()
        {
            unidades["45363"] = 1;

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_KIT,
                new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto", Asistentes = 2 }, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(ReglasProductosRequeridosEvento.ERROR_REQUIERE_COMPRA, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "ha comprado 1 unidad de 45363");
            StringAssert.Contains(ex.Message, "quiere inscribir 2 asistentes");
            Assert.AreEqual(0, inscripciones.Count);
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Inscribir_UnKitDosAsistentes_DireccionSeLoSaltaConAviso()
        {
            unidades["45363"] = 1;

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_KIT,
                new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto", Asistentes = 2 }, DIRECCION);

            Assert.AreEqual(2, dto.Asistentes);
            Assert.IsTrue(dto.Avisos.Any(a => a.Contains("Inscrita por Dirección") && a.Contains("quiere inscribir 2 asistentes")));
        }

        [TestMethod]
        public async Task Inscribir_SinAsistentes_UnoComoAntes_NiGuardaNiMiraUnidades()
        {
            unidades["45363"] = 1;

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_KIT, new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto" }, VENDEDOR);

            Assert.AreEqual(1, dto.Asistentes);
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => requeridos.UnidadesCompradas(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Inscribir_AsistentesCero_400()
        {
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", Asistentes = 0 }, VENDEDOR));
            Assert.AreEqual(0, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_CadaInscripcionOcupaSusAsistentes_SiNoCabenDiceCuantasQuedan()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO, asistentes: 2);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", Asistentes = 2 }, VENDEDOR));

            Assert.AreEqual("En «Masterclass Cloasma» solo queda 1 plaza libre y quiere inscribir 2 asistentes. Inscriba como mucho 1 asistente, por favor.",
                ex.Message);
            Assert.AreEqual(1, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_LasPlazasDeLasDeVariosAsistentesCuentan_LlenoSinPlazas()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO, asistentes: 3);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR));

            StringAssert.StartsWith(ex.Message, "No quedan plazas en «Masterclass Cloasma»");
        }

        [TestMethod]
        public async Task Inscribir_CabenJusto_Inscrita()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO);

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", Asistentes = 2 }, VENDEDOR);

            Assert.AreEqual(2, dto.Asistentes);
            Assert.AreEqual(2, guardados[dto.Id]);
        }

        [TestMethod]
        public async Task Inscribir_YaInscrito_ElMensajeDiceQueSeCambienLosAsistentes()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_KIT,
                new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto" }, VENDEDOR));

            StringAssert.Contains(ex.Message, "ya está inscrito");
            StringAssert.Contains(ex.Message, "cambie el número de asistentes");
        }

        // ---------------- Compatibilidad: sin el script ----------------

        [TestMethod]
        public async Task SinScript_UnoComoSiempre_YVariosDiceQueFaltaElScript()
        {
            A.CallTo(() => repositorio.Disponible()).Returns(false);

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_PEQUENO, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);
            Assert.AreEqual(1, dto.Asistentes);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "20000", Origen = "Nesto", Asistentes = 2 }, VENDEDOR));
            StringAssert.Contains(ex.Message, "Issue811_EventosInscripcionesAsistentes.sql");
            Assert.AreEqual(1, inscripciones.Count);
        }

        [TestMethod]
        public async Task SinRepositorio_LecturasConUnoYVariosDiceQueFaltaElScript()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO);

            List<InscripcionEventoDTO> lista = await Servicio(conRepositorio: false).LeerInscripcionesEvento(EVENTO_PEQUENO, VENDEDOR);
            Assert.AreEqual(1, lista.Single().Asistentes);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(conRepositorio: false).Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", Asistentes = 2 }, VENDEDOR));
            StringAssert.Contains(ex.Message, "falta lanzar el script");
        }

        [TestMethod]
        public async Task SiFallaLaLectura_CuentaUnoPorInscripcionYNoRompe()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO, asistentes: 3);
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).Throws(new InvalidOperationException("El nombre de columna 'Asistentes' no es válido."));

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_PEQUENO, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);
            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_PEQUENO, VENDEDOR);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.IsTrue(lista.All(i => i.Asistentes == 1));
        }

        [TestMethod]
        public async Task SiNoSePuedeGuardar_SeQuitaLaInscripcion()
        {
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).Returns(Task.FromResult(false));

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_PEQUENO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", Asistentes = 2 }, VENDEDOR));

            Assert.AreEqual(0, inscripciones.Count);
        }

        // ---------------- Lecturas ----------------

        [TestMethod]
        public async Task Lecturas_LlevanLosAsistentes()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT, asistentes: 2);
            _ = Inscrita(2, "15191", EVENTO_KIT);

            List<InscripcionEventoDTO> delEvento = await Servicio().LeerInscripcionesEvento(EVENTO_KIT, VENDEDOR);
            List<InscripcionEventoDTO> delCliente = await Servicio().LeerInscripcionesCliente("1", "41286", null, VENDEDOR);

            Assert.AreEqual(2, delEvento.Single(i => i.Id == 1).Asistentes);
            Assert.AreEqual(1, delEvento.Single(i => i.Id == 2).Asistentes);
            Assert.AreEqual(2, delCliente.Single().Asistentes);
        }

        [TestMethod]
        public async Task Catalogo_PlazasOcupadasSumaAsistentes()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT, asistentes: 2);
            _ = Inscrita(2, "15191", EVENTO_KIT);
            _ = Inscrita(3, "20000", EVENTO_KIT, asistentes: 4, estado: EstadoInscripcionEvento.Anulada);

            var catalogo = new CatalogoCursosPlantilla(db, () => HOY) { AsistentesInscripciones = repositorio };
            Dictionary<int, int> ocupadas = await catalogo.PlazasOcupadas(new List<int> { EVENTO_KIT });

            Assert.AreEqual(3, ocupadas[EVENTO_KIT]);
        }

        [TestMethod]
        public async Task Evento_PlazasOcupadasYSinPagarSumanAsistentes()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT, asistentes: 2);
            _ = Inscrita(2, "15191", EVENTO_KIT, asistentes: 3, estado: EstadoInscripcionEvento.Reservada);

            var servicio = new ServicioEventos(db, () => HOY, (e, u) => null) { AsistentesInscripciones = repositorio };
            EventoDTO evento = await servicio.LeerEvento(EVENTO_KIT);

            Assert.AreEqual(5, evento.PlazasOcupadas);
            Assert.AreEqual(3, evento.PlazasSinPagar);
        }

        // ---------------- Confirmar asistencia ----------------

        [TestMethod]
        public async Task ConfirmarAsistencia_VuelveConSusAsistentes_SiNoCabenDiceCuantasQuedan()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO, asistentes: 2);
            _ = Inscrita(2, "15191", EVENTO_PEQUENO, asistentes: 2, respuesta: RespuestaAsistenciaEvento.NoPodraIr.ToString());

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().ConfirmarAsistencia(2, new ConfirmarAsistenciaDTO { Respuesta = "Confirma" }, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "solo queda 1 plaza libre y su inscripción es de 2 asistentes");
        }

        [TestMethod]
        public async Task ConfirmarAsistencia_SiCaben_DevuelveSusAsistentes()
        {
            _ = Inscrita(2, "15191", EVENTO_PEQUENO, asistentes: 2, respuesta: RespuestaAsistenciaEvento.NoPodraIr.ToString());

            InscripcionEventoDTO dto = await Servicio().ConfirmarAsistencia(2, new ConfirmarAsistenciaDTO { Respuesta = "Confirma" }, ADMINISTRACION);

            Assert.AreEqual(2, dto.Asistentes);
            Assert.AreEqual(EstadoAsistenciaEvento.Confirmada, dto.EstadoAsistencia);
        }

        // ---------------- PUT api/Eventos/Inscripciones/{id}/Asistentes ----------------

        [TestMethod]
        public async Task CambiarAsistentes_ConKitsSuficientes_SeGuarda()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);

            InscripcionEventoDTO dto = await Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION);

            Assert.AreEqual(2, dto.Asistentes);
            Assert.AreEqual(2, guardados[1]);
            Assert.AreEqual("NUEVAVISION\\Aida", inscripciones.Single().Usuario);
        }

        [TestMethod]
        public async Task CambiarAsistentes_SinKitsSuficientes_400YNoSeGuarda()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);
            unidades["45363"] = 1;

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION));

            Assert.AreEqual(ReglasProductosRequeridosEvento.ERROR_REQUIERE_COMPRA, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "ha comprado 1 unidad");
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarAsistentes_SinKitsSuficientes_DireccionConAviso()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);
            unidades["45363"] = 1;

            InscripcionEventoDTO dto = await Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, DIRECCION);

            Assert.AreEqual(2, dto.Asistentes);
            Assert.AreEqual(1, dto.Avisos.Count);
        }

        [TestMethod]
        public async Task CambiarAsistentes_SinPlazas_400()
        {
            _ = Inscrita(1, "20000", EVENTO_PEQUENO, asistentes: 2);
            _ = Inscrita(2, "15191", EVENTO_PEQUENO);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(2, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION));

            Assert.AreEqual("En «Masterclass Cloasma» solo queda 1 plaza libre y quiere inscribir 2 asistentes. Inscriba como mucho 1 asistente, por favor.",
                ex.Message);
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarAsistentes_Bajar_SinMirarPlazasNiCompra()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT, asistentes: 3);
            unidades["45363"] = 0;

            InscripcionEventoDTO dto = await Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 1 }, ADMINISTRACION);

            Assert.AreEqual(1, dto.Asistentes);
            A.CallTo(() => repositorio.Poner(1, 1)).MustHaveHappenedOnceExactly();
            A.CallTo(() => requeridos.UnidadesCompradas(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarAsistentes_ElVendedorNoPuede_403()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task CambiarAsistentes_CeroOVacio_400()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 0 }, ADMINISTRACION));
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO(), ADMINISTRACION));
        }

        [TestMethod]
        public async Task CambiarAsistentes_Anulada_400()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT, estado: EstadoInscripcionEvento.Anulada);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "no se puede cambiar el número de asistentes");
        }

        [TestMethod]
        public async Task CambiarAsistentes_SinScript_DiceQueFalta()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio(conRepositorio: false).CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "Issue811_EventosInscripcionesAsistentes.sql");
        }

        // ---------------- NestoAPI#843: importe mínimo por asistente (Carlos 09/11/26) ----------------

        private void ConMinimo(decimal minimo, decimal lleva)
        {
            A.CallTo(() => requeridos.ImportesMinimos(A<IReadOnlyCollection<int>>._)).ReturnsLazily((IReadOnlyCollection<int> ids) =>
                Task.FromResult(ids.Contains(EVENTO_KIT) ? new Dictionary<int, decimal> { [EVENTO_KIT] = minimo } : new Dictionary<int, decimal>()));
            A.CallTo(() => requeridos.ImporteComprado("1", "41286", A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(Task.FromResult(lleva));
        }

        [TestMethod]
        public async Task ImporteMinimo_Inscribir_DosAsistentes_HaceFaltaElDoble()
        {
            ConMinimo(400, 600);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_KIT,
                new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto", Asistentes = 2 }, VENDEDOR));

            Assert.AreEqual(ReglasProductosRequeridosEvento.ERROR_REQUIERE_COMPRA, ex.Context.ErrorCode);
            StringAssert.StartsWith(ex.Message, "Para inscribir 2 asistentes tiene que haber comprado al menos 800,00 € de los productos requeridos " +
                "(400,00 € por asistente); lleva 600,00 €.");
            Assert.AreEqual(2, ex.Context.AdditionalData["asistentes"]);
            Assert.AreEqual(0, inscripciones.Count);
        }

        [TestMethod]
        public async Task ImporteMinimo_Inscribir_DosAsistentesConElDoble_Inscrita()
        {
            ConMinimo(400, 800);

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_KIT,
                new InscribirEventoDTO { Cliente = "41286", Origen = "Nesto", Asistentes = 2 }, VENDEDOR);

            Assert.AreEqual(2, dto.Asistentes);
        }

        [TestMethod]
        public async Task ImporteMinimo_CambiarAsistentes_SinElDoble_400YNoSeGuarda()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);
            ConMinimo(400, 600);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "al menos 800,00 €");
            A.CallTo(() => repositorio.Poner(A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ImporteMinimo_CambiarAsistentes_ConElDoble_SeGuarda()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);
            ConMinimo(400, 800);

            InscripcionEventoDTO dto = await Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, ADMINISTRACION);

            Assert.AreEqual(2, dto.Asistentes);
        }

        [TestMethod]
        public async Task ImporteMinimo_CambiarAsistentes_DireccionSinElDobleNiLasUnidades_UnAvisoConLasDosCosas()
        {
            _ = Inscrita(1, "41286", EVENTO_KIT);
            unidades["45363"] = 1;
            ConMinimo(400, 600);

            InscripcionEventoDTO dto = await Servicio().CambiarAsistentes(1, new CambiarAsistentesInscripcionDTO { Asistentes = 2 }, DIRECCION);

            Assert.AreEqual(2, dto.Asistentes);
            Assert.AreEqual(1, dto.Avisos.Count);
            StringAssert.Contains(dto.Avisos[0], "ha comprado 1 unidad");
            StringAssert.Contains(dto.Avisos[0], "al menos 800,00 €");
        }

        [TestMethod]
        public async Task Controlador_PutAsistentes_LlamaAlServicio()
        {
            IServicioInscripcionesEventos servicio = A.Fake<IServicioInscripcionesEventos>();
            A.CallTo(() => servicio.CambiarAsistentes(7, A<CambiarAsistentesInscripcionDTO>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 7, Asistentes = 2 }));
            var controlador = new NestoAPI.Controllers.EventosController(db, A.Fake<IServicioEventos>(), servicio, A.Fake<IServicioPropuestasSenales>());

            var respuesta = await controlador.PutAsistentesInscripcion(7, new CambiarAsistentesInscripcionDTO { Asistentes = 2 })
                as System.Web.Http.Results.OkNegotiatedContentResult<InscripcionEventoDTO>;

            Assert.AreEqual(2, respuesta.Content.Asistentes);
            A.CallTo(() => servicio.CambiarAsistentes(7, A<CambiarAsistentesInscripcionDTO>.That.Matches(p => p.Asistentes == 2), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
