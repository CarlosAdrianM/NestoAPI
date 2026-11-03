using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Inventarios
{
    /// <summary>
    /// NestoAPI#795 (épica #784, corte 5 de Ariadna#79): siguientes ubicaciones, rellenar una ubicación de Algete con su teórico, los
    /// evolutivos de las tiendas (familia o referencia) y lo contado.
    /// </summary>
    [TestClass]
    public class InventarioUbicacionesTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 21);
        private const string USUARIO = "NUEVAVISION\\Andre";

        private IRepositorioInventarioUbicaciones repositorio;
        private IUnidadInventarioUbicaciones unidad;
        private ServicioInventarioUbicaciones servicio;
        private List<Inventario> insertadas;
        private List<LineaInventarioContadaDTO> pendientes;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioInventarioUbicaciones>();
            unidad = A.Fake<IUnidadInventarioUbicaciones>();
            insertadas = new List<Inventario>();
            pendientes = new List<LineaInventarioContadaDTO>();
            A.CallTo(() => repositorio.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repositorio.LeerAlmacen("1", "REI")).Returns(new AlmacenInventario { Nombre = "Reina", ControlUbicaciones = false });
            A.CallTo(() => repositorio.LeerAlmacen("1", "XXX")).Returns(Task.FromResult<AlmacenInventario>(null));
            A.CallTo(() => repositorio.Empezar(A<string>._)).Returns(unidad);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).Invokes((IList<Inventario> f) => insertadas.AddRange(f));
            // Antes de insertar, lo que haya en «pendientes»; después, una línea por fila insertada.
            A.CallTo(() => unidad.Pendientes(A<string>._, A<string>._, A<DateTime>._, A<FiltroInventarioContado>._))
                .ReturnsLazily(() => insertadas.Any()
                    ? insertadas.Select((f, n) => new LineaInventarioContadaDTO { NumeroOrden = 100 + n, Producto = f.Número, StockCalculado = f.StockCalculado ?? 0, Pasillo = f.Pasillo }).ToList()
                    : pendientes);
            servicio = new ServicioInventarioUbicaciones(repositorio);
        }

        private static StockProductoInventario Stock(string numero, int stock, bool ficticio = false, string familia = "Lisap")
            => new StockProductoInventario { Numero = numero, Descripcion = "Producto " + numero, Grupo = "PEL", SubGrupo = "TIN", Familia = familia, Stock = stock, Ficticio = ficticio };

        private static RellenarUbicacionInventarioDTO Ubicacion(string almacen = "ALG") =>
            new RellenarUbicacionInventarioDTO { Empresa = "1", Almacen = almacen, Fecha = DIA.AddHours(9), Pasillo = " 003", Fila = "001 ", Columna = "002" };

        // ---------- Siguientes ----------

        [TestMethod]
        public async Task Siguientes_CompletaProductosYYaRellenadaHoy_EnElOrdenDelSP()
        {
            A.CallTo(() => repositorio.UbicacionesParaInventario(3)).Returns(new List<UbicacionPrioridadInventario>
            {
                new UbicacionPrioridadInventario { Pasillo = "012", Fila = "001", Columna = "005", DiasUltInventario = 340 },
                new UbicacionPrioridadInventario { Pasillo = "003", Fila = "001", Columna = "002", DiasUltInventario = 300 },
                new UbicacionPrioridadInventario { Pasillo = "020", Fila = "004", Columna = "001", DiasUltInventario = null }
            });
            A.CallTo(() => repositorio.ProductosPorUbicacion("ALG")).Returns(new List<UbicacionConProductos>
            {
                new UbicacionConProductos { Pasillo = "012", Fila = "001", Columna = "005", Productos = 7 },
                new UbicacionConProductos { Pasillo = "003", Fila = "001", Columna = "002", Productos = 2 }
            });
            A.CallTo(() => repositorio.UbicacionesRellenadas("1", "ALG", DIA)).Returns(new List<UbicacionConProductos>
            {
                new UbicacionConProductos { Pasillo = "003", Fila = "001", Columna = "002", Productos = 2 }
            });

            var resultado = await servicio.Siguientes("1", "alg", 3, DIA.AddHours(10));

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            List<UbicacionParaInventarioDTO> ubicaciones = resultado.Respuesta;
            CollectionAssert.AreEqual(new[] { "012", "003", "020" }, ubicaciones.Select(u => u.Pasillo).ToArray());
            Assert.AreEqual(7, ubicaciones[0].Productos);
            Assert.AreEqual(340, ubicaciones[0].DiasUltInventario);
            Assert.IsFalse(ubicaciones[0].YaRellenadaHoy);
            Assert.IsTrue(ubicaciones[1].YaRellenadaHoy);
            Assert.AreEqual(0, ubicaciones[2].Productos);
            Assert.AreEqual(0, ubicaciones[2].DiasUltInventario);
        }

        [TestMethod]
        public async Task Siguientes_AlmacenSinUbicaciones_NoLlamaAlSP()
        {
            var resultado = await servicio.Siguientes("1", "REI", 15, DIA);

            Assert.AreEqual(EstadoInventarioUbicaciones.AlmacenNoAdmite, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "Reina no tiene control de ubicaciones");
            A.CallTo(() => repositorio.UbicacionesParaInventario(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Siguientes_NumeroFueraDeRango_PeticionIncorrecta()
        {
            Assert.AreEqual(EstadoInventarioUbicaciones.PeticionIncorrecta, (await servicio.Siguientes("1", "ALG", 0, DIA)).Estado);
            Assert.AreEqual(EstadoInventarioUbicaciones.PeticionIncorrecta, (await servicio.Siguientes("1", "ALG", 201, DIA)).Estado);
        }

        [TestMethod]
        public async Task Siguientes_AlmacenQueNoExiste_NoExisteAlmacen()
        {
            Assert.AreEqual(EstadoInventarioUbicaciones.NoExisteAlmacen, (await servicio.Siguientes("1", "XXX", 15, DIA)).Estado);
        }

        // ---------- RellenarUbicacion ----------

        [TestMethod]
        public async Task RellenarUbicacion_UnaFilaPorProductoConSuCantidadYLaUbicacion()
        {
            A.CallTo(() => unidad.TeoricoUbicacion("1", "ALG", "003", "001", "002")).Returns(new List<StockProductoInventario>
            {
                Stock("40245", 5), Stock("27534", 3), Stock("40245", 2), Stock("44925", 0)
            });

            var resultado = await servicio.RellenarUbicacion(Ubicacion(), USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            CollectionAssert.AreEqual(new[] { "27534", "40245", "44925" }, insertadas.Select(f => f.Número).ToArray());
            Inventario fila = insertadas.Single(f => f.Número == "40245");
            Assert.AreEqual(7, fila.StockCalculado);
            Assert.AreEqual(0, fila.StockReal);
            Assert.AreEqual(1, fila.Estado);
            Assert.AreEqual("003", fila.Pasillo);
            Assert.AreEqual("001", fila.Fila);
            Assert.AreEqual("002", fila.Columna);
            Assert.AreEqual("NestoAPI", fila.Aplicacion);
            Assert.AreEqual(USUARIO, fila.Usuario);
            Assert.AreEqual(DIA, fila.Fecha);
            Assert.AreEqual("Lisap", fila.Familia);
            Assert.AreEqual(3, resultado.Respuesta.Filas);
            Assert.AreEqual(10, resultado.Respuesta.Unidades);
            Assert.AreEqual(3, resultado.Respuesta.Lineas.Count);
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Empezar("InventarioRellenar:1:ALG:003:001:002")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarUbicacion_UbicacionVacia_CeroFilasYOk()
        {
            A.CallTo(() => unidad.TeoricoUbicacion(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(new List<StockProductoInventario>());

            var resultado = await servicio.RellenarUbicacion(Ubicacion(), USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            Assert.AreEqual(0, resultado.Respuesta.Filas);
            Assert.AreEqual(0, resultado.Respuesta.Lineas.Count);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarUbicacion_YaTieneFilasPendientesEseDia_YaRellenadoConLasLineasYNoInserta()
        {
            pendientes.Add(new LineaInventarioContadaDTO { NumeroOrden = 7, Producto = "40245", Pasillo = "003", Fila = "001", Columna = "002" });

            var resultado = await servicio.RellenarUbicacion(Ubicacion(), USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.YaRellenado, resultado.Estado);
            Assert.AreEqual(7, resultado.Lineas.Single().NumeroOrden);
            Assert.AreEqual("La ubicación 003-001-002 ya está rellenada el 21/11/2026: tiene 1 fila sin contabilizar.", resultado.Mensaje);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
            A.CallTo(() => unidad.Pendientes("1", "ALG", DIA, A<FiltroInventarioContado>.That.Matches(f => f.Pasillo == "003" && f.Fila == "001" && f.Columna == "002")))
                .MustHaveHappened();
        }

        [TestMethod]
        public async Task RellenarUbicacion_AlmacenSinUbicaciones_NoAdmite()
        {
            var resultado = await servicio.RellenarUbicacion(Ubicacion("REI"), USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.AlmacenNoAdmite, resultado.Estado);
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RellenarUbicacion_SinColumna_PeticionIncorrecta()
        {
            RellenarUbicacionInventarioDTO peticion = Ubicacion();
            peticion.Columna = " ";

            var resultado = await servicio.RellenarUbicacion(peticion, USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.PeticionIncorrecta, resultado.Estado);
            Assert.AreEqual("Falta el pasillo, la fila o la columna de la ubicación.", resultado.Mensaje);
        }

        // ---------- RellenarParcial ----------

        [TestMethod]
        public async Task RellenarParcial_Familia_SoloNoFicticiosConStockDistintoDeCero()
        {
            A.CallTo(() => unidad.Stocks("1", "REI", "Lisap", null)).Returns(new List<StockProductoInventario>
            {
                Stock("29608", 3), Stock("29609", 0), Stock("29613", -1), Stock("FIC", 4, ficticio: true)
            });

            var resultado = await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Empresa = "1", Almacen = "REI", Fecha = DIA, Familia = " Lisap " }, null, USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            CollectionAssert.AreEqual(new[] { "29608", "29613" }, insertadas.Select(f => f.Número).ToArray());
            Assert.IsTrue(insertadas.All(f => f.Pasillo == null && f.StockReal == 0 && f.Estado == 1 && f.Usuario == USUARIO));
            Assert.AreEqual("Lisap", resultado.Respuesta.Familia);
            Assert.AreEqual(2, resultado.Respuesta.Filas);
            Assert.AreEqual(2, resultado.Respuesta.Unidades);
            A.CallTo(() => repositorio.Empezar("InventarioRellenar:1:REI")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarParcial_Referencia_SuFilaAunqueElStockSeaCero()
        {
            A.CallTo(() => unidad.Stocks("1", "REI", null, "40245")).Returns(new List<StockProductoInventario> { Stock("40245", 0) });

            var resultado = await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Almacen = "REI", Fecha = DIA }, "40245", USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            Assert.AreEqual("40245", insertadas.Single().Número);
            Assert.AreEqual(0, insertadas.Single().StockCalculado);
            Assert.AreEqual("40245", resultado.Respuesta.Producto);
            Assert.AreEqual(1, resultado.Respuesta.Lineas.Count);
        }

        [TestMethod]
        public async Task RellenarParcial_ReferenciaFicticia_PeticionIncorrectaYNoInserta()
        {
            A.CallTo(() => unidad.Stocks("1", "REI", null, "FIC")).Returns(new List<StockProductoInventario> { Stock("FIC", 0, ficticio: true) });

            var resultado = await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Almacen = "REI", Fecha = DIA }, "FIC", USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.PeticionIncorrecta, resultado.Estado);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RellenarParcial_YaHayPendientesDeEsaFamilia_YaRellenado()
        {
            pendientes.Add(new LineaInventarioContadaDTO { NumeroOrden = 1, Producto = "29608" });
            pendientes.Add(new LineaInventarioContadaDTO { NumeroOrden = 2, Producto = "29613" });

            var resultado = await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Almacen = "REI", Fecha = DIA, Familia = "Lisap" }, null, USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.YaRellenado, resultado.Estado);
            Assert.AreEqual(2, resultado.Lineas.Count);
            Assert.AreEqual("El inventario de la familia Lisap en REI del 21/11/2026 ya está rellenado: hay 2 filas sin contabilizar.", resultado.Mensaje);
            A.CallTo(() => unidad.Pendientes("1", "REI", DIA, A<FiltroInventarioContado>.That.Matches(f => f.Familia == "Lisap" && f.Producto == null && !f.PorUbicacion)))
                .MustHaveHappened();
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RellenarParcial_FamiliaYReferenciaONinguna_PeticionIncorrecta()
        {
            Assert.AreEqual(EstadoInventarioUbicaciones.PeticionIncorrecta,
                (await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Almacen = "REI", Familia = "Lisap" }, "40245", USUARIO)).Estado);
            Assert.AreEqual(EstadoInventarioUbicaciones.PeticionIncorrecta,
                (await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Almacen = "REI" }, null, USUARIO)).Estado);
        }

        [TestMethod]
        public async Task RellenarParcial_AlmacenConUbicaciones_NoAdmite()
        {
            var resultado = await servicio.RellenarParcial(new RellenarParcialInventarioDTO { Almacen = "ALG", Familia = "Lisap" }, null, USUARIO);

            Assert.AreEqual(EstadoInventarioUbicaciones.AlmacenNoAdmite, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "Algete se inventaría por ubicaciones");
        }

        // ---------- Contado ----------

        [TestMethod]
        public async Task Contado_SinFiltro_ResumenPorUbicacion()
        {
            DateTime ahora = DIA.AddHours(10);
            A.CallTo(() => repositorio.LeerContado("1", "ALG", DIA, A<FiltroInventarioContado>._)).Returns(new List<LineaInventarioContadaDTO>
            {
                new LineaInventarioContadaDTO { Producto = "1", Pasillo = "003", Fila = "001", Columna = "002", StockReal = 4, StockCalculado = 5, Usuario = "NUEVAVISION\\Aida", FechaModificacion = ahora },
                new LineaInventarioContadaDTO { Producto = "2", Pasillo = "003", Fila = "001", Columna = "002", StockReal = 1, StockCalculado = 0, Usuario = "NUEVAVISION\\Carlos", FechaModificacion = ahora.AddMinutes(5) },
                new LineaInventarioContadaDTO { Producto = "3", Pasillo = "001", Fila = "001", Columna = "001", StockReal = 2, StockCalculado = 2, Usuario = "NUEVAVISION\\Aida", FechaModificacion = ahora }
            });

            var resultado = await servicio.Contado("1", "ALG", DIA.AddHours(15), new FiltroInventarioContado());

            InventarioContadoDTO contado = resultado.Respuesta;
            Assert.AreEqual(0, contado.Lineas.Count);
            Assert.AreEqual(2, contado.Ubicaciones.Count);
            Assert.AreEqual("001", contado.Ubicaciones[0].Pasillo);
            ResumenUbicacionContadaDTO ubicacion = contado.Ubicaciones[1];
            Assert.AreEqual(2, ubicacion.Filas);
            Assert.AreEqual(5, ubicacion.Unidades);
            Assert.AreEqual(5, ubicacion.UnidadesCalculadas);
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Aida", "NUEVAVISION\\Carlos" }, ubicacion.Usuarios);
            Assert.AreEqual(ahora.AddMinutes(5), ubicacion.UltimaModificacion);
            Assert.AreEqual(DIA, contado.Fecha);
        }

        [TestMethod]
        public async Task Contado_PorUbicacion_LasLineasYElFiltroLimpio()
        {
            A.CallTo(() => repositorio.LeerContado("1", "ALG", DIA, A<FiltroInventarioContado>._)).Returns(new List<LineaInventarioContadaDTO>
            {
                new LineaInventarioContadaDTO { Producto = "B", Pasillo = "003" },
                new LineaInventarioContadaDTO { Producto = "A", Pasillo = "003" }
            });

            var resultado = await servicio.Contado(" 1", "alg", DIA, new FiltroInventarioContado { Pasillo = " 003 ", Fila = "001", Columna = "" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, resultado.Respuesta.Lineas.Select(l => l.Producto).ToArray());
            Assert.AreEqual(0, resultado.Respuesta.Ubicaciones.Count);
            A.CallTo(() => repositorio.LeerContado("1", "ALG", DIA, A<FiltroInventarioContado>.That.Matches(f => f.Pasillo == "003" && f.Fila == "001" && f.Columna == null)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void RepositorioSql_ComandoContado_SoloFiltraLoQueViene()
        {
            using (var comando = RepositorioInventarioUbicacionesSql.ComandoContado(null, null, "1", "ALG", DIA,
                new FiltroInventarioContado { Pasillo = "003", Producto = "40245" }))
            {
                StringAssert.Contains(comando.CommandText, "AND i.Pasillo = @Pasillo");
                StringAssert.Contains(comando.CommandText, "AND i.Número = @Producto");
                Assert.IsFalse(comando.CommandText.Contains("@Fila"));
                Assert.IsFalse(comando.CommandText.Contains("@Familia"));
                StringAssert.Contains(comando.CommandText, "i.Estado = 1 AND i.NºTraspaso IS NULL");
                Assert.AreEqual(DIA.AddDays(1), comando.Parameters["@Hasta"].Value);
            }
        }
    }
}
