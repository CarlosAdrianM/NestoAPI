using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class GeneradorPendientesTest
    {
        /*
        [TestMethod]
        public void GeneradorPendientes_Ejecutar_SiNoSaleEnPickingNoDivideLineas()
        {
            NVEntities db = new NVEntities();
            PedidoPicking pedido = new PedidoPicking { 
                Borrar = true // no sale en picking
            };
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "1",
                Cantidad = 2
            };
            pedido.Lineas = new List<LineaPedidoPicking>
            {
                linea
            };
            List<PedidoPicking> listaPedidos = new List<PedidoPicking> { pedido };
            LinPedidoVta linPedidoVta = new LinPedidoVta
            {
                Nº_Orden = 1,
                Cantidad = 2
            };
            db.LinPedidoVtas.Add(linPedidoVta);
            GeneradorPendientes generadorPendientes = new GeneradorPendientes(db, listaPedidos);

            generadorPendientes.Ejecutar();

            Assert.AreEqual(1, db.LinPedidoVtas.Local.Count);
        }
        */
    }
}
