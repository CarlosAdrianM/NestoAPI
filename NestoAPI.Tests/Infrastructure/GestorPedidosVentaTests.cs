using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorPedidosVentaTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 12345;

        #region RellenarEstadoProducto - Issue #299

        // #299: los clientes no envían EstadoProducto en el PUT/POST, así que el servidor debe
        // rellenarlo desde la ficha del producto antes de calcular la base de portes (#211).
        [TestMethod]
        public void RellenarEstadoProducto_CompletaLasLineasDeProductoDesdeLaFicha()
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);
            A.CallTo(() => servicio.LeerProducto(EMPRESA, "PROD1")).Returns(new Producto { Número = "PROD1", Estado = 0 });
            A.CallTo(() => servicio.LeerProducto(EMPRESA, "PROD4")).Returns(new Producto { Número = "PROD4", Estado = 4 });
            var pedido = new PedidoVentaDTO { empresa = EMPRESA, numero = PEDIDO };
            var lineaNormal = new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "PROD1", estado = 1 };
            var lineaSobrePedido = new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "PROD4", estado = -1 };
            var lineaCuenta = new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE, Producto = "62400002", estado = 1 };
            pedido.Lineas.Add(lineaNormal);
            pedido.Lineas.Add(lineaSobrePedido);
            pedido.Lineas.Add(lineaCuenta);

            gestor.RellenarEstadoProducto(pedido);

            Assert.AreEqual((short)0, lineaNormal.EstadoProducto);
            Assert.AreEqual((short)4, lineaSobrePedido.EstadoProducto);
            Assert.IsNull(lineaCuenta.EstadoProducto, "Las cuentas contables no tienen estado de producto");
        }

        [TestMethod]
        public void RellenarEstadoProducto_ProductoInexistente_DejaNull()
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);
            A.CallTo(() => servicio.LeerProducto(EMPRESA, "NOEXISTE")).Returns(null);
            var pedido = new PedidoVentaDTO { empresa = EMPRESA, numero = PEDIDO };
            var linea = new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "NOEXISTE", estado = 1 };
            pedido.Lineas.Add(linea);

            gestor.RellenarEstadoProducto(pedido);

            Assert.IsNull(linea.EstadoProducto);
        }

        #endregion

        #region ImporteReembolso - Issue #250

        [TestMethod]
        public void ImporteReembolso_ConEfectosManualesEFC_DevuelveSumaEfectosEFC()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "TRN" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Efectos manuales: 909.75 TRN + 499.80 EFC (caso real del issue)
            var efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 909.75m, FormaPago = "TRN" },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 499.80m, FormaPago = Constantes.FormasPago.EFECTIVO }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(efectos);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Solo debe devolver el importe de los efectos con EFC
            Assert.AreEqual(499.80m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_ConEfectosManualesSinEFC_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "EFC" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Efectos manuales: todos son TRN, ninguno EFC
            var efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 500m, FormaPago = "TRN" },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 300m, FormaPago = "RCB" }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(efectos);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Aunque la cabecera diga EFC, si hay efectos manuales sin EFC, devuelve 0
            Assert.AreEqual(0m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_SinEfectosManualesYFormaPagoEFC_DevuelveTotalLineas()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "EFC" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Sin efectos manuales
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(new List<EfectoPedidoVenta>());

            // Lineas con picking
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Total = 100m },
                new LinPedidoVta { Total = 50m }
            };
            A.CallTo(() => servicio.CargarLineasPedidoSinPicking(PEDIDO)).Returns(lineas);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Comportamiento original: suma las lineas
            Assert.AreEqual(150m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_SinEfectosManualesYFormaPagoTRN_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "TRN" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Sin efectos manuales
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(new List<EfectoPedidoVenta>());

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Comportamiento original: TRN = sin reembolso
            Assert.AreEqual(0m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_ConMultiplesEfectosEFC_DevuelveSumaTotal()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "TRN" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Multiples efectos EFC
            var efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 100m, FormaPago = Constantes.FormasPago.EFECTIVO },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 200m, FormaPago = Constantes.FormasPago.EFECTIVO },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 50m, FormaPago = "TRN" }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(efectos);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert
            Assert.AreEqual(300m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_PedidoNoExiste_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns((CabPedidoVta)null);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert
            Assert.AreEqual(0m, resultado);
        }

        #endregion

        #region EsLineaPortesOReembolso - Issue #346

        [TestMethod]
        public void EsLineaPortesOReembolso_LineaDeProducto_DevuelveFalse()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AB01"
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContable624_DevuelveTrue()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "62400017"
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContable624ConEspacios_DevuelveTrue()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "  62400005   "
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaReembolso_DevuelveTrue()
        {
            // Tras el refactor a 62400000, la cuenta reembolso es un 624xxx y se considera
            // portes/reembolso sin depender del texto.
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso"
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContableNo624_DevuelveFalse()
        {
            // Cuentas contables ajenas a transporte (p. ej. 70000000 ventas, 75900000 ingresos
            // diversos) no deben tratarse como portes aunque el texto mencione "reembolso".
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "70000000",
                texto = "Venta normal"
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContable624PeroTipoLineaProducto_DevuelveFalse()
        {
            // Protege de un caso raro: si alguien pone producto 624xxx en tipoLinea PRODUCTO
            // no queremos que se considere portes.
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "62400017"
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_ProductoNull_DevuelveFalse()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = null
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_TextoReembolsoMayusculas_DevuelveTrue()
        {
            // La detección de "reembolso" debe ser case-insensitive.
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "COMISION REEMBOLSO"
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        #endregion

        #region MoverLineasAmpliacionAPedidoOriginal - Issue #346

        private static LineaPedidoVentaDTO NuevaLineaProducto(string producto, short estado = Constantes.EstadosLineaVenta.PENDIENTE, int id = 0)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                estado = estado
            };
        }

        private static LineaPedidoVentaDTO NuevaLineaPortes(string cuenta = "62400017", short estado = Constantes.EstadosLineaVenta.EN_CURSO, int id = 100)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = cuenta,
                estado = estado
            };
        }

        private static LineaPedidoVentaDTO NuevaLineaReembolso(short estado = Constantes.EstadosLineaVenta.EN_CURSO, int id = 200)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso",
                estado = estado
            };
        }

        private static PedidoVentaDTO NuevoPedidoDto(params LineaPedidoVentaDTO[] lineas)
        {
            return new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>(lineas)
            };
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineasDeProducto_SeMuevenAlOriginal()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B"), NuevaLineaProducto("C"));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            Assert.AreEqual(3, original.Lineas.Count);
            Assert.AreEqual(0, ampliacion.Lineas.Count);
            CollectionAssert.AreEquivalent(
                new[] { "A", "B", "C" },
                original.Lineas.Select(l => l.Producto).ToList());
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineaPortesEnAmpliacion_NoSeMueve()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"), NuevaLineaPortes("62400005"));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B"), NuevaLineaPortes("62400017"));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            // Solo la línea de producto B se ha movido; la de portes del ampliación se ha descartado.
            int lineasDePortesEnOriginal = original.Lineas.Count(l => l.Producto.StartsWith("624"));
            Assert.AreEqual(1, lineasDePortesEnOriginal, "El pedido original no debe quedar con portes duplicados");
            Assert.IsTrue(original.Lineas.Any(l => l.Producto == "B"));
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineaReembolsoEnAmpliacion_NoSeMueve()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B"), NuevaLineaReembolso());

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            Assert.IsFalse(original.Lineas.Any(l => l.texto?.ToLower().Contains("reembolso") == true));
            Assert.AreEqual(2, original.Lineas.Count);
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineasEnEstadoFacturado_NoSeMueven()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"));
            var ampliacion = NuevoPedidoDto(
                NuevaLineaProducto("B", estado: Constantes.EstadosLineaVenta.PENDIENTE),
                NuevaLineaProducto("C", estado: Constantes.EstadosLineaVenta.FACTURA));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            Assert.IsTrue(original.Lineas.Any(l => l.Producto == "B"));
            Assert.IsFalse(original.Lineas.Any(l => l.Producto == "C"));
            Assert.IsTrue(ampliacion.Lineas.Any(l => l.Producto == "C"));
        }

        [TestMethod]
        public void MoverLineasAmpliacion_OriginalEsPresupuesto_LineasMovidasQuedanEnPresupuesto()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A", estado: Constantes.EstadosLineaVenta.PRESUPUESTO));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B", estado: Constantes.EstadosLineaVenta.PENDIENTE));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            var lineaMovida = original.Lineas.Single(l => l.Producto == "B");
            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO, lineaMovida.estado);
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineaMovida_PierdeId()
        {
            // Las líneas movidas deben insertarse como nuevas en el pedido original,
            // así que su id debe resetearse a 0 antes del save.
            var original = NuevoPedidoDto(NuevaLineaProducto("A", id: 1));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B", id: 42));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            var lineaMovida = original.Lineas.Single(l => l.Producto == "B");
            Assert.AreEqual(0, lineaMovida.id);
        }

        #endregion

        #region UnirPedidos preserva PedidoValidacionException - NestoAPI#216

        [TestMethod]
        public void UnirPedidos_SiLaValidacionDelPedidoUnidoFalla_PropagaPedidoValidacionExceptionConSuCodigo()
        {
            // Regresión NestoAPI#216: al unir pedidos, si el pedido resultante no pasa la validación,
            // PutPedidoVenta lanza PedidoValidacionException (código PEDIDO_VALIDACION_FALLO). Antes
            // UnirPedidos lo envolvía en una Exception genérica y se perdía el código, así que el cliente
            // NO ofrecía "¿Crear el pedido de todas formas?" (sí lo ofrece al crear un pedido normal).
            var servicio = A.Fake<IServicioPedidosVenta>();
            var pedidoValidacion = new PedidoValidacionException(
                "No se encuentra autorización para la oferta del producto 380",
                new RespuestaValidacion { ValidacionSuperada = false });
            var gestor = new GestorUnirPedidosConFallo(servicio, pedidoValidacion);

            PedidoValidacionException ex = Assert.ThrowsException<PedidoValidacionException>(() =>
                gestor.UnirPedidos(NuevoPedidoDto(), NuevoPedidoDto()).GetAwaiter().GetResult());

            Assert.AreEqual("PEDIDO_VALIDACION_FALLO", ex.Context.ErrorCode);
        }

        [TestMethod]
        public void UnirPedidos_SiFallaPorOtroMotivo_SeSigueEnvolviendoEnExceptionGenerica()
        {
            // El resto de errores se siguen aplanando a Exception(message) como antes (comportamiento
            // intacto): solo las NestoBusinessException se dejan pasar sin envolver.
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorUnirPedidosConFallo(servicio, new InvalidOperationException("boom"));

            Exception ex = Assert.ThrowsException<Exception>(() =>
                gestor.UnirPedidos(NuevoPedidoDto(), NuevoPedidoDto()).GetAwaiter().GetResult());

            Assert.IsFalse(ex is NestoBusinessException);
            Assert.AreEqual("boom", ex.Message);
        }

        [TestMethod]
        public void UnirPedidos_TransactionAborted_ConservaLaCausaRaizEnMensajeYCadena()
        {
            // Regresión NestoAPI#274: la causa real del abort (deadlock, timeout del scope, DTC) viene
            // en la cadena de InnerException. Antes se envolvía con new Exception(ex.Message) y ELMAH
            // solo registraba "Se anuló la transacción", indiagnosticable. Ahora la causa raíz va en
            // el mensaje y la cadena se conserva.
            var servicio = A.Fake<IServicioPedidosVenta>();
            var abort = new System.Transactions.TransactionAbortedException(
                "Se anuló la transacción.",
                new TimeoutException("Transaction Timeout"));
            var gestor = new GestorUnirPedidosConFallo(servicio, abort);

            Exception ex = Assert.ThrowsException<Exception>(() =>
                gestor.UnirPedidos(NuevoPedidoDto(), NuevoPedidoDto()).GetAwaiter().GetResult());

            StringAssert.Contains(ex.Message, "Se anuló la transacción.");
            StringAssert.Contains(ex.Message, "Causa: Transaction Timeout");
            Assert.IsInstanceOfType(ex.GetBaseException(), typeof(TimeoutException),
                "La cadena de InnerException debe conservarse para que ELMAH registre la causa real");
        }

        [TestMethod]
        public void UnirPedidos_ExcepcionGenerica_ConservaLaCadenaParaElmah()
        {
            // NestoAPI#274: también el catch genérico conserva ahora el InnerException.
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorUnirPedidosConFallo(servicio,
                new InvalidOperationException("boom", new Exception("causa profunda")));

            Exception ex = Assert.ThrowsException<Exception>(() =>
                gestor.UnirPedidos(NuevoPedidoDto(), NuevoPedidoDto()).GetAwaiter().GetResult());

            Assert.AreEqual("causa profunda", ex.GetBaseException().Message);
        }

        /// <summary>
        /// Sustituye la persistencia real (controller + BD) por el lanzamiento de una excepción dada,
        /// para verificar el manejo de excepciones de UnirPedidos sin tocar la BD.
        /// </summary>
        private class GestorUnirPedidosConFallo : GestorPedidosVenta
        {
            private readonly Exception _aLanzar;

            public GestorUnirPedidosConFallo(IServicioPedidosVenta servicio, Exception aLanzar)
                : base(servicio)
            {
                _aLanzar = aLanzar;
            }

            protected override Task PersistirUnion(PedidoVentaDTO pedidoOriginal, PedidoVentaDTO pedidoAmpliacion)
            {
                throw _aLanzar;
            }
        }

        #endregion

        #region CrearLineaVta - Issue #280

        [TestMethod]
        public void CrearLineaVta_ProductoNoExiste_LanzaExcepcionClaraNoNRE()
        {
            // Issue #280: LeerProducto (SingleOrDefault) devuelve null si el producto no existe en la
            // empresa; antes CrearLineaVta lanzaba una NullReferenceException opaca al hacer producto.Estado.
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.LeerProducto(A<string>._, A<string>._)).Returns(null);
            var gestor = new GestorPedidosVenta(servicio);

            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "99999999",
                Cantidad = 1,
                texto = "PRODUCTO INEXISTENTE",
                fechaEntrega = DateTime.Today
            };
            var plazoPago = new PlazoPago { DtoProntoPago = 0 };

            Exception excepcion = null;
            try
            {
                gestor.CrearLineaVta(linea, PEDIDO, EMPRESA, "G21", plazoPago, "12786", "0", "FW", "MRM");
            }
            catch (Exception ex)
            {
                excepcion = ex;
            }

            Assert.IsNotNull(excepcion, "Debe lanzar una excepción cuando el producto no existe");
            Assert.IsFalse(excepcion is NullReferenceException, "No debe ser una NullReferenceException opaca");
            StringAssert.Contains(excepcion.Message, "99999999");
        }

        #endregion

        #region Vendedor de líneas de cuenta contable (portes/reembolso) - Issue #277

        private static IDictionary<string, string> Grupos(params string[] grupoVendedor)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < grupoVendedor.Length; i += 2)
            {
                d[grupoVendedor[i]] = grupoVendedor[i + 1];
            }
            return d;
        }

        [TestMethod]
        public void VendedorPredominanteContable_TodasPeluqueria_DevuelveVendedorPeluqueria()
        {
            // 5 líneas PEL (grupo→IF), base DV (cosmética) → portes IF (el caso que planteaste).
            var grupos = new[] { "PEL", "PEL", "PEL", "PEL", "PEL" };
            Assert.AreEqual("IF", GestorPedidosVenta.CalcularVendedorPredominanteContable(grupos, Grupos("PEL", "IF"), "DV"));
        }

        [TestMethod]
        public void VendedorPredominanteContable_MasPeluqueriaQueResto_DevuelveVendedorPeluqueria()
        {
            // 3 COS (base DV) + 7 PEL (IF) → 7 IF vs 3 DV → IF.
            var grupos = new[] { "COS", "COS", "COS", "PEL", "PEL", "PEL", "PEL", "PEL", "PEL", "PEL" };
            Assert.AreEqual("IF", GestorPedidosVenta.CalcularVendedorPredominanteContable(grupos, Grupos("PEL", "IF"), "DV"));
        }

        [TestMethod]
        public void VendedorPredominanteContable_MasRestoQuePeluqueria_DevuelveBase()
        {
            // 7 COS (base DV) + 3 PEL (IF) → 7 DV vs 3 IF → DV (base).
            var grupos = new[] { "COS", "COS", "COS", "COS", "COS", "COS", "COS", "PEL", "PEL", "PEL" };
            Assert.AreEqual("DV", GestorPedidosVenta.CalcularVendedorPredominanteContable(grupos, Grupos("PEL", "IF"), "DV"));
        }

        [TestMethod]
        public void VendedorPredominanteContable_Empate_DevuelveBase()
        {
            // 3 PEL (IF) + 3 COS (base DV) → empate → base.
            var grupos = new[] { "PEL", "PEL", "PEL", "COS", "COS", "COS" };
            Assert.AreEqual("DV", GestorPedidosVenta.CalcularVendedorPredominanteContable(grupos, Grupos("PEL", "IF"), "DV"));
        }

        [TestMethod]
        public void VendedorPredominanteContable_SinLineasProducto_DevuelveBase()
        {
            Assert.AreEqual("DV", GestorPedidosVenta.CalcularVendedorPredominanteContable(new string[0], Grupos("PEL", "IF"), "DV"));
        }

        [TestMethod]
        public void CalcularVendedorCuentaContable_SinVendedorEnDto_ResuelveBaseYGrupoDeLaFicha()
        {
            // El pedido no trae vendedor ni vendedores de grupo (caso del crash): se reconstruyen de la ficha.
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.LeerVendedorCliente(EMPRESA, "12786", "0")).Returns("DV");
            A.CallTo(() => servicio.LeerVendedoresClienteGrupo(EMPRESA, "12786", "0"))
                .Returns(new List<VendedorGrupoProductoDTO> { new VendedorGrupoProductoDTO { grupoProducto = "PEL", vendedor = "IF" } });
            var gestor = new GestorPedidosVenta(servicio);
            var pedido = new PedidoVentaDTO
            {
                empresa = EMPRESA,
                cliente = "12786",
                contacto = "0",
                vendedor = null,
                VendedoresGrupoProducto = null,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "1", Cantidad = 1, GrupoProducto = "PEL" },
                    new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "2", Cantidad = 1, GrupoProducto = "PEL" }
                }
            };

            // Todo peluquería → vendedor peluquería IF, aunque el base de la ficha sea DV.
            Assert.AreEqual("IF", gestor.CalcularVendedorCuentaContable(pedido));
        }

        #endregion
    }
}
