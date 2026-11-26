using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#878 (Carlos 26/11/26): la «Dirección complementaria» de PrestaShop (address2) y la empresa (company) de un pedido web
    /// son la Dirección 2 de la entrega, compuesta como en el alta de clientes («· RÓTULO: …»); nunca se inventa «Piso».
    /// </summary>
    [TestClass]
    public class Direccion2PedidoTiendaTests
    {
        [TestMethod]
        public void Componer_SoloAddress2_EnMayusculasYSinTipo()
        {
            Direccion2PedidoTiendaDTO d = Direccion2PedidoTienda.Componer("  3a ", null);

            Assert.AreEqual("3A", d.Direccion2);
            Assert.IsNull(d.TipoLugar, "no se inventa «Piso»");
        }

        [TestMethod]
        public void Componer_Address2YCompany_ConRotuloYLocal()
        {
            Direccion2PedidoTiendaDTO d = Direccion2PedidoTienda.Componer("Bajo", "Estética Goji");

            Assert.AreEqual("BAJO · RÓTULO: ESTÉTICA GOJI", d.Direccion2);
            Assert.AreEqual(TiposLugar.LOCAL, d.TipoLugar);
        }

        [TestMethod]
        public void Componer_CompanyYaContenidaEnAddress2_NoSeRepite()
        {
            Direccion2PedidoTiendaDTO d = Direccion2PedidoTienda.Componer("Local estetica goji", "Estética Goji");

            Assert.AreEqual("LOCAL ESTETICA GOJI", d.Direccion2);
            Assert.AreEqual(TiposLugar.LOCAL, d.TipoLugar);
        }

        [TestMethod]
        public void Componer_SoloCompany_ComoElAlta()
        {
            Assert.AreEqual("LOCAL · RÓTULO: VENUS ESTILISTAS", Direccion2PedidoTienda.Componer("", "Venus estilistas").Direccion2);
        }

        [TestMethod]
        public void Componer_Nada_Null()
        {
            Direccion2PedidoTiendaDTO d = Direccion2PedidoTienda.Componer(" ", null);

            Assert.IsNull(d.Direccion2);
            Assert.IsNull(d.TipoLugar);
        }

        [TestMethod]
        public void Componer_MuyLarga_SeQuedaEnCien()
        {
            string d2 = Direccion2PedidoTienda.Componer(new string('A', 80), new string('B', 80)).Direccion2;

            Assert.AreEqual(Direccion2Cliente.LONGITUD, d2.Length);
        }

        [TestMethod]
        public void LineaComentario_YLeerla_IdaYVuelta()
        {
            string linea = Direccion2PedidoTienda.LineaComentario("3a · rótulo: estética goji");
            Assert.AreEqual("DIRECCIÓN 2: 3A · RÓTULO: ESTÉTICA GOJI", linea);
            Assert.IsNull(Direccion2PedidoTienda.LineaComentario("  "));

            string comentarios = "RLJTDQBKU \r\nANA PÉREZ\r\nana@correo.es\r\nCALLE LAS NEVERAS 2\r\n" + linea + "\r\n28110 ALGETE\r\nTel.: 600000000\r\nTOTAL PEDIDO: 84,80 €";

            Assert.AreEqual("3A · RÓTULO: ESTÉTICA GOJI", Direccion2PedidoTienda.LeerDeComentarios(comentarios));
            Assert.IsTrue(VigenciaPedidoTienda_SigueSiendoDeLaTienda(comentarios));
        }

        [DataTestMethod]
        [DataRow("direccion 2: 1º d", "1º D")]
        [DataRow("  Dirección 2 :   nave 4  ", "NAVE 4")]
        [DataRow("DIRECCION2:CASA", "CASA")]
        public void LeerDeComentarios_Tolerante(string linea, string esperado)
        {
            Assert.AreEqual(esperado, Direccion2PedidoTienda.LeerDeComentarios("PEDIDO X\n" + linea + "\nTOTAL PEDIDO: 1,00 €"));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("CALLE LAS NEVERAS 2\r\n3A\r\nTOTAL PEDIDO: 84,80 €")]
        [DataRow("DIRECCIÓN 2:   \r\nTOTAL PEDIDO: 1,00 €")]
        [DataRow("Ver dirección 2: no es una línea propia")]
        public void LeerDeComentarios_SinLinea_Null(string comentarios)
        {
            Assert.IsNull(Direccion2PedidoTienda.LeerDeComentarios(comentarios));
        }

        [TestMethod]
        public void TipoLugarDe_ConRotuloLocal_SinNull()
        {
            Assert.AreEqual(TiposLugar.LOCAL, Direccion2PedidoTienda.TipoLugarDe("BAJO · RÓTULO: GOJI"));
            Assert.AreEqual(TiposLugar.LOCAL, Direccion2PedidoTienda.TipoLugarDe("BAJO · ROTULO: GOJI"));
            Assert.IsNull(Direccion2PedidoTienda.TipoLugarDe("3A"));
            Assert.IsNull(Direccion2PedidoTienda.TipoLugarDe(null));
        }

        private static bool VigenciaPedidoTienda_SigueSiendoDeLaTienda(string comentarios)
            => comentarios.Contains(VigenciaPedidoTienda.MARCA_COMENTARIO_TIENDA);
    }
}
