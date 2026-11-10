using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Feria online 12-16/11/26: los pedidos de la tienda online se validan con las ofertas vigentes el día del
    /// pedido (se pasan a Nesto uno o más días después). El resto de pedidos, con hoy.
    /// </summary>
    [TestClass]
    public class VigenciaPedidoTiendaReglaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 17);

        private static PedidoVentaDTO Pedido(bool deTienda, DateTime? fecha)
        {
            PedidoVentaDTO pedido = new PedidoVentaDTO
            {
                fecha = fecha,
                comentarios = deTienda ? "TOTAL PEDIDO: 60,00 €" : "Pedido de un vendedor"
            };
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "P1", Cantidad = 1, formaVenta = deTienda ? Constantes.FormasVenta.TIENDA_ONLINE : "VAR" });
            return pedido;
        }

        [TestMethod]
        public void DiaVigencia_PedidoTiendaDeAyer_EsElDiaDelPedido()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), VigenciaPedidoTienda.DiaVigencia(Pedido(true, new DateTime(2026, 11, 16, 23, 50, 0)), new DateTime(2026, 11, 16, 23, 50, 0), HOY));
        }

        [TestMethod]
        public void DiaVigencia_PedidoSinFormaDeTienda_EsHoy()
        {
            Assert.IsNull(VigenciaPedidoTienda.DiaVigencia(Pedido(false, new DateTime(2026, 11, 16)), new DateTime(2026, 11, 16), HOY));
        }

        [TestMethod]
        public void DiaVigencia_ComentarioDeTiendaPeroAlgunaLineaNoWeb_EsHoy()
        {
            PedidoVentaDTO pedido = Pedido(true, new DateTime(2026, 11, 16));
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "P2", Cantidad = 1, formaVenta = Constantes.FormasVenta.APP });
            Assert.IsNull(VigenciaPedidoTienda.DiaVigencia(pedido, new DateTime(2026, 11, 16), HOY));
        }

        [TestMethod]
        public void DiaVigencia_PedidoTiendaDeHoy_EsHoy()
        {
            Assert.IsNull(VigenciaPedidoTienda.DiaVigencia(Pedido(true, HOY), HOY, HOY));
        }

        [TestMethod]
        public void DiaVigencia_PedidoTiendaDeHaceMasDeNDias_EsHoy()
        {
            DateTime fecha = HOY.AddDays(-VigenciaPedidoTienda.DIAS_MAXIMOS_ATRAS - 1);
            Assert.IsNull(VigenciaPedidoTienda.DiaVigencia(Pedido(true, fecha), fecha, HOY));
        }

        [TestMethod]
        public void DiaVigencia_PedidoTiendaDeHaceJustoNDias_EsElDiaDelPedido()
        {
            DateTime fecha = HOY.AddDays(-VigenciaPedidoTienda.DIAS_MAXIMOS_ATRAS);
            Assert.AreEqual(fecha, VigenciaPedidoTienda.DiaVigencia(Pedido(true, fecha), fecha, HOY));
        }

        [TestMethod]
        public void DiaVigencia_PedidoTiendaConFechaFutura_EsHoy()
        {
            DateTime fecha = HOY.AddDays(2);
            Assert.IsNull(VigenciaPedidoTienda.DiaVigencia(Pedido(true, fecha), fecha, HOY));
        }

        [TestMethod]
        public void DiaVigencia_SinFecha_EsHoy()
        {
            Assert.IsNull(VigenciaPedidoTienda.DiaVigencia(Pedido(true, null), null, HOY));
        }

        [TestMethod]
        public void ServicioParaValidar_PedidoTiendaDeAyer_PideElServicioDelDiaDelPedido()
        {
            IServicioPrecios servicio = A.Fake<IServicioPrecios>(o => o.Implements<IServicioPreciosConDiaVigencia>());
            IServicioPrecios delDia = A.Fake<IServicioPrecios>();
            _ = A.CallTo(() => ((IServicioPreciosConDiaVigencia)servicio).ParaDia(new DateTime(2026, 11, 16))).Returns(delDia);

            IServicioPrecios resultado = VigenciaPedidoTienda.ServicioParaValidar(servicio, Pedido(true, new DateTime(2026, 11, 16)), new DateTime(2026, 11, 16), HOY);

            Assert.AreSame(delDia, resultado);
        }

        [TestMethod]
        public void ServicioParaValidar_PedidoNormal_NoCambiaElServicio()
        {
            IServicioPrecios servicio = A.Fake<IServicioPrecios>(o => o.Implements<IServicioPreciosConDiaVigencia>());

            IServicioPrecios resultado = VigenciaPedidoTienda.ServicioParaValidar(servicio, Pedido(false, new DateTime(2026, 11, 16)), new DateTime(2026, 11, 16), HOY);

            Assert.AreSame(servicio, resultado);
            A.CallTo(() => ((IServicioPreciosConDiaVigencia)servicio).ParaDia(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ServicioPrecios_ParaDia_LlevaElDiaSinHora()
        {
            ServicioPrecios delDia = (ServicioPrecios)new ServicioPrecios().ParaDia(new DateTime(2026, 11, 16, 18, 30, 0));
            Assert.AreEqual(new DateTime(2026, 11, 16), delDia.DiaVigencia);
            Assert.IsNull(new ServicioPrecios().DiaVigencia);
        }
    }

    /// <summary>
    /// La validación ENTERA (GestorPrecios.EsPedidoValido) con una «base de datos» en memoria que filtra la
    /// vigencia como ServicioPrecios: con hoy, o con el día que se le pida por ParaDia.
    /// Las fechas son relativas a hoy (el pedido es de AYER y la oferta acabó AYER = el 16/11 validado el 17/11).
    /// </summary>
    [TestClass]
    public class VigenciaPedidoTiendaValidacionTests
    {
        private static readonly DateTime HOY = DateTime.Today;
        private static readonly DateTime AYER = DateTime.Today.AddDays(-1);

        private IServicioPrecios _servicioAnterior;
        private List<OfertaPermitida> _ofertas;
        private List<DescuentosProducto> _descuentos;
        private List<OfertaCombinada> _combinadas;

        [TestInitialize]
        public void Inicializar()
        {
            _servicioAnterior = GestorPrecios.servicio;
            _ofertas = new List<OfertaPermitida>();
            _descuentos = new List<DescuentosProducto>();
            _combinadas = new List<OfertaCombinada>();

            IServicioPrecios servicio = A.Fake<IServicioPrecios>(o => o.Implements<IServicioPreciosConDiaVigencia>());
            Configurar(servicio, () => HOY);
            _ = A.CallTo(() => ((IServicioPreciosConDiaVigencia)servicio).ParaDia(A<DateTime>._))
                .ReturnsLazily((DateTime dia) =>
                {
                    IServicioPrecios delDia = A.Fake<IServicioPrecios>();
                    Configurar(delDia, () => dia);
                    return delDia;
                });
            GestorPrecios.servicio = servicio;
        }

        [TestCleanup]
        public void Limpiar()
        {
            GestorPrecios.servicio = _servicioAnterior;
        }

        private void Configurar(IServicioPrecios servicio, Func<DateTime> dia)
        {
            foreach (string numero in new[] { "FERIA_NM", "FERIA_DTO", "FERIA_C1", "FERIA_C2" })
            {
                string n = numero;
                _ = A.CallTo(() => servicio.BuscarProducto(n)).Returns(new Producto
                {
                    Número = n,
                    Nombre = "PRODUCTO " + n,
                    PVP = n == "FERIA_C2" ? 20 : 10,
                    Grupo = "COS",
                    SubGrupo = "CER",
                    Familia = "DeMarca",
                    Aplicar_Dto = true,
                    Estado = 0 // con Estado > 2 (o null) ValidadorRegalosTiendaOnline acepta cualquier línea web
                });
                _ = A.CallTo(() => servicio.BuscarOfertasPermitidas(n))
                    .ReturnsLazily(() => _ofertas.Where(o => o.Número == n && Vigencia.EsVigente(o, dia())).ToList());
                _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos(n, A<string>._, A<string>._))
                    .ReturnsLazily(() => _descuentos.Where(d => d.Nº_Producto == n && Vigencia.EsVigente(d, dia())).ToList());
                _ = A.CallTo(() => servicio.BuscarOfertasCombinadas(n))
                    .ReturnsLazily(() => _combinadas.Where(o => o.OfertasCombinadasDetalles.Any(d => d.Producto == n)
                        && (o.FechaHasta == null || o.FechaHasta >= dia()) && (o.FechaDesde == null || o.FechaDesde <= dia())).ToList());
                _ = A.CallTo(() => servicio.BuscarOfertasEscalonadas(n)).Returns(new List<OfertaEscalonada>());
                _ = A.CallTo(() => servicio.BuscarRegaloPorImportePedido(n)).Returns(new List<RegaloImportePedido>());
            }
            _ = A.CallTo(() => servicio.BuscarStockDisponibleParaRegalar(A<string>._, A<string>._)).Returns(999);
            _ = A.CallTo(() => servicio.BuscarIncompatibilidadesFamilia(A<string>._)).Returns(new List<FamiliaIncompatibilidad>());
        }

        private static PedidoVentaDTO Pedido(bool deTienda, DateTime fecha)
        {
            return new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                cliente = "5",
                contacto = "0",
                fecha = fecha,
                comentarios = deTienda ? "TOTAL PEDIDO: 60,00 €" : "Pedido de un vendedor"
            };
        }

        private static void Linea(PedidoVentaDTO pedido, string producto, int cantidad, decimal precio, bool deTienda)
        {
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                AplicarDescuento = false,
                formaVenta = deTienda ? Constantes.FormasVenta.TIENDA_ONLINE : "VAR",
                texto = "PRODUCTO " + producto
            });
        }

        private PedidoVentaDTO PedidoSeisMasUno(bool deTienda, DateTime fecha)
        {
            PedidoVentaDTO pedido = Pedido(deTienda, fecha);
            Linea(pedido, "FERIA_NM", 6, 10, deTienda);
            Linea(pedido, "FERIA_NM", 1, 0, deTienda);
            return pedido;
        }

        private void OfertaSeisMasUno(DateTime? desde, DateTime? hasta)
        {
            _ofertas.Add(new OfertaPermitida { Número = "FERIA_NM", CantidadConPrecio = 6, CantidadRegalo = 1, FechaDesde = desde, FechaHasta = hasta });
        }

        private PedidoVentaDTO PedidoVeintePorCiento(bool deTienda, DateTime fecha)
        {
            PedidoVentaDTO pedido = Pedido(deTienda, fecha);
            Linea(pedido, "FERIA_DTO", 1, 8, deTienda); // ficha 10 → 20 %
            return pedido;
        }

        private void DescuentoVeintePorCiento(DateTime? desde, DateTime? hasta)
        {
            _descuentos.Add(new DescuentosProducto { Nº_Producto = "FERIA_DTO", CantidadMínima = 0, Descuento = .2M, FechaDesde = desde, FechaHasta = hasta });
        }

        private PedidoVentaDTO PedidoCombinada(bool deTienda, DateTime fecha)
        {
            PedidoVentaDTO pedido = Pedido(deTienda, fecha);
            Linea(pedido, "FERIA_C1", 1, 10, deTienda);
            Linea(pedido, "FERIA_C2", 1, 0, deTienda);
            return pedido;
        }

        private void Combinada(DateTime? desde, DateTime? hasta)
        {
            _combinadas.Add(new OfertaCombinada
            {
                Id = 9001,
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                FechaDesde = desde,
                FechaHasta = hasta,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 9001, Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Producto = "FERIA_C1", Precio = 10, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 9001, Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Producto = "FERIA_C2", Precio = 0, Cantidad = 1 }
                }
            });
        }

        // --- N+M (OfertasPermitidas) ---

        [TestMethod]
        public void NM_PedidoTiendaDeAyer_OfertaQueAcaboAyer_Pasa()
        {
            OfertaSeisMasUno(AYER.AddDays(-4), AYER);
            Assert.IsTrue(GestorPrecios.EsPedidoValido(PedidoSeisMasUno(true, AYER)).ValidacionSuperada);
        }

        [TestMethod]
        public void NM_MismoPedidoSinFormaDeTienda_SeRechazaComoHoy()
        {
            OfertaSeisMasUno(AYER.AddDays(-4), AYER);
            Assert.IsFalse(GestorPrecios.EsPedidoValido(PedidoSeisMasUno(false, AYER)).ValidacionSuperada);
        }

        [TestMethod]
        public void NM_PedidoTiendaDeHaceMasDeNDias_SeRechazaComoHoy()
        {
            DateTime fecha = HOY.AddDays(-VigenciaPedidoTienda.DIAS_MAXIMOS_ATRAS - 1);
            OfertaSeisMasUno(fecha.AddDays(-4), fecha);
            Assert.IsFalse(GestorPrecios.EsPedidoValido(PedidoSeisMasUno(true, fecha)).ValidacionSuperada);
        }

        [TestMethod]
        public void NM_PedidoTiendaDeHoy_ComoHoy()
        {
            OfertaSeisMasUno(AYER.AddDays(-4), AYER);
            var r = GestorPrecios.EsPedidoValido(PedidoSeisMasUno(true, HOY)); Assert.IsFalse(r.ValidacionSuperada, r.Motivo + " / " + string.Join(" | ", r.Motivos));
        }

        [TestMethod]
        public void NM_PedidoTiendaDeHoy_OfertaVigenteHoy_Pasa()
        {
            OfertaSeisMasUno(AYER, HOY);
            Assert.IsTrue(GestorPrecios.EsPedidoValido(PedidoSeisMasUno(true, HOY)).ValidacionSuperada);
        }

        [TestMethod]
        public void NM_OfertaQueEmpiezaDespuesDelPedido_NoVale()
        {
            OfertaSeisMasUno(HOY, HOY.AddDays(3));
            Assert.IsFalse(GestorPrecios.EsPedidoValido(PedidoSeisMasUno(true, AYER)).ValidacionSuperada);
        }

        [TestMethod]
        public void NM_Modificacion_ConLaFechaDelPedidoGuardado_Pasa()
        {
            OfertaSeisMasUno(AYER.AddDays(-4), AYER);
            PedidoVentaDTO pedido = PedidoSeisMasUno(true, HOY); // el DTO del PUT puede traer otra fecha
            Assert.IsTrue(GestorPrecios.EsPedidoValidoConFechaDelPedido(pedido, AYER).ValidacionSuperada);
        }

        // --- % (DescuentosProducto) ---

        [TestMethod]
        public void Descuento_PedidoTiendaDeAyer_DescuentoQueAcaboAyer_Pasa()
        {
            DescuentoVeintePorCiento(AYER.AddDays(-4), AYER);
            Assert.IsTrue(GestorPrecios.EsPedidoValido(PedidoVeintePorCiento(true, AYER)).ValidacionSuperada);
        }

        [TestMethod]
        public void Descuento_MismoPedidoSinFormaDeTienda_SeRechazaComoHoy()
        {
            DescuentoVeintePorCiento(AYER.AddDays(-4), AYER);
            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(PedidoVeintePorCiento(false, AYER));
            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void Descuento_QueEmpiezaDespuesDelPedido_NoVale()
        {
            DescuentoVeintePorCiento(HOY, null);
            Assert.IsFalse(GestorPrecios.EsPedidoValido(PedidoVeintePorCiento(true, AYER)).ValidacionSuperada);
        }

        // --- Combinada ---

        [TestMethod]
        public void Combinada_PedidoTiendaDeAyer_CombinadaQueAcaboAyer_Pasa()
        {
            Combinada(AYER.AddDays(-4), AYER);
            Assert.IsTrue(GestorPrecios.EsPedidoValido(PedidoCombinada(true, AYER)).ValidacionSuperada);
        }

        [TestMethod]
        public void Combinada_MismoPedidoSinFormaDeTienda_SeRechazaComoHoy()
        {
            Combinada(AYER.AddDays(-4), AYER);
            Assert.IsFalse(GestorPrecios.EsPedidoValido(PedidoCombinada(false, AYER)).ValidacionSuperada);
        }
    }
}
