using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Picking;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// RellenadorPickingService lee de la base de datos y por eso no se testea entero, pero la
    /// guarda de "el pedido existe" sí es pura y sí se testea: era la causa de 30
    /// NullReferenceException en ELMAH en 60 días (la última, Santiago el 20\08 a las 11:30).
    ///
    /// Rellenar(empresa, numeroPedido) hacía SingleOrDefault y metía el resultado en la lista
    /// SIN comprobar null; despues Ejecutar() hacía p.LinPedidoVtas sobre ese null y salía un
    /// 500 con "Referencia a objeto no establecida", sin decir siquiera qué pedido se pedía.
    /// </summary>
    [TestClass]
    public class RellenadorPickingServiceTests
    {
        [TestMethod]
        public void ValidarPedidoExiste_SiElPedidoExiste_LoDevuelveTalCual()
        {
            var pedido = new CabPedidoVta { Empresa = "1  ", Número = 924625 };

            CabPedidoVta resultado = RellenadorPickingService.ValidarPedidoExiste(pedido, "1", 924625);

            Assert.AreSame(pedido, resultado);
        }

        [TestMethod]
        public void ValidarPedidoExiste_SiElPedidoNoExiste_LanzaErrorDeNegocioYNoNullReference()
        {
            // Sin el fix esto no lanzaba nada aquí: el null llegaba a Ejecutar() y reventaba
            // allí con NullReferenceException (500), que es justo lo que no queremos.
            NestoBusinessException excepcion = Assert.ThrowsException<NestoBusinessException>(
                () => RellenadorPickingService.ValidarPedidoExiste(null, "1", 924625));

            Assert.IsTrue(excepcion.Message.Contains("924625"),
                $"El mensaje debe decir qué pedido se pidió, y dice: '{excepcion.Message}'");
        }

        [TestMethod]
        public void ValidarPedidoExiste_SiElPedidoNoExiste_LlevaContextoParaDiagnosticar()
        {
            NestoBusinessException excepcion = Assert.ThrowsException<NestoBusinessException>(
                () => RellenadorPickingService.ValidarPedidoExiste(null, "1  ", 924625));

            Assert.AreEqual("PICKING_PEDIDO_NO_EXISTE", excepcion.Context.ErrorCode);
            Assert.AreEqual("1", excepcion.Context.Empresa, "la empresa va sin el padding de char(3)");
            Assert.AreEqual(924625, excepcion.Context.Pedido);
        }

        [TestMethod]
        public void ValidarPedidoExiste_SiLaEmpresaEsNula_NoRevientaAlConstruirElMensaje()
        {
            // Defensa: el mensaje de error no puede provocar OTRA excepción distinta.
            NestoBusinessException excepcion = Assert.ThrowsException<NestoBusinessException>(
                () => RellenadorPickingService.ValidarPedidoExiste(null, null, 924625));

            Assert.IsTrue(excepcion.Message.Contains("924625"));
        }
    }
}
