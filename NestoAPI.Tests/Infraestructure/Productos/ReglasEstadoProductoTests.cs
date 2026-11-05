using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models.Productos;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace NestoAPI.Tests.Infraestructure.Productos
{
    /// <summary>NestoAPI#738 (sugerencia 793, Manuel): reglas puras del cambio de estado de los productos.</summary>
    [TestClass]
    public class ReglasEstadoProductoTests
    {
        private static readonly EstadoProductoDTO DISPONIBLE = new EstadoProductoDTO { Estado = 0, Descripcion = "DISPONIBLE" };
        private static readonly EstadoProductoDTO SOBRE_PEDIDO = new EstadoProductoDTO { Estado = 1, Descripcion = "SOBRE PEDIDO" };
        private static readonly EstadoProductoDTO EXTINGUIR = new EstadoProductoDTO { Estado = 3, Descripcion = "EXTINGUIR", AExtinguir = true };
        private static readonly EstadoProductoDTO ANULAR = new EstadoProductoDTO { Estado = 4, Descripcion = "ANULAR", AAnular = true };

        private static DatosEstadoProducto Producto(short? estado, params ControlStockActual[] controles)
        {
            return new DatosEstadoProducto { Producto = "17404", Nombre = "ROLLO", Estado = estado, Controles = controles.ToList() };
        }

        private static ControlStockActual Control(string almacen, int maximo, int minimo = 0)
            => new ControlStockActual { Almacen = almacen, Maximo = maximo, Minimo = minimo };

        [TestMethod]
        public void Decidir_SaleDeDisponibleConControlesSinQuitarlos_SeOmiteConElMotivo()
        {
            DatosEstadoProducto datos = Producto(0, Control("REI", 3), Control("ALG", 10, 4), Control("ALC", 0));

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 3, EXTINGUIR, quitarControlesStock: false, sugeridos: null);

            Assert.IsTrue(d.Omitir);
            Assert.AreEqual("Tiene controles de stock en ALG y REI.", d.Motivo);
            CollectionAssert.AreEqual(new[] { "ALG", "REI" }, d.AlmacenesConControles);
            Assert.AreEqual(0, d.ControlesAQuitar.Count);
        }

        [TestMethod]
        public void Decidir_SaleDeDisponibleConControlesQuitandolos_SeCambiaYSeQuitan()
        {
            DatosEstadoProducto datos = Producto(0, Control("ALG", 10, 4), Control("REI", 3), Control("ALC", 2));

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 1, SOBRE_PEDIDO, quitarControlesStock: true, sugeridos: null);

            Assert.IsFalse(d.Omitir);
            CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALC" }, d.ControlesAQuitar);
            Assert.IsTrue(d.Avisos.Contains("Se quitarán sus controles de stock en ALG, REI y ALC."));
        }

        [TestMethod]
        public void Decidir_SaleDeDisponibleConControlesACero_NoCuentanComoControles()
        {
            DatosEstadoProducto datos = Producto(0, Control("ALG", 0), Control("REI", 0));

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 3, EXTINGUIR, quitarControlesStock: false, sugeridos: null);

            Assert.IsFalse(d.Omitir);
            Assert.AreEqual(0, d.AlmacenesConControles.Count);
        }

        [TestMethod]
        public void Decidir_SoloMinimoEnAlgete_CuentaComoControl()
        {
            DatosEstadoProducto datos = Producto(0, Control("ALG", 0, 2));

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 3, EXTINGUIR, quitarControlesStock: false, sugeridos: null);

            Assert.IsTrue(d.Omitir);
            Assert.AreEqual("Tiene controles de stock en ALG.", d.Motivo);
        }

        [TestMethod]
        public void Decidir_EntreEstadosQueNoSonDisponible_NoMiraLosControles()
        {
            DatosEstadoProducto datos = Producto(1, Control("ALG", 5));

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 4, ANULAR, quitarControlesStock: false, sugeridos: null);

            Assert.IsFalse(d.Omitir);
            Assert.AreEqual(0, d.ControlesAQuitar.Count);
        }

        [TestMethod]
        public void Decidir_VuelveADisponible_CreaSoloLosSugeridosConMaximo()
        {
            DatosEstadoProducto datos = Producto(3);
            List<ControlStockSugeridoDTO> sugeridos = new List<ControlStockSugeridoDTO>
            {
                new ControlStockSugeridoDTO { Almacen = "ALG", StockMaximo = 12, StockMinimo = 4 },
                new ControlStockSugeridoDTO { Almacen = "REI", StockMaximo = 0 },
                new ControlStockSugeridoDTO { Almacen = "ALC", StockMaximo = 2 }
            };

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 0, DISPONIBLE, quitarControlesStock: false, sugeridos: sugeridos);

            Assert.IsFalse(d.Omitir);
            CollectionAssert.AreEqual(new[] { "ALG", "ALC" }, d.ControlesACrear.Select(c => c.Almacen).ToList());
            Assert.IsFalse(d.VuelveADisponibleSinControles);
            Assert.IsTrue(d.Avisos.Contains("Se crearán sus controles de stock con los máximos sugeridos: ALG máx. 12 / mín. 4, ALC máx. 2."));
        }

        [TestMethod]
        public void Decidir_VuelveADisponibleSinMaximosSugeridos_VuelveSinControlesYAvisa()
        {
            DatosEstadoProducto datos = Producto(-1);
            List<ControlStockSugeridoDTO> sugeridos = new List<ControlStockSugeridoDTO>
            {
                new ControlStockSugeridoDTO { Almacen = "ALG", StockMaximo = 0, StockMinimo = 0 }
            };

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 0, DISPONIBLE, quitarControlesStock: false, sugeridos: sugeridos);

            Assert.IsFalse(d.Omitir);
            Assert.AreEqual(0, d.ControlesACrear.Count);
            Assert.IsTrue(d.VuelveADisponibleSinControles);
            Assert.IsTrue(d.Avisos.Contains(ReglasEstadoProducto.AVISO_SIN_CONTROLES));
        }

        [TestMethod]
        public void Decidir_EstadoNuloNoEsDisponible_AlPasarA0CreaControles()
        {
            DatosEstadoProducto datos = Producto(null);

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 0, DISPONIBLE, false,
                new List<ControlStockSugeridoDTO> { new ControlStockSugeridoDTO { Almacen = "ALG", StockMaximo = 5 } });

            Assert.AreEqual(1, d.ControlesACrear.Count);
        }

        [TestMethod]
        public void Decidir_MismoEstado_SeOmite()
        {
            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", Producto(3), 3, EXTINGUIR, true, null);

            Assert.IsTrue(d.Omitir);
            Assert.AreEqual("Ya está en estado 3.", d.Motivo);
        }

        [TestMethod]
        public void Decidir_ProductoQueNoExiste_SeOmite()
        {
            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("99999", null, 3, EXTINGUIR, true, null);

            Assert.IsTrue(d.Omitir);
            Assert.AreEqual(ReglasEstadoProducto.MOTIVO_NO_EXISTE, d.Motivo);
        }

        [TestMethod]
        public void Decidir_PasarAExtinguirConStockYPendienteDeRecibir_AvisaDeLasDosCosas()
        {
            DatosEstadoProducto datos = Producto(0);
            datos.Stock["ALG"] = 5;
            datos.Stock["ALC"] = 1;
            datos.PendienteRecibir = 10;

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 3, EXTINGUIR, false, null);

            Assert.IsTrue(d.QuedaStockOPendiente);
            Assert.IsTrue(d.Avisos.Contains("Le queda stock: ALG 5, ALC 1."));
            Assert.IsTrue(d.Avisos.Contains("Tiene 10 uds pendientes de recibir del proveedor."));
        }

        [TestMethod]
        public void Decidir_PasarASobrePedidoConStock_NoAvisaDelStock()
        {
            DatosEstadoProducto datos = Producto(0);
            datos.Stock["ALG"] = 5;

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 1, SOBRE_PEDIDO, false, null);

            Assert.IsFalse(d.QuedaStockOPendiente);
            Assert.AreEqual(0, d.Avisos.Count);
        }

        [TestMethod]
        public void Decidir_ConPedidosPendientes_AvisaConPedidosYUnidades()
        {
            DatosEstadoProducto datos = Producto(0);
            datos.PedidosPendientes.Add(new PedidoPendienteEstadoDTO { Pedido = 900001, Cliente = "15191", Cantidad = 2 });
            datos.PedidosPendientes.Add(new PedidoPendienteEstadoDTO { Pedido = 900002, Cliente = "10458", Cantidad = 3 });

            DecisionEstadoProducto d = ReglasEstadoProducto.Decidir("17404", datos, 4, ANULAR, false, null);

            Assert.IsTrue(d.TienePedidosPendientes);
            Assert.IsTrue(d.Avisos.Contains("Hay 2 pedidos de clientes pendientes de servir con este producto (5 uds)."));
        }

        [TestMethod]
        public void EsEstadoValido_SoloLosDeLaTabla()
        {
            List<EstadoProductoDTO> estados = new List<EstadoProductoDTO> { DISPONIBLE, SOBRE_PEDIDO, EXTINGUIR };

            Assert.IsTrue(ReglasEstadoProducto.EsEstadoValido(3, estados));
            Assert.IsFalse(ReglasEstadoProducto.EsEstadoValido(8, estados));
            Assert.IsFalse(ReglasEstadoProducto.EsEstadoValido(0, null));
        }

        [TestMethod]
        public void PuedeCambiar_ComprasYDireccion_SiConDominio_LosDemasNo()
        {
            Assert.IsTrue(ReglasEstadoProducto.PuedeCambiar(ConRoles("Compras")));
            Assert.IsTrue(ReglasEstadoProducto.PuedeCambiar(ConRoles("NUEVAVISION\\Dirección")));
            Assert.IsFalse(ReglasEstadoProducto.PuedeCambiar(ConRoles("Almacén", "Tiendas")));
            Assert.IsFalse(ReglasEstadoProducto.PuedeCambiar(null));
        }

        [TestMethod]
        public void PreguntaControlesStock_UnaVezPorLote_ConLosAlmacenesDeTodos()
        {
            List<DecisionEstadoProducto> decisiones = new List<DecisionEstadoProducto>
            {
                ReglasEstadoProducto.Decidir("1", Producto(0, Control("REI", 2)), 3, EXTINGUIR, true, null),
                ReglasEstadoProducto.Decidir("2", Producto(0, Control("ALG", 2)), 3, EXTINGUIR, true, null),
                ReglasEstadoProducto.Decidir("3", Producto(0), 3, EXTINGUIR, true, null)
            };

            Assert.AreEqual("2 productos tienen controles de stock en ALG y REI. ¿Los quito y cambio el estado, o no cambio el estado de esos productos?",
                ReglasEstadoProducto.PreguntaControlesStock(decisiones));
            Assert.IsNull(ReglasEstadoProducto.PreguntaControlesStock(decisiones.Skip(2).ToList()));
        }

        [TestMethod]
        public void AvisosDelLote_CuentaCadaCaso()
        {
            DatosEstadoProducto conPedido = Producto(0, Control("ALG", 2));
            conPedido.PedidosPendientes.Add(new PedidoPendienteEstadoDTO { Pedido = 1, Cantidad = 1 });
            List<DecisionEstadoProducto> decisiones = new List<DecisionEstadoProducto>
            {
                ReglasEstadoProducto.Decidir("1", conPedido, 3, EXTINGUIR, true, null),
                ReglasEstadoProducto.Decidir("2", Producto(3), 3, EXTINGUIR, true, null)
            };

            List<string> avisos = ReglasEstadoProducto.AvisosDelLote(decisiones);

            CollectionAssert.Contains(avisos, "1 producto tiene controles de stock: si no los quita, no se le cambiará el estado.");
            CollectionAssert.Contains(avisos, "1 producto tiene pedidos de clientes pendientes de servir.");
            CollectionAssert.Contains(avisos, "1 producto no se cambiará (ya está en ese estado o no existe).");
        }

        private static ClaimsPrincipal ConRoles(params string[] roles)
        {
            List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Manuel") };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"));
        }
    }
}
