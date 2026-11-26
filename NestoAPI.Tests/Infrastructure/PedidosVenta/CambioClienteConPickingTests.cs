using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>NestoAPI#876: un pedido con picking cambia de cliente solo si, al recalcular, no cambia nada.</summary>
    [TestClass]
    public class CambioClienteConPickingTests
    {
        private static LinPedidoVta Linea(int orden = 1) => new LinPedidoVta
        {
            Nº_Orden = orden, Nº_Cliente = "37283", Contacto = "0", Producto = "41752", Precio = 12.35M, DescuentoProducto = 0.5M, Aplicar_Dto = true,
            IVA = "G21", PorcentajeIVA = 21, PorcentajeRE = 0.052M, Bruto = 12.35M, Base_Imponible = 6.18M, ImporteIVA = 1.2978M, ImporteRE = 0.3214M, Total = 7.7992M,
            Picking = 100198
        };

        [TestMethod]
        public void Diferencias_SoloCambiaElCliente_NoHayNinguna()
        {
            LinPedidoVta linea = Linea();
            List<FotoLineaCambioCliente> antes = ReglaCambioClienteConPicking.Fotografiar(new[] { linea });
            linea.Nº_Cliente = "42178";

            List<FotoLineaCambioCliente> despues = ReglaCambioClienteConPicking.Fotografiar(new[] { linea });

            Assert.AreEqual(0, ReglaCambioClienteConPicking.Diferencias(antes, despues, "E52", "E52").Count);
        }

        [TestMethod]
        public void Diferencias_ImportesQueSoloDifierenPorDebajoDelCentimo_NoCuentan()
        {
            LinPedidoVta linea = Linea();
            List<FotoLineaCambioCliente> antes = ReglaCambioClienteConPicking.Fotografiar(new[] { linea });
            linea.ImporteIVA = 1.29780000001M;
            linea.Total = 7.79921M;

            Assert.AreEqual(0, ReglaCambioClienteConPicking.Diferencias(antes, ReglaCambioClienteConPicking.Fotografiar(new[] { linea }), "E52", "E52").Count);
        }

        [TestMethod]
        public void Diferencias_SinRecargoNiDescuento_LoDiceTodoEnUnaFrasePorLinea()
        {
            LinPedidoVta linea = Linea(3);
            List<FotoLineaCambioCliente> antes = ReglaCambioClienteConPicking.Fotografiar(new[] { linea });
            linea.DescuentoProducto = 0;
            linea.PorcentajeRE = 0;
            linea.ImporteRE = 0;
            linea.Base_Imponible = 12.35M;
            linea.Total = 14.94M;

            List<string> diferencias = ReglaCambioClienteConPicking.Diferencias(antes, ReglaCambioClienteConPicking.Fotografiar(new[] { linea }), "E52", "G21");

            Assert.AreEqual(2, diferencias.Count);
            StringAssert.StartsWith(diferencias[0], "IVA del pedido: E52 → G21");
            StringAssert.StartsWith(diferencias[1], "Línea 3 (41752): ");
            StringAssert.Contains(diferencias[1], "descuento de producto 50 % → 0 %");
            StringAssert.Contains(diferencias[1], "base 6,18 € → 12,35 €");
        }

        [TestMethod]
        public void Restaurar_DejaLaLineaComoEstaba()
        {
            LinPedidoVta linea = Linea();
            List<FotoLineaCambioCliente> foto = ReglaCambioClienteConPicking.Fotografiar(new[] { linea });
            linea.Nº_Cliente = "42178";
            linea.Precio = 1;
            linea.Total = 2;

            ReglaCambioClienteConPicking.Restaurar(new[] { linea }, foto);

            Assert.AreEqual("37283", linea.Nº_Cliente);
            Assert.AreEqual(12.35M, linea.Precio);
            Assert.AreEqual(7.7992M, linea.Total);
        }

        [TestMethod]
        public void MotivoNoSePuede_NombraLasDiferenciasYLaSalida()
        {
            string motivo = ReglaCambioClienteConPicking.MotivoNoSePuede(931691, "42178 ", new List<string> { "IVA del pedido: E52 → G21" });

            StringAssert.StartsWith(motivo, "No se puede cambiar el cliente del pedido 931691: ya tiene picking");
            StringAssert.Contains(motivo, "cliente 42178 cambiaría: IVA del pedido: E52 → G21.");
            StringAssert.Contains(motivo, "copie antes al cliente nuevo las condiciones del anterior");
        }

        [TestMethod]
        public void MotivoNoSePuede_ConPickingAdmitido_NoLoParaElPicking()
        {
            List<LinPedidoVta> lineas = new List<LinPedidoVta> { Linea() };

            Assert.IsNull(CambioClientePedido.MotivoNoSePuede(931691, false, lineas, 0, 0, new int[0], 0, admitirPicking: true));
            StringAssert.Contains(CambioClientePedido.MotivoNoSePuede(931691, false, lineas, 0, 0, new int[0], 0), "picking");
        }

        [TestMethod]
        public void MotivoNoSePuede_ConPickingAdmitido_ElEnvioDeAgenciaSigueParando()
        {
            Assert.IsNotNull(CambioClientePedido.MotivoNoSePuede(931691, false, new[] { Linea() }, 0, 0, new[] { 251000 }, 0, admitirPicking: true));
        }

        [TestMethod]
        public void Notificacion_ParaAlmacen_DiceQueElPickingSigueIgual()
        {
            NotificacionPushDTO n = ReglaCambioClienteConPicking.Notificacion("1", 931691, "37283 ", "0", "42178", "0", new[] { 100198, 100198 }, "NUEVAVISION\\Carlos");

            Assert.AreEqual("El pedido 931691 ha pasado del cliente 37283 al 42178", n.Titulo);
            StringAssert.Contains(n.Cuerpo, "el picking 100198");
            StringAssert.Contains(n.Cuerpo, "(Carlos)");
            Assert.AreEqual("100198", n.Datos["pickings"]);
        }

        [TestMethod]
        public void CambiosCabecera_SoloLoQueCambia()
        {
            CabPedidoVta cab = new CabPedidoVta { Nº_Cliente = "37283", Contacto = "0", IVA = "E52", Forma_Pago = "RCB", PlazosPago = "1/30" };
            FotoCabeceraCambioCliente antes = FotoCabeceraCambioCliente.De(cab);
            cab.Nº_Cliente = "42178";

            List<string> cambios = FotoCabeceraCambioCliente.Cambios(antes, FotoCabeceraCambioCliente.De(cab));

            CollectionAssert.AreEqual(new[] { "Cliente: 37283/0 → 42178/0" }, cambios.ToArray());
        }
    }
}
