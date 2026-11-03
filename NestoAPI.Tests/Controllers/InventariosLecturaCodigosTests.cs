using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#788 (épica #784, inventario del 21/11/26): api/Inventarios lee lo escaneado o tecleado con la misma regla que la
    /// recepción de reposiciones (referencia, ficha, ProductosCodigosBarras e internos del 29) y marca PorCodigoBarras.
    /// </summary>
    [TestClass]
    public class InventariosLecturaCodigosTests
    {
        private const string MASCARILLA = "40945";
        private const string EAN_TABLA = "8435454004144";
        private const string INTERNO = "2900000174047";
        private const string REPUESTO = "17404";
        private const string COMPARTIDO = "8436620930427";

        private NVEntities db;
        private DbSet<Inventario> fakeInventarios;
        private IRepositorioCodigosBarras codigos;
        private InventariosController controller;
        private List<Inventario> anadidos;
        private readonly DateTime dia = new DateTime(2026, 11, 21);

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeInventarios = A.Fake<DbSet<Inventario>>(o => o.Implements<IQueryable<Inventario>>().Implements<IDbAsyncEnumerable<Inventario>>());
            A.CallTo(() => db.Inventarios).Returns(fakeInventarios);
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>().AsQueryable());
            anadidos = new List<Inventario>();
            A.CallTo(() => fakeInventarios.Add(A<Inventario>.Ignored)).Invokes((Inventario i) => anadidos.Add(i)).ReturnsLazily((Inventario i) => i);

            codigos = A.Fake<IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.LeerProducto(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<Producto>(null));
            A.CallTo(() => codigos.ProductosConCodigo(A<string>.Ignored, A<string>.Ignored)).Returns(new List<ProductoPorCodigoBarrasDTO>());
            Producto(MASCARILLA, "MASCARILLA QUIRURGICA");
            Producto(REPUESTO, "REPUESTO SIN CÓDIGO");
            Producto("11111", "UNO");
            Producto("22222", "OTRO");
            Codigo(EAN_TABLA, MASCARILLA);
            Codigo(INTERNO, REPUESTO);
            A.CallTo(() => codigos.ProductosConCodigo("1", COMPARTIDO)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = "11111", Nombre = "UNO", Cantidad = 1, Principal = true },
                new ProductoPorCodigoBarrasDTO { Producto = "22222", Nombre = "OTRO", Cantidad = 1, Principal = false }
            });

            controller = new InventariosController(db, codigos);
        }

        [TestMethod]
        public async Task PostInventario_CodigoDeLaTabla_GrabaLaReferenciaYPorCodigoBarras()
        {
            var resultado = await controller.PostInventario(Linea(EAN_TABLA));

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<Inventario>));
            Assert.AreEqual(1, anadidos.Count);
            Assert.AreEqual(MASCARILLA, anadidos[0].Número);
            Assert.AreEqual(true, anadidos[0].PorCodigoBarras);
            Assert.AreEqual("MASCARILLA QUIRURGICA", anadidos[0].Descripción);
        }

        [TestMethod]
        public async Task PostInventario_InternoDel29_GrabaElProducto()
        {
            await controller.PostInventario(Linea(INTERNO));

            Assert.AreEqual(REPUESTO, anadidos.Single().Número);
            Assert.AreEqual(true, anadidos.Single().PorCodigoBarras);
        }

        [TestMethod]
        public async Task PostInventario_ReferenciaTecleada_PorCodigoBarrasFalse()
        {
            await controller.PostInventario(Linea(MASCARILLA));

            Assert.AreEqual(MASCARILLA, anadidos.Single().Número);
            Assert.AreEqual(false, anadidos.Single().PorCodigoBarras);
            A.CallTo(() => codigos.ProductosConCodigo(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostInventario_CodigoCompartido_Devuelve409ConLosProductosYNoGraba()
        {
            var resultado = await controller.PostInventario(Linea(COMPARTIDO));

            var conflicto = resultado as NegotiatedContentResult<ProductoLeidoInventarioDTO>;
            Assert.IsNotNull(conflicto);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            CollectionAssert.AreEqual(new[] { "11111", "22222" }, conflicto.Content.Productos.Select(p => p.Producto).ToArray());
            StringAssert.Contains(conflicto.Content.Message, COMPARTIDO);
            Assert.AreEqual(0, anadidos.Count);
        }

        [TestMethod]
        public async Task PostInventario_CodigoInexistente_Devuelve404ConTextoYNoGraba()
        {
            var resultado = await controller.PostInventario(Linea("8400000000000"));

            var noEncontrado = resultado as NegotiatedContentResult<ProductoLeidoInventarioDTO>;
            Assert.IsNotNull(noEncontrado);
            Assert.AreEqual(HttpStatusCode.NotFound, noEncontrado.StatusCode);
            Assert.AreEqual("No hay ningún producto con la referencia o el código de barras 8400000000000.", noEncontrado.Content.Message);
            Assert.AreEqual(0, anadidos.Count);
        }

        [TestMethod]
        public async Task GetInventario_PorCodigoDeLaTabla_DevuelveLaLineaDelProducto()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>
            {
                new Inventario { NºOrden = 7, Empresa = "1", Almacén = "REI", Fecha = dia.AddHours(9), Número = MASCARILLA, Estado = 1, StockReal = 3 }
            }.AsQueryable());

            var resultado = await controller.GetInventario("1", "REI", dia, EAN_TABLA);

            var ok = resultado as OkNegotiatedContentResult<Inventario>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(7, ok.Content.NºOrden);
        }

        [TestMethod]
        public async Task GetInventario_CodigoCompartido_Devuelve409()
        {
            var resultado = await controller.GetInventario("1", "REI", dia, COMPARTIDO);

            Assert.AreEqual(HttpStatusCode.Conflict, ((NegotiatedContentResult<ProductoLeidoInventarioDTO>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task GetInventario_Inexistente_Devuelve404ConTexto()
        {
            var resultado = await controller.GetInventario("1", "REI", dia, "NOEXISTE");

            var noEncontrado = (NegotiatedContentResult<ProductoLeidoInventarioDTO>)resultado;
            Assert.AreEqual(HttpStatusCode.NotFound, noEncontrado.StatusCode);
            StringAssert.Contains(noEncontrado.Content.Message, "NOEXISTE");
        }

        [TestMethod]
        public async Task LectorProductoLeido_CodigoQueEsTambienReferencia_SeQuedaComoReferencia()
        {
            // Un GTIN válido que además es la referencia de un producto: manda la referencia (como en la recepción)
            Producto(EAN_TABLA, "PRODUCTO CON REFERENCIA EAN");

            ResultadoProductoLeido leido = await new LectorProductoLeido(codigos).Leer("1", EAN_TABLA);

            Assert.AreEqual(EAN_TABLA, leido.Producto.Número);
            Assert.IsFalse(leido.PorCodigoBarras);
        }

        [TestMethod]
        public void MarcaTrasSumar_TecleadaEsDefinitiva_PorCodigoSoloMarcaLasSinMarca_SinParametroNoSeToca()
        {
            Assert.AreEqual(false, InventariosController.MarcaTrasSumar(true, false));
            Assert.AreEqual(false, InventariosController.MarcaTrasSumar(null, false));
            Assert.AreEqual(false, InventariosController.MarcaTrasSumar(false, true));
            Assert.AreEqual(true, InventariosController.MarcaTrasSumar(null, true));
            Assert.AreEqual(true, InventariosController.MarcaTrasSumar(true, true));
            Assert.AreEqual(true, InventariosController.MarcaTrasSumar(true, null));
            Assert.AreEqual(false, InventariosController.MarcaTrasSumar(false, null));
            Assert.IsNull(InventariosController.MarcaTrasSumar(null, null));
        }

        [TestMethod]
        public async Task GetPendientes_AgrupaPorUsuarioYDia_SoloEstado1SinTraspasoDelAlmacen()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>
            {
                Fila("REI", "NUEVAVISION\\Andre ", dia.AddHours(9), 3, 1, true),
                Fila("REI", "NUEVAVISION\\Andre", dia.AddHours(11), 2, 2, false),
                Fila("REI", "NUEVAVISION\\Andre", dia.AddHours(12), 1, null, null),
                Fila("REI", "NUEVAVISION\\Andre", dia.AddDays(1), 5, 5, true),
                Fila("REI", "NUEVAVISION\\Javier", dia.AddHours(10), 4, 0, true),
                Fila("ALC", "NUEVAVISION\\Andre", dia.AddHours(9), 9, 9, true),
                Fila("REI", "NUEVAVISION\\Andre", dia.AddHours(9), 9, 9, true, estado: 2),
                Fila("REI", "NUEVAVISION\\Andre", dia.AddHours(9), 9, 9, true, traspaso: 77263)
            }.AsQueryable());

            var resultado = (OkNegotiatedContentResult<List<InventarioPendienteDTO>>)await controller.GetPendientes("REI", "1", dia);

            List<InventarioPendienteDTO> resumen = resultado.Content;
            Assert.AreEqual(2, resumen.Count);
            InventarioPendienteDTO andre = resumen[0];
            Assert.AreEqual("NUEVAVISION\\Andre", andre.Usuario);
            Assert.AreEqual(dia, andre.Fecha);
            Assert.AreEqual(3, andre.Filas);
            Assert.AreEqual(6, andre.StockReal);
            Assert.AreEqual(3, andre.StockCalculado);
            Assert.AreEqual(1, andre.FilasPorCodigoBarras);
            Assert.AreEqual(1, andre.FilasTecleadas);
            Assert.AreEqual(1, andre.FilasSinMarca);
            Assert.AreEqual("NUEVAVISION\\Javier", resumen[1].Usuario);
            Assert.AreEqual(4, resumen[1].StockReal);

            var todas = (OkNegotiatedContentResult<List<InventarioPendienteDTO>>)await controller.GetPendientes("REI", "1");
            Assert.AreEqual(3, todas.Content.Count);
            Assert.AreEqual(dia.AddDays(1), todas.Content[2].Fecha);
        }

        private void Producto(string numero, string nombre)
        {
            A.CallTo(() => codigos.LeerProducto("1", numero))
                .Returns(Task.FromResult(new Producto { Empresa = "1", Número = numero, Nombre = nombre, Familia = "Fam", Grupo = "COS", SubGrupo = "COS" }));
        }

        private void Codigo(string codigo, string producto)
        {
            A.CallTo(() => codigos.ProductosConCodigo("1", codigo)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = producto, Nombre = "n", Cantidad = 1, Principal = false }
            });
        }

        private Inventario Linea(string leido)
            => new Inventario { Empresa = "1", Almacén = "REI", Fecha = dia, Número = leido, StockReal = 1, StockCalculado = 0, Usuario = "NUEVAVISION\\Andre" };

        private static Inventario Fila(string almacen, string usuario, DateTime fecha, int real, int? calculado, bool? porCodigo, byte estado = 1, int? traspaso = null)
            => new Inventario
            {
                Empresa = "1",
                Almacén = almacen,
                Usuario = usuario,
                Fecha = fecha,
                StockReal = real,
                StockCalculado = calculado,
                PorCodigoBarras = porCodigo,
                Estado = estado,
                NºTraspaso = traspaso
            };

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
