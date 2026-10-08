using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>NestoAPI#606: el servicio que lee los datos y llama a la calculadora (repositorio falso).</summary>
    [TestClass]
    public class ServicioFechaEntregaAgenciaTests
    {
        // Martes 13/10/2026 a las 10:30 (antes del corte de las 11:00).
        private static readonly DateTime AHORA = new DateTime(2026, 10, 13, 10, 30, 0);
        private IRepositorioFechaEntregaAgencia repositorio;
        private List<Exception> registradas;
        private ServicioFechaEntregaAgencia servicio;

        private static bool EsFestivo(DateTime dia, string almacen) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        [TestInitialize]
        public void Preparar()
        {
            ServicioFechaEntregaAgencia.OlvidarAvisos();
            repositorio = A.Fake<IRepositorioFechaEntregaAgencia>();
            registradas = new List<Exception>();
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).Returns(Task.FromResult(new List<ReposicionCalendario>()));
            A.CallTo(() => repositorio.LeerHoraCorte(A<string>._)).Returns(new TimeSpan(11, 0, 0));
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).ReturnsLazily(() => ConStockEnAlgete("A", 10));
            servicio = new ServicioFechaEntregaAgencia(repositorio, () => AHORA, EsFestivo, registradas.Add);
        }

        private static DatosSombraModoServicio ConStockEnAlgete(string producto, int unidades)
        {
            var datos = new DatosSombraModoServicio();
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, "ALG"), unidades);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave(producto), unidades);
            return datos;
        }

        private static PedidoFechaEntregaAgencia Pedido(byte? modoServicio = 1) => new PedidoFechaEntregaAgencia
        {
            Empresa = "1",
            Numero = 927000,
            Ruta = "FW",
            ModoServicio = modoServicio,
            ServirJunto = modoServicio == 1, // en BD van siempre a la par (trgCabPedidoVtaModoServicio)
            Lineas = new List<LineaPedidoFechaEntregaAgencia>
            {
                new LineaPedidoFechaEntregaAgencia { Producto = "A", Almacen = "ALG", Cantidad = 2, BaseImponible = 20 }
            }
        };

        [TestMethod]
        public async Task CalcularPedido_PedidoQueNoExiste_Null()
        {
            A.CallTo(() => repositorio.LeerPedido("1", 1)).Returns(Task.FromResult<PedidoFechaEntregaAgencia>(null));

            Assert.IsNull(await servicio.CalcularPedido("1  ", 1));
        }

        [TestMethod]
        public async Task CalcularPedido_DevuelveLaFechaLaPrometidaYExcluyeSusPropiasLineasDeLosPendientes()
        {
            A.CallTo(() => repositorio.LeerPedido("1", 927000)).Returns(Task.FromResult(Pedido()));
            A.CallTo(() => repositorio.LeerPrometida("1", 927000)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 12)));

            FechaEntregaAgenciaDTO dto = await servicio.CalcularPedido("1", 927000);

            Assert.AreEqual(new DateTime(2026, 10, 13), dto.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 13), dto.EntregaCompleta);
            Assert.AreEqual(new DateTime(2026, 10, 13), dto.FechaEntregaAgencia);
            Assert.AreEqual(FechaEntregaAgenciaDTO.APLICA_COMPLETA, dto.Aplica);
            Assert.AreEqual(new DateTime(2026, 10, 12), dto.FechaPrometida);
            Assert.IsFalse(string.IsNullOrWhiteSpace(dto.Motivo));
            A.CallTo(() => repositorio.LeerStock("1", 927000, A<IEnumerable<string>>.That.Contains("A"))).MustHaveHappened();
        }

        [TestMethod]
        public async Task CalcularPedido_SinModoInformado_MandaServirJunto()
        {
            PedidoFechaEntregaAgencia pedido = Pedido(modoServicio: null);
            pedido.ServirJunto = false;
            A.CallTo(() => repositorio.LeerPedido("1", 927000)).Returns(Task.FromResult(pedido));

            FechaEntregaAgenciaDTO dto = await servicio.CalcularPedido("1", 927000);

            Assert.AreEqual(FechaEntregaAgenciaDTO.APLICA_PRIMERA, dto.Aplica, "Sin modo y sin servir junto: «según vaya entrando»");
        }

        [TestMethod]
        public async Task CalcularPlantilla_SinNumero_NoExcluyeNadaYLeeLosDiasDeServirDelCliente()
        {
            A.CallTo(() => repositorio.LeerDiasEnServir("1", "15191", "0")).Returns(Task.FromResult("10111")); // cierra los martes
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                cliente = "15191 ",
                contacto = "0",
                ruta = "FW ",
                modoServicio = 1,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { Producto = "A", almacen = "ALG", Cantidad = 2, PrecioUnitario = 10, tipoLinea = 1, estado = -1 },
                    new LineaPedidoVentaDTO { Producto = "T", almacen = "REI", Cantidad = 1, PrecioUnitario = 10, tipoLinea = 1, estado = -1 },
                    new LineaPedidoVentaDTO { Producto = "624", almacen = "ALG", Cantidad = 1, PrecioUnitario = 6, tipoLinea = 2, estado = -1 }
                }
            };

            FechaEntregaAgenciaDTO dto = await servicio.CalcularPlantilla(pedido);

            // El picking del martes 13 se entregaría el miércoles 14 (abre); el del lunes no cuenta. Sale hoy.
            Assert.AreEqual(new DateTime(2026, 10, 13), dto.FechaEntregaAgencia);
            Assert.AreEqual(FechaEntregaAgenciaDTO.APLICA_COMPLETA, dto.Aplica, "El modo informado manda aunque no venga el bit servirJunto");
            A.CallTo(() => repositorio.LeerStock("1", null, A<IEnumerable<string>>.That.Matches(p => p.SequenceEqual(new[] { "A" })))).MustHaveHappened();
            A.CallTo(() => repositorio.LeerPrometida(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CalcularPlantilla_ClienteQueCierraElDiaDeLaEntrega_SeRetrasa()
        {
            A.CallTo(() => repositorio.LeerDiasEnServir("1", "15191", "0")).Returns(Task.FromResult("11011")); // cierra los miércoles
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                cliente = "15191",
                contacto = "0",
                ruta = "FW",
                modoServicio = 1,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { Producto = "A", almacen = "ALG", Cantidad = 2, PrecioUnitario = 10, tipoLinea = 1, estado = -1 }
                }
            };

            FechaEntregaAgenciaDTO dto = await servicio.CalcularPlantilla(pedido);

            Assert.AreEqual(new DateTime(2026, 10, 14), dto.FechaEntregaAgencia);
        }

        [TestMethod]
        public async Task CalcularPlantilla_ConNumero_ExcluyeSusLineasGrabadas()
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 927000,
                ruta = "FW",
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { Producto = "A", almacen = "ALG", Cantidad = 1, PrecioUnitario = 10, tipoLinea = 1, estado = -1 }
                }
            };

            _ = await servicio.CalcularPlantilla(pedido);

            A.CallTo(() => repositorio.LeerStock("1", 927000, A<IEnumerable<string>>._)).MustHaveHappened();
        }

        [TestMethod]
        public async Task GuardarPrometidaAlCrear_GuardaLaFechaQueAplica()
        {
            A.CallTo(() => repositorio.LeerPedido("1", 927000)).Returns(Task.FromResult(Pedido()));

            DateTime? fecha = await servicio.GuardarPrometidaAlCrear("1", 927000);

            Assert.AreEqual(new DateTime(2026, 10, 13), fecha);
            A.CallTo(() => repositorio.GuardarPrometida("1", 927000, new DateTime(2026, 10, 13))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GuardarPrometidaAlCrear_SinFecha_NoGuardaNada()
        {
            PedidoFechaEntregaAgencia pedido = Pedido();
            pedido.Lineas[0].Producto = "SINSTOCK";
            A.CallTo(() => repositorio.LeerPedido("1", 927000)).Returns(Task.FromResult(pedido));
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(new DatosSombraModoServicio());

            DateTime? fecha = await servicio.GuardarPrometidaAlCrear("1", 927000);

            Assert.IsNull(fecha);
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GuardarPrometidaAlCrear_SiFallaAlGuardar_NoLanzaYLoRegistraUnaSolaVez()
        {
            // Script sin lanzar: «El nombre de columna FechaEntregaAgenciaPrometida no es válido». Crear el pedido no puede fallar.
            A.CallTo(() => repositorio.LeerPedido("1", 927000)).Returns(Task.FromResult(Pedido()));
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).ThrowsAsync(new InvalidOperationException("columna no válida"));

            DateTime? primera = await servicio.GuardarPrometidaAlCrear("1", 927000);
            DateTime? segunda = await servicio.GuardarPrometidaAlCrear("1", 927000);

            Assert.IsNull(primera);
            Assert.IsNull(segunda);
            Assert.AreEqual(1, registradas.Count, "Como mucho un aviso cada media hora");
            StringAssert.Contains(registradas[0].Message, "927000");
        }

        [TestMethod]
        public async Task GuardarPrometidaAlCrear_SiFallaAlLeer_NoLanza()
        {
            A.CallTo(() => repositorio.LeerPedido(A<string>._, A<int>._)).ThrowsAsync(new Exception("BD caída"));

            Assert.IsNull(await servicio.GuardarPrometidaAlCrear("1", 927000));
        }
    }
}
