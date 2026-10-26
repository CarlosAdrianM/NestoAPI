using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // Sugerencia 778 (Andre, 26/10/26): en la reposición 81153 desde Reina se leyó el EAN 8435454004144 (MASCARILLA QUIRURGICA,
    // la 40945) cuando no casaba con la lista; Ariadna lo apuntó «No se esperaba» con el EAN y al terminar la API dijo «El producto
    // 8435454004144 no existe: no se puede dar entrada a lo leído. No se ha recibido nada.». Un código de barras se cambia por su
    // referencia antes de usarlo, y si el producto está en la lista cuenta contra su línea.
    [TestClass]
    public class TraductorCodigosRecepcionTests
    {
        private const string EAN = "8435454004144";
        private const string MASCARILLA = "40945";

        private IRepositorioCodigosBarras codigos;
        private IOrigenRecepcion reposiciones;
        private ServicioRecepciones servicio;
        private IPrincipal andre;

        [TestInitialize]
        public void Inicializar()
        {
            codigos = A.Fake<IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.ProductosConCodigo(A<string>.Ignored, A<string>.Ignored)).Returns(new List<ProductoPorCodigoBarrasDTO>());
            A.CallTo(() => codigos.LeerProducto(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<Producto>(null));
            A.CallTo(() => codigos.ProductosConCodigo("1", EAN)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = MASCARILLA, Nombre = "MASCARILLA QUIRURGICA 3 CAPAS TIPO IIR", Cantidad = 1, Principal = true }
            });
            reposiciones = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => reposiciones.Tipo).Returns("REPO");
            A.CallTo(() => reposiciones.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => reposiciones.PuedeTerminar(A<IPrincipal>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            A.CallTo(() => reposiciones.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).Returns(new ResultadoTerminarRecepcionDTO());
            servicio = new ServicioRecepciones(new[] { reposiciones }, codigos);
            andre = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre") }, "prueba"));
        }

        private void Esperado(params (string producto, int cantidad)[] lineas)
        {
            A.CallTo(() => reposiciones.LeerEsperado("1", "REI", "81153")).Returns(new RecepcionDTO
            {
                Tipo = "REPO",
                Documento = "81153",
                Lineas = lineas.Select(l => new LineaRecepcionDTO { Producto = l.producto, Descripcion = "d" + l.producto, Cantidad = l.cantidad }).ToList()
            });
        }

        private static TerminarRecepcionDTO Leido(params (string producto, int cantidad)[] lecturas)
        {
            return new TerminarRecepcionDTO
            {
                IdRecepcion = Guid.NewGuid(),
                Lecturas = lecturas.Select(l => new LecturaRecepcionDTO { Producto = l.producto, Cantidad = l.cantidad }).ToList()
            };
        }

        private async Task<Dictionary<string, int>> LoQueLlegaAlTipo(TerminarRecepcionDTO terminar)
        {
            SolicitudTerminarRecepcion solicitud = null;
            A.CallTo(() => reposiciones.Terminar(A<SolicitudTerminarRecepcion>.Ignored))
                .Invokes((SolicitudTerminarRecepcion s) => solicitud = s)
                .Returns(new ResultadoTerminarRecepcionDTO());
            _ = await servicio.Terminar("REPO", "1", "REI", "81153", terminar, andre);
            return solicitud.Lecturas;
        }

        [TestMethod]
        public async Task Sugerencia778_Terminar_ElEanDeUnProductoDeLaLista_CuentaContraSuLinea()
        {
            Esperado((MASCARILLA, 3), ("17404", 1));

            Dictionary<string, int> lecturas = await LoQueLlegaAlTipo(Leido((MASCARILLA, 2), (EAN, 1), ("17404", 1)));

            Assert.AreEqual(3, lecturas[MASCARILLA], "Las dos leídas por referencia y la del EAN, en la misma línea");
            Assert.IsFalse(lecturas.ContainsKey(EAN), "El EAN no llega como si fuera un producto");
            Assert.AreEqual(1, lecturas["17404"]);
        }

        [TestMethod]
        public async Task Sugerencia778_Terminar_ElEanDeUnProductoQueNoVenia_LlegaConSuReferencia()
        {
            Esperado(("17404", 1));

            Dictionary<string, int> lecturas = await LoQueLlegaAlTipo(Leido(("17404", 1), (EAN, 2)));

            Assert.AreEqual(2, lecturas[MASCARILLA]);
            Assert.IsFalse(lecturas.ContainsKey(EAN));
        }

        [TestMethod]
        public async Task Sugerencia778_Casar_ElEanCuentaContraSuLinea_YCuadra()
        {
            Esperado((MASCARILLA, 2));

            ResultadoCasarRecepcionDTO resultado = await servicio.Casar("REPO", "1", "REI", "81153",
                new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = MASCARILLA, Cantidad = 1 }, new LecturaRecepcionDTO { Producto = EAN, Cantidad = 1 } });

            Assert.IsTrue(resultado.Cuadra);
            Assert.AreEqual(0, resultado.Productos.Single().Diferencia);
            Assert.IsFalse(resultado.Productos.Any(p => p.Ajeno));
        }

        [TestMethod]
        public async Task Sugerencia778_UnCodigoQueNoEsDeNingunProducto_SeQuedaComoEsta()
        {
            Esperado(("17404", 1));

            Dictionary<string, int> lecturas = await LoQueLlegaAlTipo(Leido(("17404", 1), ("8437017506362", 1)));

            Assert.AreEqual(1, lecturas["8437017506362"], "El tipo de recepción dará su error claro");
        }

        [TestMethod]
        public async Task Sugerencia778_UnaReferencia_NoSePreguntaPorCodigoDeBarras()
        {
            Esperado(("17404", 1));

            Dictionary<string, int> lecturas = await LoQueLlegaAlTipo(Leido(("17404", 1), ("41899", 1)));

            Assert.AreEqual(1, lecturas["41899"]);
            A.CallTo(() => codigos.ProductosConCodigo(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sugerencia778_UnCodigoQueTambienEsUnaReferencia_SeQuedaComoEsta()
        {
            const string referencia = "12345670"; // EAN-8 válido
            A.CallTo(() => codigos.ProductosConCodigo("1", referencia)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = "17404", Principal = true }
            });
            A.CallTo(() => codigos.LeerProducto("1", referencia)).Returns(new Producto { Número = referencia });

            Dictionary<string, string> traducidos = await new TraductorCodigosRecepcion(codigos).Traducir("1", new[] { referencia }, null);

            Assert.AreEqual(0, traducidos.Count);
        }

        [TestMethod]
        public async Task Sugerencia778_UnCodigoCompartido_SeQuedaElProductoDeLaLista()
        {
            A.CallTo(() => codigos.ProductosConCodigo("1", EAN)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = "40944", Principal = true },
                new ProductoPorCodigoBarrasDTO { Producto = MASCARILLA, Principal = false }
            });
            Esperado((MASCARILLA, 1));

            Dictionary<string, int> lecturas = await LoQueLlegaAlTipo(Leido((EAN, 1)));

            Assert.AreEqual(1, lecturas[MASCARILLA]);
            Assert.IsFalse(lecturas.ContainsKey("40944"));
        }

        [TestMethod]
        public async Task Sugerencia778_SinRepositorioDeCodigos_NoTraduceNada()
        {
            Dictionary<string, string> traducidos = await new TraductorCodigosRecepcion(null).Traducir("1", new[] { EAN }, null);

            Assert.AreEqual(0, traducidos.Count);
        }
    }
}
