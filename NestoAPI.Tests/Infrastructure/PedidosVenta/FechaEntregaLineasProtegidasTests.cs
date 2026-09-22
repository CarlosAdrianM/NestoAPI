using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#508 (pedido 926673, 22/09/26): cambiar la fecha de entrega de una línea con picking se
    /// ignoraba en silencio, el correo salía con la fecha nueva y la línea de portes que añadía el PUT
    /// nacía en esa fecha, sola. Dos reglas puras: detectar el intento para rechazarlo, y que las
    /// líneas de cuenta contable hereden la fecha de la mercancía viva en BD.
    /// </summary>
    [TestClass]
    public class FechaEntregaLineasProtegidasTests
    {
        private static readonly DateTime Dia22 = new DateTime(2026, 9, 22);
        private static readonly DateTime Dia24 = new DateTime(2026, 9, 24);

        [TestMethod]
        public void CambiaFechaEntregaDeLineaConPicking_ConPickingYFechaDistinta_True()
        {
            // El caso real: picking 99600, BD 22/09, el cliente manda 24/09.
            Assert.IsTrue(GestorPedidosVenta.CambiaFechaEntregaDeLineaConPicking(
                tienePicking: true, estado: Constantes.EstadosLineaVenta.EN_CURSO, fechaBD: Dia22, fechaDto: Dia24));
        }

        [TestMethod]
        public void CambiaFechaEntregaDeLineaConPicking_MismaFechaAunqueCambieLaHora_False()
        {
            Assert.IsFalse(GestorPedidosVenta.CambiaFechaEntregaDeLineaConPicking(
                tienePicking: true, estado: Constantes.EstadosLineaVenta.EN_CURSO, fechaBD: Dia22, fechaDto: Dia22.AddHours(10)));
        }

        [TestMethod]
        public void CambiaFechaEntregaDeLineaConPicking_SinPicking_False()
        {
            // Sin picking la fecha se puede cambiar: ese camino lo lleva la comparación normal de campos.
            Assert.IsFalse(GestorPedidosVenta.CambiaFechaEntregaDeLineaConPicking(
                tienePicking: false, estado: Constantes.EstadosLineaVenta.PENDIENTE, fechaBD: Dia22, fechaDto: Dia24));
        }

        [DataTestMethod]
        [DataRow((short)2)]
        [DataRow((short)4)]
        public void CambiaFechaEntregaDeLineaConPicking_YaServidaOFacturada_False(short estado)
        {
            // Las servidas son historia: un cliente puede mandarlas con la fecha normalizada sin que
            // eso sea un intento de cambio.
            Assert.IsFalse(GestorPedidosVenta.CambiaFechaEntregaDeLineaConPicking(
                tienePicking: true, estado: estado, fechaBD: Dia22, fechaDto: Dia24));
        }

        [TestMethod]
        public void CambiaFechaEntregaDeLineaConPicking_FechaSinInformar_False()
        {
            Assert.IsFalse(GestorPedidosVenta.CambiaFechaEntregaDeLineaConPicking(
                tienePicking: true, estado: Constantes.EstadosLineaVenta.EN_CURSO, fechaBD: Dia22, fechaDto: default(DateTime)));
        }

        [TestMethod]
        public void FechaEntregaLineaCuentaContable_HayProductosVivos_LaMenorDeBD()
        {
            // Regresión 926673: los productos con picking siguen el 22 aunque el DTO diga 24.
            var lineasBD = new List<LinPedidoVta>
            {
                Producto(Constantes.EstadosLineaVenta.EN_CURSO, Dia24),
                Producto(Constantes.EstadosLineaVenta.EN_CURSO, Dia22),
                Producto(Constantes.EstadosLineaVenta.FACTURA, new DateTime(2026, 9, 1)) // una entrega vieja no cuenta
            };

            Assert.AreEqual(Dia22, GestorPortes.FechaEntregaLineaCuentaContable(lineasBD, fechaDto: Dia24));
        }

        [TestMethod]
        public void FechaEntregaLineaCuentaContable_PresupuestoVivo_TambienCuenta()
        {
            var lineasBD = new List<LinPedidoVta> { Producto(Constantes.EstadosLineaVenta.PRESUPUESTO, Dia22) };

            Assert.AreEqual(Dia22, GestorPortes.FechaEntregaLineaCuentaContable(lineasBD, fechaDto: Dia24));
        }

        [TestMethod]
        public void FechaEntregaLineaCuentaContable_SinProductosVivos_LaDelDTO()
        {
            var soloCuentas = new List<LinPedidoVta>
            {
                new LinPedidoVta { TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE, Estado = Constantes.EstadosLineaVenta.EN_CURSO, Fecha_Entrega = Dia22 }
            };

            Assert.AreEqual(Dia24, GestorPortes.FechaEntregaLineaCuentaContable(soloCuentas, fechaDto: Dia24));
            Assert.AreEqual(Dia24, GestorPortes.FechaEntregaLineaCuentaContable(new List<LinPedidoVta>(), fechaDto: Dia24));
            Assert.AreEqual(Dia24, GestorPortes.FechaEntregaLineaCuentaContable(null, fechaDto: Dia24));
        }

        private static LinPedidoVta Producto(short estado, DateTime fechaEntrega)
        {
            return new LinPedidoVta
            {
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "38669",
                Estado = estado,
                Fecha_Entrega = fechaEntrega
            };
        }
    }
}
