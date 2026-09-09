using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Tests de la validación server-side de servirJunto al crear/modificar pedidos
    /// (NestoAPI#176). El helper <c>ValidarServirJuntoDesdePedidoAsync</c> se testea
    /// directamente; los tests de integración con POST/PUT completos quedan fuera
    /// porque requerirían mockear media base de datos — la cobertura actual demuestra
    /// el pegamento entre el pedido y el servicio de validación.
    /// </summary>
    [TestClass]
    public class PedidosVentaServirJuntoTests
    {
        private NVEntities db;
        private IServicioValidarServirJunto servicio;
        private DbSet<Ganavision> fakeGanavisiones;
        private PedidosVentaController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicio = A.Fake<IServicioValidarServirJunto>();
            fakeGanavisiones = A.Fake<DbSet<Ganavision>>(o =>
                o.Implements<IQueryable<Ganavision>>().Implements<IDbAsyncEnumerable<Ganavision>>());

            A.CallTo(() => db.Ganavisiones).Returns(fakeGanavisiones);
            ConfigurarFakeDbSet(fakeGanavisiones, new List<Ganavision>().AsQueryable());

            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            controller = new PedidosVentaController(db, servicio);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_ServirJuntoTrue_RetornaNull()
        {
            var pedido = new PedidoVentaDTO
            {
                servirJunto = true,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: false)
                }
            };

            var resultado = await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsNull(resultado);
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_SinLineasRelevantes_RetornaNull()
        {
            // Todas las líneas ya están en albarán → no cuentan.
            var pedido = new PedidoVentaDTO
            {
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: false, estado: Constantes.EstadosLineaVenta.ALBARAN)
                }
            };

            var resultado = await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsNull(resultado);
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_ServicioRechaza_RetornaRespuesta()
        {
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Returns(Task.FromResult(new ValidarServirJuntoResponse
                {
                    PuedeDesmarcar = false,
                    Mensaje = "Se quedaría pendiente"
                }));

            var pedido = new PedidoVentaDTO
            {
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 1, baseImponibleCero: false)
                }
            };

            var resultado = await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsNotNull(resultado);
            Assert.IsFalse(resultado.PuedeDesmarcar);
            Assert.AreEqual("Se quedaría pendiente", resultado.Mensaje);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_ServicioAcepta_RetornaNull()
        {
            var pedido = new PedidoVentaDTO
            {
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: false)
                }
            };

            var resultado = await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_LineaBonificadoGanavisiones_SeMarcaEnRequest()
        {
            // Una línea con BaseImponible==0 + oferta nula + producto con Ganavisiones
            // configurado → debe enviar EsBonificadoGanavisiones=true al servicio.
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, ProductoId = "BONIF1", Ganavisiones = 1 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("BONIF1", cantidad: 1, baseImponibleCero: true),
                    NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: false)
                }
            };

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsNotNull(requestCapturado);
            Assert.AreEqual(2, requestCapturado.LineasPedido.Count);
            var bonif = requestCapturado.LineasPedido.First(l => l.ProductoId == "BONIF1");
            var normal = requestCapturado.LineasPedido.First(l => l.ProductoId == "PROD1");
            Assert.IsTrue(bonif.EsBonificadoGanavisiones);
            Assert.IsFalse(normal.EsBonificadoGanavisiones);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_LineaConOferta_NoSeMarcaComoBonificado()
        {
            // Una línea a 0€ con oferta asignada (ej. 5+5) no es Ganavisiones: se manda
            // sin marcar aunque el producto esté registrado en la tabla Ganavision.
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, ProductoId = "PROD1", Ganavisiones = 1 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var linea = NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: true);
            linea.oferta = 42;

            var pedido = new PedidoVentaDTO
            {
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO> { linea }
            };

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsFalse(requestCapturado.LineasPedido[0].EsBonificadoGanavisiones);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_LineaYaAlbaranada_NoSeIncluye()
        {
            // Sólo se mandan al validador las líneas aún no despachadas (estado <= EN_CURSO).
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: false, estado: Constantes.EstadosLineaVenta.PENDIENTE),
                    NuevaLinea("PROD2", cantidad: 1, baseImponibleCero: false, estado: Constantes.EstadosLineaVenta.ALBARAN),
                    NuevaLinea("PROD3", cantidad: 1, baseImponibleCero: false, estado: Constantes.EstadosLineaVenta.FACTURA)
                }
            };

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.AreEqual(1, requestCapturado.LineasPedido.Count);
            Assert.AreEqual("PROD1", requestCapturado.LineasPedido[0].ProductoId);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_PedidoExistente_MandaSuNumeroParaNoContarSuPropiaReserva()
        {
            // Regresión NestoAPI#469: al guardar un pedido ya existente hay que decirle al validador
            // QUÉ pedido se está guardando. Sin ese número, CalcularStockBase cuenta como "pendiente
            // de entregar" las líneas del propio pedido y la muestra que se quiere servir compite
            // contra su propia reserva: sale disponible 0 y se deniega con stock de sobra.
            // Es lo que pasó con el pedido 925807 el 09/09/26 (stock 26, pendiente 26, de los
            // cuales 4 eran suyos).
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false)
                }
            };

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.AreEqual(925807, requestCapturado.Pedido,
                "El validador debe excluir del pendiente las líneas del propio pedido (NestoAPI#262)");
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_PedidoNuevo_NoMandaNumero()
        {
            // Al CREAR (POST) el pedido todavía no tiene número: no hay nada que excluir y el
            // request debe ir sin él, como hasta ahora.
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 0,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false)
                }
            };

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.IsNull(requestCapturado.Pedido);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_PedidoYaDesmarcadoYSinCambios_NoValidaNada()
        {
            // NestoAPI#470: un pedido que ya estaba con "servir junto" desmarcado y con una muestra
            // dentro quedaba bloqueado para CUALQUIER modificacion, aunque el cambio no tuviera nada
            // que ver. Es lo que le paso al 925807: la casilla llevaba desmarcada desde las 11:12 y
            // cada intento de ampliar volvia a validar el desmarcado.
            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false)
                }
            };
            CabPedidoVta guardado = PedidoGuardado(servirJunto: false, LineaGuardada("MMP1", 4));

            var resultado = await controller.ValidarServirJuntoDesdePedidoAsync(pedido, guardado);

            Assert.IsNull(resultado);
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_PedidoYaDesmarcadoConProductoNuevo_ValidaSoloElNuevo()
        {
            // El agujero de NestoAPI#176 sigue cerrado: desmarcar primero y anadir la muestra
            // despues se sigue detectando, porque la linea nueva si se valida.
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false),
                    NuevaLinea("MMP2", cantidad: 2, baseImponibleCero: false)
                }
            };
            CabPedidoVta guardado = PedidoGuardado(servirJunto: false, LineaGuardada("MMP1", 4));

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido, guardado);

            Assert.AreEqual(1, requestCapturado.LineasPedido.Count);
            Assert.AreEqual("MMP2", requestCapturado.LineasPedido[0].ProductoId);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_PedidoYaDesmarcadoYSubeLaCantidad_ValidaElTotal()
        {
            // Se valida la cantidad TOTAL, no el incremento: el stock disponible se calcula
            // excluyendo el propio pedido (#262), asi que las unidades que ya estaban no cuentan
            // como pendiente y hay que comprobar que hay stock para todas.
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 6, baseImponibleCero: false)
                }
            };
            CabPedidoVta guardado = PedidoGuardado(servirJunto: false, LineaGuardada("MMP1", 4));

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido, guardado);

            Assert.AreEqual(1, requestCapturado.LineasPedido.Count);
            Assert.AreEqual(6, requestCapturado.LineasPedido[0].Cantidad);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_PedidoYaDesmarcadoYBajaLaCantidad_NoValida()
        {
            // Quitar unidades nunca empeora la situacion.
            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 2, baseImponibleCero: false)
                }
            };
            CabPedidoVta guardado = PedidoGuardado(servirJunto: false, LineaGuardada("MMP1", 4));

            var resultado = await controller.ValidarServirJuntoDesdePedidoAsync(pedido, guardado);

            Assert.IsNull(resultado);
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_SeEstaDesmarcandoAhora_ValidaTodasLasLineas()
        {
            // Regresion NestoAPI#176: cuando el guardado SI cambia la casilla, se comprueba el
            // pedido entero, que es el momento en el que la decision se toma de verdad.
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false),
                    NuevaLinea("PROD1", cantidad: 1, baseImponibleCero: false)
                }
            };
            CabPedidoVta guardado = PedidoGuardado(servirJunto: true, LineaGuardada("MMP1", 4), LineaGuardada("PROD1", 1));

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido, guardado);

            Assert.AreEqual(2, requestCapturado.LineasPedido.Count);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_SinEstadoGuardado_ValidaTodasLasLineas()
        {
            // El POST de creacion no tiene estado anterior con el que comparar.
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 0,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false)
                }
            };

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.AreEqual(1, requestCapturado.LineasPedido.Count);
        }

        [TestMethod]
        public async Task ValidarServirJuntoDesdePedido_LineaGuardadaYaServida_CuentaComoNueva()
        {
            // Una linea que en BD esta en albaran ya no reserva stock pendiente: si vuelve al pedido
            // como pendiente, es una peticion nueva y hay que comprobarla.
            ValidarServirJuntoRequest requestCapturado = null;
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._))
                .Invokes((ValidarServirJuntoRequest r) => requestCapturado = r)
                .Returns(Task.FromResult(new ValidarServirJuntoResponse { PuedeDesmarcar = true }));

            var pedido = new PedidoVentaDTO
            {
                numero = 925807,
                servirJunto = false,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    NuevaLinea("MMP1", cantidad: 4, baseImponibleCero: false)
                }
            };
            CabPedidoVta guardado = PedidoGuardado(servirJunto: false,
                LineaGuardada("MMP1", 4, estado: (short)Constantes.EstadosLineaVenta.ALBARAN));

            await controller.ValidarServirJuntoDesdePedidoAsync(pedido, guardado);

            Assert.AreEqual(1, requestCapturado.LineasPedido.Count);
        }

        private static CabPedidoVta PedidoGuardado(bool servirJunto, params LinPedidoVta[] lineas)
        {
            return new CabPedidoVta
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Número = 925807,
                ServirJunto = servirJunto,
                LinPedidoVtas = new List<LinPedidoVta>(lineas)
            };
        }

        private static LinPedidoVta LineaGuardada(
            string producto, short cantidad, short estado = (short)Constantes.EstadosLineaVenta.PENDIENTE)
        {
            return new LinPedidoVta
            {
                Producto = producto,
                Cantidad = cantidad,
                Almacén = "ALG",
                TipoLinea = PedidosVentaController.TIPO_LINEA_PRODUCTO,
                Estado = estado
            };
        }

        private static LineaPedidoVentaDTO NuevaLinea(
            string producto,
            int cantidad,
            bool baseImponibleCero,
            short estado = (short)Constantes.EstadosLineaVenta.PENDIENTE)
        {
            var linea = new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                almacen = "ALG",
                tipoLinea = PedidosVentaController.TIPO_LINEA_PRODUCTO,
                estado = estado,
                PrecioUnitario = baseImponibleCero ? 10m : 10m,
                DescuentoLinea = baseImponibleCero ? 1m : 0m
            };
            return linea;
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
