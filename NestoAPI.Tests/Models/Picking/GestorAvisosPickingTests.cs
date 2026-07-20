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
        // #314: Total (con IVA y RE) se prorratea igual en TotalEntrega. Por defecto se pone un
        // Total coherente con IVA 21% para no tener que repetirlo en cada test.
        private static LineaPedidoPicking Linea(decimal baseImponible, int cantidad, int reservada, decimal? total = null) =>
            new LineaPedidoPicking
            {
                Producto = "PROD1",
                BaseImponible = baseImponible,
                Total = total ?? baseImponible * 1.21m,
                Cantidad = cantidad,
                CantidadReservada = reservada
            };

        [TestMethod]
        public void ImporteCogido_SoloSumaLoQueSaleEnEstaTanda()
        {
            // 100€ de 10 uds con 5 reservadas -> salen 50€; la línea sin reserva no suma
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5), Linea(40, 2, 0));

            Assert.AreEqual(50m, GestorAvisosPicking.ImporteCogido(pedido));
        }

        [TestMethod]
        public void TotalConIvaCogido_ProrrateaElTotalPorLaCantidadReservada()
        {
            // #314: 121€ (100 + IVA) de 10 uds con 5 reservadas -> 60,50€ a cobrar
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5, total: 121m), Linea(40, 2, 0));

            Assert.AreEqual(60.50m, GestorAvisosPicking.TotalConIvaCogido(pedido));
        }

        [TestMethod]
        public void TotalConIvaCogido_ConRecargoDeEquivalencia_UsaElTotalDeLaLinea()
        {
            // El recargo ya viene incluido en Total (100 + 21 IVA + 5,20 RE)
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 10, total: 126.20m));

            Assert.AreEqual(126.20m, GestorAvisosPicking.TotalConIvaCogido(pedido));
        }

        [TestMethod]
        public void ComponerCorreo_ConVendedorYUsuario_VanLosDosYElAsuntoAgrupaPorPedido()
        {
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5));

            MailMessage mail = GestorAvisosPicking.ComponerCorreo(pedido, "vendedor@nv.es", "usuario@nv.es");

            Assert.IsNotNull(mail);
            Assert.AreEqual(2, mail.To.Count);
            // #314: formato "Pedido {n} - c/ {cliente}" para que Outlook agrupe la conversación.
            // El importe ya NO va en el asunto (antes sí): ahora está en el cuerpo.
            Assert.AreEqual("Pedido 922500 - c/ 15191", mail.Subject);
            StringAssert.Contains(mail.Body, "PROD1");
        }

        [TestMethod]
        public void ComponerCorreo_LasRespuestasVanAAlmacen()
        {
            // #314: el remitente sigue siendo el buzón técnico (enviar DESDE almacén exigiría
            // permiso Send As), pero al pulsar Responder la respuesta va a almacén.
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5));

            MailMessage mail = GestorAvisosPicking.ComponerCorreo(pedido, "vendedor@nv.es", null);

            Assert.AreEqual(1, mail.ReplyToList.Count);
            Assert.AreEqual("almacen@nuevavision.es", mail.ReplyToList[0].Address);
        }

        [TestMethod]
        public void ComponerCorreo_ElCuerpoLlevaElTotalConIvaYLaBaseImponible()
        {
            // #314: el aviso se usa para saber cuánto dinero tiene que preparar el cliente
            PedidoPicking pedido = CrearPedido(Linea(100, 10, 5, total: 121m));

            MailMessage mail = GestorAvisosPicking.ComponerCorreo(pedido, "vendedor@nv.es", null);

            StringAssert.Contains(mail.Body, 60.50m.ToString("C"), "Debe mostrar el total con IVA");
            StringAssert.Contains(mail.Body, 50m.ToString("C"), "Debe seguir mostrando la base imponible");
            StringAssert.Contains(mail.Body, "IVA incluido");
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
