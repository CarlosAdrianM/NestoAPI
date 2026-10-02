using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Sugerencia 462 de Novedades: un pedido en NV que solo lleva cursos pasa a la serie CV.
    /// </summary>
    [TestClass]
    public class SerieSegunLineasTests
    {
        [TestMethod]
        public void Resolver_NVSoloConCursos_PasaACV()
        {
            var serie = SerieSegunLineas.Resolver("NV", "1  ", Lineas("CUR"));

            Assert.AreEqual(Constantes.Series.SERIE_CURSOS, serie);
        }

        [TestMethod]
        public void Resolver_NVConCursosYPortesSinGrupo_PasaACV()
        {
            var serie = SerieSegunLineas.Resolver("NV ", "1", Lineas("CUR", null));

            Assert.AreEqual(Constantes.Series.SERIE_CURSOS, serie);
        }

        [TestMethod]
        public void Resolver_NVConCursosYProducto_SeQuedaEnNV()
        {
            var serie = SerieSegunLineas.Resolver("NV", "1", Lineas("CUR", "COS"));

            Assert.AreEqual("NV", serie);
        }

        [TestMethod]
        public void Resolver_NVSinCursos_SeQuedaEnNV()
        {
            var serie = SerieSegunLineas.Resolver("NV", "1", Lineas("COS", "PEL"));

            Assert.AreEqual("NV", serie);
        }

        [TestMethod]
        public void Resolver_NVSinLineasConGrupo_SeQuedaEnNV()
        {
            var serie = SerieSegunLineas.Resolver("NV", "1", Lineas(new string[] { null }));

            Assert.AreEqual("NV", serie);
        }

        [TestMethod]
        public void Resolver_OtraSerieSoloConCursos_NoSeToca()
        {
            var serie = SerieSegunLineas.Resolver("UL", "1", Lineas("CUR"));

            Assert.AreEqual("UL", serie);
        }

        [TestMethod]
        public void Resolver_Empresa3SoloConCursos_SeQuedaEnNV()
        {
            // prdCrearFacturaVta no exige CV para los cursos de la empresa 3
            var serie = SerieSegunLineas.Resolver("NV", "3  ", Lineas("CUR"));

            Assert.AreEqual("NV", serie);
        }

        private static List<LinPedidoVta> Lineas(params string[] grupos)
        {
            var lineas = new List<LinPedidoVta>();
            foreach (var grupo in grupos)
            {
                lineas.Add(new LinPedidoVta { Grupo = grupo });
            }
            return lineas;
        }
    }
}
