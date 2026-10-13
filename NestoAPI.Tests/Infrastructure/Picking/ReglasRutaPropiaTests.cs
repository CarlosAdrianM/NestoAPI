using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Picking;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Picking
{
    /// <summary>
    /// Sugerencia 575 (Alfredo, almacén): pedidos que van por OnTime (FW) y que seguramente saldrán por nuestra ruta
    /// (AT o 16). Reglas del estudio de 6 meses: empresa 3 + CP 28; último pedido por AT/16; empresa 1 con aparatos y
    /// 1.500 € o más → AT. Ruta por zona: 288, 289 y 283 → AT; el resto → 16.
    /// </summary>
    [TestClass]
    public class ReglasRutaPropiaTests
    {
        private static DatosPedidoSugerenciaRuta Pedido(string empresa = "1", string ruta = "FW ", string cp = "28100",
            decimal baseImponible = 100M, bool aparatos = false, string rutaUltimo = null)
        {
            return new DatosPedidoSugerenciaRuta
            {
                Empresa = empresa.PadRight(3),
                Pedido = 928241,
                Cliente = "5825      ",
                Contacto = "0  ",
                Nombre = "PELUQUERÍA PRUEBA",
                CodigoPostal = cp,
                Poblacion = "ALCOBENDAS",
                Ruta = ruta,
                BaseImponible = baseImponible,
                TieneAparatos = aparatos,
                RutaUltimoPedido = rutaUltimo
            };
        }

        // ===== Ruta por zona

        [DataTestMethod]
        [DataRow("28800", "AT")]
        [DataRow("28901", "AT")]
        [DataRow("28340", "AT")]
        [DataRow(" 28830 ", "AT")]
        [DataRow("28001", "16")]
        [DataRow("28100", "16")]
        [DataRow("28290", "16")]
        [DataRow("19200", "16")]
        [DataRow("", "16")]
        [DataRow(null, "16")]
        public void RutaPorZona_288_289_283_VanPorAT_ElRestoPorLa16(string cp, string esperada)
        {
            Assert.AreEqual(esperada, ReglasRutaPropia.RutaPorZona(cp));
        }

        // ===== R1: empresa 3 + código postal de Madrid

        [TestMethod]
        public void Sugerir_Empresa3ConCodigoPostalDeMadrid_SugiereRutaPorZona()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "3", cp: "28100"));

            Assert.IsNotNull(s);
            Assert.AreEqual("3", s.Empresa);
            Assert.AreEqual("FW", s.RutaActual);
            Assert.AreEqual("16", s.RutaPropuesta);
            Assert.AreEqual(1, s.Motivos.Count);
            StringAssert.Contains(s.Motivos[0], "empresa 3");
            Assert.AreEqual("5825", s.Cliente);
        }

        [TestMethod]
        public void Sugerir_Empresa3EnZonaDeAT_SugiereAT()
        {
            Assert.AreEqual("AT", ReglasRutaPropia.Sugerir(Pedido(empresa: "3", cp: "28830")).RutaPropuesta);
        }

        [TestMethod]
        public void Sugerir_Empresa3FueraDeMadrid_NoSugiereNada()
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "3", cp: "19200")));
        }

        [TestMethod]
        public void Sugerir_Empresa1ConCodigoPostalDeMadrid_SinMasMotivos_NoSugiereNada()
        {
            // R1 es solo de la empresa 3: en la 1 solo el 2,8 % acaba por nuestra ruta
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "1", cp: "28100")));
        }

        // ===== R3: el último pedido del cliente salió por AT/16 (cualquier empresa)

        [TestMethod]
        public void Sugerir_UltimoPedidoPorAT_EnEmpresa1_SugiereLaRutaDelUltimo()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "1", cp: "28100", rutaUltimo: "AT "));

            Assert.IsNotNull(s);
            Assert.AreEqual("AT", s.RutaPropuesta, "La del último pedido, no la de la zona (que sería la 16)");
            Assert.AreEqual("Su último pedido salió por la AT", s.Motivos.Single());
        }

        [TestMethod]
        public void Sugerir_UltimoPedidoPorLa16_FueraDeMadrid_TambienSugiere()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "1", cp: "19200", rutaUltimo: "16"));

            Assert.AreEqual("16", s.RutaPropuesta);
        }

        [TestMethod]
        public void Sugerir_UltimoPedidoPorAgencia_NoCuenta()
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "1", rutaUltimo: "FW ")));
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "1", rutaUltimo: "00 ")));
        }

        [TestMethod]
        public void Sugerir_Empresa3ConUltimoPorATEnZonaDeLa16_DaLosDosMotivosYLaRutaDelUltimo()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "3", cp: "28001", rutaUltimo: "AT"));

            Assert.AreEqual("AT", s.RutaPropuesta);
            Assert.AreEqual(2, s.Motivos.Count);
        }

        // ===== Empresa 1: aparatos y 1.500 € o más → AT

        [TestMethod]
        public void Sugerir_Empresa1ConAparatosYMilQuinientos_SugiereAT()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "1", cp: "28001", baseImponible: 1500M, aparatos: true));

            Assert.IsNotNull(s);
            Assert.AreEqual("AT", s.RutaPropuesta);
            StringAssert.Contains(s.Motivos.Single(), "aparatos");
            StringAssert.Contains(s.Motivos.Single(), "1.500,00 €");
        }

        [TestMethod]
        public void Sugerir_Empresa1ConAparatosPorDebajoDeMilQuinientos_NoSugiere()
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "1", baseImponible: 1499.99M, aparatos: true)));
        }

        [TestMethod]
        public void Sugerir_Empresa1SinAparatosAunqueSeaGrande_NoSugiere()
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "1", baseImponible: 5000M, aparatos: false)));
        }

        [TestMethod]
        public void Sugerir_AparatosEnEmpresa3_NoEsMotivoDeAparatos_PeroR1SiAplica()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "3", cp: "28001", baseImponible: 3000M, aparatos: true));

            Assert.AreEqual("16", s.RutaPropuesta, "Empresa 3: por zona, no AT por aparatos");
            Assert.IsFalse(s.Motivos.Any(m => m.Contains("aparatos")));
        }

        [TestMethod]
        public void Sugerir_AparatosYUltimoPorLa16_GananLosAparatos()
        {
            SugerenciaRutaPedidoDTO s = ReglasRutaPropia.Sugerir(Pedido(empresa: "1", baseImponible: 2000M, aparatos: true, rutaUltimo: "16"));

            Assert.AreEqual("AT", s.RutaPropuesta);
            Assert.AreEqual(2, s.Motivos.Count);
        }

        // ===== Lo que no se sugiere nunca

        [DataTestMethod]
        [DataRow("AT ")]
        [DataRow("16 ")]
        [DataRow("at")]
        public void Sugerir_PedidoQueYaVaPorNuestraRuta_NoSeSugiere(string ruta)
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "3", ruta: ruta, cp: "28100", rutaUltimo: "AT")));
        }

        [DataTestMethod]
        [DataRow("00 ")]
        [DataRow("AM ")]
        [DataRow("GLV")]
        [DataRow(null)]
        public void Sugerir_PedidoQueNoVaPorOnTime_NoSeSugiere(string ruta)
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "3", ruta: ruta, cp: "28100", rutaUltimo: "AT")));
        }

        [TestMethod]
        public void Sugerir_PedidoPorOT_TambienEsOnTime()
        {
            Assert.IsNotNull(ReglasRutaPropia.Sugerir(Pedido(empresa: "3", ruta: "OT ", cp: "28100")));
        }

        [TestMethod]
        public void Sugerir_Null_NoRevienta()
        {
            Assert.IsNull(ReglasRutaPropia.Sugerir(null));
        }

        [TestMethod]
        public void RutasPropias_SonLasDeLaFacturacionDeRutas()
        {
            CollectionAssert.AreEquivalent(new[] { "AT", "16" }, ReglasRutaPropia.RUTAS_PROPIAS.ToArray());
        }
    }
}
