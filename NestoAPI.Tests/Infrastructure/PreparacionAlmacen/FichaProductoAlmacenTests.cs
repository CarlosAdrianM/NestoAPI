using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Sugerencia 824 (Andre): la ficha del producto de Ariadna (GET api/Almacen/Productos/{codigo}/Ficha).</summary>
    [TestClass]
    public class FichaProductoAlmacenTests
    {
        private IServicioUbicacionesAlmacen ubicaciones;
        private IFichasProductoAlmacen fichas;
        private IServicioCodigosBarras codigos;
        private IProductoService stocks;
        private ServicioFichaProductoAlmacen servicio;

        [TestInitialize]
        public void Preparar()
        {
            ubicaciones = A.Fake<IServicioUbicacionesAlmacen>();
            fichas = A.Fake<IFichasProductoAlmacen>();
            codigos = A.Fake<IServicioCodigosBarras>();
            stocks = A.Fake<IProductoService>();
            A.CallTo(() => ubicaciones.BuscarProducto(A<string>._, A<string>._, A<string>._)).Returns(new List<ProductoAlmacenDTO>());
            A.CallTo(() => codigos.Listar(A<string>._, A<string>._)).Returns(new List<CodigoBarrasProductoDTO>());
            A.CallTo(() => stocks.CalcularStockProducto(A<string>._, A<string>._, A<int?>._))
                .ReturnsLazily((string p, string a, int? _) => new ProductoDTO.StockProducto { Almacen = a });
            servicio = new ServicioFichaProductoAlmacen(ubicaciones, fichas, codigos, stocks);
        }

        private static ProductoAlmacenDTO Producto(string numero, string nombre, string almacen, int pendienteDeUbicar = 0, params UbicacionAlmacenDTO[] huecos)
        {
            return new ProductoAlmacenDTO
            {
                Producto = numero,
                Descripcion = nombre,
                CodigoBarras = "8436620930427",
                Almacen = almacen,
                PendienteDeUbicar = pendienteDeUbicar,
                Ubicaciones = huecos.ToList()
            };
        }

        private static UbicacionAlmacenDTO Hueco(string pasillo, string fila, string columna, int cantidad)
        {
            return new UbicacionAlmacenDTO { Pasillo = pasillo, Fila = fila, Columna = columna, Ubicacion = $"{pasillo}/{fila}/{columna}", Cantidad = cantidad };
        }

        [TestMethod]
        public async Task Leer_PorCodigoDeBarras_ReuneHuecosStockYPendienteDeLosTresAlmacenes()
        {
            A.CallTo(() => ubicaciones.BuscarProducto("1", "ALG", "8436620930427"))
                .Returns(new List<ProductoAlmacenDTO> { Producto("18004", "CREMA SUAVE ", "ALG", 3, Hueco("002", "004", "001", 12), Hueco("010", "001", "002", 6)) });
            A.CallTo(() => ubicaciones.BuscarProducto("1", "REI", "18004"))
                .Returns(new List<ProductoAlmacenDTO> { Producto("18004", "CREMA SUAVE", "REI", 0, Hueco("001", "001", "001", 2)) });
            A.CallTo(() => stocks.CalcularStockProducto("18004", "ALG", A<int?>._))
                .Returns(new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 21, PendienteEntregar = 5, PendienteRecibir = 24 });
            A.CallTo(() => stocks.CalcularStockProducto("18004", "REI", A<int?>._))
                .Returns(new ProductoDTO.StockProducto { Almacen = "REI", Stock = 2, PendienteEntregar = 1 });

            ResultadoFichaProductoAlmacen resultado = await servicio.Leer("1", " 8436620930427 ");

            FichaProductoAlmacenDTO ficha = resultado.Ficha;
            Assert.IsNotNull(ficha);
            Assert.AreEqual("18004", ficha.Producto);
            Assert.AreEqual("CREMA SUAVE", ficha.Nombre);
            CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALC" }, ficha.Almacenes.Select(a => a.Almacen).ToList());
            AlmacenFichaProductoDTO algete = ficha.Almacenes[0];
            Assert.AreEqual(21, algete.Stock);
            Assert.AreEqual(5, algete.PendienteEntregar);
            Assert.AreEqual(24, algete.PendienteRecibir);
            Assert.AreEqual(16, algete.Disponible);
            Assert.AreEqual(3, algete.PendienteDeUbicar);
            CollectionAssert.AreEqual(new[] { "002/004/001", "010/001/002" }, algete.Ubicaciones.Select(u => u.Ubicacion).ToList());
            Assert.AreEqual(12, algete.Ubicaciones[0].Cantidad);
            Assert.AreEqual(1, ficha.Almacenes[1].PendienteEntregar);
            Assert.AreEqual(2, ficha.Almacenes[1].Ubicaciones.Single().Cantidad);
            Assert.AreEqual(0, ficha.Almacenes[2].Ubicaciones.Count, "En Alcobendas no está: sin huecos, sin fallar");
            // La familia, la foto y la referencia del proveedor, por el sitio de siempre
            A.CallTo(() => fichas.Completar("1", A<IEnumerable<IConFichaProducto>>.That.Matches(l => l.Single() == ficha))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Leer_CodigosConSusUnidades_ElPrincipalPrimeroYSinLosDeBaja()
        {
            A.CallTo(() => ubicaciones.BuscarProducto("1", "ALG", "18004"))
                .Returns(new List<ProductoAlmacenDTO> { Producto("18004", "CREMA", "ALG") });
            A.CallTo(() => codigos.Listar("1", "18004")).Returns(new List<CodigoBarrasProductoDTO>
            {
                new CodigoBarrasProductoDTO { Codigo = "18436620930424", Cantidad = 12, Activo = true },
                new CodigoBarrasProductoDTO { Codigo = "4006381333931", Cantidad = 1, Activo = false },
                new CodigoBarrasProductoDTO { Codigo = "8436620930427", Cantidad = 1, Principal = true, Activo = true }
            });

            FichaProductoAlmacenDTO ficha = (await servicio.Leer("1", "18004")).Ficha;

            Assert.AreEqual(2, ficha.CodigosBarras.Count);
            Assert.AreEqual("8436620930427", ficha.CodigosBarras[0].Codigo);
            Assert.IsTrue(ficha.CodigosBarras[0].Principal);
            Assert.AreEqual(12, ficha.CodigosBarras[1].Cantidad);
        }

        [TestMethod]
        public async Task Leer_ElCodigoDeLaFichaSinFilaEnLaTabla_VaIgualComoPrincipal()
        {
            A.CallTo(() => ubicaciones.BuscarProducto("1", "ALG", "18004"))
                .Returns(new List<ProductoAlmacenDTO> { Producto("18004", "CREMA", "ALG") });

            FichaProductoAlmacenDTO ficha = (await servicio.Leer("1", "18004")).Ficha;

            Assert.AreEqual("8436620930427", ficha.CodigosBarras.Single().Codigo);
            Assert.AreEqual(1, ficha.CodigosBarras.Single().Cantidad);
            Assert.IsTrue(ficha.CodigosBarras.Single().Principal);
        }

        [TestMethod]
        public async Task Leer_NoHayProducto_NoExiste()
        {
            ResultadoFichaProductoAlmacen resultado = await servicio.Leer("1", "99999");

            Assert.IsTrue(resultado.NoExiste);
            A.CallTo(() => stocks.CalcularStockProducto(A<string>._, A<string>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Leer_CodigoQueLlevanDosProductos_DevuelveLosCandidatosSinFicha()
        {
            A.CallTo(() => ubicaciones.BuscarProducto("1", "ALG", "8436620930427")).Returns(new List<ProductoAlmacenDTO>
            {
                Producto("18004", "GUANTE M", "ALG"),
                Producto("18005", "GUANTE L", "ALG")
            });

            ResultadoFichaProductoAlmacen resultado = await servicio.Leer("1", "8436620930427");

            Assert.IsNull(resultado.Ficha);
            CollectionAssert.AreEqual(new[] { "18004", "18005" }, resultado.Candidatos.Select(c => c.Producto).ToList());
            Assert.AreEqual("GUANTE L", resultado.Candidatos[1].Nombre);
        }

        [TestMethod]
        public async Task Leer_LaReferenciaTecleadaMandaSobreOtroProductoConEseCodigo()
        {
            A.CallTo(() => ubicaciones.BuscarProducto("1", "ALG", "18005")).Returns(new List<ProductoAlmacenDTO>
            {
                Producto("18004", "OTRO", "ALG"),
                Producto("18005", "GUANTE L", "ALG")
            });

            Assert.AreEqual("18005", (await servicio.Leer("1", "18005")).Ficha.Producto);
        }

        private static AlmacenController Controlador(IServicioFichaProductoAlmacen fichaProducto)
        {
            return new AlmacenController(A.Fake<IServicioPreparacionAlmacen>(), A.Fake<IServicioUbicacionesAlmacen>(),
                A.Fake<IServicioRecepcionCompras>(), A.Fake<IServicioRecepcionReposiciones>(), fichaProducto: fichaProducto)
            {
                User = new GenericPrincipal(new GenericIdentity("Andre", "Bearer"), new string[0]),
                Request = new HttpRequestMessage()
            };
        }

        [TestMethod]
        public async Task GetFichaProducto_Existe_Ok()
        {
            IServicioFichaProductoAlmacen fake = A.Fake<IServicioFichaProductoAlmacen>();
            var ficha = new FichaProductoAlmacenDTO { Producto = "18004" };
            A.CallTo(() => fake.Leer("1", "18004")).Returns(new ResultadoFichaProductoAlmacen { Ficha = ficha });

            var respuesta = await Controlador(fake).GetFichaProducto(" 18004 ") as OkNegotiatedContentResult<FichaProductoAlmacenDTO>;

            Assert.AreSame(ficha, respuesta.Content);
        }

        [TestMethod]
        public async Task GetFichaProducto_NoExiste_404ConMotivo()
        {
            IServicioFichaProductoAlmacen fake = A.Fake<IServicioFichaProductoAlmacen>();
            A.CallTo(() => fake.Leer("1", "99999")).Returns(new ResultadoFichaProductoAlmacen());

            var respuesta = await Controlador(fake).GetFichaProducto("99999") as NegotiatedContentResult<MensajeFichaProductoDTO>;

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            StringAssert.Contains(respuesta.Content.Message, "99999");
        }

        [TestMethod]
        public async Task GetFichaProducto_CodigoCompartido_409ConLosCandidatos()
        {
            IServicioFichaProductoAlmacen fake = A.Fake<IServicioFichaProductoAlmacen>();
            A.CallTo(() => fake.Leer("1", "8436620930427")).Returns(new ResultadoFichaProductoAlmacen
            {
                Candidatos = new List<ProductoCandidatoFichaDTO> { new ProductoCandidatoFichaDTO { Producto = "18004" }, new ProductoCandidatoFichaDTO { Producto = "18005" } }
            });

            var respuesta = await Controlador(fake).GetFichaProducto("8436620930427") as NegotiatedContentResult<FichaProductoAmbiguaDTO>;

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.AreEqual(2, respuesta.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetFichaProducto_SinServicio_503()
        {
            var respuesta = await Controlador(null).GetFichaProducto("18004") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        }
    }
}
