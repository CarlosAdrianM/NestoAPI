using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#47 (19/10/26): los demás productos con stock en el hueco de cada línea, para distinguir los que se parecen
    /// (SERUM / MÁSCARA LEVEL LASH, 44724 / 44726, reposición 81040).
    /// </summary>
    [TestClass]
    public class ProductosMismoHuecoTests
    {
        private const string GTIN_SERUM = "8437020512176";
        private const string GTIN_MASCARA = "8437020512237";

        private IRepositorioProductosMismoHueco repositorio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioProductosMismoHueco>();
        }

        private static LineaPickingAlmacenDTO Linea(string producto, string pasillo, string fila, string columna)
        {
            return new LineaPickingAlmacenDTO { Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna };
        }

        private static ProductoEnHueco EnHueco(string producto, string pasillo, string fila, string columna, string nombre = null,
            string codigo = null, short? tamano = null, string unidad = null)
        {
            return new ProductoEnHueco
            {
                Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna, Nombre = nombre,
                CodigoBarras = codigo, Tamano = tamano, UnidadMedida = unidad
            };
        }

        private void ConFilas(params ProductoEnHueco[] filas)
        {
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<string>._, A<IReadOnlyCollection<UbicacionAlmacenDTO>>._, A<int>._))
                .Returns(filas.ToList());
        }

        // ---------------------------------------------------------------- UltimasCifras

        [TestMethod]
        public void UltimasCifras_ConGtinValido_DaLasCincoUltimas()
        {
            Assert.AreEqual("12237", ProductosMismoHueco.UltimasCifras(GTIN_MASCARA));
            Assert.AreEqual("12176", ProductosMismoHueco.UltimasCifras(" " + GTIN_SERUM + " "));
        }

        [TestMethod]
        public void UltimasCifras_SinGtinValido_EsNull()
        {
            Assert.IsNull(ProductosMismoHueco.UltimasCifras(null));
            Assert.IsNull(ProductosMismoHueco.UltimasCifras(""));
            Assert.IsNull(ProductosMismoHueco.UltimasCifras("1"));
            Assert.IsNull(ProductosMismoHueco.UltimasCifras("44724"), "Una referencia interna no es un código de barras");
            Assert.IsNull(ProductosMismoHueco.UltimasCifras("8437020512177"), "Dígito de control mal");
            Assert.IsNull(ProductosMismoHueco.UltimasCifras("0000000000000"), "Relleno");
        }

        // ---------------------------------------------------------------- Poner (la parte pura)

        [TestMethod]
        public void Poner_AgrupaPorHuecoYQuitaElPropioProducto()
        {
            LineaPickingAlmacenDTO serum = Linea("44724", "002", "004", "001");
            LineaPickingAlmacenDTO otra = Linea("10000", "005", "001", "001");
            var filas = new[]
            {
                EnHueco("44724", "002", "004", "001", "SERUM LEVEL LASH", GTIN_SERUM),
                EnHueco("44726", "002", "004", "001", "MÁSCARA LEVEL LASH", GTIN_MASCARA, 10, "ml"),
                EnHueco("20000", "005", "001", "001", "OTRO HUECO")
            };

            ProductosMismoHueco.Poner(new IConProductosMismoHueco[] { serum, otra }, filas,
                ProductosMismoHueco.HuecosAConsultar(new IConProductosMismoHueco[] { serum, otra }));

            Assert.AreEqual(1, serum.ProductosMismoHueco.Count);
            ProductoMismoHuecoDTO mascara = serum.ProductosMismoHueco.Single();
            Assert.AreEqual("44726", mascara.Producto);
            Assert.AreEqual("MÁSCARA LEVEL LASH", mascara.Nombre);
            Assert.AreEqual((short)10, mascara.Tamano);
            Assert.AreEqual("ml", mascara.UnidadMedida);
            Assert.AreEqual("12237", mascara.UltimasCifras);
            Assert.AreEqual("20000", otra.ProductosMismoHueco.Single().Producto, "Cada línea, solo los de su hueco");
        }

        [TestMethod]
        public void Poner_SinCodigoValido_UltimasCifrasNull()
        {
            LineaPickingAlmacenDTO linea = Linea("44724", "002", "004", "001");
            ProductosMismoHueco.Poner(new[] { linea }, new[] { EnHueco("44726", "002", "004", "001", "MÁSCARA", "1") },
                new[] { UbicacionAlmacenDTO.De("002", "004", "001") });

            Assert.IsNull(linea.ProductosMismoHueco.Single().UltimasCifras);
        }

        [TestMethod]
        public void Poner_HuecoConsultadoSinNadieMas_ListaVacia()
        {
            LineaPickingAlmacenDTO linea = Linea("44724", "002", "004", "001");
            ProductosMismoHueco.Poner(new[] { linea }, new[] { EnHueco("44724", "002", "004", "001") },
                new[] { UbicacionAlmacenDTO.De("002", "004", "001") });

            Assert.IsNotNull(linea.ProductosMismoHueco);
            Assert.AreEqual(0, linea.ProductosMismoHueco.Count);
        }

        [TestMethod]
        public void Poner_LineaSinHueco_SeQuedaSinElCampo()
        {
            LineaPickingAlmacenDTO sinHueco = Linea("44724", null, null, null);
            ProductosMismoHueco.Poner(new[] { sinHueco }, new[] { EnHueco("44726", "002", "004", "001") },
                new[] { UbicacionAlmacenDTO.De("002", "004", "001") });

            Assert.IsNull(sinHueco.ProductosMismoHueco);
        }

        [TestMethod]
        public void Poner_ComparaElHuecoSinFijarseEnLosCeros()
        {
            LineaPickingAlmacenDTO linea = Linea("44724", "2", "4", "1");
            ProductosMismoHueco.Poner(new[] { linea }, new[] { EnHueco("44726", "002", "004", "001") },
                ProductosMismoHueco.HuecosAConsultar(new[] { linea }));

            Assert.AreEqual("44726", linea.ProductosMismoHueco.Single().Producto);
        }

        [TestMethod]
        public void Poner_NoRepiteNiPasaDelLimiteYOrdenaPorProducto()
        {
            LineaPickingAlmacenDTO linea = Linea("00000", "001", "001", "001");
            var filas = Enumerable.Range(1, ProductosMismoHueco.LIMITE_POR_HUECO + 3)
                .Reverse()
                .Select(i => EnHueco(i.ToString("00000"), "001", "001", "001"))
                .Concat(new[] { EnHueco("00001", "001", "001", "001") })
                .ToList();

            ProductosMismoHueco.Poner(new[] { linea }, filas, new[] { UbicacionAlmacenDTO.De("001", "001", "001") });

            Assert.AreEqual(ProductosMismoHueco.LIMITE_POR_HUECO, linea.ProductosMismoHueco.Count);
            CollectionAssert.AreEqual(
                Enumerable.Range(1, ProductosMismoHueco.LIMITE_POR_HUECO).Select(i => i.ToString("00000")).ToList(),
                linea.ProductosMismoHueco.Select(p => p.Producto).ToList());
        }

        [TestMethod]
        public void Poner_ElPropioSeQuitaSinImportarEspaciosNiMayusculas()
        {
            LineaPickingAlmacenDTO linea = Linea("ab12 ", "001", "001", "001");
            ProductosMismoHueco.Poner(new[] { linea }, new[] { EnHueco("AB12", "001", "001", "001"), EnHueco("AB13", "001", "001", "001") },
                new[] { UbicacionAlmacenDTO.De("001", "001", "001") });

            Assert.AreEqual("AB13", linea.ProductosMismoHueco.Single().Producto);
        }

        [TestMethod]
        public void Poner_EnUnHuecoLleno_ElQueSeConfundeVaPrimeroAunqueSuNumeroQuedeLejos()
        {
            // 44724 en un hueco con 15 productos más: el que comparte nombre tiene que salir aunque por número fuera el último
            LineaPickingAlmacenDTO serum = Linea("44724", "002", "002", "001");
            var filas = Enumerable.Range(1, 15)
                .Select(i => EnHueco("4000" + i.ToString("00"), "002", "002", "001", "PESTAÑAS SEDA " + i))
                .Concat(new[]
                {
                    EnHueco("44724", "002", "002", "001", "SERUM LEVEL LASH"),
                    EnHueco("99999", "002", "002", "001", "MÁSCARA LEVEL LASH", GTIN_MASCARA)
                })
                .ToList();

            ProductosMismoHueco.Poner(new[] { serum }, filas, new[] { UbicacionAlmacenDTO.De("002", "002", "001") });

            Assert.AreEqual(ProductosMismoHueco.LIMITE_POR_HUECO, serum.ProductosMismoHueco.Count);
            Assert.AreEqual("99999", serum.ProductosMismoHueco.First().Producto);
            Assert.AreEqual("12237", serum.ProductosMismoHueco.First().UltimasCifras);
        }

        [TestMethod]
        public void Ordenar_AIgualdadDeNombre_ElNumeroMasCercano()
        {
            var otros = new[]
            {
                EnHueco("10000", "1", "1", "1", "OTRA COSA"),
                EnHueco("44726", "1", "1", "1", "OTRA COSA"),
                EnHueco("ABC", "1", "1", "1", "OTRA COSA"),
                EnHueco("44700", "1", "1", "1", "OTRA COSA")
            };

            CollectionAssert.AreEqual(new[] { "44726", "44700", "10000", "ABC" },
                ProductosMismoHueco.Ordenar("44724", "SERUM", otros).Select(f => f.Producto).ToArray());
        }

        [TestMethod]
        public void Palabras_SinTildesNiMayusculasYConLosNumeros()
        {
            HashSet<string> palabras = ProductosMismoHueco.Palabras("Máscara level-lash LK OPC 5/55 y");

            CollectionAssert.AreEquivalent(new[] { "MASCARA", "LEVEL", "LASH", "LK", "OPC", "5", "55" }, palabras.ToArray());
            Assert.AreEqual(0, ProductosMismoHueco.Palabras(null).Count);
        }

        // ---------------------------------------------------------------- Huecos de referencia de cada línea

        [TestMethod]
        public void HuecosAConsultar_UnoPorHuecoDistinto()
        {
            List<UbicacionAlmacenDTO> huecos = ProductosMismoHueco.HuecosAConsultar(new IConProductosMismoHueco[]
            {
                Linea("1", "002", "004", "001"),
                Linea("2", "002", "004", "001"),
                Linea("3", "2", "4", "1"),
                Linea("4", "003", "001", "001"),
                Linea("5", null, null, null)
            });

            Assert.AreEqual(2, huecos.Count);
        }

        [TestMethod]
        public void HuecoDeReferencia_PendienteDeUbicar_ElPrimeroActualOElUltimo()
        {
            var conActual = new ProductoPendienteDeUbicarDTO
            {
                Producto = "1",
                UbicacionesActuales = new List<UbicacionAlmacenDTO> { UbicacionAlmacenDTO.De("002", "004", "001"), UbicacionAlmacenDTO.De("009", "009", "009") },
                UltimaUbicacion = "007/007/007"
            };
            var soloUltima = new ProductoPendienteDeUbicarDTO { Producto = "2", UltimaUbicacion = "007/001/003" };
            var nuevo = new ProductoPendienteDeUbicarDTO { Producto = "3" };

            Assert.AreEqual("002/004/001", ((IConProductosMismoHueco)conActual).HuecoDeReferencia.Ubicacion);
            Assert.AreEqual("007/001/003", ((IConProductosMismoHueco)soloUltima).HuecoDeReferencia.Ubicacion);
            Assert.IsNull(((IConProductosMismoHueco)nuevo).HuecoDeReferencia);
        }

        [TestMethod]
        public void HuecoDeReferencia_ProductoAlmacen_ElUbicadoAntesQueElPrimero()
        {
            var ubicado = new ProductoAlmacenDTO
            {
                Producto = "1",
                Ubicaciones = new List<UbicacionAlmacenDTO> { UbicacionAlmacenDTO.De("001", "001", "001") },
                HuecoUbicado = UbicacionAlmacenDTO.De("002", "004", "001")
            };
            var buscado = new ProductoAlmacenDTO { Producto = "1", Ubicaciones = new List<UbicacionAlmacenDTO> { UbicacionAlmacenDTO.De("001", "001", "001") } };

            Assert.AreEqual("002/004/001", ((IConProductosMismoHueco)ubicado).HuecoDeReferencia.Ubicacion);
            Assert.AreEqual("001/001/001", ((IConProductosMismoHueco)buscado).HuecoDeReferencia.Ubicacion);
        }

        // ---------------------------------------------------------------- Completar (con el repositorio)

        [TestMethod]
        public async Task Completar_UnaSolaConsultaParaTodosLosHuecos()
        {
            ConFilas(EnHueco("44726", "002", "004", "001", "MÁSCARA LEVEL LASH", GTIN_MASCARA));
            var lineas = new List<IConProductosMismoHueco>
            {
                Linea("44724", "002", "004", "001"),
                Linea("44725", "002", "004", "001"),
                Linea("10000", "005", "001", "001"),
                new LineaReposicionEnPreparacionDTO { Producto = "30000", HuecoDeReferencia = UbicacionAlmacenDTO.De("006", "001", "001") }
            };

            await ProductosMismoHueco.Completar(repositorio, "1", "ALG", lineas);

            A.CallTo(() => repositorio.LeerProductos("1", "ALG",
                    A<IReadOnlyCollection<UbicacionAlmacenDTO>>.That.Matches(h => h.Count == 3), ProductosMismoHueco.LECTURA_POR_HUECO))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("12237", lineas[0].ProductosMismoHueco.Single().UltimasCifras);
            Assert.AreEqual(0, lineas[2].ProductosMismoHueco.Count);
        }

        [TestMethod]
        public async Task Completar_SinHuecos_NoConsulta()
        {
            await ProductosMismoHueco.Completar(repositorio, "1", "ALG", new[] { Linea("1", null, null, null) });
            await ProductosMismoHueco.Completar(repositorio, "1", null, new[] { Linea("1", "001", "001", "001") });

            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Completar_SiFallaLaConsulta_LasLineasSalenSinElCampoYNoRompe()
        {
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<string>._, A<IReadOnlyCollection<UbicacionAlmacenDTO>>._, A<int>._))
                .Throws(new InvalidOperationException("Tiempo de espera agotado"));
            LineaPickingAlmacenDTO linea = Linea("44724", "002", "004", "001");

            await ProductosMismoHueco.Completar(repositorio, "1", "ALG", new[] { linea });

            Assert.IsNull(linea.ProductosMismoHueco);
        }

        [TestMethod]
        public async Task FichasProductoAlmacen_CompletarMismoHueco_UsaSuRepositorio()
        {
            ConFilas(EnHueco("44726", "002", "004", "001"));
            var fichas = new FichasProductoAlmacen(A.Fake<IRepositorioFichasProducto>(), mismoHueco: repositorio);
            LineaPickingAlmacenDTO linea = Linea("44724", "002", "004", "001");

            await fichas.CompletarMismoHueco("1", "ALG", new[] { linea });

            Assert.AreEqual("44726", linea.ProductosMismoHueco.Single().Producto);
        }

        [TestMethod]
        public void Json_SinElCampo_NoLoManda()
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(Linea("44724", "002", "004", "001"));

            Assert.IsFalse(json.Contains("ProductosMismoHueco"), json);
            Assert.IsFalse(json.Contains("HuecoDeReferencia"), json);
        }
    }
}
