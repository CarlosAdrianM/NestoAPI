using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

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

        // NestoAPI#540: en un pedido que no sale, lo que ya tiene sus unidades pasa de -1 a 1 y
        // solo se queda en -1 lo que falta.

        [TestMethod]
        public void GeneradorPendientes_PedidoQueNoSale_LaLineaConStockReservadoPasaDePendienteAEnCurso()
        {
            LinPedidoVta enBd = LineaBd(1, Constantes.EstadosLineaVenta.PENDIENTE);
            NVEntities db = DbCon(enBd);
            PedidoPicking pedido = PedidoQueNoSale(LineaPicking(1, cantidad: 2, reservada: 2));

            new GeneradorPendientes(db, new List<PedidoPicking> { pedido }).Ejecutar();

            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, enBd.Estado);
        }

        [TestMethod]
        public void GeneradorPendientes_PedidoQueNoSale_LaLineaReservadaSoloEnParteSigueEnPendiente()
        {
            LinPedidoVta enBd = LineaBd(1, Constantes.EstadosLineaVenta.PENDIENTE);
            enBd.Cantidad = 2;
            NVEntities db = DbCon(enBd);
            PedidoPicking pedido = PedidoQueNoSale(LineaPicking(1, cantidad: 2, reservada: 1));

            new GeneradorPendientes(db, new List<PedidoPicking> { pedido }).Ejecutar();

            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, enBd.Estado);
        }

        [TestMethod]
        public void GeneradorPendientes_PedidoQueSale_NoTocaElEstado()
        {
            // Si el pedido sale, el estado lo pone AsignadorPicking al asignar el número de picking
            LinPedidoVta enBd = LineaBd(1, Constantes.EstadosLineaVenta.PENDIENTE);
            NVEntities db = DbCon(enBd);
            PedidoPicking pedido = PedidoQueNoSale(LineaPicking(1, cantidad: 2, reservada: 2));
            pedido.Borrar = false;

            new GeneradorPendientes(db, new List<PedidoPicking> { pedido }).Ejecutar();

            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, enBd.Estado);
        }

        [TestMethod]
        public void GeneradorPendientes_PedidoQueNoSale_UnaCuentaContableNoPasaAEnCurso()
        {
            // Las cuentas contables se «reservan» enteras siempre: no dicen nada del stock
            LinPedidoVta enBd = LineaBd(1, Constantes.EstadosLineaVenta.PENDIENTE);
            NVEntities db = DbCon(enBd);
            LineaPedidoPicking cuenta = LineaPicking(1, cantidad: 1, reservada: 1);
            cuenta.TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE;
            PedidoPicking pedido = PedidoQueNoSale(cuenta);

            new GeneradorPendientes(db, new List<PedidoPicking> { pedido }).Ejecutar();

            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, enBd.Estado);
        }

        [TestMethod]
        public void GeneradorPendientes_PedidoQueNoSale_SiHayStockPeroLoNecesitaUnPedidoMasAntiguoSigueEnPendiente()
        {
            // El criterio de Carlos: no basta con que haya stock; tiene que haberlo para ESTE pedido
            // después de atender a los más antiguos. 1 unidad en stock, la pide antes otro pedido.
            LinPedidoVta enBd = LineaBd(2, Constantes.EstadosLineaVenta.PENDIENTE);
            NVEntities db = DbCon(enBd);
            LineaPedidoPicking delAntiguo = LineaPicking(1, cantidad: 1, reservada: 0, fechaModificacion: new DateTime(2026, 9, 1));
            LineaPedidoPicking deEste = LineaPicking(2, cantidad: 1, reservada: 0, fechaModificacion: new DateTime(2026, 9, 20));
            PedidoPicking antiguo = new PedidoPicking { Id = 1, Lineas = new List<LineaPedidoPicking> { delAntiguo } };
            PedidoPicking este = PedidoQueNoSale(deEste);
            este.Id = 2;
            var candidatos = new List<PedidoPicking> { antiguo, este };
            var stocks = new List<StockProducto> { new StockProducto { Producto = "P1", StockDisponible = 1 } };
            GestorReservasStock.Reservar(stocks, candidatos, new List<LineaPedidoPicking> { delAntiguo, deEste });

            new GeneradorPendientes(db, new List<PedidoPicking> { este }).Ejecutar();

            Assert.AreEqual(0, deEste.CantidadReservada);
            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, enBd.Estado);
        }

        [TestMethod]
        public void GeneradorPendientes_PedidoQueNoSale_ConStockParaLosDosLaDelMasNuevoTambienPasaAEnCurso()
        {
            LinPedidoVta enBd = LineaBd(2, Constantes.EstadosLineaVenta.PENDIENTE);
            NVEntities db = DbCon(enBd);
            LineaPedidoPicking delAntiguo = LineaPicking(1, cantidad: 1, reservada: 0, fechaModificacion: new DateTime(2026, 9, 1));
            LineaPedidoPicking deEste = LineaPicking(2, cantidad: 1, reservada: 0, fechaModificacion: new DateTime(2026, 9, 20));
            PedidoPicking antiguo = new PedidoPicking { Id = 1, Lineas = new List<LineaPedidoPicking> { delAntiguo } };
            PedidoPicking este = PedidoQueNoSale(deEste);
            este.Id = 2;
            var candidatos = new List<PedidoPicking> { antiguo, este };
            var stocks = new List<StockProducto> { new StockProducto { Producto = "P1", StockDisponible = 2 } };
            GestorReservasStock.Reservar(stocks, candidatos, new List<LineaPedidoPicking> { delAntiguo, deEste });

            new GeneradorPendientes(db, new List<PedidoPicking> { este }).Ejecutar();

            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, enBd.Estado);
        }

        // NestoAPI#542: en un pedido que sale y se factura «todo ahora», lo que GestorFacturarTodoAhora convirtió
        // pasa a LinPedidoVta.Recoger y la línea se queda en curso.

        [TestMethod]
        public void GeneradorPendientes_TodoAhora_PedidoQueSale_LoQueFaltaSeEscribeEnRecogerYLaLineaSigueEnCurso()
        {
            LinPedidoVta enBd = LineaBd(1, Constantes.EstadosLineaVenta.PENDIENTE);
            enBd.Cantidad = 5;
            enBd.Recoger = 1; // ya tenía 1 a recoger puesto a mano
            NVEntities db = DbCon(enBd);
            LineaPedidoPicking convertida = LineaPicking(1, cantidad: 2, reservada: 2); // de 4 quedaban 2 sin stock
            convertida.CantidadRecogida = 3;
            convertida.CantidadARecoger = 2;
            PedidoPicking pedido = PedidoQueNoSale(convertida);
            pedido.Borrar = false;
            pedido.ModoFacturacion = Constantes.Pedidos.ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

            new GeneradorPendientes(db, new List<PedidoPicking> { pedido }).Ejecutar();

            Assert.AreEqual(3, enBd.Recoger);
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, enBd.Estado);
            Assert.AreEqual(1, pedido.Lineas.Count, "La línea sigue en el picking para que le asignen número");
        }

        [TestMethod]
        public void GeneradorPendientes_TodoAhora_PedidoRetenido_NoEscribeRecoger()
        {
            LinPedidoVta enBd = LineaBd(1, Constantes.EstadosLineaVenta.EN_CURSO);
            enBd.Cantidad = 5;
            NVEntities db = DbCon(enBd);
            LineaPedidoPicking linea = LineaPicking(1, cantidad: 5, reservada: 3);
            linea.CantidadARecoger = 2;
            PedidoPicking pedido = PedidoQueNoSale(linea); // Borrar = true
            pedido.ModoFacturacion = Constantes.Pedidos.ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

            new GeneradorPendientes(db, new List<PedidoPicking> { pedido }).Ejecutar();

            Assert.AreEqual(0, enBd.Recoger);
        }

        private static PedidoPicking PedidoQueNoSale(params LineaPedidoPicking[] lineas)
        {
            return new PedidoPicking
            {
                Id = 1,
                Borrar = true,
                Lineas = lineas.ToList()
            };
        }

        private static LineaPedidoPicking LineaPicking(int id, int cantidad, int reservada, DateTime? fechaModificacion = null)
        {
            return new LineaPedidoPicking
            {
                Id = id,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "P1",
                Almacen = Constantes.Almacenes.ALGETE,
                Cantidad = cantidad,
                CantidadReservada = reservada,
                FechaModificacion = fechaModificacion ?? new DateTime(2026, 9, 1)
            };
        }

        private static LinPedidoVta LineaBd(int numeroOrden, short estado)
        {
            return new LinPedidoVta { Nº_Orden = numeroOrden, Cantidad = 1, Estado = estado };
        }

        private static NVEntities DbCon(params LinPedidoVta[] lineas)
        {
            IQueryable<LinPedidoVta> datos = lineas.AsQueryable();
            DbSet<LinPedidoVta> fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>());
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).Provider).Returns(datos.Provider);
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            return db;
        }
    }
}
