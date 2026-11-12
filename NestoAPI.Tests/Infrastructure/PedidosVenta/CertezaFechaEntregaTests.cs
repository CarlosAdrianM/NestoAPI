using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Infrastructure.Stocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#853: la fecha de entrega con su grado de certeza, de punta a punta (servicio real con repositorio y lector falsos) y en los
    /// tres compositores: el motivo del motor, el correo del pedido y la promesa en riesgo. Ninguna fecha del cálculo cambia.
    /// </summary>
    [TestClass]
    public class CertezaFechaEntregaTests
    {
        // Martes 13/10/2026 a las 10:30 (antes del corte de las 11:00). Solo cierran sábados y domingos.
        private static readonly DateTime AHORA = new DateTime(2026, 10, 13, 10, 30, 0);
        private const int PEDIDO = 927000;
        private IRepositorioFechaEntregaAgencia repositorio;
        private ILectorCertezaFechaEntrega lector;
        private List<Exception> registradas;
        private ServicioFechaEntregaAgencia servicio;

        private static bool EsFestivo(DateTime dia, string almacen) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        [TestInitialize]
        public void Preparar()
        {
            ServicioFechaEntregaAgencia.OlvidarAvisos();
            repositorio = A.Fake<IRepositorioFechaEntregaAgencia>();
            lector = A.Fake<ILectorCertezaFechaEntrega>();
            A.CallTo(() => lector.CorreosAplicados(A<IEnumerable<int>>._)).Returns(new List<CorreoAplicadoPedidoCompra>());
            A.CallTo(() => lector.Plazos(A<string>._)).Returns(null);
            registradas = new List<Exception>();
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
            var pedido = new PedidoFechaEntregaAgencia
            {
                Empresa = "1",
                Numero = PEDIDO,
                Ruta = "FW",
                ModoServicio = 1,
                ServirJunto = true,
                Lineas = new List<LineaPedidoFechaEntregaAgencia>
                {
                    new LineaPedidoFechaEntregaAgencia { Producto = "A", Almacen = "ALG", Cantidad = 2, BaseImponible = 20 },
                    new LineaPedidoFechaEntregaAgencia { Producto = "B", Almacen = "ALG", Cantidad = 1, BaseImponible = 10 }
                }
            };
            A.CallTo(() => repositorio.LeerPedido("1", PEDIDO)).Returns(Task.FromResult(pedido));
            servicio = Servicio(lector);
        }

        private ServicioFechaEntregaAgencia Servicio(ILectorCertezaFechaEntrega conLector) =>
            new ServicioFechaEntregaAgencia(repositorio, () => AHORA, EsFestivo, registradas.Add,
                repartoStock: RepartoStockFalso.DesdeSombra(repositorio), lectorCerteza: conLector);

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
            datos.EmpresaCompraPrevista[clave] = "1";
            return datos;
        }

        private void Stock(DatosSombraModoServicio datos) =>
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(datos);

        private void ConCorreoAplicado(DateTime fecha, string agencia = "UPS") =>
            A.CallTo(() => lector.CorreosAplicados(A<IEnumerable<int>>._)).Returns(new List<CorreoAplicadoPedidoCompra>
            {
                new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 220513, FechaRecepcionAplicada = fecha, Agencia = agencia, AplicadoEl = AHORA.AddDays(-1) }
            });

        /// <summary>B: lo suele traer el proveedor 9100, que pide cada 7 días (el último hace 3) y tarda 6 (P80).</summary>
        private void ConPlazos()
        {
            A.CallTo(() => lector.Plazos(A<string>._)).Returns(new PlazosProveedores(new[]
            {
                new PlazoProveedor { Empresa = "1", Proveedor = "9100", Recepciones = 50, DiasMediana = 4, DiasP80 = 6, IntervaloPedidosMediano = 7, UltimoPedido = AHORA.Date.AddDays(-3) },
                new PlazoProveedor { Empresa = "1", Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 2748, DiasMediana = 4, DiasP80 = 11 }
            }));
            A.CallTo(() => lector.ProveedorHabitual(A<string>._, A<IEnumerable<string>>._))
                .Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["B"] = "9100" });
        }

        private static string Html(FechaEntregaAgenciaDTO fecha) =>
            GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(fecha, false, false, new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { Producto = "A", texto = "Champú", tipoLinea = 1 },
                new LineaPedidoVentaDTO { Producto = "B", texto = "Guantes talla L", tipoLinea = 1 }
            }, 6);

        private static PartidaFechaEntregaAgenciaDTO B(FechaEntregaAgenciaDTO fecha) => fecha.Desglose.Single(p => p.Producto == "B");

        [TestMethod]
        public async Task Algete_Segura_SinEtiqueta()
        {
            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO + 0);

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA, fecha.Desglose.First(p => p.Producto == "A").Certeza);
            Assert.IsFalse(Html(fecha).Contains("<b>["), "Lo de Algete no lleva etiqueta");
        }

        [TestMethod]
        public async Task Tienda_PracticamenteSegura()
        {
            Stock(ConBEnReina());

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA, B(fecha).Certeza);
            StringAssert.Contains(fecha.Motivo, "1 ud. de B en Reina: llega con la reposición y puede salir el jueves 15/10 " +
                "(fecha prácticamente segura: ya está en nuestras tiendas).");
            StringAssert.Contains(Html(fecha), "<li><b>[Prácticamente segura]</b> Reposición Reina &rarr; Algete (llega a Algete el miércoles 14/10/2026 " +
                "hacia las 13:30; puede salir el jueves 15/10/2026; ya está en nuestras tiendas): B Guantes talla L (1 ud.).</li>");
        }

        [TestMethod]
        public async Task ProveedorConSeguimiento_PracticamenteSeguraConLaAgencia()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 16)));
            ConCorreoAplicado(new DateTime(2026, 10, 16));

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            PartidaFechaEntregaAgenciaDTO b = B(fecha);
            Assert.IsTrue(b.ConSeguimientoAgencia);
            Assert.AreEqual("UPS", b.AgenciaSeguimiento);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA, b.Certeza);
            Assert.AreEqual(new DateTime(2026, 10, 19), fecha.FechaEntregaAgencia, "La fecha no cambia");
            StringAssert.Contains(fecha.Motivo, "1 ud. de B del pedido a proveedor 220513, ya enviado por UPS, que prevé entregarlo el 16/10 " +
                "(fecha prácticamente segura): puede salir el lunes 19/10.");
            StringAssert.Contains(Html(fecha), "<li><b>[Prácticamente segura]</b> Pedido a proveedor 220513, ya enviado por UPS, que prevé entregarlo el " +
                "viernes 16/10/2026 (fecha prácticamente segura; puede salir el lunes 19/10/2026): B Guantes talla L (1 ud.).</li>");
        }

        [TestMethod]
        public async Task ProveedorConSeguimientoSinAgencia_LoDiceSinNombrarla()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 16)));
            ConCorreoAplicado(new DateTime(2026, 10, 16), agencia: null);

            StringAssert.Contains(Html(await servicio.CalcularPedido("1", PEDIDO)),
                "Pedido a proveedor 220513, ya enviado por el proveedor, con seguimiento de la agencia, que prevé entregarlo el viernes 16/10/2026");
        }

        [TestMethod]
        public async Task ProveedorConUnCorreoDeOtraFecha_YaNoEsSeguimiento()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 16)));
            ConCorreoAplicado(new DateTime(2026, 10, 15)); // Compras cambió la fecha después

            Assert.IsFalse(B(await servicio.CalcularPedido("1", PEDIDO)).ConSeguimientoAgencia);
        }

        [TestMethod]
        public async Task ProveedorSinSeguimiento_Aproximada()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 16)));

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA, B(fecha).Certeza);
            StringAssert.Contains(fecha.Motivo, "1 ud. de B del pedido a proveedor 220513 (prevista el 16/10; fecha aproximada, aún sin seguimiento " +
                "de la agencia): puede salir el lunes 19/10.");
            StringAssert.Contains(Html(fecha), "<li><b>[Aproximada]</b> Pedido a proveedor 220513, llegada estimada el viernes 16/10/2026 " +
                "(fecha aproximada, aún sin seguimiento de la agencia; puede salir el lunes 19/10/2026): B Guantes talla L (1 ud.).</li>");
        }

        [TestMethod]
        public async Task ProveedorVencido_ElAvisoDeSiempreConLaEtiquetaAproximada()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 9)));

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA, B(fecha).Certeza);
            string html = Html(fecha);
            StringAssert.Contains(html, "<li style=\"color:red\"><b>[Aproximada]</b> Pedido a proveedor 220513, prevista el viernes 09/10/2026");
            StringAssert.Contains(html, "suponemos que llega el miércoles 14/10/2026 y puede salir el jueves 15/10/2026, pero puede retrasarse");
        }

        [TestMethod]
        public async Task SinPedir_ConPlazos_OrientativaSinTocarNingunaFecha()
        {
            ConPlazos();

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularYGuardarPrometidaAlCrear("1", PEDIDO);

            PartidaFechaEntregaAgenciaDTO b = B(fecha);
            // Próximo pedido dentro de 4 días (el 17/10) + 6 de plazo: llega el viernes 23/10 y sale el lunes 26/10
            Assert.AreEqual(new DateTime(2026, 10, 26), b.FechaOrientativa);
            Assert.IsNull(b.PuedeSalirEl);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA, b.Certeza);
            Assert.IsNull(fecha.FechaEntregaAgencia, "El pedido sigue sin fecha");
            Assert.IsNull(fecha.EntregaCompleta);
            A.CallTo(() => repositorio.GuardarPrometida(A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
            StringAssert.Contains(fecha.Motivo, "1 ud. de B sin fecha todavía: aún no hay pedido al proveedor que lo cubra; estimamos que podría salir " +
                "sobre el lunes 26/10 (orientativo, no es un compromiso).");
            string html = Html(fecha);
            StringAssert.Contains(html, "Entrega a la agencia: sin fecha</b>");
            StringAssert.Contains(html, "<li><b>[Orientativa]</b> Sin fecha todavía: B Guantes talla L (1 ud.): aún no hay pedido al proveedor que lo cubra; " +
                "estimamos que podría salir sobre el lunes 26/10/2026 (orientativo, no es un compromiso).</li>");
        }

        [TestMethod]
        public async Task SinPedir_SinPlazos_ComoHoy()
        {
            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            Assert.IsNull(B(fecha).FechaOrientativa);
            Assert.IsNull(B(fecha).Certeza);
            StringAssert.Contains(Html(fecha), "Sin fecha: B Guantes talla L (1 ud.): no hay stock ni fecha de llegada.");
            A.CallTo(() => lector.ProveedorHabitual(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PedidoAMasDe15Dias_SinFechaPeroConElPedidoYLaOrientativa()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 11, 20)));

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            PartidaFechaEntregaAgenciaDTO b = B(fecha);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, b.Origen, "La regla de los 15 días no cambia");
            Assert.IsNull(fecha.FechaEntregaAgencia);
            Assert.AreEqual(220513, b.PedidoProveedorOrientativo);
            Assert.AreEqual(new DateTime(2026, 11, 23), b.FechaOrientativa);
            Assert.AreEqual("hay un pedido a proveedor (220513) previsto para el viernes 20/11, a más de 15 días", b.MotivoSinFecha);
            StringAssert.Contains(Html(fecha), "<li><b>[Orientativa]</b> Sin fecha todavía: B Guantes talla L (1 ud.): el pedido a proveedor 220513 está " +
                "previsto para el viernes 20/11/2026; estimamos que podría salir sobre el lunes 23/11/2026 (orientativo, no es un compromiso).</li>");
            A.CallTo(() => lector.ProveedorHabitual(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiElLectorFalla_SinCertezaYLaMismaFecha()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 16)));
            A.CallTo(() => lector.CorreosAplicados(A<IEnumerable<int>>._)).Throws(new InvalidOperationException("caída"));

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", PEDIDO);

            Assert.AreEqual(new DateTime(2026, 10, 19), fecha.FechaEntregaAgencia);
            Assert.IsFalse(B(fecha).ConSeguimientoAgencia);
            Assert.AreEqual(1, registradas.Count);
        }

        [TestMethod]
        public async Task ConYSinLector_LasMismasFechas()
        {
            Stock(ConBDelProveedor(new DateTime(2026, 10, 16)));
            ConCorreoAplicado(new DateTime(2026, 10, 16));
            ConPlazos();

            FechaEntregaAgenciaDTO con = await servicio.CalcularPedido("1", PEDIDO);
            FechaEntregaAgenciaDTO sin = await Servicio(null).CalcularPedido("1", PEDIDO);

            Assert.AreEqual(sin.PrimeraEntrega, con.PrimeraEntrega);
            Assert.AreEqual(sin.EntregaCompleta, con.EntregaCompleta);
            CollectionAssert.AreEqual(sin.Desglose.Select(p => p.PuedeSalirEl).ToList(), con.Desglose.Select(p => p.PuedeSalirEl).ToList());
        }

        #region Piezas puras

        [TestMethod]
        public void Vigente_ElUltimoAplicadoConLaFechaDeAhora_YLaAgenciaDeCualquieraDelEnvio()
        {
            var correos = new List<CorreoAplicadoPedidoCompra>
            {
                new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 7, FechaRecepcionAplicada = new DateTime(2026, 11, 16), Agencia = "UPS", AplicadoEl = AHORA },
                new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 7, FechaRecepcionAplicada = new DateTime(2026, 11, 16), Agencia = null, AplicadoEl = AHORA.AddHours(1) },
                new CorreoAplicadoPedidoCompra { Empresa = "3", Pedido = 7, FechaRecepcionAplicada = new DateTime(2026, 11, 16), Agencia = "GLS", AplicadoEl = AHORA }
            };

            CorreoAplicadoPedidoCompra vigente = ReglaSeguimientoAgencia.Vigente(correos, "1", 7, new DateTime(2026, 11, 16));

            Assert.AreEqual("UPS", vigente.Agencia);
            Assert.IsNull(ReglaSeguimientoAgencia.Vigente(correos, "1", 7, new DateTime(2026, 11, 17)), "Otra fecha: no vale");
            Assert.IsNull(ReglaSeguimientoAgencia.Vigente(correos, "1", 8, new DateTime(2026, 11, 16)));
        }

        [TestMethod]
        public void Certeza_PorOrigen()
        {
            DateTime dia = AHORA.Date;
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA, TextosCertezaFechaEntrega.Certeza(new PartidaFechaEntregaAgenciaDTO { Origen = "Kit", PuedeSalirEl = dia }));
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA, TextosCertezaFechaEntrega.Certeza(new PartidaFechaEntregaAgenciaDTO { Origen = "EnCamino", PuedeSalirEl = dia }));
            Assert.IsNull(TextosCertezaFechaEntrega.Certeza(new PartidaFechaEntregaAgenciaDTO { Origen = "Tienda" }), "Tienda sin calendario");
            Assert.IsNull(TextosCertezaFechaEntrega.Certeza(new PartidaFechaEntregaAgenciaDTO { Origen = "Proveedor" }), "Pedido sin fecha prevista");
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA, TextosCertezaFechaEntrega.Certeza(new PartidaFechaEntregaAgenciaDTO
            {
                Origen = "Proveedor", PuedeSalirEl = dia, FechaPrevistaProveedor = dia, LlegadaSupuesta = dia, ConSeguimientoAgencia = true
            }), "Con seguimiento pero vencida: aproximada");
            Assert.AreEqual("Prácticamente segura", TextosCertezaFechaEntrega.Etiqueta(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA));
            Assert.IsNull(TextosCertezaFechaEntrega.Etiqueta(PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA));
        }

        [TestMethod]
        public void Fotos_ProveedorConSeguimiento_EsEnTransito()
        {
            var calculo = new FechaEntregaAgenciaDTO
            {
                Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    new PartidaFechaEntregaAgenciaDTO { Origen = "Proveedor", Producto = "B", Unidades = 1, PuedeSalirEl = new DateTime(2026, 10, 19),
                        FechaPrevistaProveedor = new DateTime(2026, 10, 16), PedidoProveedor = 220513, ConSeguimientoAgencia = true },
                    new PartidaFechaEntregaAgenciaDTO { Origen = "Proveedor", Producto = "C", Unidades = 1, PuedeSalirEl = new DateTime(2026, 10, 19),
                        FechaPrevistaProveedor = new DateTime(2026, 10, 16), PedidoProveedor = 220514 }
                }
            };

            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", PEDIDO, calculo, AHORA.Date);

            Assert.AreEqual(CasosFotoFechaEntrega.EN_TRANSITO, fotos.Single(f => f.Producto == "B").Caso);
            Assert.AreEqual(CasosFotoFechaEntrega.PEDIDO_ENVIADO, fotos.Single(f => f.Producto == "C").Caso);
        }

        [TestMethod]
        public void PromesaEnRiesgo_ProveedorConSeguimientoYSinPedirConOrientativa()
        {
            var conSeguimiento = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = "Proveedor", Producto = "B", Unidades = 2, PuedeSalirEl = new DateTime(2026, 11, 16), FechaPrevistaProveedor = new DateTime(2026, 11, 13),
                PedidoProveedor = 220672, ConSeguimientoAgencia = true, AgenciaSeguimiento = "GLS"
            };
            var orientativa = new PartidaFechaEntregaAgenciaDTO { Origen = "SinStock", Producto = "C", Unidades = 1, FechaOrientativa = new DateTime(2026, 11, 24) };

            Assert.AreEqual("no hay stock: llegan con el pedido al proveedor 220672, ya enviado por GLS, que prevé entregarlo el viernes 13/11/2026, " +
                "y pueden salir el lunes 16/11/2026 (fecha prácticamente segura)", TextosPromesaRiesgo.FraseCulpable(conSeguimiento));
            Assert.AreEqual("no hay stock: aún no hay pedido al proveedor que lo cubra; estimamos que podría salir sobre el martes 24/11/2026 " +
                "(orientativo, no es un compromiso)", TextosPromesaRiesgo.FraseCulpable(orientativa));
        }

        #endregion
    }
}
