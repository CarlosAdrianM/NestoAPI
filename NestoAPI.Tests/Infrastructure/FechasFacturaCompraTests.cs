using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models;
using NestoAPI.Models.PedidosCompra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#668 (caso real 19/10/26): con el IVA del 3T presentado, la regla «Remesa recibos» de la
    /// conciliación no pudo crear la factura de la comisión del banco (septiembre) porque las compras
    /// estaban cerradas hasta el 30/09. Antes de crear nada se comprueba la fecha y se propone la
    /// primera permitida; con FechaFactura, la factura va con ella y el pago con la del movimiento.
    /// </summary>
    [TestClass]
    public class FechasFacturaCompraTests
    {
        private const string EMPRESA = "1";
        private const string USUARIO = @"NUEVAVISION\Carlos";
        private static readonly DateTime FECHA_MOVIMIENTO = new DateTime(2026, 9, 30);
        private static readonly DateTime PRIMERA_FECHA_COMPRAS = new DateTime(2026, 10, 1);

        private IFechasPermitidasCompras fechasPermitidas;
        private NVEntities db;
        private ComprobadorFechaFacturaCompra comprobador;

        [TestInitialize]
        public void Setup()
        {
            fechasPermitidas = A.Fake<IFechasPermitidasCompras>();
            db = A.Fake<NVEntities>();
            comprobador = new ComprobadorFechaFacturaCompra(fechasPermitidas);
        }

        [TestMethod]
        public async Task Comprobar_FechaPermitida_EsValidaYNoBuscaPropuesta()
        {
            A.CallTo(() => fechasPermitidas.EsFechaCompraValida(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db)).Returns(true);

            ComprobacionFechaCompra resultado = await comprobador.Comprobar(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db);

            Assert.IsTrue(resultado.EsValida);
            Assert.IsNull(resultado.FechaPropuesta);
            A.CallTo(() => fechasPermitidas.LeerFechasCompra(A<string>._, A<string>._, A<NVEntities>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_ComprasCerradas_ProponeLaFechaInicialDeCompras()
        {
            A.CallTo(() => fechasPermitidas.EsFechaCompraValida(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db)).Returns(false);
            A.CallTo(() => fechasPermitidas.EsFechaCompraValida(EMPRESA, PRIMERA_FECHA_COMPRAS, USUARIO, db)).Returns(true);
            A.CallTo(() => fechasPermitidas.LeerFechasCompra(EMPRESA, USUARIO, db))
                .Returns(new FechasPermitidasCompra { FechaInicialCmp = PRIMERA_FECHA_COMPRAS, FechaFinalCmp = new DateTime(2026, 12, 31) });

            ComprobacionFechaCompra resultado = await comprobador.Comprobar(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db);

            Assert.IsFalse(resultado.EsValida);
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, resultado.FechaPropuesta);
        }

        [TestMethod]
        public async Task Comprobar_ComprasCerradasSinFilaDeFechas_NoHayPropuesta()
        {
            A.CallTo(() => fechasPermitidas.EsFechaCompraValida(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db)).Returns(false);
            A.CallTo(() => fechasPermitidas.LeerFechasCompra(EMPRESA, USUARIO, db)).Returns((FechasPermitidasCompra)null);

            ComprobacionFechaCompra resultado = await comprobador.Comprobar(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db);

            Assert.IsFalse(resultado.EsValida);
            Assert.IsNull(resultado.FechaPropuesta, "Sin propuesta, el controlador sigue y el SP da el error de siempre");
        }

        [TestMethod]
        public async Task Comprobar_LaPropuestaTampocoLaAceptaElSP_NoHayPropuesta()
        {
            // Nunca se ofrece una fecha con la que la factura volvería a fallar
            A.CallTo(() => fechasPermitidas.EsFechaCompraValida(EMPRESA, A<DateTime>._, USUARIO, db)).Returns(false);
            A.CallTo(() => fechasPermitidas.LeerFechasCompra(EMPRESA, USUARIO, db))
                .Returns(new FechasPermitidasCompra { FechaInicialCmp = PRIMERA_FECHA_COMPRAS, FechaFinalCmp = null });

            ComprobacionFechaCompra resultado = await comprobador.Comprobar(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db);

            Assert.IsFalse(resultado.EsValida);
            Assert.IsNull(resultado.FechaPropuesta);
        }

        [TestMethod]
        public async Task Comprobar_SiLaComprobacionFalla_NoBloqueaLaFactura()
        {
            // Sin el GRANT del script Issue668 la comprobación falla: se sigue como antes
            A.CallTo(() => fechasPermitidas.EsFechaCompraValida(A<string>._, A<DateTime>._, A<string>._, A<NVEntities>._))
                .Throws(new InvalidOperationException("Se denegó el permiso EXECUTE"));

            ComprobacionFechaCompra resultado = await comprobador.Comprobar(EMPRESA, FECHA_MOVIMIENTO, USUARIO, db);

            Assert.IsTrue(resultado.EsValida);
            Assert.IsNull(resultado.FechaPropuesta);
        }

        [TestMethod]
        public void CalcularFechaPropuesta_InicialPosteriorYDentroDelFinal_LaPropone()
        {
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, ComprobadorFechaFacturaCompra.CalcularFechaPropuesta(
                FECHA_MOVIMIENTO, PRIMERA_FECHA_COMPRAS, new DateTime(2026, 12, 31)));
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, ComprobadorFechaFacturaCompra.CalcularFechaPropuesta(
                FECHA_MOVIMIENTO, PRIMERA_FECHA_COMPRAS, null), "Sin fecha final, abierta");
        }

        [TestMethod]
        public void CalcularFechaPropuesta_SinPropuestaPosible_DevuelveNull()
        {
            // La inicial no es posterior: el cierre es por el final (o por otra causa), no hay hacia dónde mover
            Assert.IsNull(ComprobadorFechaFacturaCompra.CalcularFechaPropuesta(new DateTime(2026, 10, 5), PRIMERA_FECHA_COMPRAS, null));
            Assert.IsNull(ComprobadorFechaFacturaCompra.CalcularFechaPropuesta(PRIMERA_FECHA_COMPRAS, PRIMERA_FECHA_COMPRAS, null));
            // La inicial pasa del final: el periodo está vacío
            Assert.IsNull(ComprobadorFechaFacturaCompra.CalcularFechaPropuesta(FECHA_MOVIMIENTO, PRIMERA_FECHA_COMPRAS, new DateTime(2026, 9, 30)));
            // Sin fecha inicial
            Assert.IsNull(ComprobadorFechaFacturaCompra.CalcularFechaPropuesta(FECHA_MOVIMIENTO, null, null));
        }

        [TestMethod]
        public void Fechas_SinFechaFactura_TodoComoSiempre()
        {
            FechasFacturaCompra fechas = FechasFacturaCompra.Calcular(FECHA_MOVIMIENTO, null);
            PedidoCompraDTO pedido = CrearPedido();

            fechas.AplicarAlPedido(pedido);

            Assert.AreEqual(FECHA_MOVIMIENTO, fechas.FechaFactura);
            Assert.AreEqual(FECHA_MOVIMIENTO, fechas.FechaPago);
            Assert.IsNull(fechas.FechaAlbaran, "El albarán sigue con la fecha de hoy, como siempre");
            Assert.AreEqual(FECHA_MOVIMIENTO, pedido.Fecha);
            Assert.AreEqual(FECHA_MOVIMIENTO, pedido.PrimerVencimiento);
            Assert.AreEqual(FECHA_MOVIMIENTO, pedido.Lineas.Single().FechaRecepcion);
        }

        [TestMethod]
        public void Fechas_ConFechaFactura_FacturaEnEsaFechaYPagoEnLaDelMovimiento()
        {
            FechasFacturaCompra fechas = FechasFacturaCompra.Calcular(FECHA_MOVIMIENTO, PRIMERA_FECHA_COMPRAS);
            PedidoCompraDTO pedido = CrearPedido();

            fechas.AplicarAlPedido(pedido);
            fechas.AplicarAlPedido(pedido); // el reintento por deadlock vuelve a pasar por aquí

            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, fechas.FechaFactura);
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, fechas.FechaAlbaran);
            Assert.AreEqual(FECHA_MOVIMIENTO, fechas.FechaPago, "El pago, con la fecha del banco: la conciliación sigue cuadrando");
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, pedido.Fecha);
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, pedido.PrimerVencimiento, "El vencimiento no queda antes de la factura");
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, pedido.Lineas.Single().FechaRecepcion);
        }

        [TestMethod]
        public void FechaNoPermitida_RespuestaDel409_LlevaCodigoFechasYMensajeDeUsted()
        {
            var ex = new FechaCompraNoPermitidaException(FECHA_MOVIMIENTO, PRIMERA_FECHA_COMPRAS);

            FechaCompraNoPermitidaDTO dto = ex.ToDTO();

            Assert.AreEqual("FechaCompraNoPermitida", dto.Codigo);
            Assert.AreEqual(FECHA_MOVIMIENTO, dto.Fecha);
            Assert.AreEqual(PRIMERA_FECHA_COMPRAS, dto.FechaPropuesta);
            Assert.AreEqual("Las compras están cerradas hasta el 30/09/2026: no se puede contabilizar la factura con fecha " +
                "30/09/2026. La primera fecha permitida es el 01/10/2026.", dto.Message);
        }

        private static PedidoCompraDTO CrearPedido()
        {
            var pedido = new PedidoCompraDTO
            {
                Empresa = EMPRESA,
                Proveedor = "433",
                Fecha = FECHA_MOVIMIENTO,
                PrimerVencimiento = FECHA_MOVIMIENTO,
                Usuario = USUARIO
            };
            pedido.Lineas.Add(new LineaPedidoCompraDTO
            {
                TipoLinea = Constantes.TiposLineaCompra.CUENTA_CONTABLE,
                Producto = "62600000",
                Cantidad = 1,
                PrecioUnitario = 5.98M,
                FechaRecepcion = FECHA_MOVIMIENTO
            });
            return pedido;
        }
    }
}
