using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class RegistroOfertasMostradasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);
        private static readonly DateTime AHORA = new DateTime(2026, 11, 26, 10, 30, 0);
        private const string BF = "Black Friday 2026";

        private static OfertaElegida Oferta(string producto, string motivo = MotivosOfertaParaTi.TE_TOCA, float? p = 0.5f)
            => new OfertaElegida { Candidata = new CandidataOferta { Producto = producto, Campana = BF }, Motivo = motivo, Probabilidad = p };

        private static OfertaConPrecio ConPrecio(OfertaElegida o) => new OfertaConPrecio { Oferta = o, Precios = new PreciosOferta { Precio = 7, PrecioAntes = 10, Tarifa = 10 } };

        // ---------------------------------------------------------------- reglas puras

        [TestMethod]
        public void Filas_PosicionEnLoServidoYEnElRanking_YSinRepetir()
        {
            List<OfertaElegida> ranking = new[] { "9", "3", "1", "7" }.Select(p => Oferta(p)).ToList();
            var servidas = new List<OfertaConPrecio> { ConPrecio(ranking[2]), ConPrecio(ranking[3]), ConPrecio(ranking[2]) };

            List<FilaOfertaMostrada> filas = ReglaRegistroOfertas.Filas(AHORA, "1", "15191", "0", "cinco", servidas, ranking, "m1-x");

            Assert.AreEqual(2, filas.Count, "La misma oferta una vez por día y origen");
            Assert.AreEqual("1", filas[0].Producto);
            Assert.AreEqual(1, filas[0].Posicion);
            Assert.AreEqual(3, filas[0].PosicionRanking);
            Assert.AreEqual(2, filas[1].Posicion);
            Assert.AreEqual(4, filas[1].PosicionRanking);
            Assert.AreEqual(HOY, filas[0].Fecha);
            Assert.AreEqual(OrigenesOfertaMostrada.CINCO, filas[0].Origen);
            Assert.AreEqual(0.3m, filas[0].Descuento);
            Assert.AreEqual(7m, filas[0].Precio);
            Assert.AreEqual("m1-x", filas[0].VersionModelo);
            Assert.AreEqual(MotivosOfertaParaTi.TE_TOCA, filas[0].Motivo);
        }

        [TestMethod]
        public void Deduplicar_PorDiaClienteProductoYOrigen()
        {
            var a = new FilaOfertaMostrada { Fecha = HOY, Empresa = "1", Cliente = "1000", Producto = "45929", Origen = "Cinco", Posicion = 1 };
            var b = new FilaOfertaMostrada { Fecha = HOY.AddHours(5), Empresa = "1", Cliente = "1000 ", Producto = "45929", Origen = "Cinco", Posicion = 4 };
            var otroOrigen = new FilaOfertaMostrada { Fecha = HOY, Empresa = "1", Cliente = "1000", Producto = "45929", Origen = "Todas" };
            var otroDia = new FilaOfertaMostrada { Fecha = HOY.AddDays(1), Empresa = "1", Cliente = "1000", Producto = "45929", Origen = "Cinco" };

            List<FilaOfertaMostrada> r = ReglaRegistroOfertas.Deduplicar(new[] { a, b, otroOrigen, otroDia });

            CollectionAssert.AreEqual(new[] { a, otroOrigen, otroDia }, r);
        }

        [TestMethod]
        public void Eventos_SoloLosQueValen_YComoMucho100()
        {
            var peticion = new PeticionEventosOfertasDTO
            {
                Eventos = new List<EventoOfertaDTO> { new EventoOfertaDTO { Producto = " 45929 ", Accion = "anadida", Cantidad = 0, Origen = "todas" },
                    new EventoOfertaDTO { Producto = "1", Accion = "Comprada" }, new EventoOfertaDTO { Producto = "", Accion = "Vista" } }
            };
            peticion.Eventos.AddRange(Enumerable.Range(0, 150).Select(i => new EventoOfertaDTO { Producto = "P" + i, Accion = "Vista" }));

            List<EventoOfertaDTO> eventos = ReglaRegistroOfertas.Eventos(peticion);

            Assert.AreEqual(100, eventos.Count);
            Assert.AreEqual("45929", eventos[0].Producto);
            Assert.AreEqual(AccionesEventoOferta.ANADIDA, eventos[0].Accion);
            Assert.AreEqual(1, eventos[0].Cantidad);
            Assert.AreEqual(OrigenesOfertaMostrada.TODAS, eventos[0].Origen);
            Assert.AreEqual(OrigenesOfertaMostrada.CINCO, eventos[1].Origen, "Sin origen, «Cinco»");
        }

        [TestMethod]
        public void Aplicar_SobreLaFilaServida_CadaMomentoLaPrimeraVezYLasUnidadesSeSuman()
        {
            var servida = new FilaOfertaMostrada { Id = 7, Fecha = HOY, Empresa = "1", Cliente = "1000", Producto = "45929", Origen = "Cinco", Posicion = 2,
                VistaEl = HOY.AddHours(9) };

            FilaOfertaMostrada abierta = ReglaRegistroOfertas.Aplicar(servida, new EventoOfertaDTO { Producto = "45929", Accion = AccionesEventoOferta.ABIERTA }, AHORA, "1", "1000", BF);
            FilaOfertaMostrada anadida = ReglaRegistroOfertas.Aplicar(abierta, new EventoOfertaDTO { Producto = "45929", Accion = AccionesEventoOferta.ANADIDA, Cantidad = 2 }, AHORA.AddMinutes(1), "1", "1000", BF);
            FilaOfertaMostrada otra = ReglaRegistroOfertas.Aplicar(anadida, new EventoOfertaDTO { Producto = "45929", Accion = AccionesEventoOferta.ANADIDA, Cantidad = 3 }, AHORA.AddMinutes(5), "1", "1000", BF);

            Assert.AreEqual(7, otra.Id);
            Assert.AreEqual(2, otra.Posicion);
            Assert.AreEqual(HOY.AddHours(9), otra.VistaEl, "La vista ya estaba");
            Assert.AreEqual(AHORA, otra.AbiertaEl);
            Assert.AreEqual(AHORA.AddMinutes(1), otra.AnadidaEl);
            Assert.AreEqual(5, otra.CantidadAnadida);
        }

        [TestMethod]
        public void Aplicar_SinFilaServida_CreaUnaSinPosicion()
        {
            FilaOfertaMostrada nueva = ReglaRegistroOfertas.Aplicar(null, new EventoOfertaDTO { Producto = "45929", Accion = AccionesEventoOferta.VISTA, Origen = "Todas" }, AHORA, "1", "1000", BF);

            Assert.AreEqual(0, nueva.Id);
            Assert.IsNull(nueva.Posicion);
            Assert.AreEqual(HOY, nueva.Fecha);
            Assert.AreEqual(AHORA, nueva.VistaEl);
            Assert.AreEqual("Todas", nueva.Origen);
            Assert.AreEqual(BF, nueva.Campana);
        }

        [TestMethod]
        public void PrimeraCompra_EnLosSieteDiasSiguientes_LaPrimera()
        {
            var fila = new FilaOfertaMostrada { Id = 5, Fecha = HOY };
            var compras = new List<CompraTrasOferta>
            {
                new CompraTrasOferta { Id = 5, Pedido = 900, Fecha = HOY.AddDays(8) },   // fuera
                new CompraTrasOferta { Id = 5, Pedido = 800, Fecha = HOY.AddDays(3) },
                new CompraTrasOferta { Id = 5, Pedido = 700, Fecha = HOY.AddDays(7) },
                new CompraTrasOferta { Id = 6, Pedido = 600, Fecha = HOY },              // de otra fila
                new CompraTrasOferta { Id = 5, Pedido = 500, Fecha = HOY.AddDays(-1) }   // antes de enseñársela
            };

            Assert.AreEqual(800, ReglaRegistroOfertas.PrimeraCompra(fila, compras).Pedido);
            Assert.IsNull(ReglaRegistroOfertas.PrimeraCompra(fila, compras.Where(c => c.Pedido != 800 && c.Pedido != 700)));
            Assert.AreEqual(700, ReglaRegistroOfertas.PrimeraCompra(fila, compras.Where(c => c.Pedido != 800)).Pedido, "El séptimo día cuenta");
        }

        [TestMethod]
        public void Job_MarcaLasComprasPorTandas_YSinTablaNada()
        {
            IRepositorioOfertasMostradas repo = A.Fake<IRepositorioOfertasMostradas>();
            A.CallTo(() => repo.TablaExiste()).Returns(true);
            A.CallTo(() => repo.LeerSinCompra(HOY.AddDays(-8), 0, CompraOfertasMostradasJobsService.TANDA)).Returns(new List<FilaOfertaMostrada>
            {
                new FilaOfertaMostrada { Id = 1, Fecha = HOY.AddDays(-2) }, new FilaOfertaMostrada { Id = 2, Fecha = HOY.AddDays(-1) }
            });
            A.CallTo(() => repo.LeerCompras(A<IReadOnlyList<long>>._)).Returns(new List<CompraTrasOferta> { new CompraTrasOferta { Id = 2, Pedido = 930001, Fecha = HOY } });

            int marcadas = CompraOfertasMostradasJobsService.Marcar(repo, HOY);

            Assert.AreEqual(1, marcadas);
            A.CallTo(() => repo.MarcarCompra(2, HOY, 930001)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.MarcarCompra(1, A<DateTime>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => repo.LeerCompras(A<IReadOnlyList<long>>._)).MustHaveHappenedOnceExactly();

            IRepositorioOfertasMostradas sinTabla = A.Fake<IRepositorioOfertasMostradas>();
            A.CallTo(() => sinTabla.TablaExiste()).Returns(false);
            Assert.AreEqual(0, CompraOfertasMostradasJobsService.Marcar(sinTabla, HOY));
            A.CallTo(() => sinTabla.LeerSinCompra(A<DateTime>._, A<long>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Registro_EventoSobreFilaExistente_YSinTablaNoEscribeNiRompe()
        {
            IRepositorioOfertasMostradas repo = A.Fake<IRepositorioOfertasMostradas>();
            A.CallTo(() => repo.TablaExiste()).Returns(true);
            A.CallTo(() => repo.Leer(HOY, "1", "1000", "45929", "Cinco"))
                .Returns(new FilaOfertaMostrada { Id = 9, Fecha = HOY, Empresa = "1", Cliente = "1000", Producto = "45929", Origen = "Cinco", Posicion = 1 });
            var registro = new RegistroOfertasMostradas(() => repo, t => t(), ex => { });

            registro.Eventos("1", "1000", new PeticionEventosOfertasDTO { Campana = BF, Eventos = new List<EventoOfertaDTO>
                { new EventoOfertaDTO { Producto = "45929", Accion = "Anadida", Cantidad = 2 } } }, AHORA);

            A.CallTo(() => repo.GuardarEvento(A<FilaOfertaMostrada>.That.Matches(f => f.Id == 9 && f.AnadidaEl == AHORA && f.CantidadAnadida == 2 && f.VistaEl == AHORA)))
                .MustHaveHappenedOnceExactly();

            IRepositorioOfertasMostradas sinTabla = A.Fake<IRepositorioOfertasMostradas>();
            A.CallTo(() => sinTabla.TablaExiste()).Returns(false);
            new RegistroOfertasMostradas(() => sinTabla, t => t(), ex => { }).Servidas(new[] { new FilaOfertaMostrada { Fecha = HOY, Cliente = "1", Producto = "2", Origen = "Cinco" } });
            A.CallTo(() => sinTabla.Insertar(A<IReadOnlyList<FilaOfertaMostrada>>._)).MustNotHaveHappened();

            var errores = new List<Exception>();
            IRepositorioOfertasMostradas roto = A.Fake<IRepositorioOfertasMostradas>();
            A.CallTo(() => roto.TablaExiste()).Throws(new Exception("BD caída"));
            new RegistroOfertasMostradas(() => roto, t => t(), errores.Add).Servidas(new[] { new FilaOfertaMostrada { Fecha = HOY, Cliente = "1", Producto = "2", Origen = "Cinco" } });
            Assert.AreEqual(1, errores.Count, "A ELMAH, sin lanzar");
        }

        [TestMethod]
        public void Resultado_PorOrigenMotivoYDecil()
        {
            var filas = new List<FilaResultadoOferta>();
            for (int i = 0; i < 20; i++)
            {
                filas.Add(new FilaResultadoOferta
                {
                    Servida = true,
                    Origen = i < 15 ? "Cinco" : "Push",
                    Motivo = i % 2 == 0 ? "TeToca" : "CompradoJunto",
                    Probabilidad = (20 - i) / 20f,
                    Vista = i < 10,
                    Abierta = i < 4,
                    Anadida = i < 2,
                    Comprada = i == 0
                });
            }
            filas.Add(new FilaResultadoOferta { Servida = false, Origen = "Cinco", Vista = true }); // la creó un evento: no es servida

            ResultadoOfertasMostradasDTO r = ReglaResultadoOfertas.Resumir(filas, BF, 30, HOY.AddDays(-29));

            Assert.AreEqual(20, r.Total.Servidas);
            Assert.AreEqual(0.5m, r.Total.PorcentajeVistas);
            Assert.AreEqual(0.05m, r.Total.PorcentajeCompradas);
            Assert.AreEqual(15, r.PorOrigen.Single(g => g.Grupo == "Cinco").Servidas);
            Assert.AreEqual(10, r.PorMotivo.Single(g => g.Grupo == "TeToca").Servidas);
            Assert.AreEqual(10, r.PorDecil.Count);
            Assert.AreEqual("Decil 1", r.PorDecil[0].Grupo);
            Assert.AreEqual(2, r.PorDecil[0].Servidas);
            Assert.AreEqual(1f, r.PorDecil[0].ProbabilidadHasta);
            Assert.AreEqual(2, r.PorDecil[0].Anadidas, "Las de más probabilidad son las que se añaden");
            Assert.AreEqual(0, r.PorDecil[9].Vistas);
            Assert.AreEqual("2026-10-28", r.Desde);
        }

        // ---------------------------------------------------------------- dónde se apunta

        private static ILectorOfertasParaTi Lector()
        {
            ILectorOfertasParaTi lector = A.Fake<ILectorOfertasParaTi>();
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._)).Returns(new[] { "1", "2" }.Select(p => new FilaCampanaOferta
            {
                Campana = BF, Producto = p, Descuento = 0.3m, AudienciaOferta = 2, FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30),
                Nombre = "P" + p, Familia = "F" + p, Grupo = "COS", SubGrupo = "CRE", PVP = 10, Estado = 0
            }).ToList());
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._)).ReturnsLazily((IReadOnlyCollection<string> ps) => ps.ToDictionary(p => p, p => 5));
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._)).Returns(new List<CompraDiaReposicion>());
            A.CallTo(() => lector.LeerPendientes(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new List<PendienteClienteOferta>());
            return lector;
        }

        private static IPuntuadorReposicion Modelo()
        {
            IPuntuadorReposicion puntuador = A.Fake<IPuntuadorReposicion>();
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-x");
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .Returns(new Dictionary<string, float> { { "1", 0.8f }, { "2", 0.6f } });
            return puntuador;
        }

        private static ICalculadorPreciosOferta Precios()
        {
            ICalculadorPreciosOferta precios = A.Fake<ICalculadorPreciosOferta>();
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(new PreciosOferta { Precio = 7, PrecioAntes = 10, Tarifa = 10 });
            return precios;
        }

        [TestMethod]
        public async Task Servicio_ApuntaLoServidoEnLaFranjaYEnVerTodas()
        {
            IRegistroOfertasMostradas registro = A.Fake<IRegistroOfertasMostradas>();
            var servicio = new ServicioOfertasCliente(new ServicioOfertasParaTi(Lector(), Modelo()), Precios(), c => Task.FromResult("0"),
                p => Task.FromResult<string>(null), registro: registro);

            _ = await servicio.Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.SinPrecios, HOY);
            _ = await servicio.Todas("1000", BF, 0, null, null, null, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            A.CallTo(() => registro.Servidas(A<IReadOnlyList<FilaOfertaMostrada>>.That.Matches(f => f.Count == 2 && f.All(x => x.Origen == "Cinco")
                && f[0].Producto == "1" && f[0].Probabilidad == 0.8f && f[0].PosicionRanking == 1 && f[1].Posicion == 2 && f[0].Precio == 7m)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => registro.Servidas(A<IReadOnlyList<FilaOfertaMostrada>>.That.Matches(f => f.All(x => x.Origen == "Todas"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Push_SoloDeVerdadYConEnvio_ApuntaLoQuePropone()
        {
            IRegistroOfertasMostradas registro = A.Fake<IRegistroOfertasMostradas>();
            IFuentePushCampana fuente = A.Fake<IFuentePushCampana>();
            A.CallTo(() => fuente.ClientesConConsejos()).Returns(new List<string> { "1000" });
            A.CallTo(() => fuente.ClientesDeBaja()).Returns(new HashSet<string>());
            A.CallTo(() => fuente.ClientesConPushDeCampana(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(new HashSet<string>());
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Returns(1);
            var servicio = new ServicioPushCampana(new ServicioOfertasParaTi(Lector(), Modelo()), fuente, push, Precios(), c => Task.FromResult("0"),
                p => Task.FromResult<string>(null), ex => { }, registro: registro);

            _ = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");
            A.CallTo(() => registro.Servidas(A<IReadOnlyList<FilaOfertaMostrada>>._)).MustNotHaveHappened();

            _ = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, false, AHORA, "Carlos");
            A.CallTo(() => registro.Servidas(A<IReadOnlyList<FilaOfertaMostrada>>.That.Matches(f => f.Count == 2 && f.All(x => x.Origen == "Push"))))
                .MustHaveHappenedOnceExactly();
        }

        // ---------------------------------------------------------------- controladores

        [TestMethod]
        public void Eventos_204ConElClienteDelToken_Y401SinEl()
        {
            IServicioOfertasCliente servicio = A.Fake<IServicioOfertasCliente>();
            A.CallTo(() => servicio.RegistrarEventos(A<string>._, A<PeticionEventosOfertasDTO>._, A<DateTime>._)).Throws(new Exception("da igual"));
            OfertasClienteController Controlador(params Claim[] claims) => new OfertasClienteController(servicio)
            {
                RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT")) },
                Ahora = () => AHORA
            };
            var peticion = new PeticionEventosOfertasDTO { Campana = BF };

            IHttpActionResult r = Controlador(new Claim("cliente", " 15191 ")).PostEventos(peticion);
            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)r).StatusCode, "Aunque falle por dentro");
            A.CallTo(() => servicio.RegistrarEventos("15191", peticion, AHORA)).MustHaveHappenedOnceExactly();

            Assert.IsInstanceOfType(Controlador(new Claim("IsEmployee", "true")).PostEventos(peticion), typeof(UnauthorizedResult));
        }

        [TestMethod]
        public void Resultado_403DeUsted_503SinTabla_Y200()
        {
            IRepositorioOfertasMostradas repo = A.Fake<IRepositorioOfertasMostradas>();
            CampanasOfertasResultadoController Controlador(params string[] roles) => new CampanasOfertasResultadoController(() => repo)
            {
                RequestContext = new HttpRequestContext { Principal = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Carlos", "Bearer"), roles) },
                Hoy = () => HOY
            };

            var prohibido = (NegotiatedContentResult<string>)Controlador("Tiendas").GetResultado(BF);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);

            A.CallTo(() => repo.TablaExiste()).Returns(false);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)Controlador("Dirección").GetResultado(BF)).StatusCode);

            A.CallTo(() => repo.TablaExiste()).Returns(true);
            A.CallTo(() => repo.LeerResultado(BF, HOY.AddDays(-6))).Returns(new List<FilaResultadoOferta> { new FilaResultadoOferta { Servida = true, Origen = "Cinco" } });
            var ok = (OkNegotiatedContentResult<ResultadoOfertasMostradasDTO>)Controlador("Informática").GetResultado(BF, 7);
            Assert.AreEqual(1, ok.Content.Total.Servidas);
            Assert.AreEqual(7, ok.Content.Dias);
        }
    }
}
