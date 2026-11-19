using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Novedad 1365 (Carlos 19/11/26): en una nota de entrega se permiten los cambios que solo tocan la CABECERA (p. ej. «Recoger producto»);
    /// añadir, quitar o cambiar líneas sigue prohibido («No se puede ampliar una nota de entrega»).
    /// </summary>
    [TestClass]
    public class NotaEntregaSoloCabeceraTests
    {
        private static readonly DateTime ENTREGA = new DateTime(2026, 11, 20);

        private static CabPedidoVta Nota(short estado = Constantes.EstadosLineaVenta.PENDIENTE)
            => new CabPedidoVta
            {
                NotaEntrega = true,
                Nº_Cliente = "15339",
                Contacto = "0",
                IVA = "G21",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Estado = estado, Producto = "17404     ", Texto = "CREMA MANOS ", Cantidad = 2, Precio = 10m,
                        Descuento = 0.1m, DescuentoProducto = 0m, DescuentoPP = 0m, Aplicar_Dto = true, Fecha_Entrega = ENTREGA, Almacén = "ALG" },
                    new LinPedidoVta { Nº_Orden = 2, Estado = estado, Producto = "45929", Texto = "PACK", Cantidad = 1, Precio = 30m,
                        Fecha_Entrega = ENTREGA, Almacén = "ALG" }
                }
            };

        private static PedidoVentaDTO Espejo(CabPedidoVta cab)
        {
            var dto = new PedidoVentaDTO { cliente = "15339", contacto = "0", iva = "G21", DescuentoPP = 0m };
            foreach (LinPedidoVta l in cab.LinPedidoVtas)
            {
                dto.Lineas.Add(new LineaPedidoVentaDTO
                {
                    id = l.Nº_Orden, Producto = l.Producto.Trim(), texto = l.Texto.Trim(), Cantidad = l.Cantidad ?? 0, PrecioUnitario = l.Precio ?? 0,
                    DescuentoLinea = l.Descuento, DescuentoProducto = l.DescuentoProducto, AplicarDescuento = l.Aplicar_Dto, fechaEntrega = l.Fecha_Entrega,
                    almacen = l.Almacén
                });
            }
            return dto;
        }

        [TestMethod]
        public void Novedad1365_RecogerProducto_SoloCabecera_SePuede()
        {
            CabPedidoVta cab = Nota();
            PedidoVentaDTO dto = Espejo(cab);
            dto.comentarios = "Recoger el sillón viejo";

            Assert.IsTrue(PedidosVentaController.EsCambioSoloCabeceraEnNotaEntrega(cab, dto));
        }

        [TestMethod]
        public void ClienteViejo_FechaSinInformarYAlmacenVacio_NoCuentanComoCambio()
        {
            CabPedidoVta cab = Nota();
            PedidoVentaDTO dto = Espejo(cab);
            foreach (LineaPedidoVentaDTO l in dto.Lineas) { l.fechaEntrega = default; l.almacen = null; }

            Assert.IsTrue(PedidosVentaController.EsCambioSoloCabeceraEnNotaEntrega(cab, dto));
        }

        [DataTestMethod]
        [DataRow("nueva")]
        [DataRow("quitada")]
        [DataRow("cantidad")]
        [DataRow("precio")]
        [DataRow("producto")]
        [DataRow("descuento")]
        [DataRow("fecha")]
        [DataRow("cliente")]
        [DataRow("dtopp")]
        [DataRow("iva")]
        public void CualquierCambioDeLineas_NoEsSoloCabecera(string cambio)
        {
            CabPedidoVta cab = Nota();
            PedidoVentaDTO dto = Espejo(cab);
            switch (cambio)
            {
                case "nueva": dto.Lineas.Add(new LineaPedidoVentaDTO { id = 0, Producto = "17404", Cantidad = 1 }); break;
                case "quitada": dto.Lineas.Remove(dto.Lineas.Last()); break;
                case "cantidad": dto.Lineas.First().Cantidad = 3; break;
                case "precio": dto.Lineas.First().PrecioUnitario = 9m; break;
                case "producto": dto.Lineas.First().Producto = "17405"; break;
                case "descuento": dto.Lineas.First().DescuentoLinea = 0.2m; break;
                case "fecha": dto.Lineas.First().fechaEntrega = ENTREGA.AddDays(1); break;
                case "cliente": dto.cliente = "15191"; break;
                case "dtopp": dto.DescuentoPP = 0.02m; break;
                case "iva": dto.iva = null; break;
            }

            Assert.IsFalse(PedidosVentaController.EsCambioSoloCabeceraEnNotaEntrega(cab, dto), cambio);
        }

        [TestMethod]
        public void NotaYaProcesada_SoloCabecera_SePuedeModificar_PeroNoSiCambiaUnaLinea()
        {
            CabPedidoVta cab = Nota(Constantes.EstadosLineaVenta.NOTA_ENTREGA);
            PedidoVentaDTO dto = Espejo(cab);

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto));
            dto.Lineas.First().Cantidad = 5;
            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto));
        }

        [TestMethod]
        public void PedidoNormalConTodoEnNotaProcesadaSinMarca_NoCambiaNada()
        {
            CabPedidoVta cab = Nota(Constantes.EstadosLineaVenta.NOTA_ENTREGA);
            cab.NotaEntrega = false;

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, Espejo(cab)), "Solo las notas de entrega");
        }
    }
}
