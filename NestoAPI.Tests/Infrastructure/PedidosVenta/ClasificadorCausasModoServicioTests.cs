using System;
using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using M = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#563 (Carlos, 29/09/26): el modo de servicio por CAUSAS del stock (libres en el almacén, tiendas, en camino
    /// del proveedor, sobre pedido, sin fecha) y no por los colores del correo.
    /// </summary>
    [TestClass]
    public class ClasificadorCausasModoServicioTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 29);

        private static DatosStockCausaModoServicio Datos(int stock = 0, int pendientes = 0, int? disponibleTodos = null, short estado = 0,
            int pendienteRecibir = 0, DateTime? fechaPrevista = null)
            => new DatosStockCausaModoServicio
            {
                StockAlmacen = stock,
                PendientesAlmacen = pendientes,
                DisponibleTodos = disponibleTodos ?? stock - pendientes,
                EstadoProducto = estado,
                PendienteRecibir = pendienteRecibir,
                FechaPrevista = fechaPrevista
            };

        private static PedidoVentaDTO Pedido(string almacen, params (string producto, short cantidad)[] lineas)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0", Lineas = new List<LineaPedidoVentaDTO>() };
            foreach (var (producto, cantidad) in lineas)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = producto, Cantidad = cantidad, almacen = almacen, tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            }
            return pedido;
        }

        private static IFuenteDatosCausasModoServicio Fuente(Dictionary<string, DatosStockCausaModoServicio> porProducto)
        {
            var fuente = A.Fake<IFuenteDatosCausasModoServicio>();
            A.CallTo(() => fuente.Leer(A<string>._, A<string>._))
                .ReturnsLazily((string producto, string almacen) => porProducto.TryGetValue(producto, out var d) ? d : Datos());
            return fuente;
        }

        // ---- Reparto de una línea ----

        [TestMethod]
        public void Linea_ConStockLibreSuficiente_TodoLibre()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 3, Datos(stock: 10, pendientes: 2), HOY);
            Assert.AreEqual(3, c.Libres);
            Assert.AreEqual(0, c.Faltan);
            Assert.AreEqual(CausaModoServicio.Libre, c.Principal);
        }

        [TestMethod]
        public void Linea_UnaDeDosYNadaMas_UnaLibreYUnaSinFecha()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 2, Datos(stock: 1), HOY);
            Assert.AreEqual(1, c.Libres);
            Assert.AreEqual(1, c.SinFecha);
            Assert.AreEqual(CausaModoServicio.SinFecha, c.Principal);
        }

        [TestMethod]
        public void Linea_FaltaEnAlmacenPeroHayEnTiendas_Tiendas()
        {
            // 1 libre en Algete y 3 más en las tiendas (disponible de todas las sedes = 4)
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 3, Datos(stock: 1, disponibleTodos: 4), HOY);
            Assert.AreEqual(1, c.Libres);
            Assert.AreEqual(2, c.Tiendas);
            Assert.AreEqual(CausaModoServicio.Tiendas, c.Principal);
        }

        [TestMethod]
        public void Linea_TiendasParcial_ElRestoSinFecha()
        {
            // Se piden 3, en las tiendas hay 2 y no hay nada pedido: el color sería rojo; la causa, tiendas + sin fecha
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 3, Datos(stock: 0, disponibleTodos: 2), HOY);
            Assert.AreEqual(2, c.Tiendas);
            Assert.AreEqual(1, c.SinFecha);
        }

        [TestMethod]
        public void Linea_PedidoAProveedorEnCamino_EnCaminoConFecha()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 2, Datos(pendienteRecibir: 6, fechaPrevista: HOY.AddDays(5)), HOY);
            Assert.AreEqual(2, c.EnCamino);
            Assert.AreEqual(HOY.AddDays(5), c.FechaPrevista);
            Assert.AreEqual(CausaModoServicio.EnCamino, c.Principal);
        }

        [TestMethod]
        public void Linea_EnCamino_PrimeroSeLoLlevanLosPedidosQueYaEsperan()
        {
            // Llegan 6, pero ya se deben 5 (disponible de todas las sedes = -5): para este pedido queda 1 de 3
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 3, Datos(stock: 0, pendientes: 5, disponibleTodos: -5, pendienteRecibir: 6, fechaPrevista: HOY.AddDays(3)), HOY);
            Assert.AreEqual(1, c.EnCamino);
            Assert.AreEqual(2, c.SinFecha);
        }

        [TestMethod]
        public void Linea_EnCaminoConFechaLejana_CuentaComoSinFecha()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 1,
                Datos(pendienteRecibir: 5, fechaPrevista: HOY.AddDays(ClasificadorCausasModoServicio.DIAS_MAXIMOS_EN_CAMINO + 1)), HOY);
            Assert.AreEqual(0, c.EnCamino);
            Assert.AreEqual(1, c.SinFecha);
        }

        [TestMethod]
        public void Linea_EnCaminoConFechaPasada_CuentaComoEnCamino()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 1, Datos(pendienteRecibir: 5, fechaPrevista: HOY.AddDays(-4)), HOY);
            Assert.AreEqual(1, c.EnCamino);
        }

        [TestMethod]
        public void Linea_ProductoSobrePedidoSinStock_SobrePedido()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 1, Datos(estado: Constantes.Productos.ESTADO_SOBRE_PEDIDO), HOY);
            Assert.AreEqual(1, c.SobrePedido);
            Assert.AreEqual(CausaModoServicio.SobrePedido, c.Principal);
        }

        [TestMethod]
        public void Linea_ProductoAExtinguirSinStock_SinFecha_NoSobrePedido()
        {
            var c = ClasificadorCausasModoServicio.ClasificarLinea("A", "ALG", 1, Datos(estado: Constantes.Productos.ESTADO_A_EXTINGUIR), HOY);
            Assert.AreEqual(0, c.SobrePedido);
            Assert.AreEqual(1, c.SinFecha);
        }

        // ---- Pedidos reales de la issue ----

        [TestMethod]
        public void Pedido927293_UnSoloProductoSinNingunaUnidadLibre_SoloTodoJunto()
        {
            // 45149 (diapasón, estado 0): 1 en Algete y 1 ya comprometida en otro pedido; nada pedido al proveedor.
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio> { ["45149"] = Datos(stock: 1, pendientes: 1) });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("45149", 1)), fuente, HOY);

            Assert.AreEqual(M.TODO_JUNTO, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO }, s.ModosPermitidos);
            Assert.AreEqual(CausaModoServicio.SinFecha, s.Lineas[0].Principal);
        }

        [TestMethod]
        public void Pedido927312_DosConStockYCincoSobrePedido_TodoJunto_NoAhoraLoQueHay()
        {
            // Presupuesto: 24092 y 23532 con stock de sobra en Algete; 35944, 35946, 34100, 33932 y 34101 en estado 1
            // (sobre pedido) sin unidades libres. Por colores salía «Ahora lo que hay» (2 verdes + 5 rojas); por causas,
            // lo que falta llega en días: todo junto.
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio>
            {
                ["24092"] = Datos(stock: 9, disponibleTodos: 13),
                ["23532"] = Datos(stock: 17, disponibleTodos: 24),
                ["35944"] = Datos(estado: 1),
                ["35946"] = Datos(stock: 1, pendientes: 1, estado: 1),
                ["34100"] = Datos(estado: 1),
                ["33932"] = Datos(estado: 1),
                ["34101"] = Datos(estado: 1)
            });
            var pedido = Pedido("ALG", ("24092", 1), ("23532", 1), ("35944", 1), ("35946", 1), ("34100", 1), ("33932", 1), ("34101", 1));

            var s = ClasificadorCausasModoServicio.Sugerir(pedido, fuente, HOY);

            Assert.AreEqual(M.TODO_JUNTO, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ }, s.ModosPermitidos);
            StringAssert.Contains(s.Motivo, "llegan del proveedor");
        }

        // ---- Regla del pedido ----

        [TestMethod]
        public void Pedido_StockParcialUnaDeDos_AhoraLoQueHay()
        {
            // #561 (matiz de Carlos): con 1 de 2 sí vale «Ahora lo que hay, el resto de una vez».
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio> { ["A"] = Datos(stock: 1) });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 2)), fuente, HOY);

            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ }, s.ModosPermitidos);
        }

        [TestMethod]
        public void Pedido_TodoLibre_SoloTodoJunto()
        {
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio> { ["A"] = Datos(stock: 5), ["B"] = Datos(stock: 5) });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 1), ("B", 2)), fuente, HOY);

            Assert.AreEqual(M.TODO_JUNTO, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO }, s.ModosPermitidos);
        }

        [TestMethod]
        public void Pedido_AlgoDeTiendas_TrasReponer_Todos()
        {
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio>
            {
                ["A"] = Datos(stock: 5),
                ["B"] = Datos(stock: 0, disponibleTodos: 3),
                ["C"] = Datos()
            });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 1), ("B", 1), ("C", 1)), fuente, HOY);

            Assert.AreEqual(M.TRAS_REPONER_DE_TIENDAS, s.Modo);
            Assert.AreEqual(4, s.ModosPermitidos.Count);
        }

        [TestMethod]
        public void Pedido_LibresYEnCamino_TodoJunto_SinTrasReponer()
        {
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio>
            {
                ["A"] = Datos(stock: 5),
                ["B"] = Datos(pendienteRecibir: 10, fechaPrevista: HOY.AddDays(4))
            });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 1), ("B", 1)), fuente, HOY);

            Assert.AreEqual(M.TODO_JUNTO, s.Modo);
            CollectionAssert.DoesNotContain(s.ModosPermitidos, M.TRAS_REPONER_DE_TIENDAS);
            StringAssert.Contains(s.Motivo, "03/10");
        }

        [TestMethod]
        public void Pedido_LibresSobrePedidoYSinFecha_AhoraLoQueHay()
        {
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio>
            {
                ["A"] = Datos(stock: 5),
                ["B"] = Datos(estado: 1),
                ["C"] = Datos()
            });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 1), ("B", 1), ("C", 1)), fuente, HOY);

            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, s.Modo);
        }

        [TestMethod]
        public void Pedido_NadaLibreSoloSobrePedido_SoloTodoJunto()
        {
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio> { ["A"] = Datos(estado: 1), ["B"] = Datos(estado: 1) });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 1), ("B", 1)), fuente, HOY);

            Assert.AreEqual(M.TODO_JUNTO, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO }, s.ModosPermitidos);
        }

        [TestMethod]
        public void Pedido_MismoProductoEnDosLineas_SeSumanLasCantidades()
        {
            // 3 libres; 2 + 2 = 4 pedidas: falta 1 (sin fecha) aunque cada línea por separado cabría
            var fuente = Fuente(new Dictionary<string, DatosStockCausaModoServicio> { ["A"] = Datos(stock: 3) });

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("ALG", ("A", 2), ("A", 2)), fuente, HOY);

            Assert.AreEqual(1, s.Lineas.Count);
            Assert.AreEqual(1, s.Lineas[0].SinFecha);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, s.Modo);
        }

        [TestMethod]
        public void Pedido_DeTienda_SoloSegunVayaEntrando_SinMirarStock()
        {
            var fuente = A.Fake<IFuenteDatosCausasModoServicio>();

            var s = ClasificadorCausasModoServicio.Sugerir(Pedido("REI", ("A", 1)), fuente, HOY);

            Assert.AreEqual(M.SEGUN_VAYA_ENTRANDO, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.SEGUN_VAYA_ENTRANDO }, s.ModosPermitidos);
            A.CallTo(() => fuente.Leer(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Pedido_SinLineasDeProducto_ModoPorDefectoYTodos()
        {
            var s = ClasificadorCausasModoServicio.Sugerir(new PedidoVentaDTO { Lineas = new List<LineaPedidoVentaDTO>() }, A.Fake<IFuenteDatosCausasModoServicio>(), HOY);

            Assert.AreEqual(M.POR_DEFECTO, s.Modo);
            Assert.AreEqual(4, s.ModosPermitidos.Count);
        }
    }
}
