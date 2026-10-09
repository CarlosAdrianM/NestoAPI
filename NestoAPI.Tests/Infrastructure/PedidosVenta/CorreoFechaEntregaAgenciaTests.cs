using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606 (petición de Carlos, 09/10/26): el correo del pedido enseña la fecha de entrega a la agencia que se le ha
    /// dicho al cliente y por qué. De punta a punta: el servicio real (repositorio falso) calcula y
    /// <see cref="GestorPresupuestos.GenerarHtmlFechaEntregaAgencia"/> lo pinta.
    /// </summary>
    [TestClass]
    public class CorreoFechaEntregaAgenciaTests
    {
        // Martes 13/10/2026 a las 10:30 (antes del corte de las 11:00). Solo cierran sábados y domingos.
        private static readonly DateTime AHORA = new DateTime(2026, 10, 13, 10, 30, 0);
        private const int PEDIDO = 927000;
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
            // Reina → Algete los miércoles, llega a las 13:30 (después del corte: el pedido sale el jueves).
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).Returns(Task.FromResult(new List<ReposicionCalendario>
            {
                new ReposicionCalendario
                {
                    Empresa = "1", AlmacenOrigen = "REI", AlmacenDestino = "ALG", DiaSemana = 3,
                    HoraCierre = new TimeSpan(9, 0, 0), HoraLlegadaHabitual = new TimeSpan(13, 30, 0), Activo = true
                }
            }));
            A.CallTo(() => repositorio.LeerHoraCorte(A<string>._)).Returns(new TimeSpan(11, 0, 0));
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).ReturnsLazily(() => Datos());
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).Returns(Task.FromResult(true));
            servicio = new ServicioFechaEntregaAgencia(repositorio, () => AHORA, EsFestivo, registradas.Add);
        }

        /// <summary>A: 10 en Algete.</summary>
        private static DatosSombraModoServicio Datos()
        {
            var datos = new DatosSombraModoServicio();
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("A", "ALG"), 10);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave("A"), 10);
            return datos;
        }

        private static DatosSombraModoServicio ConBEnReina()
        {
            DatosSombraModoServicio datos = Datos();
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("B", "REI"), 3);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave("B"), 3);
            return datos;
        }

        private static DatosSombraModoServicio ConBDelProveedor(DateTime fechaPrevista)
        {
            DatosSombraModoServicio datos = Datos();
            string clave = ResumenStocksProductos.Clave("B", "ALG");
            datos.PendienteRecibir[clave] = 5;
            datos.FechaPrevista[clave] = fechaPrevista;
            datos.PedidoCompraPrevisto[clave] = 220513;
            return datos;
        }

        /// <summary>«Todo junto»: A (2 uds.) y, si se pide, B (1 ud.).</summary>
        private void ConPedido(bool conB)
        {
            var pedido = new PedidoFechaEntregaAgencia
            {
                Empresa = "1",
                Numero = PEDIDO,
                Ruta = "FW",
                ModoServicio = 1,
                ServirJunto = true,
                Lineas = new List<LineaPedidoFechaEntregaAgencia>
                {
                    new LineaPedidoFechaEntregaAgencia { Producto = "A", Almacen = "ALG", Cantidad = 2, BaseImponible = 20 }
                }
            };
            if (conB)
            {
                pedido.Lineas.Add(new LineaPedidoFechaEntregaAgencia { Producto = "B", Almacen = "ALG", Cantidad = 1, BaseImponible = 10 });
            }
            A.CallTo(() => repositorio.LeerPedido("1", PEDIDO)).Returns(Task.FromResult(pedido));
        }

        private static List<LineaPedidoVentaDTO> LineasCorreo() => new List<LineaPedidoVentaDTO>
        {
            new LineaPedidoVentaDTO { Producto = "A", texto = "Champú & acondicionador", tipoLinea = 1 },
            new LineaPedidoVentaDTO { Producto = "B", texto = "Guantes talla L", tipoLinea = 1 }
        };

        private static string Html(FechaEntregaAgenciaDTO fecha, bool esModificacion = false, bool esPresupuesto = false) =>
            GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(fecha, esPresupuesto, esModificacion, LineasCorreo(), 6);

        [TestMethod]
        public async Task Alta_ConStock_LaFechaPrometidaYQueSaleConStock()
        {
            ConPedido(conB: false);

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO);
            string html = Html(fecha);

            A.CallTo(() => repositorio.GuardarPrometida("1", PEDIDO, new DateTime(2026, 10, 13))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(new DateTime(2026, 10, 13), fecha.FechaPrometida, "La del correo es la que se ha guardado");
            StringAssert.Contains(html, "Entrega a la agencia: martes 13/10/2026");
            StringAssert.Contains(html, "la que se le ha dicho al cliente");
            StringAssert.Contains(html, "Con stock en Algete (puede salir el martes 13/10/2026): A Champú &amp; acondicionador (2 uds.)");
            StringAssert.Contains(html, "colspan='6'");
        }

        [TestMethod]
        public async Task Alta_ConReposicionDeTienda_RutaLlegadaYSalida()
        {
            ConPedido(conB: true);
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(ConBEnReina());

            string html = Html(await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO));

            StringAssert.Contains(html, "Entrega a la agencia: jueves 15/10/2026");
            StringAssert.Contains(html, "Reposición Reina &rarr; Algete (llega a Algete el miércoles 14/10/2026 hacia las 13:30; " +
                "puede salir el jueves 15/10/2026): B Guantes talla L (1 ud.)");
            StringAssert.Contains(html, "Con stock en Algete");
        }

        [TestMethod]
        public async Task Alta_ConPedidoAProveedor_NumeroYFechaPrevista()
        {
            ConPedido(conB: true);
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(ConBDelProveedor(new DateTime(2026, 10, 16)));

            string html = Html(await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO));

            StringAssert.Contains(html, "Entrega a la agencia: lunes 19/10/2026");
            StringAssert.Contains(html, "<li>Pedido a proveedor 220513, prevista el viernes 16/10/2026 (puede salir el lunes 19/10/2026): B Guantes talla L (1 ud.).</li>");
        }

        [TestMethod]
        public async Task Alta_ConPedidoAProveedorConLaPrevistaVencida_AvisaEnRojo()
        {
            ConPedido(conB: true);
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(ConBDelProveedor(new DateTime(2026, 10, 9)));

            string html = Html(await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO));

            StringAssert.Contains(html, "Entrega a la agencia: jueves 15/10/2026");
            StringAssert.Contains(html, "<li style=\"color:red\">Pedido a proveedor 220513, prevista el viernes 09/10/2026");
            StringAssert.Contains(html, "suponemos que llega el miércoles 14/10/2026 y puede salir el jueves 15/10/2026, pero puede retrasarse");
        }

        [TestMethod]
        public async Task Alta_SinStockNiFecha_DiceSinFechaYPorQue()
        {
            ConPedido(conB: true); // B no está en ningún sitio

            string html = Html(await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO));

            StringAssert.Contains(html, "Entrega a la agencia: sin fecha</b>");
            StringAssert.Contains(html, "Sin fecha: B Guantes talla L (1 ud.): no hay stock ni fecha de llegada.");
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Modificacion_LaFechaCambia_AntesYAhoraResaltado()
        {
            ConPedido(conB: true);
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(ConBEnReina());
            A.CallTo(() => repositorio.LeerPrometida("1", PEDIDO)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 13)));

            string html = Html(await servicio.CalcularPedidoParaCorreo("1", PEDIDO), esModificacion: true);

            StringAssert.Contains(html, "background-color:#fff3cd");
            StringAssert.Contains(html, "Cambia la fecha de entrega a la agencia. Antes: martes 13/10/2026 &rarr; Ahora: jueves 15/10/2026");
            StringAssert.Contains(html, "Por qué sale el jueves 15/10/2026 con el pedido como queda:");
            StringAssert.Contains(html, "Reposición Reina &rarr; Algete");
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Modificacion_LaFechaNoCambia_LaMismaSinResaltar()
        {
            ConPedido(conB: false);
            A.CallTo(() => repositorio.LeerPrometida("1", PEDIDO)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 13)));

            string html = Html(await servicio.CalcularPedidoParaCorreo("1", PEDIDO), esModificacion: true);

            StringAssert.Contains(html, "Entrega a la agencia: martes 13/10/2026</b> (la misma que se le dijo al cliente al crear el pedido)");
            Assert.IsFalse(html.Contains("Cambia la fecha"));
            Assert.IsFalse(html.Contains("#fff3cd"));
        }

        [TestMethod]
        public async Task Modificacion_PedidoAntiguoSinPrometida_LoDiceSinRomper()
        {
            ConPedido(conB: false);
            A.CallTo(() => repositorio.LeerPrometida("1", PEDIDO)).Returns(Task.FromResult<DateTime?>(null));

            string html = Html(await servicio.CalcularPedidoParaCorreo("1", PEDIDO), esModificacion: true);

            StringAssert.Contains(html, "Entrega a la agencia: martes 13/10/2026</b> (el pedido no tiene fecha prometida guardada");
            Assert.IsFalse(html.Contains("Antes:"));
        }

        [TestMethod]
        public async Task Modificacion_SeQuedaSinFecha_AntesYSinFecha()
        {
            ConPedido(conB: true); // B sin stock: «Todo junto» se queda sin fecha
            A.CallTo(() => repositorio.LeerPrometida("1", PEDIDO)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 13)));

            string html = Html(await servicio.CalcularPedidoParaCorreo("1", PEDIDO), esModificacion: true);

            StringAssert.Contains(html, "Antes: martes 13/10/2026 &rarr; Ahora: sin fecha");
            StringAssert.Contains(html, "Por qué sale sin fecha con el pedido como queda:");
        }

        [TestMethod]
        public void Presupuesto_SinSeccion()
        {
            var fecha = new FechaEntregaAgenciaDTO { FechaEntregaAgencia = new DateTime(2026, 10, 13), Aplica = FechaEntregaAgenciaDTO.APLICA_COMPLETA };

            Assert.AreEqual(string.Empty, Html(fecha, esPresupuesto: true));
            Assert.AreEqual(string.Empty, Html(fecha, esModificacion: true, esPresupuesto: true));
        }

        [TestMethod]
        public void NadaSaleDeAlgetePorAgencia_SinSeccion()
        {
            // P. ej. un pedido para recoger en tienda: la calculadora no tiene líneas de Algete.
            var fecha = new FechaEntregaAgenciaDTO { Motivo = "El pedido no tiene productos pendientes que salgan de Algete por agencia." };

            Assert.AreEqual(string.Empty, Html(fecha));
            Assert.AreEqual(string.Empty, Html(fecha, esModificacion: true));
        }

        [TestMethod]
        public async Task FalloDelCalculo_CorreoSinSeccionYUnSoloAvisoAElmah()
        {
            A.CallTo(() => repositorio.LeerPedido(A<string>._, A<int>._)).ThrowsAsync(new Exception("BD caída"));

            FechaEntregaAgenciaDTO alta = await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO);
            FechaEntregaAgenciaDTO modificacion = await servicio.CalcularPedidoParaCorreo("1", PEDIDO);

            Assert.IsNull(alta);
            Assert.IsNull(modificacion);
            Assert.AreEqual(string.Empty, Html(alta));
            Assert.AreEqual(string.Empty, Html(modificacion, esModificacion: true));
            Assert.AreEqual(1, registradas.Count, "Como mucho un aviso cada media hora");
            StringAssert.Contains(registradas[0].Message, PEDIDO.ToString());
        }

        [TestMethod]
        public async Task Alta_SiSoloFallaAlGuardar_ElCorreoLlevaLaFechaIgual()
        {
            ConPedido(conB: false);
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).ThrowsAsync(new InvalidOperationException("columna no válida"));

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO);

            Assert.IsNull(fecha.FechaPrometida);
            StringAssert.Contains(Html(fecha), "Entrega a la agencia: martes 13/10/2026");
            Assert.AreEqual(1, registradas.Count);
            Assert.IsNull(await servicio.GuardarPrometidaAlCrear("1", PEDIDO), "GuardarPrometidaAlCrear sigue devolviendo null si no ha podido guardar");
        }

        [TestMethod]
        public void PrimeraEntregaDistintaDeLaCompleta_LoDice()
        {
            var fecha = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = new DateTime(2026, 10, 13),
                PrimeraEntrega = new DateTime(2026, 10, 13),
                EntregaCompleta = new DateTime(2026, 10, 15),
                Aplica = FechaEntregaAgenciaDTO.APLICA_PRIMERA,
                Aviso = "Aviso <de> prueba"
            };

            string html = Html(fecha);

            StringAssert.Contains(html, "Es la primera entrega; el pedido queda completo el jueves 15/10/2026.");
            StringAssert.Contains(html, "Aviso &lt;de&gt; prueba");
        }
    }
}
