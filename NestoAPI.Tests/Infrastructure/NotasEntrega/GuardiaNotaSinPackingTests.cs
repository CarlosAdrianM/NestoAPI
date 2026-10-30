using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.NotasEntrega;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#765 (decisión 4 de #755): una nota de ENTREGA (también la de solo texto) no se procesa sin pasar por picking y packing
    /// salvo que quien la procesa lo confirme. Las de RECOGIDA no pasan por packing.
    /// </summary>
    [TestClass]
    public class GuardiaNotaSinPackingTests
    {
        private static EstadoNotaPacking Estado(bool nota = true, string tipo = null, bool picking = true, bool envio = true, string ruta = "FW")
            => new EstadoNotaPacking { NotaEntrega = nota, Tipo = tipo, TienePicking = picking, TieneEnvio = envio, Ruta = ruta };

        [TestMethod]
        public void Motivo_NotaDeEntregaSinPicking_LoDice()
        {
            string motivo = GuardiaNotaSinPacking.MotivoSinPacking(Estado(picking: false, envio: false), 929667);

            StringAssert.Contains(motivo, "929667");
            StringAssert.Contains(motivo, "no ha pasado por el picking ni por el packing");
        }

        [TestMethod]
        public void Motivo_NotaDeEntregaConPickingSinEnvio_LoDice()
        {
            string motivo = GuardiaNotaSinPacking.MotivoSinPacking(Estado(envio: false), 929486);

            StringAssert.Contains(motivo, "no ha pasado por el packing");
        }

        [TestMethod]
        public void Motivo_SinFilaEnLaCabecera_EsDeEntrega()
        {
            Assert.IsNotNull(GuardiaNotaSinPacking.MotivoSinPacking(Estado(tipo: null, envio: false), 1));
            Assert.IsNotNull(GuardiaNotaSinPacking.MotivoSinPacking(Estado(tipo: "Entrega ", envio: false), 1));
        }

        [TestMethod]
        public void Motivo_NotaConPickingYEnvio_NoHayNadaQuePreguntar()
        {
            Assert.IsNull(GuardiaNotaSinPacking.MotivoSinPacking(Estado(), 929179));
        }

        [TestMethod]
        public void Motivo_NotaDeRecogida_NoPasaPorPacking_NuncaSePara()
        {
            Assert.IsNull(GuardiaNotaSinPacking.MotivoSinPacking(Estado(tipo: "Recogida", picking: false, envio: false), 929554));
            Assert.IsNull(GuardiaNotaSinPacking.MotivoSinPacking(Estado(tipo: "recogida ", picking: true, envio: false), 929554));
        }

        [TestMethod]
        public void Motivo_PedidoNormalOInexistente_Null()
        {
            Assert.IsNull(GuardiaNotaSinPacking.MotivoSinPacking(Estado(nota: false, picking: false, envio: false), 927519));
            Assert.IsNull(GuardiaNotaSinPacking.MotivoSinPacking(null, 1));
        }

        [TestMethod]
        public async Task Motivo_LeeDelRepositorio()
        {
            var repositorio = A.Fake<IRepositorioEstadoNotaPacking>();
            A.CallTo(() => repositorio.Leer("1", 929486)).Returns(Estado(envio: false));

            string motivo = await new GuardiaNotaSinPacking(repositorio).Motivo("1", 929486);

            StringAssert.Contains(motivo, "929486");
        }

        [TestMethod]
        public void EsNotaDeRepartoPropio_SoloNotasDeRutasPropias()
        {
            // AT y 16 son las rutas propias por defecto (RutasPropiasConfiguradas)
            Assert.IsTrue(GuardiaNotaSinPacking.EsNotaDeRepartoPropio(Estado(ruta: "AT ")));
            Assert.IsTrue(GuardiaNotaSinPacking.EsNotaDeRepartoPropio(Estado(ruta: "16", tipo: "Recogida")));
            Assert.IsFalse(GuardiaNotaSinPacking.EsNotaDeRepartoPropio(Estado(ruta: "FW")));
            Assert.IsFalse(GuardiaNotaSinPacking.EsNotaDeRepartoPropio(Estado(nota: false, ruta: "AT")));
            Assert.IsFalse(GuardiaNotaSinPacking.EsNotaDeRepartoPropio(null));
        }

        [TestMethod]
        public void SqlEstado_MiraPickingEnCursoEnviosGrabadosYElTipoDeLaCabecera()
        {
            string sql = RepositorioEstadoNotaPacking.SQL_ESTADO;
            StringAssert.Contains(sql, "l.Estado = 1 AND l.Picking > 0");
            StringAssert.Contains(sql, "e.Estado >= 0", "Un envío pendiente de la tienda (−1) no es haber pasado por el packing");
            StringAssert.Contains(sql, "LEFT JOIN NotasEntregaCabecera n");
        }
    }
}
