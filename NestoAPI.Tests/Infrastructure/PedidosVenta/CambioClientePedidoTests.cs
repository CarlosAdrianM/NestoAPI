using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#519: cambiar el cliente de un pedido que todavía no ha salido. Las decisiones puras: cuándo se
    /// puede, de dónde sale cada dato de la cabecera y qué líneas cambian de precio.
    /// </summary>
    [TestClass]
    public class CambioClientePedidoTests
    {
        private static LinPedidoVta Linea(int orden, short estado = Constantes.EstadosLineaVenta.EN_CURSO, int picking = 0)
        {
            return new LinPedidoVta
            {
                Nº_Orden = orden,
                Producto = "AA11   ",
                Estado = estado,
                Picking = picking,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Cantidad = 2,
                Precio = 10
            };
        }

        private static string Motivo(IEnumerable<LinPedidoVta> lineas, bool notaEntrega = false, int prepagos = 0,
            int efectos = 0, IEnumerable<int> envios = null, int pagosTarjeta = 0)
        {
            return CambioClientePedido.MotivoNoSePuede(926000, notaEntrega, lineas, prepagos, efectos, envios ?? new int[0], pagosTarjeta);
        }

        #region MotivoNoSePuede

        [TestMethod]
        public void MotivoNoSePuede_LineasPendientesYEnCursoSinPicking_SePuede()
        {
            Assert.IsNull(Motivo(new[] { Linea(1, Constantes.EstadosLineaVenta.PENDIENTE), Linea(2), Linea(3, Constantes.EstadosLineaVenta.PRESUPUESTO) }));
        }

        [TestMethod]
        public void MotivoNoSePuede_UnaLineaConPicking_NoSePuedeYDiceCual()
        {
            string motivo = Motivo(new[] { Linea(1), Linea(2, picking: 55) });

            StringAssert.Contains(motivo, "picking");
            StringAssert.Contains(motivo, "2 (AA11)");
            StringAssert.Contains(motivo, "926000");
        }

        [TestMethod]
        public void MotivoNoSePuede_LineaEnAlbaran_NoSePuede()
        {
            StringAssert.Contains(Motivo(new[] { Linea(1), Linea(2, Constantes.EstadosLineaVenta.ALBARAN) }), "albarán");
        }

        [TestMethod]
        public void MotivoNoSePuede_LineaConNumeroDeAlbaranAunqueElEstadoNoLoDiga_NoSePuede()
        {
            LinPedidoVta linea = Linea(1);
            linea.Nº_Albarán = 123;

            StringAssert.Contains(Motivo(new[] { linea }), "albarán");
        }

        [TestMethod]
        public void MotivoNoSePuede_LineaFacturada_NoSePuede()
        {
            LinPedidoVta linea = Linea(1, Constantes.EstadosLineaVenta.FACTURA);
            linea.Nº_Factura = "NV26/001";

            StringAssert.Contains(Motivo(new[] { linea }), "facturada");
        }

        [TestMethod]
        public void MotivoNoSePuede_LineaYaFacturadaDeTodoAhora_NoSePuede()
        {
            // #542: lo pendiente de un «todo ahora» ya está facturado aunque la línea siga viva
            LinPedidoVta linea = Linea(1);
            linea.YaFacturado = true;

            StringAssert.Contains(Motivo(new[] { linea }), "facturada");
        }

        [TestMethod]
        public void MotivoNoSePuede_MasDeTresLineasConPicking_NombraTresYCuentaElResto()
        {
            string motivo = Motivo(Enumerable.Range(1, 5).Select(i => Linea(i, picking: 9)));

            StringAssert.Contains(motivo, "las líneas 1 (AA11), 2 (AA11), 3 (AA11) y 2 más");
        }

        [TestMethod]
        public void MotivoNoSePuede_NotaDeEntrega_NoSePuede()
        {
            StringAssert.Contains(Motivo(new[] { Linea(1) }, notaEntrega: true), "nota de entrega");
        }

        [TestMethod]
        public void MotivoNoSePuede_ConEnvioDeAgencia_NoSePuedeYDiceElEnvio()
        {
            StringAssert.Contains(Motivo(new[] { Linea(1) }, envios: new[] { 248944 }), "248944");
        }

        [TestMethod]
        public void MotivoNoSePuede_ConPrepagoVivo_NoSePuede()
        {
            StringAssert.Contains(Motivo(new[] { Linea(1) }, prepagos: 1), "prepagos");
        }

        [TestMethod]
        public void MotivoNoSePuede_ConCobroConTarjeta_NoSePuede()
        {
            StringAssert.Contains(Motivo(new[] { Linea(1) }, pagosTarjeta: 1), "tarjeta");
        }

        [TestMethod]
        public void MotivoNoSePuede_ConEfectosManuales_NoSePuede()
        {
            StringAssert.Contains(Motivo(new[] { Linea(1) }, efectos: 2), "efectos");
        }

        #endregion

        #region Ficha y condiciones

        [TestMethod]
        public void MotivoFichaNoValida_FichaActivaConNif_Vale()
        {
            Assert.IsNull(CambioClientePedido.MotivoFichaNoValida(new Cliente { Estado = 0, CIF_NIF = "B12345678" }, "20000", "0"));
        }

        [TestMethod]
        public void MotivoFichaNoValida_SinFicha_DiceQueNoExiste()
        {
            StringAssert.Contains(CambioClientePedido.MotivoFichaNoValida(null, "20000", "0"), "No existe el cliente 20000/0");
        }

        [TestMethod]
        public void MotivoFichaNoValida_DeBaja_NoVale()
        {
            StringAssert.Contains(CambioClientePedido.MotivoFichaNoValida(new Cliente { Estado = -1, CIF_NIF = "B1" }, "20000", null), "de baja");
        }

        [TestMethod]
        public void MotivoFichaNoValida_SinNif_NoVale()
        {
            StringAssert.Contains(CambioClientePedido.MotivoFichaNoValida(new Cliente { Estado = 0, CIF_NIF = " " }, "20000", null), "NIF");
        }

        [TestMethod]
        public void ResolverCondicionesPago_CogeLaDeMayorImporteMinimoQueNoSuperaElTotal()
        {
            List<CondPagoCliente> condiciones = new List<CondPagoCliente>
            {
                new CondPagoCliente { FormaPago = "EFC", PlazosPago = "CONTADO", ImporteMínimo = 0 },
                new CondPagoCliente { FormaPago = "RCB", PlazosPago = "30D", ImporteMínimo = 300 },
                new CondPagoCliente { FormaPago = "RCB", PlazosPago = "60D", ImporteMínimo = 1000 }
            };

            Assert.AreEqual("30D", CambioClientePedido.ResolverCondicionesPago(condiciones, 500).PlazosPago);
            Assert.AreEqual("CONTADO", CambioClientePedido.ResolverCondicionesPago(condiciones, 100).PlazosPago);
            Assert.IsNull(CambioClientePedido.ResolverCondicionesPago(new CondPagoCliente[0], 100));
        }

        #endregion

        #region Precios

        [TestMethod]
        public void SeRecalculaElPrecio_ProductoNormal_Si()
        {
            Assert.IsTrue(CambioClientePedido.SeRecalculaElPrecio(Linea(1)));
        }

        [TestMethod]
        public void SeRecalculaElPrecio_ConOferta_No()
        {
            LinPedidoVta linea = Linea(1);
            linea.NºOferta = 7;
            Assert.IsFalse(CambioClientePedido.SeRecalculaElPrecio(linea));
        }

        [TestMethod]
        public void SeRecalculaElPrecio_RegaloOConDescuentoDeLinea_No()
        {
            LinPedidoVta linea = Linea(1);
            linea.Descuento = 1;
            Assert.IsFalse(CambioClientePedido.SeRecalculaElPrecio(linea));
        }

        [TestMethod]
        public void SeRecalculaElPrecio_CuentaContableODevolucionOPrecioCero_No()
        {
            LinPedidoVta cuenta = Linea(1);
            cuenta.TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE;
            LinPedidoVta devolucion = Linea(2);
            devolucion.Cantidad = -1;
            LinPedidoVta gratis = Linea(3);
            gratis.Precio = 0;

            Assert.IsFalse(CambioClientePedido.SeRecalculaElPrecio(cuenta));
            Assert.IsFalse(CambioClientePedido.SeRecalculaElPrecio(devolucion));
            Assert.IsFalse(CambioClientePedido.SeRecalculaElPrecio(gratis));
        }

        [TestMethod]
        public void AplicarALinea_ConPrecioNuevo_CambiaClienteDescuentosYPrecio()
        {
            LinPedidoVta linea = Linea(1);
            linea.DescuentoProducto = 0.1M;

            bool cambia = CambioClientePedido.AplicarALinea(linea, "20000", "0", 0.05M, 0.02M,
                new ProductoPlantillaDTO { precio = 9.5M, descuento = 0.15M, aplicarDescuento = true });

            Assert.IsTrue(cambia);
            Assert.AreEqual("20000", linea.Nº_Cliente);
            Assert.AreEqual("0", linea.Contacto);
            Assert.AreEqual(0.05M, linea.DescuentoCliente);
            Assert.AreEqual(0.02M, linea.DescuentoPP);
            Assert.AreEqual(9.5M, linea.Precio);
            Assert.AreEqual(0.15M, linea.DescuentoProducto);
        }

        [TestMethod]
        public void AplicarALinea_SinPrecioNuevo_ConservaElPrecio()
        {
            LinPedidoVta linea = Linea(1);
            linea.NºOferta = 3;

            bool cambia = CambioClientePedido.AplicarALinea(linea, "20000", "0", 0, 0, null);

            Assert.IsFalse(cambia);
            Assert.AreEqual(10M, linea.Precio);
            Assert.AreEqual("20000", linea.Nº_Cliente);
        }

        #endregion

        #region Cabecera

        private static Cliente Ficha(bool mantenerJunto = false)
        {
            return new Cliente
            {
                Nº_Cliente = "20000",
                Contacto = "1  ",
                IVA = "R52",
                CCC = "2  ",
                PeriodoFacturación = "FDM",
                Ruta = "AT ",
                Vendedor = "JE ",
                NoComisiona = 0.5M,
                MantenerJunto = mantenerJunto
            };
        }

        private static CabPedidoVta Cabecera()
        {
            return new CabPedidoVta
            {
                Nº_Cliente = "10000",
                Contacto = "0  ",
                ContactoCobro = "0  ",
                IVA = "G21",
                Forma_Pago = "EFC",
                PlazosPago = "CONTADO",
                CCC = null,
                Primer_Vencimiento = new DateTime(2026, 10, 1),
                vtoBuenoPlazosPago = true,
                Periodo_Facturacion = "NRM",
                Ruta = "FW ",
                Vendedor = "NV ",
                ModoFacturacion = Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS
            };
        }

        [TestMethod]
        public void AplicarACabecera_TomaDeLaFichaLoQueDependeDelCliente()
        {
            CabPedidoVta cab = Cabecera();
            DateTime ahora = new DateTime(2026, 9, 25, 10, 0, 0);

            CambioClientePedido.AplicarACabecera(cab, Ficha(), new CondPagoCliente { FormaPago = "RCB", PlazosPago = "30D" },
                cccObligatorio: true, usuario: "NUEVAVISION\\lidia", ahora: ahora);

            Assert.AreEqual("20000", cab.Nº_Cliente);
            Assert.AreEqual("1  ", cab.Contacto);
            Assert.AreEqual("1  ", cab.ContactoCobro);
            Assert.AreEqual("R52", cab.IVA);
            Assert.AreEqual("RCB", cab.Forma_Pago);
            Assert.AreEqual("30D", cab.PlazosPago);
            Assert.AreEqual("2  ", cab.CCC);
            Assert.IsNull(cab.Primer_Vencimiento);
            Assert.IsFalse(cab.vtoBuenoPlazosPago);
            Assert.AreEqual("FDM", cab.Periodo_Facturacion);
            Assert.AreEqual("AT ", cab.Ruta);
            Assert.AreEqual("JE ", cab.Vendedor);
            Assert.AreEqual(0.5M, cab.NoComisiona);
            Assert.AreEqual("NUEVAVISION\\lidia", cab.Usuario);
            Assert.AreEqual(ahora, cab.Fecha_Modificación);
        }

        [TestMethod]
        public void AplicarACabecera_FormaDePagoSinCccObligatorio_QuitaElCcc()
        {
            CabPedidoVta cab = Cabecera();
            cab.CCC = "1  ";

            CambioClientePedido.AplicarACabecera(cab, Ficha(), new CondPagoCliente { FormaPago = "EFC", PlazosPago = "CONTADO" },
                cccObligatorio: false, usuario: "u", ahora: DateTime.Now);

            Assert.IsNull(cab.CCC);
        }

        [TestMethod]
        public void AplicarACabecera_FichaSinIvaNiRuta_PedidoSinIvaYConservaLaRuta()
        {
            CabPedidoVta cab = Cabecera();
            Cliente ficha = Ficha();
            ficha.IVA = "   ";
            ficha.Ruta = null;

            CambioClientePedido.AplicarACabecera(cab, ficha, new CondPagoCliente { FormaPago = "EFC", PlazosPago = "CONTADO" }, false, "u", DateTime.Now);

            Assert.IsNull(cab.IVA);
            Assert.AreEqual("FW ", cab.Ruta);
        }

        [TestMethod]
        public void AplicarACabecera_FichaMantenerJunto_FacturaAlCompletar()
        {
            CabPedidoVta cab = Cabecera();

            CambioClientePedido.AplicarACabecera(cab, Ficha(mantenerJunto: true), new CondPagoCliente { FormaPago = "EFC", PlazosPago = "CONTADO" }, false, "u", DateTime.Now);

            Assert.AreEqual(Constantes.Pedidos.ModosFacturacion.AL_COMPLETAR, cab.ModoFacturacion);
            Assert.IsTrue(cab.MantenerJunto);
        }

        [TestMethod]
        public void ModoFacturacionParaCliente_TodoAhoraElegidoEnElPedido_SeRespeta()
        {
            Assert.AreEqual(Constantes.Pedidos.ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES,
                CambioClientePedido.ModoFacturacionParaCliente(Constantes.Pedidos.ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, false, mantenerJuntoFicha: true));
        }

        [TestMethod]
        public void ModoFacturacionParaCliente_AlCompletarDelClienteAnterior_PasaAPorEntregasSiLaFichaNuevaNoMantieneJunto()
        {
            Assert.AreEqual(Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS,
                CambioClientePedido.ModoFacturacionParaCliente(Constantes.Pedidos.ModosFacturacion.AL_COMPLETAR, true, mantenerJuntoFicha: false));
        }

        #endregion

        #region DescribirCambios

        [TestMethod]
        public void DescribirCambios_CuentaCabeceraYPreciosQueCambian()
        {
            PedidoVentaDTO antes = new PedidoVentaDTO { cliente = "10000", contacto = "0", formaPago = "EFC", plazosPago = "CONTADO", iva = "G21" };
            antes.Lineas.Add(new LineaPedidoVentaDTO { id = 1, Producto = "AA11", Cantidad = 1, PrecioUnitario = 10 });
            antes.Lineas.Add(new LineaPedidoVentaDTO { id = 2, Producto = "BB22", Cantidad = 1, PrecioUnitario = 5 });
            PedidoVentaDTO despues = new PedidoVentaDTO { cliente = "20000", contacto = "0", formaPago = "RCB", plazosPago = "CONTADO", iva = "G21" };
            despues.Lineas.Add(new LineaPedidoVentaDTO { id = 1, Producto = "AA11", Cantidad = 1, PrecioUnitario = 9 });
            despues.Lineas.Add(new LineaPedidoVentaDTO { id = 2, Producto = "BB22", Cantidad = 1, PrecioUnitario = 5 });

            List<string> cambios = CambioClientePedido.DescribirCambios(antes, despues);

            CollectionAssert.Contains(cambios, "Cliente: 10000/0 → 20000/0");
            CollectionAssert.Contains(cambios, "Forma de pago: EFC → RCB");
            Assert.IsTrue(cambios.Any(c => c.StartsWith("Precio de AA11: 10,00 € → 9,00 €")), string.Join(" | ", cambios));
            Assert.IsFalse(cambios.Any(c => c.Contains("BB22")));
            Assert.IsFalse(cambios.Any(c => c.StartsWith("Plazos")));
        }

        [TestMethod]
        public void DescribirCambios_LineaDePortesNueva_SeCuenta()
        {
            PedidoVentaDTO antes = new PedidoVentaDTO { cliente = "10000", contacto = "0" };
            PedidoVentaDTO despues = new PedidoVentaDTO { cliente = "20000", contacto = "0" };
            despues.Lineas.Add(new LineaPedidoVentaDTO { id = 9, Producto = "62400003", texto = "Portes", Cantidad = 1, PrecioUnitario = 5 });

            List<string> cambios = CambioClientePedido.DescribirCambios(antes, despues);

            Assert.IsTrue(cambios.Any(c => c.StartsWith("Línea nueva: 62400003 Portes")), string.Join(" | ", cambios));
        }

        #endregion
    }
}
