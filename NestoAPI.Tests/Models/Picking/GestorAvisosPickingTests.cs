using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#253: aviso automático con importe cuando el pedido coge picking.
    /// </summary>
    [TestClass]
    public class GestorAvisosPickingTests
    {
        private static PedidoPicking CrearPedido(params LineaPedidoPicking[] lineas)
        {
            return new PedidoPicking
            {
                Empresa = "1",
                Id = 922500,
                Cliente = "15191",
                Vendedor = "NV",
                Usuario = "Carlos",
                AvisarConImporteAlCogerPicking = true,
                Lineas = new List<LineaPedidoPicking>(lineas)
            };
        }

        // BaseImponibleEntrega = BaseImponible / Cantidad * CantidadReservada
        private static LineaPedidoPicking Linea(decimal baseImponible, int cantidad, int reservada) =>
            new LineaPedidoPicking { Producto = "PROD1", BaseImponible = baseImponible, Cantidad = cantidad, CantidadReservada = reservada };

        [TestMethod]
        public void ImporteCogido_SoloSumaLoQueSaleEnEstaTanda()
        {
            // 100€ de 10 uds con 5 reservadas -> salen 50€; la línea sin reserva no suma
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5), Linea(40, 2, 0));

            Assert.AreEqual(50m, GestorAvisosPicking.ImporteCogido(pedido));
        }

        [TestMethod]
        public void ComponerCorreo_ConVendedorYUsuario_VanLosDosYElImporteEnElAsunto()
        {
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5));

            MailMessage mail = GestorAvisosPicking.ComponerCorreo(pedido, "vendedor@nv.es", "usuario@nv.es");

            Assert.IsNotNull(mail);
            Assert.AreEqual(2, mail.To.Count);
            StringAssert.Contains(mail.Subject, "922500");
            StringAssert.Contains(mail.Subject, 50m.ToString("C"));
            StringAssert.Contains(mail.Body, "PROD1");
        }

        [TestMethod]
        public void ComponerCorreo_SinCorreoDeVendedor_SoloVaElUsuario()
        {
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5));

            MailMessage mail = GestorAvisosPicking.ComponerCorreo(pedido, null, "usuario@nv.es");

            Assert.IsNotNull(mail);
            Assert.AreEqual(1, mail.To.Count);
            Assert.AreEqual("usuario@nv.es", mail.To[0].Address);
        }

        [TestMethod]
        public void ComponerCorreo_MismoCorreoVendedorYUsuario_NoLoDuplica()
        {
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5));

            MailMessage mail = GestorAvisosPicking.ComponerCorreo(pedido, "unico@nv.es", "UNICO@nv.es");

            Assert.IsNotNull(mail);
            Assert.AreEqual(1, mail.To.Count);
        }

        [TestMethod]
        public void ComponerCorreo_SinDestinatarios_DevuelveNull()
        {
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5));

            Assert.IsNull(GestorAvisosPicking.ComponerCorreo(pedido, null, "  "));
        }

        [TestMethod]
        public void ComponerCorreo_SinImporteQueSale_DevuelveNull()
        {
            // Nada reservado en esta tanda -> no hay aviso que dar
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 0));

            Assert.IsNull(GestorAvisosPicking.ComponerCorreo(pedido, "vendedor@nv.es", "usuario@nv.es"));
        }
    }
}
