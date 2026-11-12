using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Inventarios
{
    /// <summary>
    /// Épica #784 (incidencia 1186; Carlos 11/11/26): DELETE api/Inventarios/Filas/{numeroOrden} solo borra filas de hoy que SOBRAN (sin
    /// ubicación de un producto que se ubica, o duplicadas), y solo quien tiene permiso; borrar no es «no hay». Y RellenarUbicacion ya
    /// no da por rellenada una ubicación que solo tiene la fila de un «Dejar a cero».
    /// </summary>
    [TestClass]
    public class InventarioBorrarFilaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11);

        private IRepositorioBorrarFilaInventario repositorio;
        private IUnidadBorrarFilaInventario unidad;
        private ServicioBorrarFilaInventario servicio;
        private IServicioContabilizarInventario contabilizar;
        private List<FilaInventarioPendiente> pendientes;
        private List<FilaInventarioBorrada> borradas;
        private Dictionary<string, string> parametros;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioBorrarFilaInventario>();
            unidad = A.Fake<IUnidadBorrarFilaInventario>();
            A.CallTo(() => repositorio.Empezar(A<string>._)).Returns(unidad);
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).Returns(1);
            borradas = new List<FilaInventarioBorrada>();
            A.CallTo(() => repositorio.Borradas(A<DateTime>._)).ReturnsLazily(() => borradas);
            servicio = new ServicioBorrarFilaInventario(repositorio);

            // Las filas pendientes de hoy en ALG: la de la incidencia 1186 (sin ubicación), una normal y dos duplicadas.
            pendientes = new List<FilaInventarioPendiente>
            {
                Pendiente(948787, null, 3, 1),
                Pendiente(948790, "001", 3, 0, producto: "27534"),
                Pendiente(948791, "002", 2, 2, producto: "40245"),
                Pendiente(948792, "002", 2, 2, producto: "40245")
            };
            contabilizar = A.Fake<IServicioContabilizarInventario>();
            A.CallTo(() => contabilizar.Comprobar(A<string>._, A<string>._, A<DateTime>._))
                .ReturnsLazily((string e, string a, DateTime f) => ComprobadorInventario.Comprobar(a, f, true, pendientes, Huecos()));
            parametros = new Dictionary<string, string> { ["Alfredo"] = "1" };
        }

        private static FilaInventarioPendiente Pendiente(int numero, string pasillo, int calculado, int real, string producto = "44901") => new FilaInventarioPendiente
        {
            NumeroOrden = numero,
            Producto = producto,
            Descripcion = "Producto " + producto,
            Pasillo = pasillo,
            Fila = pasillo == null ? null : "002",
            Columna = pasillo == null ? null : "005",
            StockCalculado = calculado,
            StockReal = real,
            Fecha = HOY,
            Ubicar = true,
            Usuario = "NUEVAVISION\\Andre",
            PrecioMedio = 1
        };

        private static List<UbicacionAlmacenInventario> Huecos() => new List<UbicacionAlmacenInventario>
        {
            new UbicacionAlmacenInventario { Pasillo = "001", Fila = "002", Columna = "005", Producto = "27534", Cantidad = 3 },
            new UbicacionAlmacenInventario { Pasillo = "002", Fila = "002", Columna = "005", Producto = "40245", Cantidad = 2 }
        };

        private FilaInventarioBorrar Fila(int numero = 948787, string almacen = "ALG", DateTime? fecha = null, int estado = 1, int? traspaso = null)
        {
            FilaInventarioPendiente p = pendientes.FirstOrDefault(x => x.NumeroOrden == numero);
            return new FilaInventarioBorrar
            {
                NumeroOrden = numero,
                Empresa = "1",
                Almacen = almacen,
                Fecha = fecha ?? HOY.AddHours(10),
                Producto = p?.Producto ?? "44901",
                Descripcion = p?.Descripcion,
                Pasillo = p?.Pasillo,
                Fila = p?.Fila,
                Columna = p?.Columna,
                StockCalculado = p?.StockCalculado ?? 3,
                StockReal = p?.StockReal ?? 1,
                Estado = estado,
                Traspaso = traspaso,
                Usuario = "NUEVAVISION\\Andre"
            };
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private InventariosController Controlador(string nombre, params string[] grupos)
        {
            return new InventariosController(A.Fake<NVEntities>(), A.Fake<IRepositorioCodigosBarras>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioBorrarFila = servicio,
                ServicioContabilizar = contabilizar,
                ParametroBorrarFilas = usuario => parametros.TryGetValue(usuario, out string v) ? v : null,
                AlmacenDelUsuario = (empresa, usuario) => usuario == "Paloma" ? "REI" : null,
                Hoy = () => HOY,
                User = Usuario(nombre, grupos)
            };
        }

        private static HttpStatusCode Codigo(IHttpActionResult resultado)
        {
            switch (resultado)
            {
                case StatusCodeResult s: return s.StatusCode;
                case NegotiatedContentResult<HttpError> e: return e.StatusCode;
                default: throw new AssertFailedException("Resultado inesperado: " + resultado?.GetType().Name);
            }
        }

        private async Task<NegotiatedContentResult<HttpError>> BorrarConError(int numero, string nombre, params string[] grupos) =>
            (NegotiatedContentResult<HttpError>)await Controlador(nombre, grupos).DeleteFila(numero);

        // ---------- Qué filas ----------

        [TestMethod]
        public async Task SinUbicacion_DeHoy_ConPermiso_204YRastroEnJson()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila());

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948787)));

            A.CallTo(() => unidad.BorrarSiPendiente(948787)).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(ServicioBorrarFilaInventario.TABLA_MODIFICACIONES,
                A<string>.That.Matches(j => JsonConvert.DeserializeObject<FilaInventarioBorrar>(j).NumeroOrden == 948787
                    && JsonConvert.DeserializeObject<FilaInventarioBorrar>(j).Almacen == "ALG"),
                A<string>.That.Matches(s => s.Contains("44901") && s.Contains("sin ubicación") && s.Contains("teórico 3") && s.Contains("contado 1") && s.Contains("Borrada por")),
                "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Empezar("InventarioRellenar:1:ALG")).MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow(948790, DisplayName = "El caso de Carlos: fila normal calculado 3, contado 0")]
        public async Task FilaNormal_409NoSobra_YNoBorra(int numero)
        {
            A.CallTo(() => repositorio.Leer(numero)).Returns(Fila(numero));

            var resultado = await BorrarConError(numero, "Alfredo", "Almacén");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreEqual(ReglaBorrarFilaInventario.MENSAJE_NO_SOBRA, resultado.Content.Message);
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FilaNormalContada3De5_409_InclusoParaDireccion()
        {
            pendientes.Add(Pendiente(948795, "001", 5, 3, producto: "27534b"));
            A.CallTo(() => repositorio.Leer(948795)).Returns(Fila(948795));

            var resultado = await BorrarConError(948795, "Carlos", "Dirección");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreEqual(ReglaBorrarFilaInventario.MENSAJE_NO_SOBRA, resultado.Content.Message);
        }

        [TestMethod]
        public async Task Duplicada_SeBorraUna_PeroNuncaLaUltima()
        {
            A.CallTo(() => repositorio.Leer(948791)).Returns(Fila(948791));
            A.CallTo(() => repositorio.Leer(948792)).Returns(Fila(948792));
            A.CallTo(() => unidad.BorrarSiPendiente(948791)).Invokes(() => pendientes.RemoveAll(p => p.NumeroOrden == 948791)).Returns(1);

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948791)));
            var segunda = await BorrarConError(948792, "Alfredo", "Almacén");

            Assert.AreEqual(HttpStatusCode.Conflict, segunda.StatusCode, "la que queda ya no está duplicada");
            A.CallTo(() => unidad.BorrarSiPendiente(948792)).MustNotHaveHappened();
        }

        [TestMethod]
        public void RecontadaSinContarYAvisos_NoSonBorrables()
        {
            var comprobaciones = new ComprobacionesInventarioDTO
            {
                Bloqueantes = { new ComprobacionInventarioDTO { Codigo = CodigosComprobacionInventario.RECONTADA_SIN_CONTAR, Filas = { new FilaComprobacionInventarioDTO { NºOrden = 1, Fecha = HOY } } } },
                Avisos = { new ComprobacionInventarioDTO { Codigo = CodigosComprobacionInventario.CONTADO_CERO_CON_STOCK, Filas = { new FilaComprobacionInventarioDTO { NºOrden = 2, Fecha = HOY } } } }
            };

            Assert.IsFalse(ReglaBorrarFilaInventario.EsBorrable(comprobaciones, 1));
            Assert.IsFalse(ReglaBorrarFilaInventario.EsBorrable(comprobaciones, 2));
            ReglaBorrarFilaInventario.Marcar(comprobaciones, true, HOY);
            Assert.IsFalse(comprobaciones.Bloqueantes[0].Filas[0].SePuedeBorrar);
            Assert.IsFalse(comprobaciones.Avisos[0].Filas[0].SePuedeBorrar);
        }

        // ---------- Incidencias 1227 y 1057 (12/11/26): filas pendientes de días anteriores ----------

        [TestMethod]
        public async Task PendienteDeAyer_FilaNormal_204ConBloqueosYRastro_SinMirarSiSobra()
        {
            // 21075 en ALG, sin ubicación, 0/0, de ayer: paraba el picking y no tenía salida (hubo que borrarla por SQL).
            A.CallTo(() => repositorio.Leer(948700)).Returns(new FilaInventarioBorrar
            {
                NumeroOrden = 948700, Empresa = "1", Almacen = "ALG", Fecha = HOY.AddDays(-1).AddHours(17), Producto = "21075",
                Pasillo = "005", Fila = "002", Columna = "001", Estado = 1, Usuario = "NUEVAVISION\\Alfredo"
            });

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948700)));

            A.CallTo(() => repositorio.Empezar("InventarioRellenar:1:ALG")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Bloquear("InventarioRellenar:1:ALG:005:002:001")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.BorrarSiPendiente(948700)).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(ServicioBorrarFilaInventario.TABLA_MODIFICACIONES, A<string>._,
                A<string>.That.Contains("del 10/11/2026"), "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
            A.CallTo(() => contabilizar.Comprobar(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PendienteDeAyer_SinPermiso_403()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(fecha: HOY.AddDays(-1)));

            var resultado = await BorrarConError(948787, "Andre", "Almacén");

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ContabilizadaDeAyer_409_NoSeBorraNunca()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(fecha: HOY.AddDays(-1), estado: 2, traspaso: 81350));

            var resultado = await BorrarConError(948787, "Carlos", "Dirección");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "contabilizada (traspaso 81350)");
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PendienteDeManana_409_InclusoParaDireccion()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(fecha: HOY.AddDays(1)));

            var resultado = await BorrarConError(948787, "Carlos", "Dirección");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, ReglaBorrarFilaInventario.MENSAJE_FECHA_POSTERIOR);
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PendienteDeAyer_LaContabilizanMientrasTanto_409YNoConfirma()
        {
            A.CallTo(() => repositorio.Leer(948787)).ReturnsNextFromSequence(Fila(fecha: HOY.AddDays(-1)),
                Fila(fecha: HOY.AddDays(-1), estado: 2, traspaso: 81352));
            A.CallTo(() => unidad.BorrarSiPendiente(948787)).Returns(0);

            Assert.AreEqual(HttpStatusCode.Conflict, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948787)));
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public void Marcar_OtraFechaAnteriorAHoy_SePuedeBorrar_YLasDeHoyNo_AlComprobarAyer()
        {
            // Se comprueba AYER: en OtraFecha salen la de anteayer (borrable) y la de HOY (no: hoy solo se borra la que sobra).
            pendientes.Add(new FilaInventarioPendiente { NumeroOrden = 948600, Producto = "21075", Fecha = HOY.AddDays(-2), Ubicar = true, Pasillo = "005", Fila = "002", Columna = "001" });
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY.AddDays(-1), true, pendientes, Huecos());

            ReglaBorrarFilaInventario.Marcar(c, true, HOY);

            List<FilaComprobacionInventarioDTO> otraFecha = c.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.OTRA_FECHA).Filas;
            Assert.IsTrue(otraFecha.Single(f => f.NºOrden == 948600).SePuedeBorrar);
            Assert.IsTrue(otraFecha.Where(f => f.NºOrden != 948600).All(f => !f.SePuedeBorrar), "las de hoy no se marcan por esta regla");
            Assert.AreEqual(5, otraFecha.Count);
        }

        [TestMethod]
        public void Marcar_OtraFechaAnterior_SinPermiso_NoSePuedeBorrar()
        {
            pendientes.Add(new FilaInventarioPendiente { NumeroOrden = 948600, Producto = "21075", Fecha = HOY.AddDays(-1), Ubicar = true });
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY, true, pendientes, Huecos());

            ReglaBorrarFilaInventario.Marcar(c, false, HOY);

            Assert.IsFalse(c.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.OTRA_FECHA).Filas.Single().SePuedeBorrar);
        }

        [TestMethod]
        public async Task Comprobaciones_DeHoy_LaPendienteDeAyerSePuedeBorrar()
        {
            pendientes.Add(new FilaInventarioPendiente { NumeroOrden = 948600, Producto = "21075", Fecha = HOY.AddDays(-1), Ubicar = true });

            ComprobacionesInventarioDTO c = await Comprobaciones("Alfredo", "Almacén");

            Assert.IsTrue(c.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.OTRA_FECHA).Filas.Single().SePuedeBorrar);
        }

        [TestMethod]
        public async Task Comprobaciones_BorradaHoyUnaFilaDeAyer_SaleEnElAvisoConSuDia()
        {
            FilaInventarioBorrar deAyer = Fila(948700, fecha: HOY.AddDays(-1));
            deAyer.Descripcion = "CARTUCHO VINOTHERAPYA";
            borradas.Add(new FilaInventarioBorrada { Fila = deAyer, BorradaPor = "NUEVAVISION\\Alfredo", BorradaEl = HOY.AddHours(9) });

            ComprobacionesInventarioDTO c = await Comprobaciones("Alfredo", "Almacén");

            FilaComprobacionInventarioDTO linea = c.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.FILAS_BORRADAS).Filas.Single();
            Assert.AreEqual("CARTUCHO VINOTHERAPYA (fila del 10/11/2026)", linea.Descripcion);
        }

        [TestMethod]
        public async Task Contabilizada_409()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(estado: 2, traspaso: 81350));

            var resultado = await BorrarConError(948787, "Carlos", "Dirección");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "contabilizada (traspaso 81350)");
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task NoExiste_404ConMessageSimple()
        {
            A.CallTo(() => repositorio.Leer(1)).Returns(Task.FromResult<FilaInventarioBorrar>(null));

            var resultado = await BorrarConError(1, "Alfredo", "Almacén");

            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
            Assert.IsFalse(resultado.Content.ContainsKey("MessageDetail"));
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SeContabilizaMientrasTanto_409YNoConfirma()
        {
            A.CallTo(() => repositorio.Leer(948787)).ReturnsNextFromSequence(Fila(), Fila(estado: 2, traspaso: 81351));
            A.CallTo(() => unidad.BorrarSiPendiente(948787)).Returns(0);

            Assert.AreEqual(HttpStatusCode.Conflict, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948787)));
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        // ---------- Quién ----------

        [TestMethod]
        public async Task AlmacenSinElParametro_403()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila());

            var resultado = await BorrarConError(948787, "Andre", "Almacén");

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(ReglaBorrarFilaInventario.MENSAJE_SIN_PERMISO, resultado.Content.Message);
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ParametroACero_403()
        {
            parametros["Andre"] = "0";
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila());

            Assert.AreEqual(HttpStatusCode.Forbidden, Codigo(await Controlador("Andre", "Almacén").DeleteFila(948787)));
        }

        [TestMethod]
        public async Task Administracion_SinParametro_204()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila());

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Pilar", "Administración").DeleteFila(948787)));
        }

        [TestMethod]
        public async Task TiendaDeOtroAlmacen_403AunqueTengaElParametro()
        {
            parametros["Paloma"] = "1";
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(almacen: "ALG"));

            var resultado = await BorrarConError(948787, "Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "REI");
        }

        // ---------- Comprobaciones: NumeroOrden, SePuedeBorrar y filas borradas ----------

        private async Task<ComprobacionesInventarioDTO> Comprobaciones(string nombre, params string[] grupos) =>
            ((OkNegotiatedContentResult<ComprobacionesInventarioDTO>)await Controlador(nombre, grupos).GetComprobaciones("ALG", HOY)).Content;

        [TestMethod]
        public async Task Comprobaciones_ConPermiso_SePuedeBorrarSoloEnSinUbicacionYDuplicadas()
        {
            ComprobacionesInventarioDTO c = await Comprobaciones("Alfredo", "Almacén");

            FilaComprobacionInventarioDTO sinUbicacion = c.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.SIN_UBICACION).Filas.Single();
            Assert.AreEqual(948787, sinUbicacion.NumeroOrden);
            Assert.IsTrue(sinUbicacion.SePuedeBorrar);
            Assert.IsTrue(c.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.FILAS_DUPLICADAS).Filas.All(f => f.SePuedeBorrar));
            Assert.IsTrue(c.Bloqueantes.Where(b => !ReglaBorrarFilaInventario.CODIGOS_BORRABLES.Contains(b.Codigo)).SelectMany(b => b.Filas).All(f => !f.SePuedeBorrar));
            Assert.IsTrue(c.Avisos.SelectMany(a => a.Filas).All(f => !f.SePuedeBorrar));
        }

        [TestMethod]
        public async Task Comprobaciones_SinPermiso_NingunaSePuedeBorrar()
        {
            ComprobacionesInventarioDTO c = await Comprobaciones("Andre", "Almacén");

            Assert.IsTrue(c.Bloqueantes.Concat(c.Avisos).SelectMany(b => b.Filas).All(f => !f.SePuedeBorrar));
        }

        [TestMethod]
        public async Task Comprobaciones_FilasBorradasHoy_AvisoConQuienYCuando()
        {
            borradas.Add(new FilaInventarioBorrada { Fila = Fila(948700), BorradaPor = "NUEVAVISION\\Alfredo", BorradaEl = HOY.AddHours(12) });
            borradas.Add(new FilaInventarioBorrada { Fila = Fila(948701, almacen: "REI"), BorradaPor = "NUEVAVISION\\Paloma", BorradaEl = HOY.AddHours(12) });

            ComprobacionesInventarioDTO c = await Comprobaciones("Alfredo", "Almacén");

            ComprobacionInventarioDTO aviso = c.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.FILAS_BORRADAS);
            StringAssert.StartsWith(aviso.Titulo, "Filas borradas hoy del recuento (1)");
            FilaComprobacionInventarioDTO linea = aviso.Filas.Single();
            Assert.IsNull(linea.NºOrden);
            Assert.IsFalse(linea.SePuedeBorrar);
            Assert.AreEqual("NUEVAVISION\\Alfredo", linea.Usuario);
            Assert.AreEqual(HOY.AddHours(12), linea.Fecha);
            Assert.AreEqual(3, linea.StockCalculado);
        }

        // ---------- RellenarUbicacion tras un «Dejar a cero» ----------

        [TestMethod]
        public async Task RellenarUbicacion_TresProductosUnoYaConFilaACero_CreaLasDosQueFaltanSinDuplicar()
        {
            var repoUbicaciones = A.Fake<IRepositorioInventarioUbicaciones>();
            var unidadUbicaciones = A.Fake<IUnidadInventarioUbicaciones>();
            var insertadas = new List<Inventario>();
            var yaHabia = new LineaInventarioContadaDTO { NumeroOrden = 948796, Producto = "44901", StockCalculado = 2, StockReal = 0, Pasillo = "001", Fila = "002", Columna = "005" };
            A.CallTo(() => repoUbicaciones.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repoUbicaciones.Empezar(A<string>._)).Returns(unidadUbicaciones);
            A.CallTo(() => unidadUbicaciones.Insertar(A<IList<Inventario>>._)).Invokes((IList<Inventario> f) => insertadas.AddRange(f));
            A.CallTo(() => unidadUbicaciones.Pendientes(A<string>._, A<string>._, A<DateTime>._, A<FiltroInventarioContado>._))
                .ReturnsLazily(() => new[] { yaHabia }.Concat(insertadas.Select((f, n) => new LineaInventarioContadaDTO
                {
                    NumeroOrden = 949000 + n, Producto = f.Número, StockCalculado = f.StockCalculado ?? 0, Pasillo = f.Pasillo
                })).ToList());
            A.CallTo(() => unidadUbicaciones.TeoricoUbicacion("1", "ALG", "001", "002", "005")).Returns(new List<StockProductoInventario>
            {
                new StockProductoInventario { Numero = "44901", Stock = 2 },
                new StockProductoInventario { Numero = "27534", Stock = 3 },
                new StockProductoInventario { Numero = "40245 ", Stock = 1 }
            });

            var resultado = await new ServicioInventarioUbicaciones(repoUbicaciones).RellenarUbicacion(new RellenarUbicacionInventarioDTO
            {
                Empresa = "1", Almacen = "ALG", Fecha = HOY, Pasillo = "001", Fila = "002", Columna = "005"
            }, "NUEVAVISION\\Andre");

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            CollectionAssert.AreEquivalent(new[] { "27534", "40245" }, insertadas.Select(f => f.Número).ToArray());
            Assert.AreEqual(2, resultado.Respuesta.Filas);
            Assert.AreEqual(3, resultado.Respuesta.Lineas.Count);
            A.CallTo(() => unidadUbicaciones.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarUbicacion_TodosConFila_SigueSiendoYaRellenado()
        {
            var repoUbicaciones = A.Fake<IRepositorioInventarioUbicaciones>();
            var unidadUbicaciones = A.Fake<IUnidadInventarioUbicaciones>();
            A.CallTo(() => repoUbicaciones.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repoUbicaciones.Empezar(A<string>._)).Returns(unidadUbicaciones);
            A.CallTo(() => unidadUbicaciones.Pendientes(A<string>._, A<string>._, A<DateTime>._, A<FiltroInventarioContado>._))
                .Returns(new List<LineaInventarioContadaDTO> { new LineaInventarioContadaDTO { NumeroOrden = 1, Producto = "44901" } });
            A.CallTo(() => unidadUbicaciones.TeoricoUbicacion("1", "ALG", "001", "002", "005"))
                .Returns(new List<StockProductoInventario> { new StockProductoInventario { Numero = "44901", Stock = 2 } });

            var resultado = await new ServicioInventarioUbicaciones(repoUbicaciones).RellenarUbicacion(new RellenarUbicacionInventarioDTO
            {
                Empresa = "1", Almacen = "ALG", Fecha = HOY, Pasillo = "001", Fila = "002", Columna = "005"
            }, "NUEVAVISION\\Andre");

            Assert.AreEqual(EstadoInventarioUbicaciones.YaRellenado, resultado.Estado);
            A.CallTo(() => unidadUbicaciones.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Siguientes_UbicacionConMenosFilasQueProductos_NoEstaYaRellenada()
        {
            var repoUbicaciones = A.Fake<IRepositorioInventarioUbicaciones>();
            A.CallTo(() => repoUbicaciones.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repoUbicaciones.UbicacionesParaInventario(2)).Returns(new List<UbicacionPrioridadInventario>
            {
                new UbicacionPrioridadInventario { Pasillo = "001", Fila = "002", Columna = "005" },
                new UbicacionPrioridadInventario { Pasillo = "003", Fila = "001", Columna = "002" }
            });
            A.CallTo(() => repoUbicaciones.ProductosPorUbicacion("ALG")).Returns(new List<UbicacionConProductos>
            {
                new UbicacionConProductos { Pasillo = "001", Fila = "002", Columna = "005", Productos = 3 },
                new UbicacionConProductos { Pasillo = "003", Fila = "001", Columna = "002", Productos = 2 }
            });
            A.CallTo(() => repoUbicaciones.UbicacionesRellenadas("1", "ALG", HOY)).Returns(new List<UbicacionConProductos>
            {
                new UbicacionConProductos { Pasillo = "001", Fila = "002", Columna = "005", Productos = 1 },
                new UbicacionConProductos { Pasillo = "003", Fila = "001", Columna = "002", Productos = 2 }
            });

            var resultado = await new ServicioInventarioUbicaciones(repoUbicaciones).Siguientes("1", "ALG", 2, HOY);

            Assert.IsFalse(resultado.Respuesta[0].YaRellenadaHoy);
            Assert.IsTrue(resultado.Respuesta[1].YaRellenadaHoy);
        }
    }
}
