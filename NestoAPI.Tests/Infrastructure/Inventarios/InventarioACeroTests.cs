using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
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
    /// Sugerencia 1185 (Alfredo): dejar una referencia a cero en todas sus ubicaciones de una vez (POST api/Inventarios/DejarACero), por
    /// las filas pendientes del inventario de hoy; se contabiliza después con lo de siempre.
    /// </summary>
    [TestClass]
    public class InventarioACeroTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11, 10, 30, 0);
        private const string ALFREDO = "NUEVAVISION\\Alfredo";

        private IRepositorioInventarioACero repositorio;
        private IUnidadInventarioACero unidad;
        private ServicioInventarioACero servicio;
        private List<Inventario> insertadas;
        private List<LineaInventarioContadaDTO> pendientes;
        private List<LineaInventarioContadaDTO> contabilizadas;
        private List<UbicacionProductoInventario> ubicaciones;
        private List<int> puestasACero;
        private List<int> borradas;
        private List<string> bloqueos;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioInventarioACero>();
            unidad = A.Fake<IUnidadInventarioACero>();
            insertadas = new List<Inventario>();
            pendientes = new List<LineaInventarioContadaDTO>();
            contabilizadas = new List<LineaInventarioContadaDTO>();
            ubicaciones = new List<UbicacionProductoInventario>();
            puestasACero = new List<int>();
            borradas = new List<int>();
            bloqueos = new List<string>();
            A.CallTo(() => repositorio.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repositorio.LeerAlmacen("1", "REI")).Returns(new AlmacenInventario { Nombre = "Reina", ControlUbicaciones = false });
            A.CallTo(() => repositorio.LeerAlmacen("1", "XXX")).Returns(Task.FromResult<AlmacenInventario>(null));
            A.CallTo(() => repositorio.Empezar(A<string>._)).Invokes((string r) => bloqueos.Add(r)).Returns(unidad);
            A.CallTo(() => unidad.Bloquear(A<string>._)).Invokes((string r) => bloqueos.Add(r)).Returns(Task.CompletedTask);
            A.CallTo(() => unidad.LeerProducto(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult<ProductoInventarioACero>(null));
            A.CallTo(() => unidad.LeerProducto("1", A<string>._, "44901")).Returns(Producto("44901", stock: 8));
            A.CallTo(() => unidad.Pendientes(A<string>._, A<string>._, A<DateTime>._, A<string>._)).ReturnsLazily(() => pendientes);
            A.CallTo(() => unidad.Contabilizadas(A<string>._, A<string>._, A<DateTime>._, A<string>._)).ReturnsLazily(() => contabilizadas);
            A.CallTo(() => unidad.Ubicaciones(A<string>._, A<string>._)).ReturnsLazily(() => ubicaciones);
            A.CallTo(() => unidad.UnidadesSinUbicar(A<string>._, A<string>._)).Returns(0);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).Invokes((IList<Inventario> f) => insertadas.AddRange(f)).Returns(Task.CompletedTask);
            A.CallTo(() => unidad.PonerACero(A<IList<int>>._)).Invokes((IList<int> n) => puestasACero.AddRange(n)).ReturnsLazily((IList<int> n) => n.Count);
            A.CallTo(() => unidad.BorrarSinUbicacion(A<IList<int>>._)).Invokes((IList<int> n) => borradas.AddRange(n)).ReturnsLazily((IList<int> n) => n.Count);
            servicio = new ServicioInventarioACero(repositorio);
        }

        private static ProductoInventarioACero Producto(string numero, int stock, bool ubicar = true, bool ficticio = false) => new ProductoInventarioACero
        {
            Numero = numero, Descripcion = "Producto " + numero, Grupo = "PEL", SubGrupo = "TIN", Familia = "Lisap", Stock = stock, Ubicar = ubicar, Ficticio = ficticio
        };

        private static DejarACeroInventarioDTO Peticion(string almacen = "ALG", bool otraVez = false, bool quitar = false, bool simular = false) =>
            new DejarACeroInventarioDTO { Empresa = "1", Almacen = almacen, Producto = "44901", OtraVez = otraVez, QuitarFilasSinUbicacion = quitar, Simular = simular };

        private static UbicacionProductoInventario Hueco(string pasillo, string fila, string columna, int cantidad) =>
            new UbicacionProductoInventario { Pasillo = pasillo, Fila = fila, Columna = columna, Cantidad = cantidad };

        private static LineaInventarioContadaDTO Fila(int orden, int calculado, int real, string pasillo = null, string fila = null, string columna = null) =>
            new LineaInventarioContadaDTO { NumeroOrden = orden, Producto = "44901", StockCalculado = calculado, StockReal = real, Pasillo = pasillo, Fila = fila, Columna = columna, Usuario = ALFREDO };

        private Task<ResultadoInventarioACero> DejarACero(DejarACeroInventarioDTO peticion) => servicio.DejarACero(peticion, "44901", ALFREDO, HOY);

        private void NoSeHaTocadoNada()
        {
            Assert.AreEqual(0, insertadas.Count);
            Assert.AreEqual(0, puestasACero.Count);
            Assert.AreEqual(0, borradas.Count);
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProductoEnTresUbicaciones_TresFilasConElTeoricoYContadoCero()
        {
            ubicaciones.AddRange(new[] { Hueco("001", "002", "005", 8), Hueco("003", "001", "002", 4), Hueco("012", "004", "001", 1) });

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            Assert.AreEqual(EstadoInventarioACero.Ok, resultado.Estado);
            Assert.AreEqual(3, insertadas.Count);
            Assert.IsTrue(insertadas.All(f => f.StockReal == 0 && f.Estado == 1 && f.Número == "44901" && f.Almacén == "ALG" && f.Fecha == HOY.Date && f.Usuario == ALFREDO));
            CollectionAssert.AreEqual(new[] { 8, 4, 1 }, insertadas.Select(f => f.StockCalculado ?? 0).ToArray());
            CollectionAssert.AreEqual(new[] { "001-002-005", "003-001-002", "012-004-001" }, insertadas.Select(f => $"{f.Pasillo}-{f.Fila}-{f.Columna}").ToArray());
            Assert.AreEqual(3, resultado.Respuesta.FilasCreadas);
            Assert.AreEqual(13, resultado.Respuesta.Unidades);
            Assert.IsTrue(resultado.Respuesta.PorUbicaciones);
            StringAssert.Contains(resultado.Respuesta.Aviso, "contabilizar");
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
            // El almacén entero (como el rellenado) y cada ubicación (como RellenarUbicacion de Ariadna).
            CollectionAssert.AreEqual(new[] { "InventarioRellenar:1:ALG", "InventarioRellenar:1:ALG:001:002:005", "InventarioRellenar:1:ALG:003:001:002", "InventarioRellenar:1:ALG:012:004:001" },
                bloqueos.ToArray());
        }

        [TestMethod]
        public async Task Simular_DiceLoMismoSinTocarNada()
        {
            ubicaciones.AddRange(new[] { Hueco("001", "002", "005", 8), Hueco("003", "001", "002", 4) });

            ResultadoInventarioACero resultado = await DejarACero(Peticion(simular: true));

            Assert.AreEqual(EstadoInventarioACero.Ok, resultado.Estado);
            Assert.IsTrue(resultado.Respuesta.Simulado);
            Assert.AreEqual(2, resultado.Respuesta.Lineas.Count);
            Assert.IsTrue(resultado.Respuesta.Lineas.All(l => l.Accion == LineaDejarACeroDTO.CREAR));
            NoSeHaTocadoNada();
        }

        [TestMethod]
        public async Task YaContabilizadoHoy_409YaContabilizadoSinTocarNada()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            contabilizadas.Add(new LineaInventarioContadaDTO { NumeroOrden = 900, Producto = "44901", StockReal = 8, Traspaso = 81350 });

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            Assert.AreEqual(EstadoInventarioACero.YaContabilizado, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "Contar otra vez");
            Assert.AreEqual(1, resultado.Lineas.Count);
            NoSeHaTocadoNada();
        }

        [TestMethod]
        public async Task YaContabilizadoHoy_ConOtraVez_LasCrea()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            contabilizadas.Add(new LineaInventarioContadaDTO { NumeroOrden = 900, Producto = "44901", StockReal = 8, Traspaso = 81350 });

            ResultadoInventarioACero resultado = await DejarACero(Peticion(otraVez: true));

            Assert.AreEqual(EstadoInventarioACero.Ok, resultado.Estado);
            Assert.AreEqual(1, insertadas.Count);
            A.CallTo(() => unidad.Contabilizadas(A<string>._, A<string>._, A<DateTime>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FilaSinUbicacionEnAlgete_409FilasSinUbicacionSinTocarNada()
        {
            // La incidencia 1186: la 44901 contada con 1 sin ubicación y su ubicación ya a 0.
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            pendientes.AddRange(new[] { Fila(948787, 0, 1), Fila(948796, 8, 0, "001", "002", "005") });

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            Assert.AreEqual(EstadoInventarioACero.FilasSinUbicacion, resultado.Estado);
            CollectionAssert.AreEqual(new[] { 948787 }, resultado.Lineas.Select(l => l.NumeroOrden).ToArray());
            StringAssert.Contains(resultado.Mensaje, "se borrará");
            NoSeHaTocadoNada();
        }

        [TestMethod]
        public async Task FilaSinUbicacionEnAlgete_ConQuitar_LaBorraYNoDuplicaLaDeLaUbicacion()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            pendientes.AddRange(new[] { Fila(948787, 0, 1), Fila(948796, 8, 0, "001", "002", "005") });

            ResultadoInventarioACero resultado = await DejarACero(Peticion(quitar: true));

            Assert.AreEqual(EstadoInventarioACero.Ok, resultado.Estado);
            CollectionAssert.AreEqual(new[] { 948787 }, borradas);
            Assert.AreEqual(0, insertadas.Count);
            Assert.AreEqual(0, puestasACero.Count);
            Assert.AreEqual(LineaDejarACeroDTO.YA_ESTABA_A_CERO, resultado.Respuesta.Lineas.Single().Accion);
            Assert.AreEqual(948787, resultado.Respuesta.FilasQuitadas.Single().NumeroOrden);
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FilaDeHoyEnUnaUbicacion_SePoneACeroYNoSeCreaOtra()
        {
            ubicaciones.AddRange(new[] { Hueco("001", "002", "005", 8), Hueco("003", "001", "002", 4) });
            pendientes.Add(Fila(948796, 8, 3, "001", "002", "005"));

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            CollectionAssert.AreEqual(new[] { 948796 }, puestasACero);
            Assert.AreEqual("003-001-002", insertadas.Select(f => $"{f.Pasillo}-{f.Fila}-{f.Columna}").Single());
            Assert.AreEqual(1, resultado.Respuesta.FilasPuestasACero);
            Assert.AreEqual(1, resultado.Respuesta.FilasCreadas);
            Assert.AreEqual(3, resultado.Respuesta.Lineas.Single(l => l.NumeroOrden == 948796).ContadoAntes);
        }

        [TestMethod]
        public async Task FilaDeHoyEnUbicacionSinStock_TambienSePoneACero()
        {
            pendientes.Add(Fila(948800, 0, 2, "020", "001", "001"));

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            CollectionAssert.AreEqual(new[] { 948800 }, puestasACero);
            Assert.AreEqual(0, insertadas.Count);
        }

        [TestMethod]
        public async Task UbicacionConCantidadCero_NoSeCreaFila()
        {
            ubicaciones.AddRange(new[] { Hueco("001", "002", "005", 0), Hueco("003", "001", "002", 4) });

            await DejarACero(Peticion());

            Assert.AreEqual("003-001-002", insertadas.Select(f => $"{f.Pasillo}-{f.Fila}-{f.Columna}").Single());
        }

        [TestMethod]
        public async Task DosFilasEnLaMismaUbicacion_409FilasDuplicadas()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            pendientes.AddRange(new[] { Fila(1, 8, 0, "001", "002", "005"), Fila(2, 8, 1, "001", "002", "005") });

            ResultadoInventarioACero resultado = await DejarACero(Peticion(quitar: true));

            Assert.AreEqual(EstadoInventarioACero.FilasDuplicadas, resultado.Estado);
            Assert.AreEqual(2, resultado.Lineas.Count);
            NoSeHaTocadoNada();
        }

        [TestMethod]
        public async Task AlmacenSinUbicaciones_UnaFilaSinUbicacionConElStockDelAlmacen()
        {
            ResultadoInventarioACero resultado = await servicio.DejarACero(Peticion("REI"), "44901", ALFREDO, HOY);

            Assert.AreEqual(EstadoInventarioACero.Ok, resultado.Estado);
            Inventario fila = insertadas.Single();
            Assert.IsNull(fila.Pasillo);
            Assert.AreEqual(8, fila.StockCalculado);
            Assert.AreEqual(0, fila.StockReal);
            Assert.IsFalse(resultado.Respuesta.PorUbicaciones);
            A.CallTo(() => unidad.Ubicaciones(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Bloquear(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AlmacenSinUbicaciones_ConFilaDeHoy_LaPoneACero()
        {
            pendientes.Add(Fila(5000, 8, 6));

            ResultadoInventarioACero resultado = await servicio.DejarACero(Peticion("REI"), "44901", ALFREDO, HOY);

            CollectionAssert.AreEqual(new[] { 5000 }, puestasACero);
            Assert.AreEqual(0, insertadas.Count);
            Assert.AreEqual(0, borradas.Count);
        }

        [TestMethod]
        public async Task ProductoQueNoSeUbicaEnAlgete_UnaFilaSinUbicacion()
        {
            A.CallTo(() => unidad.LeerProducto("1", "ALG", "44901")).Returns(Producto("44901", stock: 5, ubicar: false));

            await DejarACero(Peticion());

            Assert.IsNull(insertadas.Single().Pasillo);
            Assert.AreEqual(5, insertadas.Single().StockCalculado);
        }

        [TestMethod]
        public async Task SinStockNiFilas_NoHaceNadaYLoDice()
        {
            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            Assert.AreEqual(EstadoInventarioACero.Ok, resultado.Estado);
            Assert.AreEqual(0, resultado.Respuesta.Lineas.Count);
            StringAssert.Contains(resultado.Respuesta.Aviso, "no hay nada que dejar a cero");
            Assert.AreEqual(0, insertadas.Count);
        }

        [TestMethod]
        public async Task LoPendienteDeUbicar_SeAvisaYNoSeToca()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            A.CallTo(() => unidad.UnidadesSinUbicar("ALG", "44901")).Returns(2);

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            Assert.AreEqual(2, resultado.Respuesta.UnidadesSinUbicar);
            StringAssert.Contains(resultado.Respuesta.Aviso, "pendientes de ubicar");
        }

        [TestMethod]
        public async Task Ficticio_400SinTocarNada()
        {
            A.CallTo(() => unidad.LeerProducto("1", "ALG", "44901")).Returns(Producto("44901", stock: 0, ficticio: true));

            ResultadoInventarioACero resultado = await DejarACero(Peticion());

            Assert.AreEqual(EstadoInventarioACero.PeticionIncorrecta, resultado.Estado);
            NoSeHaTocadoNada();
        }

        [TestMethod]
        public async Task AlmacenQueNoExiste_NoExisteAlmacen()
        {
            ResultadoInventarioACero resultado = await servicio.DejarACero(Peticion("XXX"), "44901", ALFREDO, HOY);

            Assert.AreEqual(EstadoInventarioACero.NoExisteAlmacen, resultado.Estado);
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiUnaFilaSeHaContabilizadoMientrasTanto_LanzaYNoConfirma()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            pendientes.Add(Fila(948796, 8, 3, "001", "002", "005"));
            A.CallTo(() => unidad.PonerACero(A<IList<int>>._)).Returns(0);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => DejarACero(Peticion()));

            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        // ---------- Controlador ----------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private InventariosController Controlador(string nombre, params string[] grupos)
        {
            IRepositorioCodigosBarras codigos = A.Fake<IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.LeerProducto(A<string>._, A<string>._)).Returns(Task.FromResult<Producto>(null));
            A.CallTo(() => codigos.ProductosConCodigo(A<string>._, A<string>._)).Returns(new List<ProductoPorCodigoBarrasDTO>());
            A.CallTo(() => codigos.LeerProducto("1", "44901"))
                .Returns(Task.FromResult(new Producto { Empresa = "1", Número = "44901          ", Nombre = "TINTE" }));
            return new InventariosController(A.Fake<NVEntities>(), codigos)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioACero = servicio,
                AlmacenDelUsuario = (empresa, usuario) => usuario == "Paloma" ? "REI" : null,
                Hoy = () => HOY,
                User = Usuario(nombre, grupos)
            };
        }

        [TestMethod]
        public async Task Controlador_Almacen_OkConLaReferenciaYElDiaDeHoy()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));

            IHttpActionResult resultado = await Controlador("Alfredo", "Almacén").PostDejarACero(Peticion());

            var ok = resultado as OkNegotiatedContentResult<DejarACeroInventarioRespuestaDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(HOY.Date, ok.Content.Fecha);
            Assert.AreEqual(1, insertadas.Count);
        }

        [TestMethod]
        public async Task Controlador_Compras_403()
        {
            var resultado = (NegotiatedContentResult<HttpError>)await Controlador("Maria", "Compras").PostDejarACero(Peticion());

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.LeerAlmacen(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_TiendaEnAlgete_403()
        {
            var resultado = (NegotiatedContentResult<HttpError>)await Controlador("Paloma", "Tiendas").PostDejarACero(Peticion());

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_FilaSinUbicacion_409ConCodigoYLineas()
        {
            ubicaciones.Add(Hueco("001", "002", "005", 8));
            pendientes.Add(Fila(948787, 0, 1));

            var resultado = (NegotiatedContentResult<YaRellenadoInventarioDTO>)await Controlador("Alfredo", "Almacén").PostDejarACero(Peticion());

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreEqual(CodigosConflictoInventario.FILAS_SIN_UBICACION, resultado.Content.Codigo);
            Assert.IsFalse(resultado.Content.PuedeContarOtraVez);
            Assert.AreEqual(948787, resultado.Content.Lineas.Single().NumeroOrden);
        }

        [TestMethod]
        public async Task Controlador_YaContabilizado_409ConContarOtraVez()
        {
            contabilizadas.Add(new LineaInventarioContadaDTO { NumeroOrden = 900, Producto = "44901", StockReal = 8, Traspaso = 81350 });

            var resultado = (NegotiatedContentResult<YaRellenadoInventarioDTO>)await Controlador("Alfredo", "Almacén").PostDejarACero(Peticion());

            Assert.AreEqual(CodigosConflictoInventario.YA_CONTABILIZADO, resultado.Content.Codigo);
            Assert.IsTrue(resultado.Content.PuedeContarOtraVez);
        }

        [TestMethod]
        public async Task Controlador_SinProducto_400()
        {
            DejarACeroInventarioDTO peticion = Peticion();
            peticion.Producto = " ";

            Assert.IsInstanceOfType(await Controlador("Alfredo", "Almacén").PostDejarACero(peticion), typeof(BadRequestErrorMessageResult));
        }
    }
}
