using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#789 (épica #784): POST api/Inventarios/Rellenar, el INSERT inicial del inventario de las tiendas que antes se
    /// hacía a mano desde SSMS.
    /// </summary>
    [TestClass]
    public class InventariosRellenarTests
    {
        private const string USUARIO = @"NUEVAVISION\Paloma";
        private readonly DateTime dia = new DateTime(2026, 11, 21);

        private IRepositorioRellenarInventario repositorio;
        private IUnidadRellenarInventario unidad;
        private List<Inventario> insertadas;
        private List<StockProductoInventario> stocks;
        private List<PendientesInventarioUsuario> pendientes;
        private List<ContabilizadasInventarioUsuario> contabilizadas;
        private AlmacenInventario almacen;

        [TestInitialize]
        public void Inicializar()
        {
            insertadas = new List<Inventario>();
            stocks = new List<StockProductoInventario>();
            pendientes = new List<PendientesInventarioUsuario>();
            almacen = new AlmacenInventario { Nombre = "Reina", ControlUbicaciones = false };

            repositorio = A.Fake<IRepositorioRellenarInventario>();
            unidad = A.Fake<IUnidadRellenarInventario>();
            A.CallTo(() => repositorio.LeerAlmacen(A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(almacen));
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).Returns(Task.FromResult(unidad));
            A.CallTo(() => unidad.Pendientes(A<string>._, A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(pendientes));
            contabilizadas = new List<ContabilizadasInventarioUsuario>();
            A.CallTo(() => unidad.Contabilizadas(A<string>._, A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(contabilizadas));
            A.CallTo(() => unidad.Stocks(A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(stocks));
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).Invokes((IList<Inventario> filas) => insertadas.AddRange(filas)).Returns(Task.CompletedTask);
        }

        // ---------- Servicio ----------

        [TestMethod]
        public async Task Rellenar_InsertaLosDeStockPositivoYNegativo_NoLosDeCeroNiLosFicticios()
        {
            stocks.Add(Stock("17404", 5));
            stocks.Add(Stock("40945", -2));
            stocks.Add(Stock("11111", 0));
            stocks.Add(Stock("99999", 3, ficticio: true));

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(Peticion("REI"), USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.Ok, resultado.Estado);
            CollectionAssert.AreEquivalent(new[] { "17404", "40945" }, insertadas.Select(i => i.Número).ToArray());
            Inventario positivo = insertadas.Single(i => i.Número == "17404");
            Assert.AreEqual(5, positivo.StockCalculado);
            Assert.AreEqual(0, positivo.StockReal);
            Assert.AreEqual((byte)1, positivo.Estado);
            Assert.AreEqual("NestoAPI", positivo.Aplicacion);
            Assert.AreEqual(USUARIO, positivo.Usuario);
            Assert.AreEqual("1", positivo.Empresa);
            Assert.AreEqual("REI", positivo.Almacén);
            Assert.AreEqual(dia, positivo.Fecha);
            Assert.IsNull(positivo.PorCodigoBarras);
            Assert.AreEqual("PRODUCTO 17404", positivo.Descripción);
            Assert.AreEqual("COS", positivo.Grupo);
            Assert.AreEqual("ACP", positivo.Subgrupo);
            Assert.AreEqual("Eva Visnu", positivo.Familia);
            Assert.AreEqual(-2, insertadas.Single(i => i.Número == "40945").StockCalculado);
            Assert.AreEqual(2, resultado.Respuesta.Filas);
            Assert.AreEqual(3, resultado.Respuesta.Unidades);
            Assert.AreEqual("REI", resultado.Respuesta.Almacen);
            Assert.AreEqual(dia, resultado.Respuesta.Fecha);
            Assert.AreEqual(USUARIO, resultado.Respuesta.Usuario);
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Rellenar_FechaConHora_GrabaElDiaSinHora()
        {
            stocks.Add(Stock("17404", 1));
            var peticion = Peticion("REI");
            peticion.Fecha = dia.AddHours(17).AddMinutes(32);

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(peticion, USUARIO);

            Assert.AreEqual(dia, insertadas.Single().Fecha);
            Assert.AreEqual(dia, resultado.Respuesta.Fecha);
            A.CallTo(() => unidad.Pendientes("1", "REI", dia)).MustHaveHappened();
        }

        [TestMethod]
        public async Task Rellenar_YaHayPendientesEseDia_YaRellenadoDiciendoCuantasYDeQuien_SinInsertar()
        {
            stocks.Add(Stock("17404", 5));
            pendientes.Add(new PendientesInventarioUsuario { Usuario = USUARIO, Filas = 64 });
            pendientes.Add(new PendientesInventarioUsuario { Usuario = @"NUEVAVISION\Victoria", Filas = 3 });

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(Peticion("REI"), USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.YaRellenado, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "67 filas pendientes");
            StringAssert.Contains(resultado.Mensaje, @"NUEVAVISION\Paloma: 64");
            StringAssert.Contains(resultado.Mensaje, @"NUEVAVISION\Victoria: 3");
            StringAssert.Contains(resultado.Mensaje, "21/11/2026");
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
            A.CallTo(() => unidad.Dispose()).MustHaveHappened();
        }

        [TestMethod]
        public async Task Rellenar_YaHayFilasContabilizadasEseDia_YaContabilizadoSinInsertar_784()
        {
            // 09/11/26: Paloma contó y contabilizó el 42341 por la mañana; rellenar la tienda entera después lo pondría otra vez a contar.
            stocks.Add(Stock("42341", 2));
            contabilizadas.Add(new ContabilizadasInventarioUsuario { Usuario = USUARIO, Filas = 1, Traspaso = 81350 });

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(Peticion("ALC"), USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.YaContabilizado, resultado.Estado);
            Assert.AreEqual(@"Ya hay 1 fila de ALC contada y contabilizada el 21/11/2026 (NUEVAVISION\Paloma: 1, traspaso 81350). Si de verdad hay que volver a contar, pulse «Contar otra vez».",
                resultado.Mensaje);
            Assert.AreEqual(81350, resultado.Contabilizadas.Single().Traspaso);
            A.CallTo(() => unidad.Contabilizadas("1", "ALC", dia)).MustHaveHappened();
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Rellenar_ConOtraVez_AunqueHayaContabilizadas_Rellena_784()
        {
            stocks.Add(Stock("42341", 2));
            contabilizadas.Add(new ContabilizadasInventarioUsuario { Usuario = USUARIO, Filas = 1, Traspaso = 81350 });
            RellenarInventarioDTO peticion = Peticion("ALC");
            peticion.OtraVez = true;

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(peticion, USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.Ok, resultado.Estado);
            Assert.AreEqual(2, insertadas.Single().StockCalculado);
        }

        [TestMethod]
        public async Task Rellenar_ConOtraVez_SiHayPendientes_SigueSiendoYaRellenado_784()
        {
            pendientes.Add(new PendientesInventarioUsuario { Usuario = USUARIO, Filas = 2 });
            RellenarInventarioDTO peticion = Peticion("ALC");
            peticion.OtraVez = true;

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(peticion, USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.YaRellenado, resultado.Estado);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Rellenar_AlmacenConUbicaciones_ConUbicaciones_SinTocarNada()
        {
            almacen = new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true };

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(Peticion("ALG"), USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.ConUbicaciones, resultado.Estado);
            Assert.AreEqual("Algete se inventaría por ubicaciones desde Ariadna.", resultado.Mensaje);
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Rellenar_AlmacenQueNoExiste_PeticionIncorrecta()
        {
            almacen = null;

            ResultadoRellenarInventario resultado = await Servicio().Rellenar(Peticion("XXX"), USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.PeticionIncorrecta, resultado.Estado);
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Rellenar_SinStockNinguno_ConfirmaConCeroFilas()
        {
            ResultadoRellenarInventario resultado = await Servicio().Rellenar(Peticion("ALC"), USUARIO);

            Assert.AreEqual(EstadoRellenarInventario.Ok, resultado.Estado);
            Assert.AreEqual(0, resultado.Respuesta.Filas);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
        }

        // ---------- Controlador ----------

        [TestMethod]
        public async Task PostRellenar_SinRol_Prohibido()
        {
            InventariosController controller = Controlador(roles: new string[0]);

            var respuesta = await controller.PostRellenar(Peticion("REI"));

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(respuesta));
            A.CallTo(() => repositorio.LeerAlmacen(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostRellenar_Tiendas_RellenaConElUsuarioDelToken()
        {
            stocks.Add(Stock("17404", 5));
            InventariosController controller = Controlador(Constantes.GruposSeguridad.TIENDAS);
            RellenarInventarioDTO peticion = Peticion("REI");
            peticion.Confirmar = "reina";

            var respuesta = await controller.PostRellenar(peticion);

            var ok = respuesta as OkNegotiatedContentResult<RellenarInventarioRespuestaDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1, ok.Content.Filas);
            Assert.AreEqual(5, ok.Content.Unidades);
            Assert.AreEqual(USUARIO, ok.Content.Usuario);
            Assert.AreEqual(USUARIO, insertadas.Single().Usuario);
        }

        [TestMethod]
        public async Task PostRellenar_YaRellenado_Conflicto()
        {
            pendientes.Add(new PendientesInventarioUsuario { Usuario = USUARIO, Filas = 64 });
            InventariosController controller = Controlador(Constantes.GruposSeguridad.ALMACEN);

            var respuesta = await controller.PostRellenar(Peticion("REI"));

            Assert.AreEqual(HttpStatusCode.Conflict, Estado(respuesta));
        }

        [TestMethod]
        public async Task PostRellenar_YaContabilizado_ConflictoConCodigoYContarOtraVez_784()
        {
            contabilizadas.Add(new ContabilizadasInventarioUsuario { Usuario = USUARIO, Filas = 1, Traspaso = 81350 });
            InventariosController controller = Controlador(Constantes.GruposSeguridad.TIENDAS);

            var respuesta = await controller.PostRellenar(Peticion("ALC"));

            var conflicto = respuesta as System.Web.Http.Results.NegotiatedContentResult<YaRellenadoTiendaInventarioDTO>;
            Assert.IsNotNull(conflicto);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(CodigosConflictoInventario.YA_CONTABILIZADO, conflicto.Content.Codigo);
            Assert.IsTrue(conflicto.Content.PuedeContarOtraVez);
            Assert.AreEqual(81350, conflicto.Content.Contabilizadas.Single().Traspaso);
        }

        [TestMethod]
        public async Task PostRellenar_YaRellenado_ConflictoSinContarOtraVez_784()
        {
            pendientes.Add(new PendientesInventarioUsuario { Usuario = USUARIO, Filas = 64 });
            InventariosController controller = Controlador(Constantes.GruposSeguridad.ALMACEN);

            var conflicto = await controller.PostRellenar(Peticion("REI")) as System.Web.Http.Results.NegotiatedContentResult<YaRellenadoTiendaInventarioDTO>;

            Assert.IsNotNull(conflicto);
            Assert.AreEqual(CodigosConflictoInventario.YA_RELLENADO, conflicto.Content.Codigo);
            Assert.IsFalse(conflicto.Content.PuedeContarOtraVez);
            Assert.AreEqual(64, conflicto.Content.Pendientes.Single().Filas);
        }

        [TestMethod]
        public async Task PostRellenar_AlmacenConUbicaciones_BadRequest()
        {
            almacen = new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true };
            InventariosController controller = Controlador(Constantes.GruposSeguridad.DIRECCION);

            var respuesta = await controller.PostRellenar(Peticion("ALG"));

            var bad = respuesta as BadRequestErrorMessageResult;
            Assert.IsNotNull(bad);
            Assert.AreEqual("Algete se inventaría por ubicaciones desde Ariadna.", bad.Message);
        }

        // ---------- Rellenar por error (Paloma, 25/11/26) ----------

        [TestMethod]
        public async Task PostRellenar_DesdeNestoSinConfirmar_400SinCrearNada()
        {
            stocks.Add(Stock("17404", 5));
            almacen = new AlmacenInventario { Nombre = "Alcobendas", ControlUbicaciones = false };
            InventariosController controller = Controlador(Constantes.GruposSeguridad.TIENDAS);

            var respuesta = await controller.PostRellenar(Peticion("ALC")) as System.Web.Http.Results.NegotiatedContentResult<ConfirmacionRellenarInventarioDTO>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.AreEqual("CONFIRMACION_NECESARIA", respuesta.Content.Codigo);
            Assert.AreEqual("Para rellenar el inventario de Alcobendas escriba el nombre del almacén.", respuesta.Content.Message);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostRellenar_DesdeNestoConfirmandoMalONombreOCodigo()
        {
            stocks.Add(Stock("17404", 5));
            almacen = new AlmacenInventario { Nombre = "Alcobendas", ControlUbicaciones = false };
            RellenarInventarioDTO mal = Peticion("ALC");
            mal.Confirmar = "Reina";
            var rechazo = await Controlador(Constantes.GruposSeguridad.TIENDAS).PostRellenar(mal);
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(rechazo));

            // Por la query, el código en minúsculas
            var conCodigo = await Controlador(Constantes.GruposSeguridad.TIENDAS).PostRellenar(Peticion("ALC"), "alc");
            Assert.IsInstanceOfType(conCodigo, typeof(OkNegotiatedContentResult<RellenarInventarioRespuestaDTO>));
        }

        [TestMethod]
        public async Task PostRellenar_DesdeAriadna_NoPideConfirmacion()
        {
            stocks.Add(Stock("17404", 5));
            InventariosController controller = Controlador(Constantes.GruposSeguridad.TIENDAS);
            controller.Request = new System.Net.Http.HttpRequestMessage();
            controller.Request.Headers.Add("X-Ariadna-Version", "2.44");

            var respuesta = await controller.PostRellenar(Peticion("REI"));

            Assert.IsInstanceOfType(respuesta, typeof(OkNegotiatedContentResult<RellenarInventarioRespuestaDTO>));
            Assert.AreEqual(1, insertadas.Count);
        }

        [TestMethod]
        public async Task PostRellenar_DesdeNestoYaRellenado_SigueSiendo409SinPedirConfirmacion()
        {
            pendientes.Add(new PendientesInventarioUsuario { Usuario = USUARIO, Filas = 64 });

            var respuesta = await Controlador(Constantes.GruposSeguridad.TIENDAS).PostRellenar(Peticion("REI"));

            Assert.AreEqual(HttpStatusCode.Conflict, Estado(respuesta));
        }

        [TestMethod]
        public void ReglaConfirmacion_CodigoNombreOPalabraDelNombre_SinMayusculasNiTildes()
        {
            Assert.IsTrue(ReglaConfirmacionRellenado.Casa(" alc ", "ALC", "ALCOBENDAS"));
            Assert.IsTrue(ReglaConfirmacionRellenado.Casa("Alcobendas", "ALC", "ALCOBENDAS"));
            Assert.IsTrue(ReglaConfirmacionRellenado.Casa("alcobendas", "ALC", "Tienda Alcobendas"));
            Assert.IsTrue(ReglaConfirmacionRellenado.Casa("ALGETE", "ALG", "Álgete"));
            Assert.IsFalse(ReglaConfirmacionRellenado.Casa("de", "ALC", "Tienda de Alcobendas"));
            Assert.IsFalse(ReglaConfirmacionRellenado.Casa("Reina", "ALC", "Alcobendas"));
            Assert.IsFalse(ReglaConfirmacionRellenado.Casa(null, "ALC", "Alcobendas"));
            Assert.IsFalse(ReglaConfirmacionRellenado.Casa("  ", "ALC", "Alcobendas"));
        }

        [TestMethod]
        public async Task Previa_CuentaLasFilasConElMismoCriterio_SinTocarNada()
        {
            almacen = new AlmacenInventario { Nombre = "Alcobendas", ControlUbicaciones = false };
            for (int i = 0; i < 3032; i++)
            {
                stocks.Add(Stock((10000 + i).ToString(), 1));
            }
            stocks.Add(Stock("99999", 3, ficticio: true));
            stocks.Add(Stock("88888", 0));
            A.CallTo(() => repositorio.LeerStocks(A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(stocks));
            A.CallTo(() => repositorio.LeerPendientesPorUsuario(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<ResumenPendientesInventarioUsuario>());

            ResultadoPreviaRellenarInventario r = await Servicio().Previa(null, " alc ", dia.AddHours(9), USUARIO, false);

            Assert.AreEqual(EstadoRellenarInventario.Ok, r.Estado);
            Assert.AreEqual("ALC", r.Respuesta.Almacen);
            Assert.AreEqual("Alcobendas", r.Respuesta.NombreAlmacen);
            Assert.AreEqual(dia, r.Respuesta.Fecha);
            Assert.AreEqual(3032, r.Respuesta.FilasQueSeCrearian);
            Assert.AreEqual(0, r.Respuesta.FilasPendientesYa);
            Assert.AreEqual("Va a crear 3.032 filas de inventario en Alcobendas con fecha 21/11/2026.", r.Respuesta.Mensaje);
            Assert.IsFalse(r.Respuesta.PuedeDeshacer);
            StringAssert.Contains(r.Respuesta.MotivoNoPuedeDeshacer, "no hay nada que deshacer");
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Previa_ConPendientesSuyasSinContar_LoDiceYPuedeDeshacer()
        {
            almacen = new AlmacenInventario { Nombre = "Alcobendas", ControlUbicaciones = false };
            A.CallTo(() => repositorio.LeerPendientesPorUsuario("1", "ALC", dia)).Returns(new List<ResumenPendientesInventarioUsuario>
            {
                new ResumenPendientesInventarioUsuario { Usuario = USUARIO, Filas = 3032 }
            });

            ResultadoPreviaRellenarInventario r = await Servicio().Previa("1", "ALC", dia, USUARIO, false);

            Assert.AreEqual(0, r.Respuesta.FilasQueSeCrearian);
            Assert.AreEqual(3032, r.Respuesta.FilasPendientesYa);
            Assert.AreEqual("Ya hay 3.032 filas pendientes de inventario en Alcobendas con fecha 21/11/2026 (Paloma: 3.032): rellenar no crearía ninguna más.",
                r.Respuesta.Mensaje);
            Assert.IsTrue(r.Respuesta.PuedeDeshacer);
            Assert.IsNull(r.Respuesta.MotivoNoPuedeDeshacer);
            A.CallTo(() => repositorio.LeerStocks(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Previa_ConFilasContadasODeOtro_NoPuedeDeshacerSalvoDireccion()
        {
            A.CallTo(() => repositorio.LeerPendientesPorUsuario(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<ResumenPendientesInventarioUsuario>
            {
                new ResumenPendientesInventarioUsuario { Usuario = USUARIO, Filas = 100, FilasContadas = 4 }
            });

            ResultadoPreviaRellenarInventario suyo = await Servicio().Previa("1", "REI", dia, USUARIO, false);
            ResultadoPreviaRellenarInventario otro = await Servicio().Previa("1", "REI", dia, @"NUEVAVISION\Laura", false);
            ResultadoPreviaRellenarInventario direccion = await Servicio().Previa("1", "REI", dia, @"NUEVAVISION\Carlos", true);

            Assert.IsFalse(suyo.Respuesta.PuedeDeshacer);
            StringAssert.Contains(suyo.Respuesta.MotivoNoPuedeDeshacer, "ya hay 4 filas contadas");
            Assert.IsFalse(otro.Respuesta.PuedeDeshacer);
            StringAssert.Contains(otro.Respuesta.MotivoNoPuedeDeshacer, "hay filas de otro usuario (Paloma: 100)");
            Assert.IsTrue(direccion.Respuesta.PuedeDeshacer);
        }

        [TestMethod]
        public async Task GetPrevia_SinRol403_AlgeteYSinAlmacen400()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await Controlador(roles: new string[0]).GetPreviaRellenar("REI", dia)));
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await Controlador(Constantes.GruposSeguridad.TIENDAS).GetPreviaRellenar(null, dia)));
            almacen = new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true };
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await Controlador(Constantes.GruposSeguridad.ALMACEN).GetPreviaRellenar("ALG", dia)));
            A.CallTo(() => repositorio.LeerPendientesPorUsuario(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Miles_ConPuntoTambienConCuatroCifras()
        {
            Assert.AreEqual("3.032", ServicioRellenarInventario.Miles(3032));
            Assert.AreEqual("999", ServicioRellenarInventario.Miles(999));
            Assert.AreEqual("1.234.567", ServicioRellenarInventario.Miles(1234567));
        }

        [TestMethod]
        public void PuedeRellenarInventario_LosCincoGrupos_YNadieMas()
        {
            foreach (string grupo in new[] { "Tiendas", "Almacén", "Administración", "Dirección", "Informática" })
            {
                Assert.IsTrue(InventariosController.PuedeRellenarInventario(Usuario(grupo)), grupo);
            }
            Assert.IsFalse(InventariosController.PuedeRellenarInventario(Usuario(Constantes.GruposSeguridad.COMPRAS)));
            Assert.IsFalse(InventariosController.PuedeRellenarInventario(null));
        }

        // ---------- Ayudas ----------

        private ServicioRellenarInventario Servicio() => new ServicioRellenarInventario(repositorio);

        private RellenarInventarioDTO Peticion(string almacenPedido) => new RellenarInventarioDTO { Empresa = "1", Almacen = almacenPedido, Fecha = dia };

        private static StockProductoInventario Stock(string numero, int cantidad, bool ficticio = false) => new StockProductoInventario
        {
            Numero = numero + "    ",
            Descripcion = "PRODUCTO " + numero + "   ",
            Grupo = "COS",
            SubGrupo = "ACP",
            Familia = "Eva Visnu ",
            Ficticio = ficticio,
            Stock = cantidad
        };

        private InventariosController Controlador(params string[] roles)
        {
            var db = A.Fake<NVEntities>();
            var controller = new InventariosController(db, A.Fake<IRepositorioCodigosBarras>())
            {
                User = Usuario(roles),
                ServicioRellenar = Servicio()
            };
            return controller;
        }

        private static ClaimsPrincipal Usuario(params string[] roles)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, USUARIO) };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private static HttpStatusCode? Estado(System.Web.Http.IHttpActionResult respuesta)
        {
            var tipo = respuesta.GetType();
            var propiedad = tipo.GetProperty("StatusCode");
            return propiedad == null ? (HttpStatusCode?)null : (HttpStatusCode)propiedad.GetValue(respuesta);
        }
    }
}
