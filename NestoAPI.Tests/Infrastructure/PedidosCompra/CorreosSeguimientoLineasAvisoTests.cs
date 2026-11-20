using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// 20/11/26 (Carlos): la fecha de recepción, línea a línea, por los productos que dice el aviso de salida del proveedor. Caso real: «Albarán
    /// WK/OUT/78316» de Weelko (proveedor 413) con F806.N ×2 y WKB001 ×1, que son el 32831 y el 40695 del pedido 220931 (el otro pedido en el
    /// aire de Weelko, el 220181, no lleva ninguno de los dos).
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoLineasAvisoTests
    {
        private const string WEELKO_SALIDA = "Hola NUEVA VISION, SA ¡Su pedido está preparado y ha salido de nuestro almacén! Podrá seguir el estado de la entrega a través "
            + "del siguiente enlace: Tracking Le informamos que en su espacio personal de la B2B ya puede consultar y descargar su factura electrónica. La recibirá "
            + "también por correo electrónico. Dirección de entrega Fecha de la compra Número de pedido Referencia cliente NUEVA VISION, SA 19/11/2026 07:32:59 "
            + "WK/OUT/78316 29636 El pedido será enviado por: TXT 2026.V2 Resumen del pedido: Producto Cantidad [F806.N] UTECH HIGH FREQUENCY UTECH HIGH FREQUENCY - "
            + "HIGH FREQUENCY 2,00 [WKB001] HIGHTECH AIR HIGHTECH AIR - PRESSOTHERAPY MACHINE 1,00 TXT 2026.V2: Servicio de transporte 1,00 ¡Quedamos a su "
            + "disposición! Hasta pronto. WEELKO BARCELONA, S.L.";

        private static readonly DateTime AHORA = new DateTime(2026, 11, 20, 13, 30, 0);
        private static readonly DateTime ANTES = new DateTime(2026, 11, 26);
        private static int siguiente = 1;

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private List<Exception> errores;
        private List<(CorreoCompras Correo, CorreoSeguimientoFila Fila, ResultadoCorreoGuardado Resultado)> reenviados;

        #region Nivel 1: el formato

        [TestMethod]
        public void Weelko_Lineas_CasoReal_DosProductosSinElTransporte()
        {
            List<LineaAvisoSalida> lineas = new FormatoAvisoSalidaWeelko().Lineas("Albarán WK/OUT/78316\n" + WEELKO_SALIDA);

            Assert.AreEqual(2, lineas.Count, "la línea del transporte (sin corchetes) no es un producto");
            Assert.AreEqual("F806.N", lineas[0].ReferenciaProveedor);
            Assert.AreEqual(2m, lineas[0].Cantidad, "cantidad con coma decimal");
            Assert.AreEqual("UTECH HIGH FREQUENCY UTECH HIGH FREQUENCY - HIGH FREQUENCY", lineas[0].Descripcion);
            Assert.AreEqual("WKB001", lineas[1].ReferenciaProveedor);
            Assert.AreEqual(1m, lineas[1].Cantidad);
        }

        [TestMethod]
        public void Weelko_Lineas_CantidadConDecimales_YConfirmacionSinLineas()
        {
            var formato = new FormatoAvisoSalidaWeelko();
            List<LineaAvisoSalida> lineas = formato.Lineas(WEELKO_SALIDA.Replace("HIGH FREQUENCY 2,00", "HIGH FREQUENCY 12,50"));
            Assert.AreEqual(12.5m, lineas[0].Cantidad);
            Assert.AreEqual(0, formato.Lineas("Pedido SO253360 Hemos recibido su pedido correctamente Resumen del pedido: [F806.N] UTECH 2,00").Count,
                "la confirmación del pedido no pone fecha: sin líneas");
            Assert.AreEqual(0, new FormatoAvisoSalidaDduueett().Lineas("Transportista: SCHENKER\nNúmero de seguimiento: ESMNR264773770").Count,
                "por defecto, ninguna");
        }

        [TestMethod]
        public void Analizador_AvisoDeWeelko_TraeSusLineas()
        {
            AnalisisCorreoSeguimiento a = AnalizadorCorreoSeguimiento.Analizar(Correo(WEELKO_SALIDA), AgenciasEntradaRegistradas.Todas, new List<RemitenteMarcado>());
            Assert.AreEqual(2, a.LineasAviso.Count);
        }

        #endregion

        #region LA REGLA

        private static List<LineaAvisoSalida> Aviso(decimal f806 = 2, decimal wkb001 = 1) => new List<LineaAvisoSalida>
        {
            new LineaAvisoSalida { ReferenciaProveedor = "F806.N", Cantidad = f806 },
            new LineaAvisoSalida { ReferenciaProveedor = "WKB001", Cantidad = wkb001 }
        };

        private static List<ReferenciaProductoProveedor> Referencias() => new List<ReferenciaProductoProveedor>
        {
            new ReferenciaProductoProveedor { Proveedor = "413", ReferenciaProveedor = "F806.N", Producto = "32831" },
            new ReferenciaProductoProveedor { Proveedor = "413", ReferenciaProveedor = "WKB001", Producto = "40695" },
            new ReferenciaProductoProveedor { Proveedor = "413", ReferenciaProveedor = "WK045", Producto = "45654" }
        };

        private static LineaEnAireAviso L(int pedido, int orden, string producto, int cantidad) =>
            new LineaEnAireAviso { Empresa = "1", Pedido = pedido, NumeroOrden = orden, Producto = producto, Cantidad = cantidad, Proveedor = "413" };

        private static List<LineaEnAireAviso> LineasReales() => new List<LineaEnAireAviso>
        {
            L(220181, 1, "45654", 2), L(220181, 2, "45182", 6), L(220181, 3, "45182", 10), L(220181, 4, "45654", 2), L(220181, 5, "33253", 3),
            L(220931, 1, "32831", 2), L(220931, 2, "40695", 1)
        };

        [TestMethod]
        public void Regla_UnCandidatoCasaEntero()
        {
            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(), Referencias(), LineasReales());

            Assert.IsTrue(r.Decide);
            Assert.AreEqual(220931, r.Pedido.Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO, r.Pedido.Motivo);
            CollectionAssert.AreEqual(new[] { 1, 2 }, r.Casadas.Select(c => c.NumeroOrden).ToArray());
            Assert.IsFalse(r.Casadas.Any(c => c.Parcial));
            Assert.AreEqual(0, r.SinTraducir);
            Assert.AreEqual("32831 (2 uds.) y 40695 (1 ud.)", TextosLineasAviso.Lista(r.Casadas));
        }

        [TestMethod]
        public void Regla_MenosUnidadesQueLoPendiente_EsParcialYSePoneIgual()
        {
            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(f806: 1), Referencias(), LineasReales());

            Assert.AreEqual(220931, r.Pedido.Pedido);
            LineaCasadaAviso parcial = r.Casadas.Single(c => c.Producto == "32831");
            Assert.IsTrue(parcial.Parcial);
            Assert.AreEqual(1m, parcial.Unidades);
            Assert.AreEqual(2, parcial.Cantidad);
            Assert.AreEqual("Envío parcial: de 32831 vienen 1 de 2 uds.", TextosLineasAviso.Parcial(r.Casadas));
        }

        [TestMethod]
        public void Regla_UnidadesSeRepartenPorOrden_LaLineaQueNoRecibeNadaNoEsDelEnvio()
        {
            var aviso = new List<LineaAvisoSalida> { new LineaAvisoSalida { ReferenciaProveedor = "WK182", Cantidad = 6 } };
            var refs = new List<ReferenciaProductoProveedor> { new ReferenciaProductoProveedor { Proveedor = "413", ReferenciaProveedor = "WK182", Producto = "45182" } };

            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(aviso, refs, LineasReales());

            Assert.AreEqual(220181, r.Pedido.Pedido);
            Assert.AreEqual(2, r.Casadas.Single().NumeroOrden, "las 6 uds. llenan la línea 2 (6); la 3 (10) no viene en este envío");
            Assert.IsFalse(r.Casadas.Single().Parcial);
        }

        [TestMethod]
        public void Regla_ReferenciaDesconocida_SeCuentaYNoImpideCasarLasDemas()
        {
            List<LineaAvisoSalida> aviso = Aviso();
            aviso.Add(new LineaAvisoSalida { ReferenciaProveedor = "XYZ999", Cantidad = 3 });

            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(aviso, Referencias(), LineasReales());

            Assert.AreEqual(220931, r.Pedido.Pedido);
            Assert.AreEqual(1, r.SinTraducir);
            Assert.AreEqual(3, r.LineasAviso);
        }

        [TestMethod]
        public void Regla_CasanLineasDeDosPedidos_GanaElUnicoQueCasaElAvisoEntero()
        {
            // Carlos 20/11/26: «dentro de una seguridad razonable, cuanto más automático mejor». El 220931 casa las dos líneas con todas
            // sus unidades; el 220181 solo tiene una unidad de uno de los dos productos.
            List<LineaEnAireAviso> lineas = LineasReales();
            lineas.Add(L(220181, 6, "32831", 1));

            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(), Referencias(), lineas);

            Assert.AreEqual(220931, r.Pedido?.Pedido);
            CollectionAssert.AreEquivalent(new[] { 220181, 220931 }, r.PedidosQueCasan.Select(p => p.Pedido).ToArray());
            AsignacionPedidoCompra a = ReglaLineasAvisoSalida.Completar(new AsignacionPedidoCompra(), r);
            Assert.AreEqual(1, a.Candidatos.Count, "inequívoco: solo el que casa entero");
            Assert.AreEqual(220931, a.Candidatos[0].Pedido);
        }

        [TestMethod]
        public void Regla_DosPedidosCasanElAvisoEntero_NoSeEligeANingunoAciegas()
        {
            List<LineaEnAireAviso> lineas = LineasReales();
            lineas.Add(L(220181, 6, "32831", 2));
            lineas.Add(L(220181, 7, "40695", 1));

            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(), Referencias(), lineas);

            Assert.IsNull(r.Pedido);
            AsignacionPedidoCompra a = ReglaLineasAvisoSalida.Completar(new AsignacionPedidoCompra(), r);
            Assert.AreEqual(2, a.Candidatos.Count, "se deja por asignar con los dos");
        }

        [TestMethod]
        public void Regla_NingunoCasaElAvisoEntero_NoSeEligeANingunoAciegas()
        {
            // Cada pedido tiene solo uno de los dos productos del aviso
            var lineas = new List<LineaEnAireAviso> { L(220181, 6, "32831", 2), L(220931, 1, "40695", 1) };

            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(), Referencias(), lineas);

            Assert.IsNull(r.Pedido);
            Assert.AreEqual(2, r.PedidosQueCasan.Count);
        }

        [TestMethod]
        public void Regla_NoCasaNinguna_NoDecideYLaAsignacionQuedaComoEstaba()
        {
            var otras = new List<ReferenciaProductoProveedor> { new ReferenciaProductoProveedor { Proveedor = "413", ReferenciaProveedor = "OTRA", Producto = "1" } };
            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(), otras, LineasReales());
            var asignacion = new AsignacionPedidoCompra { Candidatos = new List<CandidatoPedidoCompra> { new CandidatoPedidoCompra { Pedido = 1 } } };

            Assert.IsFalse(r.Decide);
            Assert.AreEqual(2, r.SinTraducir);
            Assert.AreSame(asignacion, ReglaLineasAvisoSalida.Completar(asignacion, r));
        }

        [TestMethod]
        public void Regla_ElNumeroDelPedidoEnElTextoMandaSobreLasLineas()
        {
            ResultadoLineasAviso r = ReglaLineasAvisoSalida.Casar(Aviso(), Referencias(), LineasReales());
            var porNumero = new AsignacionPedidoCompra
            {
                Candidatos = new List<CandidatoPedidoCompra> { new CandidatoPedidoCompra { Empresa = "1", Pedido = 220181, Motivo = AsignadorPedidosCompra.MOTIVO_NUMERO } }
            };
            Assert.AreSame(porNumero, ReglaLineasAvisoSalida.Completar(porNumero, r));
        }

        #endregion

        #region Certeza por línea (#853)

        [TestMethod]
        public void Certeza_ElCorreoConLineasSoloDaSeguimientoASusProductos()
        {
            DateTime dia = new DateTime(2026, 11, 24);
            var correo = new CorreoAplicadoPedidoCompra
            {
                Empresa = "1", Pedido = 220931, FechaRecepcionAplicada = dia, Agencia = "UPS", ProductosAplicados = new List<string> { "32831", "40695" }
            };
            Assert.IsNotNull(ReglaSeguimientoAgencia.Vigente(new[] { correo }, "1", 220931, dia, "32831"));
            Assert.IsNull(ReglaSeguimientoAgencia.Vigente(new[] { correo }, "1", 220931, dia, "12345"), "a esa línea no le puso la fecha este correo");
            correo.ProductosAplicados = null;
            Assert.IsNotNull(ReglaSeguimientoAgencia.Vigente(new[] { correo }, "1", 220931, dia, "12345"), "sin líneas del aviso, todo el pedido (como antes)");
        }

        [TestMethod]
        public void Certeza_LeeLosProductosDeLineasAplicadas()
        {
            string json = LineasAvisoCorreo.SerializarAplicadas(new List<LineaCasadaAviso>
            {
                new LineaCasadaAviso { NumeroOrden = 1, Producto = "32831" }, new LineaCasadaAviso { NumeroOrden = 2, Producto = "40695" }
            });
            CollectionAssert.AreEqual(new[] { "32831", "40695" }, LectorCertezaFechaEntregaSql.ProductosAplicados(json).ToArray());
            Assert.IsNull(LectorCertezaFechaEntregaSql.ProductosAplicados(null));
            Assert.IsNull(LectorCertezaFechaEntregaSql.ProductosAplicados("no es json"));
        }

        #endregion

        #region De punta a punta (job y asignación a mano)

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            Pedido(220181, ("45654", 2), ("45182", 6), ("45182", 10), ("45654", 2), ("33253", 3));
            // El 220931 con una línea más de otro producto, para ver que no se toca
            Pedido(220931, ("32831", 2), ("40695", 1), ("12345", 4));
            repositorio.Referencias.AddRange(Referencias());
            errores = new List<Exception>();
            reenviados = new List<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>();
        }

        private void Pedido(int numero, params (string Producto, int Cantidad)[] lineas)
        {
            repositorio.AnadirPedido(numero, "413", "WEELKO BARCELONA, S.L.", new DateTime(2026, 11, 19, 7, 30, 0), lineas.Select(_ => (DateTime?)ANTES).ToArray());
            repositorio.Pedidos.Last().Web = "www.weelko.com";
            repositorio.Lineas[numero] = lineas.Select((l, i) => new LineaCompraCorreo
            {
                NumeroOrden = i + 1, Producto = l.Producto, Estado = 1, Enviado = true, Cantidad = l.Cantidad, FechaRecepcion = ANTES, Proveedor = "413"
            }).ToList();
        }

        private static CorreoCompras Correo(string cuerpo, DateTime? recibido = null) => new CorreoCompras
        {
            Id = "g" + siguiente, MensajeId = "<lineas" + siguiente++ + "@ejemplo.test>", Recibido = recibido ?? new DateTime(2026, 11, 20, 13, 3, 0),
            RemitenteDireccion = "noreply@weelko.com", Asunto = "Albarán WK/OUT/78316", Cuerpo = cuerpo
        };

        private ServicioAplicacionCorreosCompras Aplicacion() => new ServicioAplicacionCorreosCompras(repositorio, () => true, A.Fake<IAvisadorCorreosPorAsignar>(),
            (empresa, productos, descripcion, causas) => { }, () => AHORA, errores.Add, _ => false);

        private async Task Pasada(params CorreoCompras[] correos)
        {
            ILectorBuzonCompras lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(new LecturaBuzonCompras { Correos = correos.ToList() });
            IReenviadorCorreosSeguimiento reenviador = A.Fake<IReenviadorCorreosSeguimiento>();
            A.CallTo(() => reenviador.Reenviar(A<IEnumerable<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>>._))
                .ReturnsLazily((IEnumerable<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)> lista) =>
                {
                    reenviados.AddRange(lista);
                    return lista.Count();
                });
            var servicio = new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, horaCorte: () => new TimeSpan(11, 0, 0),
                esFestivo: _ => false, aplicacion: Aplicacion(), reenviador: reenviador);
            _ = await servicio.Pasada(AHORA);
        }

        [TestMethod]
        public async Task Job_AvisoDeWeelko_DosPedidosEnElAire_QuedaInequivocoYSoloCambiaSusLineas()
        {
            await Pasada(Correo(WEELKO_SALIDA));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            CandidatoPedidoCompra candidato = AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos).Single();
            Assert.AreEqual(220931, candidato.Pedido, "de los dos pedidos de Weelko, el que tiene esos productos");
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO, candidato.Motivo);
            Assert.AreEqual(220931, fila.PedidoAplicado);
            DateTime nueva = fila.FechaRecepcionAplicada.Value;
            Assert.AreNotEqual(ANTES, nueva);
            Assert.AreEqual(nueva, repositorio.Lineas[220931][0].FechaRecepcion);
            Assert.AreEqual(nueva, repositorio.Lineas[220931][1].FechaRecepcion);
            Assert.AreEqual(ANTES, repositorio.Lineas[220931][2].FechaRecepcion, "el 12345 no viene en el aviso: no se toca");
            Assert.IsTrue(repositorio.Lineas[220181].All(l => l.FechaRecepcion == ANTES), "el otro pedido no se toca");
            CollectionAssert.AreEqual(new[] { 1, 2 }, repositorio.Rastros.Single().Ordenes.ToArray());
            CollectionAssert.AreEqual(new[] { "32831", "40695" }, LineasAvisoCorreo.LeerAplicadas(fila.LineasAplicadas).Select(l => l.Producto).ToArray());
            Assert.AreEqual(2, LineasAvisoCorreo.Leer(fila.LineasAviso).Lineas.Count);
            Assert.AreEqual(1, repositorio.LecturasLineasAviso, "una lectura por tanda");

            string texto = TextosReenvioCorreoSeguimiento.QueSeHaHecho(reenviados.Single().Resultado);
            StringAssert.Contains(texto, $"Fecha de recepción del 26/11 al {nueva:dd/MM} en 2 líneas del pedido de compra 220931: 32831 (2 uds.) y 40695 (1 ud.).");
            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));
        }

        [TestMethod]
        public async Task Job_SinLaColumnaNueva_LaFechaVaATodoElPedido()
        {
            repositorio.ConLineas = false;
            await Pasada(Correo(WEELKO_SALIDA));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual(220931, fila.PedidoAplicado, "el pedido se sabe igual por las líneas");
            Assert.IsTrue(repositorio.Lineas[220931].All(l => l.FechaRecepcion == fila.FechaRecepcionAplicada), "sin la columna, como antes: todas sus líneas");
            Assert.IsNull(repositorio.Rastros.Single().Ordenes);
            Assert.IsNull(fila.LineasAplicadas);
        }

        [TestMethod]
        public async Task Job_AvisoSinLineas_ComoAntes()
        {
            repositorio.Pedidos.RemoveAll(p => p.Pedido == 220181);
            repositorio.Lineas.Remove(220181);
            string sinResumen = WEELKO_SALIDA.Substring(0, WEELKO_SALIDA.IndexOf("Resumen del pedido", StringComparison.Ordinal));

            await Pasada(Correo(sinResumen));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.IsNull(fila.LineasAviso);
            Assert.AreEqual(0, repositorio.LecturasLineasAviso, "sin líneas del aviso no se lee nada más");
            Assert.AreEqual(220931, fila.PedidoAplicado);
            Assert.IsTrue(repositorio.Lineas[220931].All(l => l.FechaRecepcion == fila.FechaRecepcionAplicada), "todo el pedido, como antes");
            Assert.IsNull(repositorio.Rastros.Single().Ordenes);
        }

        [TestMethod]
        public async Task Job_LineasDeDosPedidos_GanaElQueCasaElAvisoEntero()
        {
            // El 220181 solo tiene una unidad de uno de los dos productos: el 220931 casa el aviso entero y queda inequívoco
            repositorio.Lineas[220181].Add(new LineaCompraCorreo { NumeroOrden = 6, Producto = "32831", Estado = 1, Enviado = true, Cantidad = 1, FechaRecepcion = ANTES, Proveedor = "413" });

            await Pasada(Correo(WEELKO_SALIDA));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual(220931, fila.PedidoAplicado);
            Assert.IsTrue(repositorio.Lineas[220181].All(l => l.FechaRecepcion == ANTES), "el otro pedido no se toca");
        }

        [TestMethod]
        public async Task Job_DosPedidosCasanElAvisoEntero_QuedaPorAsignarConLosDos()
        {
            repositorio.Lineas[220181].Add(new LineaCompraCorreo { NumeroOrden = 6, Producto = "32831", Estado = 1, Enviado = true, Cantidad = 2, FechaRecepcion = ANTES, Proveedor = "413" });
            repositorio.Lineas[220181].Add(new LineaCompraCorreo { NumeroOrden = 7, Producto = "40695", Estado = 1, Enviado = true, Cantidad = 1, FechaRecepcion = ANTES, Proveedor = "413" });

            await Pasada(Correo(WEELKO_SALIDA));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.IsNull(fila.PedidoAplicado);
            CollectionAssert.AreEquivalent(new[] { 220181, 220931 }, AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos).Select(c => c.Pedido).ToArray());
        }

        [TestMethod]
        public async Task AMano_SoloLasLineasQueCasanConEsePedido_YSiNingunaTodoElPedido()
        {
            repositorio.Lineas[220181].Add(new LineaCompraCorreo { NumeroOrden = 6, Producto = "32831", Estado = 1, Enviado = true, Cantidad = 3, FechaRecepcion = ANTES, Proveedor = "413" });
            repositorio.Lineas[220181].Add(new LineaCompraCorreo { NumeroOrden = 7, Producto = "40695", Estado = 1, Enviado = true, Cantidad = 1, FechaRecepcion = ANTES, Proveedor = "413" });
            await Pasada(Correo(WEELKO_SALIDA));
            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.IsNull(fila.PedidoAplicado, "dos pedidos casan el aviso entero: por asignar");

            ResultadoOperacionCorreoCompra r = await Aplicacion().Asignar(fila.Id, new PeticionAsignarCorreoCompraDTO { Pedido = 220181 }, false, "compras");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo, r.Mensaje);
            Assert.AreEqual(2, r.Respuesta.LineasCambiadas, "solo las del 32831 y el 40695");
            Assert.IsTrue(r.Respuesta.EnvioParcial, "vienen 2 de las 3 pendientes");
            Assert.AreEqual(ANTES, repositorio.Lineas[220181][0].FechaRecepcion);
            Assert.AreNotEqual(ANTES, repositorio.Lineas[220181][5].FechaRecepcion);
            Assert.AreNotEqual(ANTES, repositorio.Lineas[220181][6].FechaRecepcion);
            StringAssert.Contains(r.Mensaje, "Envío parcial: de 32831 vienen 2 de 3 uds.");

            // Reasignado a un pedido con el que no casa nada: todo el pedido
            repositorio.AnadirPedido(220999, "413", "WEELKO BARCELONA, S.L.", new DateTime(2026, 11, 18), ANTES, ANTES);
            ResultadoOperacionCorreoCompra otro = await Aplicacion().Asignar(fila.Id, new PeticionAsignarCorreoCompraDTO { Pedido = 220999 }, true, "compras");
            Assert.AreEqual(2, otro.Respuesta.LineasCambiadas);
            Assert.IsNull(otro.Respuesta.LineasAviso);
            Assert.IsNull(repositorio.Correo(fila.Id).LineasAplicadas);
        }

        [TestMethod]
        public async Task MismoEnvio_ElCorreoDeLaAgenciaHeredaLasLineasDelAviso()
        {
            await Pasada(Correo(WEELKO_SALIDA));
            CorreoSeguimientoFila aviso = repositorio.Correos.Single();
            // Dos correos del mismo envío por código (simulado): el del proveedor ya aplicado y uno de la agencia sin líneas
            var agencia = new CorreoSeguimientoFila { Id = 99, Recibido = AHORA, Codigo = "1Z999", Agencia = "UPS" };
            Assert.AreEqual(2, LineasAvisoCorreo.DelEnvio(new[] { aviso, agencia }).Count);
            Assert.AreEqual(0, LineasAvisoCorreo.DelEnvio(new[] { agencia }).Count);
        }

        #endregion
    }
}
