using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#509: los predicados que dicen si un pedido «ya lleva» portes o comisión de reembolso.
    /// El PUT los usa para no añadir una segunda línea. La regresión: la línea de portes de un
    /// presupuesto está en PRESUPUESTO (-3) y el criterio antiguo (PENDIENTE..EN_CURSO) no la veía,
    /// así que cada guardado del presupuesto añadía otros portes («doble porte», Paloma 22/09/26).
    /// </summary>
    [TestClass]
    public class GestorPortesLineasVivasTests
    {
        private const string CUENTA_PORTES_CEX = "62400005";

        [TestMethod]
        public void EsLineaPortesViva_PortesEnPresupuesto_CuentaComoViva()
        {
            var linea = Portes(CUENTA_PORTES_CEX, "Portes Correos Express", Constantes.EstadosLineaVenta.PRESUPUESTO);

            Assert.IsTrue(GestorPortes.EsLineaPortesViva(linea), "Los portes de un presupuesto ya son los portes del pedido: no hay que añadir otros");
        }

        [DataTestMethod]
        [DataRow((short)-1)]
        [DataRow((short)1)]
        public void EsLineaPortesViva_PortesPendientesOEnCurso_CuentanComoVivos(short estado)
        {
            Assert.IsTrue(GestorPortes.EsLineaPortesViva(Portes(CUENTA_PORTES_CEX, "Portes", estado)));
        }

        [DataTestMethod]
        [DataRow((short)2)]
        [DataRow((short)4)]
        public void EsLineaPortesViva_PortesYaEntregadosOFacturados_NoCuentan(short estado)
        {
            // Ya se cobraron con su entrega; una entrega posterior decide sus propios portes.
            Assert.IsFalse(GestorPortes.EsLineaPortesViva(Portes(CUENTA_PORTES_CEX, "Portes", estado)));
        }

        [TestMethod]
        public void EsLineaPortesViva_ComisionReembolso_NoEsPortes()
        {
            // La comisión cuelga también de 624 (issue #159): se distingue por el texto.
            var reembolso = Portes(Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL, "Comisión contra reembolso", Constantes.EstadosLineaVenta.PENDIENTE);

            Assert.IsFalse(GestorPortes.EsLineaPortesViva(reembolso));
            Assert.IsTrue(GestorPortes.EsComisionReembolsoViva(reembolso));
        }

        [TestMethod]
        public void EsLineaPortesViva_ProductoOCuentaAjena_NoCuenta()
        {
            Assert.IsFalse(GestorPortes.EsLineaPortesViva(new LinPedidoVta
            {
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "62400005",
                Texto = "Portes",
                Estado = Constantes.EstadosLineaVenta.PENDIENTE
            }));
            Assert.IsFalse(GestorPortes.EsLineaPortesViva(Portes("70000000", "Otra cuenta", Constantes.EstadosLineaVenta.PENDIENTE)));
            Assert.IsFalse(GestorPortes.EsLineaPortesViva(Portes(null, "Portes", Constantes.EstadosLineaVenta.PENDIENTE)));
        }

        [TestMethod]
        public void EsComisionReembolsoViva_EnPresupuesto_CuentaComoViva()
        {
            // Misma regresión que los portes: la comisión de un presupuesto no puede duplicarse al modificarlo.
            var reembolso = Portes(Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL, "Comisión contra reembolso", Constantes.EstadosLineaVenta.PRESUPUESTO);

            Assert.IsTrue(GestorPortes.EsComisionReembolsoViva(reembolso));
        }

        private static LinPedidoVta Portes(string cuenta, string texto, short estado)
        {
            return new LinPedidoVta
            {
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = cuenta,
                Texto = texto,
                Estado = estado
            };
        }
    }
}
