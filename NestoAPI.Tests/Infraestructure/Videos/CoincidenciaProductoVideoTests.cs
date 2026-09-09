using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Videos;
using System.Collections.Generic;
using static NestoAPI.Infraestructure.Videos.CoincidenciaProductoVideo;

namespace NestoAPI.Tests.Infraestructure.Videos
{
    /// <summary>
    /// NestoAPI#454: el buscador de la tienda quiere el momento del producto que hizo que el vídeo
    /// saliera en los resultados, para enlazar con &amp;t= en vez de al minuto 0.
    /// </summary>
    [TestClass]
    public class CoincidenciaProductoVideoTests
    {
        private static List<ProductoEnVideo> Video1249()
        {
            // Los productos reales del vídeo 1249 (celulitis), en segundos, tal y como están en la BD.
            return new List<ProductoEnVideo>
            {
                new ProductoEnVideo { Nombre = "Presoterapia Libis de Unión Láser", TiempoAparicion = "1296" },
                new ProductoEnVideo { Nombre = "Electroestimulación de Unión Láser", TiempoAparicion = "1340" },
                new ProductoEnVideo { Nombre = "Tri- Acids de Anubis", TiempoAparicion = "2191" },
                new ProductoEnVideo { Nombre = "Concentrado Algas Rojas de Anubis", TiempoAparicion = "2245" },
                new ProductoEnVideo { Nombre = "Celu Top de BelClinic", TiempoAparicion = "2260" }
            };
        }

        [TestMethod]
        public void Elegir_CeluTop_DaElMomentoDelCeluTop()
        {
            Coincidencia c = Elegir("celu top", Video1249());

            Assert.IsNotNull(c);
            Assert.AreEqual("Celu Top de BelClinic", c.Producto);
            Assert.AreEqual(2260, c.Segundos);
        }

        [TestMethod]
        public void Elegir_SinMayusculasNiAcentos()
        {
            Coincidencia c = Elegir("ELECTROESTIMULACION union", Video1249());

            Assert.IsNotNull(c);
            Assert.AreEqual(1340, c.Segundos);
        }

        [TestMethod]
        public void Elegir_VariosProductosCasan_DevuelveElMasTemprano()
        {
            // "anubis" casa con Tri-Acids (2191) y con Algas Rojas (2245): no adelantar al usuario de más.
            Coincidencia c = Elegir("anubis", Video1249());

            Assert.AreEqual("Tri- Acids de Anubis", c.Producto);
            Assert.AreEqual(2191, c.Segundos);
        }

        [TestMethod]
        public void Elegir_TodasLasPalabrasTienenQueEstar()
        {
            Assert.IsNull(Elegir("celu top anubis", Video1249()), "ningún producto tiene las tres palabras");
        }

        [TestMethod]
        public void Elegir_ElVideoCasaPorLaTranscripcionYNoPorUnProducto_NoSeInventaMomento()
        {
            Assert.IsNull(Elegir("gluteos y muslos", Video1249()));
        }

        [TestMethod]
        public void Elegir_MismoProductoConVariasReferencias_EsUnSoloMomento()
        {
            List<ProductoEnVideo> repetido = new List<ProductoEnVideo>
            {
                new ProductoEnVideo { Nombre = "Champú Kach Organic Solutions", TiempoAparicion = "23" },
                new ProductoEnVideo { Nombre = "Champú Kach Organic Solutions", TiempoAparicion = "23" },
                new ProductoEnVideo { Nombre = "Champú Kach Organic Solutions", TiempoAparicion = "23" }
            };

            Coincidencia c = Elegir("champu kach", repetido);

            Assert.AreEqual(23, c.Segundos);
        }

        [TestMethod]
        public void Elegir_ProductoSinMomentoLegible_NoCuenta()
        {
            List<ProductoEnVideo> productos = new List<ProductoEnVideo>
            {
                new ProductoEnVideo { Nombre = "Celu Top de BelClinic", TiempoAparicion = "" },
                new ProductoEnVideo { Nombre = "Celu Top Gel", TiempoAparicion = "abc" }
            };

            Assert.IsNull(Elegir("celu top", productos), "mejor sin momento que un momento inventado");
        }

        [TestMethod]
        public void Elegir_QueryVaciaOSoloLetrasSueltas_Null()
        {
            Assert.IsNull(Elegir("", Video1249()));
            Assert.IsNull(Elegir("a", Video1249()));
            Assert.IsNull(Elegir(null, Video1249()));
            Assert.IsNull(Elegir("celu", null));
        }

        [TestMethod]
        public void Segundos_EnterosYFormatoReloj()
        {
            Assert.AreEqual(2260, Segundos("2260"));
            Assert.AreEqual(2260, Segundos(" 2260 "));
            Assert.AreEqual(83, Segundos("00:01:23"));
            Assert.AreEqual(83, Segundos("0:01:23"));
            Assert.IsNull(Segundos(null));
            Assert.IsNull(Segundos("-5"));
            Assert.IsNull(Segundos("minuto 3"));
        }
    }
}
