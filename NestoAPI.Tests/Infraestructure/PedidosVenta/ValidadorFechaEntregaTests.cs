using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#684 (pedido 928623, 19/10/26): al corregir la fecha de entrega 19/10/2027 se grabó 19/10/1926 en las 12
    /// líneas y 11 se sirvieron al día siguiente. Ninguna línea de venta puede grabarse con fecha de entrega anterior al
    /// 01/01/2000; las futuras (2099) siguen valiendo porque retienen líneas.
    /// </summary>
    [TestClass]
    public class ValidadorFechaEntregaTests
    {
        private static readonly DateTime FECHA_1926 = new DateTime(1926, 10, 19);
        private static readonly DateTime FECHA_2000 = new DateTime(2000, 1, 1);
        private static readonly DateTime FECHA_2099 = new DateTime(2099, 12, 31);

        private static LineaPedidoVentaDTO Linea(DateTime fechaEntrega, short estado = Constantes.EstadosLineaVenta.PENDIENTE)
        {
            return new LineaPedidoVentaDTO
            {
                id = 0,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "12345",
                Cantidad = 1,
                estado = estado,
                fechaEntrega = fechaEntrega
            };
        }

        private static PedidoVentaDTO Pedido(DateTime fechaEntrega)
        {
            return new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                numero = 928623,
                cliente = "15191",
                contacto = "0",
                Usuario = "NUEVAVISION\\carlos",
                fecha = DateTime.Today,
                formaPago = Constantes.FormasPago.TARJETA,
                plazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoVentaDTO> { Linea(fechaEntrega) }
            };
        }

        #region El validador

        [TestMethod]
        public void Validar_FechaDe1926_DevuelveElMensajeConProductoFechaYAnio()
        {
            string error = ValidadorFechaEntrega.Validar(new[] { Linea(FECHA_1926) });

            Assert.AreEqual("La fecha de entrega del producto 12345 (19/10/1926) no es válida: " +
                "no puede ser anterior al 01/01/2000. Revise el año.", error);
        }

        [TestMethod]
        public void Validar_ElPrimeroDeEnero2000_Vale()
        {
            Assert.IsNull(ValidadorFechaEntrega.Validar(new[] { Linea(FECHA_2000) }));
        }

        [TestMethod]
        public void Validar_FechaDe2099ParaRetenerLineas_Vale()
        {
            Assert.IsNull(ValidadorFechaEntrega.Validar(new[] { Linea(FECHA_2099) }));
        }

        [TestMethod]
        public void Validar_ElUltimoDiaDe1999_NoVale()
        {
            Assert.IsNotNull(ValidadorFechaEntrega.Validar(new[] { Linea(new DateTime(1999, 12, 31, 23, 59, 59)) }));
        }

        [TestMethod]
        public void Validar_FechaSinInformar_NoSeMira()
        {
            // CrearLineaVta convierte default(DateTime) en la primera fecha posible (GeneradorPortes y otros la dejan así)
            Assert.IsNull(ValidadorFechaEntrega.Validar(new[] { Linea(default) }));
        }

        [TestMethod]
        public void Validar_LineaYaEnAlbaranOFactura_NoSeMira()
        {
            // Hay 17 líneas históricas facturadas con 1899-1997; el PUT las conserva tal cual
            Assert.IsNull(ValidadorFechaEntrega.Validar(new[]
            {
                Linea(new DateTime(1931, 5, 4), Constantes.EstadosLineaVenta.ALBARAN),
                Linea(new DateTime(1899, 12, 30, 7, 0, 0), Constantes.EstadosLineaVenta.FACTURA)
            }));
        }

        [TestMethod]
        public void Validar_PresupuestoConFechaDe1926_TampocoVale()
        {
            Assert.IsNotNull(ValidadorFechaEntrega.Validar(new[] { Linea(FECHA_1926, Constantes.EstadosLineaVenta.PRESUPUESTO) }));
        }

        [TestMethod]
        public void Validar_SinLineas_NoRevienta()
        {
            Assert.IsNull(ValidadorFechaEntrega.Validar(null));
            Assert.IsNull(ValidadorFechaEntrega.Validar(new List<LineaPedidoVentaDTO>()));
        }

        [TestMethod]
        public void Comprobar_LineaDe1926_LanzaPedidoInvalidoConCodigoY400()
        {
            LinPedidoVta linea = new LinPedidoVta { Empresa = "1  ", Número = 928623, Producto = "12345  ", Fecha_Entrega = FECHA_1926 };

            PedidoInvalidoException ex = Assert.ThrowsException<PedidoInvalidoException>(() => ValidadorFechaEntrega.Comprobar(linea));

            Assert.AreEqual(ValidadorFechaEntrega.CODIGO_FECHA_ENTREGA_NO_VALIDA, ex.GetErrorCode());
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
            StringAssert.Contains(ex.Message, "producto 12345 (19/10/1926)");
            Assert.AreEqual(928623, ex.Context.Pedido);
        }

        [TestMethod]
        public void Comprobar_Lineas2000Y2099_NoLanzan()
        {
            ValidadorFechaEntrega.Comprobar(new LinPedidoVta { Producto = "12345", Fecha_Entrega = FECHA_2000 });
            ValidadorFechaEntrega.Comprobar(new LinPedidoVta { Producto = "12345", Fecha_Entrega = FECHA_2099 });
        }

        [TestMethod]
        public void HayQueComprobarAlGuardar_LineaNueva_Si()
        {
            Assert.IsTrue(ValidadorFechaEntrega.HayQueComprobarAlGuardar(EntityState.Added, () => false));
        }

        [TestMethod]
        public void HayQueComprobarAlGuardar_LineaModificadaConLaFechaCambiada_Si()
        {
            Assert.IsTrue(ValidadorFechaEntrega.HayQueComprobarAlGuardar(EntityState.Modified, () => true));
        }

        [TestMethod]
        public void HayQueComprobarAlGuardar_LineaModificadaSinTocarLaFecha_No()
        {
            // Una agrupación o un traspaso de una línea histórica de 1931 no puede romperse por esto
            Assert.IsFalse(ValidadorFechaEntrega.HayQueComprobarAlGuardar(EntityState.Modified, () => false));
            Assert.IsFalse(ValidadorFechaEntrega.HayQueComprobarAlGuardar(EntityState.Unchanged, () => true));
            Assert.IsFalse(ValidadorFechaEntrega.HayQueComprobarAlGuardar(EntityState.Deleted, () => true));
        }

        #endregion

        #region POST api/PedidosVenta

        [TestMethod]
        public async Task PostPedidoVenta_FechaEntregaDe1926_DevuelveBadRequestConElMensaje()
        {
            PedidosVentaController controller = new PedidosVentaController(A.Fake<NVEntities>());

            var resultado = await controller.PostPedidoVenta(Pedido(FECHA_1926));

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest, "Una fecha de entrega de 1926 tiene que dar 400 antes de crear nada");
            StringAssert.Contains(badRequest.Message, "19/10/1926");
            StringAssert.Contains(badRequest.Message, "Revise el año");
        }

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_FechasDe2000Y2099_Pasan()
        {
            Assert.IsNull(PedidosVentaController.ValidarDatosObligatoriosPedido(Pedido(FECHA_2000)));
            Assert.IsNull(PedidosVentaController.ValidarDatosObligatoriosPedido(Pedido(FECHA_2099)));
        }

        #endregion

        #region PUT api/PedidosVenta (el camino del pedido 928623: Nesto modificando la fecha de entrega)

        private static NVEntities DbSinPedidos()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> fakeCabs = A.Fake<DbSet<CabPedidoVta>>(o => o
                .Implements<IQueryable<CabPedidoVta>>()
                .Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            var vacio = new List<CabPedidoVta>().AsQueryable();
            A.CallTo(() => ((IQueryable<CabPedidoVta>)fakeCabs).Provider).Returns(vacio.Provider);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)fakeCabs).Expression).Returns(vacio.Expression);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)fakeCabs).ElementType).Returns(vacio.ElementType);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)fakeCabs).GetEnumerator()).Returns(vacio.GetEnumerator());
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeCabs);
            return db;
        }

        [TestMethod]
        public async Task PutPedidoVenta_FechaEntregaDe1926_DevuelveBadRequestSinLeerElPedido()
        {
            NVEntities db = DbSinPedidos();
            PedidoVentaDTO pedido = Pedido(FECHA_1926);
            pedido.Lineas.First().id = 4711;
            PedidosVentaController controller = new PedidosVentaController(db);

            var resultado = await controller.PutPedidoVenta(pedido);

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest, "Una fecha de entrega de 1926 tiene que dar 400 antes de tocar el pedido");
            StringAssert.Contains(badRequest.Message, "producto 12345 (19/10/1926)");
            A.CallTo(() => db.CabPedidoVtas).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutPedidoVenta_FechasDe2000Y2099_PasanLaValidacion()
        {
            // Pasan la fecha y siguen adelante: con la BD vacía, el PUT acaba en 404 (pedido inexistente), no en 400
            foreach (DateTime fecha in new[] { FECHA_2000, FECHA_2099 })
            {
                PedidosVentaController controller = new PedidosVentaController(DbSinPedidos());

                var resultado = await controller.PutPedidoVenta(Pedido(fecha));

                Assert.IsInstanceOfType(resultado, typeof(NotFoundResult), $"{fecha:dd/MM/yyyy}");
            }
        }

        #endregion
    }
}
