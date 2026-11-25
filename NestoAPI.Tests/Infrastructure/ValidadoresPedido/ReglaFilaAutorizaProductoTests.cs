using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    /// <summary>
    /// Carlos 25/11/26: una fila autoriza el descuento solo con los niveles del motor. Las filas del Outlet (Familia + Grupo + Subgrupo)
    /// autorizaban el 50 % en cualquier producto de PEL; una fila global de solo grupo tampoco autoriza (solo la del cliente).
    /// </summary>
    [TestClass]
    public class ReglaFilaAutorizaProductoTests
    {
        private const string CLIENTE = "206";

        private static Producto Lisap(string subgrupo = "TRA") => new Producto
        {
            Empresa = "1  ", Número = "43165", Nombre = "LISAP CHAMPU", PVP = 10M, Familia = "Lisap     ", Grupo = "PEL", SubGrupo = subgrupo, Aplicar_Dto = true
        };

        private static Producto Schwarzkopf() => new Producto
        {
            Empresa = "1", Número = "30001", Nombre = "IGORA ROYAL", PVP = 10M, Familia = "Schwarzkop", Grupo = "PEL", SubGrupo = "TIN", Aplicar_Dto = true
        };

        private static DescuentosProducto Outlet() =>
            new DescuentosProducto { Empresa = "1", Familia = "Schwarzkop", GrupoProducto = "PEL", SubGrupoProducto = "OUT", Descuento = 0.50M, Campana = "Outlet" };

        private static bool Casa(DescuentosProducto f, Producto p, params CategoriaProducto[] secundarias) =>
            ReglaFilaAutorizaProducto.Casa(f, p, secundarias, CLIENTE);

        [TestCleanup]
        public void Limpiar() => InterruptorDescuentosCriteriosEstrictos.Reiniciar();

        [TestMethod]
        public void Outlet_DeOtraFamilia_NoAutorizaUnLisapDePel()
        {
            Assert.IsFalse(Casa(Outlet(), Lisap()));
        }

        [TestMethod]
        public void Outlet_DeSuFamiliaConLaCategoriaSecundaria_Autoriza()
        {
            Assert.IsTrue(Casa(Outlet(), Schwarzkopf(), new CategoriaProducto("PEL ", "OUT")));
        }

        [TestMethod]
        public void Outlet_DeSuFamiliaSinEstarEnOutlet_NoAutoriza()
        {
            Assert.IsFalse(Casa(Outlet(), Schwarzkopf(), new CategoriaProducto("COS", "OUT")));
        }

        [TestMethod]
        public void Outlet_ConLaCategoriaPrincipal_Autoriza()
        {
            Producto p = Schwarzkopf();
            p.SubGrupo = "OUT";
            Assert.IsTrue(Casa(Outlet(), p));
        }

        [TestMethod]
        public void FamiliaGlobalSinGrupo_AutorizaCualquierLisap()
        {
            var fila = new DescuentosProducto { Familia = "Lisap", Descuento = 0.60M };

            Assert.IsTrue(Casa(fila, Lisap("TRA")));
            Assert.IsTrue(Casa(fila, Lisap("OUT")));
            Assert.IsFalse(Casa(fila, Schwarzkopf()));
        }

        [TestMethod]
        public void FamiliaYGrupoGlobal_SoloEseGrupoDeLaFicha()
        {
            var fila = new DescuentosProducto { Familia = "Lisap", GrupoProducto = "COS", Descuento = 0.60M };

            Assert.IsFalse(Casa(fila, Lisap()));
            fila.GrupoProducto = "PEL";
            Assert.IsTrue(Casa(fila, Lisap()));
        }

        [TestMethod]
        public void GrupoGlobalSinCliente_NoAutorizaNada()
        {
            var fila = new DescuentosProducto { GrupoProducto = "PEL", Descuento = 0.50M };

            Assert.IsFalse(Casa(fila, Lisap()));
        }

        [TestMethod]
        public void GrupoDelCliente_AutorizaAEseClienteYNoAOtro()
        {
            var fila = new DescuentosProducto { Nº_Cliente = CLIENTE + "       ", GrupoProducto = "PEL", Descuento = 0.50M };

            Assert.IsTrue(ReglaFilaAutorizaProducto.Casa(fila, Lisap(), null, CLIENTE));
            Assert.IsFalse(ReglaFilaAutorizaProducto.Casa(fila, Lisap(), null, "15000"));
            fila.GrupoProducto = "COS";
            Assert.IsFalse(ReglaFilaAutorizaProducto.Casa(fila, Lisap(), null, CLIENTE));
        }

        [TestMethod]
        public void FamiliaDelClienteConFiltro_SoloLosQueEmpiezanPorElFiltro()
        {
            var fila = new DescuentosProducto { Nº_Cliente = CLIENTE, Familia = "Lisap", FiltroProducto = "LISAP CH", Descuento = 0.30M };

            Assert.IsTrue(Casa(fila, Lisap()));
            Producto otro = Lisap();
            otro.Nombre = "LISAP TINTE";
            Assert.IsFalse(Casa(fila, otro));
        }

        [TestMethod]
        public void FamiliaGlobalConFiltro_NoAutoriza_ElMotorNoTieneEseNivel()
        {
            var fila = new DescuentosProducto { Familia = "Lisap", FiltroProducto = "LISAP", Descuento = 0.30M };

            Assert.IsFalse(Casa(fila, Lisap()));
        }

        [TestMethod]
        public void FilaDeProducto_SoloEseProducto()
        {
            var fila = new DescuentosProducto { Nº_Producto = "43165 ", Descuento = 0.25M, Campana = "Black Friday 2026" };

            Assert.IsTrue(Casa(fila, Lisap()));
            Assert.IsFalse(Casa(fila, Schwarzkopf()));
        }

        [TestMethod]
        public void FilaDeProveedor_NoAutoriza()
        {
            Assert.IsFalse(Casa(new DescuentosProducto { Familia = "Lisap", NºProveedor = "1", Descuento = 0.6M }, Lisap()));
        }

        [TestMethod]
        public void Interruptor_SoloCeroYParecidosLoApagan()
        {
            Assert.IsTrue(InterruptorDescuentosCriteriosEstrictos.Interpretar(null));
            Assert.IsTrue(InterruptorDescuentosCriteriosEstrictos.Interpretar("1"));
            Assert.IsTrue(InterruptorDescuentosCriteriosEstrictos.Interpretar(" "));
            Assert.IsFalse(InterruptorDescuentosCriteriosEstrictos.Interpretar("0"));
            Assert.IsFalse(InterruptorDescuentosCriteriosEstrictos.Interpretar("apagado"));
        }

        // ---------------------------------------------------------------- el validador con lo que deja pasar la regla

        private static RespuestaValidacion Validar(decimal descuento, params DescuentosProducto[] filasLeidas)
        {
            Producto producto = Lisap();
            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto(producto.Número)).Returns(producto);
            A.CallTo(() => servicio.BuscarOfertasPermitidas(A<string>._)).Returns(new List<OfertaPermitida>());
            A.CallTo(() => servicio.BuscarDescuentosPermitidos(producto.Número, CLIENTE, "0"))
                .Returns(ReglaFilaAutorizaProducto.Filtrar(filasLeidas, producto, null, CLIENTE));
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, contacto = "0" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto.Número,
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoProducto = descuento
            });
            return ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, servicio);
        }

        private static DescuentosProducto Campana25() =>
            new DescuentosProducto { Empresa = "1", Nº_Producto = "43165", CantidadMínima = 1, Descuento = 0.25M, Campana = "Black Friday 2026" };

        [TestMethod]
        public void CasoIsrael_CampanaMasFamiliaLisap_El52Pasa()
        {
            Assert.IsTrue(Validar(0.52M, Campana25(), new DescuentosProducto { Nº_Cliente = CLIENTE, Familia = "Lisap", Descuento = 0.60M }).ValidacionSuperada);
            Assert.IsTrue(Validar(0.52M, Campana25(), new DescuentosProducto { Familia = "Lisap", Descuento = 0.60M }).ValidacionSuperada);
        }

        [TestMethod]
        public void CasoIsrael_SoloCampanaYFilasDelOutletOGrupoGlobal_El52SeDeniega()
        {
            RespuestaValidacion r = Validar(0.52M, Campana25(), Outlet(), new DescuentosProducto { GrupoProducto = "PEL", Descuento = 0.60M });

            Assert.IsFalse(r.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 52,00 % para el producto 43165", r.Motivo);
        }
    }
}
