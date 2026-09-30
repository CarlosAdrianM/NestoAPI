using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#570 (parte 2) contra la base de datos de verdad: la comprobación que impide facturar
    /// la devolución de una venta que sigue en albarán. Se busca un caso real en los datos (una venta
    /// en albarán de un producto que ese cliente no tiene facturado) y se monta en memoria el pedido
    /// de devolución; no se escribe nada. Solo con NESTO_TEST_BD.
    /// </summary>
    [TestClass]
    public class DevolucionDeVentaSinFacturarIntegracionTests
    {
        private const string EMPRESA = "1";

        private static CabPedidoVta Devolucion(string cliente, string producto)
        {
            return new CabPedidoVta
            {
                Empresa = EMPRESA,
                Número = int.MaxValue, // no existe: es un pedido en memoria
                Nº_Cliente = cliente,
                Serie = "NV",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Producto = producto, Cantidad = -1, Estado = Constantes.EstadosLineaVenta.ALBARAN }
                }
            };
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_DevolverLoQueEstaEnAlbaranSinFacturar_EncuentraEsaVenta()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                DateTime desde = DateTime.Today.AddDays(-45);
                var venta = db.LinPedidoVtas
                    .Where(l => l.Empresa == EMPRESA && l.Estado == Constantes.EstadosLineaVenta.ALBARAN
                        && l.Cantidad > 0 && l.TipoLinea == 1 && l.Fecha_Albarán >= desde
                        && !db.LinPedidoVtas.Any(f => f.Empresa == l.Empresa && f.Nº_Cliente == l.Nº_Cliente
                            && f.Producto == l.Producto && f.Estado == Constantes.EstadosLineaVenta.FACTURA && f.Cantidad > 0))
                    .Select(l => new { l.Número, l.Nº_Cliente, l.Producto })
                    .FirstOrDefault();
                if (venta == null)
                {
                    Assert.Inconclusive("Hoy no hay ninguna venta en albarán de un producto que el cliente no tenga facturado.");
                }

                var servicio = new ServicioFacturas(db);
                ServicioFacturas.VentaSinFacturar encontrada = await servicio
                    .VentaSinFacturarQueImpideLaDevolucion(EMPRESA, Devolucion(venta.Nº_Cliente, venta.Producto));

                Assert.IsNotNull(encontrada, $"Cliente {venta.Nº_Cliente.Trim()}, producto {venta.Producto.Trim()}");
                Assert.AreEqual(venta.Producto.Trim(), encontrada.Producto.Trim());
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_DevolverAlgoQueNoEstaEnNingunAlbaran_NoImpideNada()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                DateTime desde = DateTime.Today.AddDays(-45);
                // Una venta facturada hace poco de un producto que ese cliente no tiene en ningún albarán
                var venta = db.LinPedidoVtas
                    .Where(l => l.Empresa == EMPRESA && l.Estado == Constantes.EstadosLineaVenta.FACTURA
                        && l.Cantidad > 0 && l.TipoLinea == 1 && l.Fecha_Factura >= desde
                        && !db.LinPedidoVtas.Any(a => a.Empresa == l.Empresa && a.Nº_Cliente == l.Nº_Cliente
                            && a.Producto == l.Producto && a.Estado == Constantes.EstadosLineaVenta.ALBARAN))
                    .Select(l => new { l.Nº_Cliente, l.Producto })
                    .First();

                var servicio = new ServicioFacturas(db);
                Assert.IsNull(await servicio.VentaSinFacturarQueImpideLaDevolucion(EMPRESA, Devolucion(venta.Nº_Cliente, venta.Producto)));
            }
        }
    }
}
