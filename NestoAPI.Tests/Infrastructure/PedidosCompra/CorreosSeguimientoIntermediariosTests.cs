using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857g (Carlos 19/11/26): Nacex lee su entrega; MBE es un intermediario (remitente, referencia del proveedor, mensajero real y
    /// sus códigos, enlazado por mismo envío con los avisos de UPS); la confirmación de pedido de Weelko enlaza el pedido del proveedor con el
    /// nuestro sin ser un envío; DB Schenker cuenta su plazo en laborables. Con los textos reales del buzón.
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoIntermediariosTests
    {
        #region Ejemplos reales

        internal const string NACEX_ASUNTO = "¡Hola NUEVA VISION SA! Tu envío de LASHES AND MORE SL está en camino con NACEX";
        internal const string NACEX_HTML = "<html><body><p>¡Hola NUEVA VISION SA!, ya tenemos tu envío de LASHES AND MORE SL y está en camino con el número de " +
            "seguimiento 3300/11571609.</p><p>Este es tu PIN para una entrega segura: 8197</p><p>Tu entrega está programada para el 16/11/2026 antes de las 20:00.</p>" +
            "<p>Si necesitas redirigir tu envío, ¡podemos ayudarte! <a href=\"https://www.nacex.es/s.do?exp=503411773&amp;idioma=ES\">Gestiona tu envío</a></p>" +
            "<p>¿No puedes recibir el envío? Entrega vecino</p><p>NACEX.shop</p><p>LA PERCHITA DE NONI (2801-27)</p><p>MAYOR 16 LOCAL B 28110 ALGETE</p>" +
            "<p>HORARIOS Lun - Vie : 10:00-13:30</p>" +
            "<p><a href=\"https://www.nacex.com/seguimientoDetalle.do?agencia_origen=3300&amp;numero_albaran=11571609&amp;idioma=ES\">Seguimiento</a></p></body></html>";
        internal const string NACEX_SIN_PROGRAMADA = "¡Hola NUEVA VISION SA!, ya tenemos tu envío de LASHES AND MORE SL y está en camino con el número de seguimiento 3300/11571609.\n" +
            "NACEX.shop\nLA PERCHITA DE NONI (2801-27)\nMAYOR 16 LOCAL B 28110 ALGETE\nHORARIOS Lun - Vie : 10:00-13:30\nSábado: 10:00-14:00";

        internal const string MBE_WEELKO = "Tus envíos MBE\nEnvío: 1\nRemitente:\nWeelko Barcelona S. L.\nCamí de Garreta, 92 Pere Bozzo\n08600 BERGA BARCELONA España\n" +
            "Destinatario:\nNUEVA VISION, SA SANTI\nRio Tietar, 11 nave 22\n28110 ALGETE MADRID España\nEnvío:\nServicio: MBE Standard\nPeso: 27,0 kg/vol.\n" +
            "N. de paquetes: 3\nRef.: SO252231WKOUT77626\nTracking MBE: ES0061-0L-000000VTHP\nCourier: UPS\nTracking: 1Z21E7X76890915153\nDetalles:\n" +
            "Tracking: 1Z21E7X76890915153- estado:Etiqueta creada\nTracking: 1Z21E7X76890089163- estado:Etiqueta creada\n" +
            "Tracking: 1Z21E7X76893119777- estado:Etiqueta creada\nSiga su envío";
        /// <summary>Tal como se pegó en la issue (todo en una línea).</summary>
        internal const string MBE_WEELKO_PLANO = "Tus envíos MBE Envío: 1 Remitente: Weelko Barcelona S. L. Camí de Garreta, 92 Pere Bozzo 08600 BERGA BARCELONA España " +
            "Destinatario: NUEVA VISION, SA SANTI Rio Tietar, 11 nave 22 28110 ALGETE MADRID España Envío: Servicio: MBE Standard Peso: 27,0 kg/vol. " +
            "N. de paquetes: 3 Ref.: SO252231WKOUT77626 Tracking MBE: ES0061-0L-000000VTHP Courier: UPS Tracking: 1Z21E7X76890915153 Detalles: " +
            "Tracking: 1Z21E7X76890915153- estado:Etiqueta creada Tracking: 1Z21E7X76890089163- estado:Etiqueta creada Tracking: 1Z21E7X76893119777- " +
            "estado:Etiqueta creada Siga su envío";
        internal const string MBE_NUESTRA = "Tus envíos MBE\nEnvío: 1\nRemitente:\nNUEVA VISION SA\nRio Tietar, 11 nave 22\n28110 ALGETE MADRID España\n" +
            "Destinatario:\nIOANA POPESCU\n12 rue de la Paix\n75002 PARIS Francia\nEnvío:\nServicio: MBE Standard\nRef.: 925001\n" +
            "Tracking MBE: ES0061-0L-000000VTAA\nCourier: UPS\nTracking: 1Z21E7X76890000011\nSiga su envío";
        internal static readonly string MBE_MIXTO = MBE_NUESTRA.Replace("Siga su envío", string.Empty) + "\nEnvío: 2\nRemitente:\nWeelko Barcelona S. L.\nCamí de Garreta, 92\n" +
            "Destinatario:\nNUEVA VISION, SA SANTI\nRef.: SO253360WKOUT80001\nTracking MBE: ES0061-0L-000000VTZZ\nCourier: UPS\nTracking: 1Z21E7X76890999999\nSiga su envío";

        internal const string UPS_ESTIMADA = "¡Tu paquete está de camino!\nDe MBE STORE C/O MULTIBOXES S.L\n\nEntrega estimada\nLunes 16/11/2026\nentre 10:45 - 14:45\n\n" +
            "Número de seguimiento\n1Z21E7X76890915153";

        internal const string WEELKO_CONFIRMACION = "Hola NUEVA VISION, S.A.\nLe agradecemos su confianza.\nHemos recibido su pedido correctamente y lo estamos tramitando. " +
            "Cuando su pedido salga de nuestros almacenes, recibirá un correo electrónico con la confirmación del envío.\n" +
            "Dirección de entrega Fecha de compra Número del pedidor Referencia del cliente\nNUEVA VISION, SA 19/11/2026 07:28:47 SO253360 21325\n" +
            "El pedido será enviado por: MAILBOXES\nResumen del pedido:\nProducto Cantidad Precio Impuestos Subtotal\n" +
            "[P02] MOVE P02 MOVE - HIGH FREQUENCY 3,00 20,00 IVA 21% (Bienes) 60,00 €\nMAILBOXES: Servicio de transporte 1,00 7,36\nTotal 81,51 €";
        internal const string WEELKO_FACTURA = "Estimado cliente:\nAdjuntamos su factura BCN/2026/27757 (con referencia SO252231).\nGracias por su confianza.";
        internal const string WEELKO_PUBLICIDAD_HTML = "<html><body><p>COT02: frío y compresión · 455 €</p><p>Descubre la nueva cabina de tratamiento.</p>" +
            "<p><a href=\"https://track.weelko.com/click.php?L=abc123&amp;J=99\">Ver oferta</a></p><p><a href=\"https://www.weelko.com/baja\">Darse de baja</a></p></body></html>";

        private static int siguiente = 1;

        private static CorreoCompras Correo(string remitente, string asunto, string cuerpo, DateTime recibido, bool html = false) => new CorreoCompras
        {
            Id = "i" + siguiente, MensajeId = "<i" + siguiente++ + "@ejemplo.test>", Recibido = recibido, RemitenteDireccion = remitente,
            Asunto = asunto, Cuerpo = cuerpo, CuerpoEsHtml = html
        };

        private static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo) =>
            AnalizadorCorreoSeguimiento.Analizar(correo, AgenciasEntradaRegistradas.Todas, new List<RemitenteMarcado>());

        private static readonly DateTime JUEVES_12 = new DateTime(2026, 11, 12, 9, 30, 0);

        #endregion

        #region Nacex

        [TestMethod]
        public void Nacex_LeeSuEntregaProgramada_HastaLas20_EnCamino()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("no-reply@nacex.com", NACEX_ASUNTO, NACEX_HTML, new DateTime(2026, 11, 13, 17, 0, 0), true));

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual("Nacex", a.Agencia);
            Assert.AreEqual("11571609", a.Seguimientos.First().Codigo, "El albarán (el mismo que su enlace)");
            Assert.AreEqual(new DateTime(2026, 11, 16), a.EntregaEstimada.Dia);
            Assert.IsNull(a.EntregaEstimada.Desde);
            Assert.AreEqual(new TimeSpan(20, 0, 0), a.EntregaEstimada.Hasta);
            Assert.AreEqual(EstadoEnvioAgencia.EnCamino, a.EstadoEnvio);
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, a.OrigenEntrega);
            Assert.AreEqual("lashes and more sl", a.RemitentePaquete);
        }

        [TestMethod]
        public void Nacex_LosHorariosDelPuntoNacexShop_NoSonLaEntrega_YSinEnlaceElCodigoSaleDelTexto()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("no-reply@nacex.com", NACEX_ASUNTO, NACEX_SIN_PROGRAMADA, new DateTime(2026, 11, 13, 17, 0, 0)));

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual("Nacex", a.Agencia);
            Assert.AreEqual("11571609", a.Seguimientos.Single().Codigo);
            Assert.IsNull(a.EntregaEstimada, "«HORARIOS Lun - Vie : 10:00-13:30» es el del punto de recogida");
            Assert.AreEqual(EstadoEnvioAgencia.EnCamino, a.EstadoEnvio);
        }

        #endregion

        #region MBE: intermediario

        [TestMethod]
        public void Mbe_EsUnIntermediario_RemitenteReferenciaMensajeroYTodosLosCodigos()
        {
            foreach (string texto in new[] { MBE_WEELKO, MBE_WEELKO_PLANO })
            {
                AnalisisCorreoSeguimiento a = Analizar(Correo("recogidas061@mbe.es", "Sus envíos con MBE 12/11/2026", texto, JUEVES_12));

                Assert.IsTrue(a.Guardar);
                Assert.AreEqual("MBE", a.Agencia, "Lo manda MBE, aunque traiga códigos 1Z de UPS");
                Assert.IsFalse(a.EsSalidaNuestra, "NUEVA VISION es el destinatario");
                Assert.AreEqual("weelko barcelona s. l", a.RemitentePaquete, "Solo el nombre, sin la dirección");
                CollectionAssert.AreEqual(new[] { "SO252231WKOUT77626" }, a.ReferenciasRemitente);
                CollectionAssert.AreEqual(new[] { "1Z21E7X76890915153", "1Z21E7X76890089163", "1Z21E7X76893119777", "ES0061-0L-000000VTHP" },
                    a.Seguimientos.Select(s => s.Codigo).ToList());
                Assert.IsTrue(a.Seguimientos.Take(3).All(s => s.Agencia == "UPS"), "Los del mensajero real, marcados con su agencia");
                Assert.IsNull(a.Seguimientos[3].Agencia, "El de MBE es del correo");
                Assert.AreEqual("https://www.ups.com/track?tracknum=1Z21E7X76890915153", a.Seguimientos[0].Url, "El enlace lo compone UPS");
                Assert.IsNull(a.EntregaEstimada, "MBE no inventa fecha");
                Assert.AreEqual(24, a.HorasHastaEntrega);
            }
        }

        [TestMethod]
        public void Mbe_ResumenDeNuestrasSalidas_EsSalidaNuestra()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("mbe225@mbe.es", "Sus envíos con MBE 12/11/2026", MBE_NUESTRA, JUEVES_12));

            Assert.IsTrue(a.Guardar);
            Assert.IsTrue(a.EsSalidaNuestra);
            Assert.IsNull(a.RemitentePaquete);
        }

        [TestMethod]
        public void Mbe_ResumenMixto_SoloCuentaElEnvioQueNosTraen()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("mbe225@mbe.es", "Sus envíos con MBE 12/11/2026", MBE_MIXTO, JUEVES_12));

            Assert.IsFalse(a.EsSalidaNuestra);
            Assert.AreEqual("weelko barcelona s. l", a.RemitentePaquete);
            CollectionAssert.AreEqual(new[] { "SO253360WKOUT80001" }, a.ReferenciasRemitente);
            CollectionAssert.AreEqual(new[] { "1Z21E7X76890999999", "ES0061-0L-000000VTZZ" }, a.Seguimientos.Select(s => s.Codigo).ToList(),
                string.Join(", ", a.Seguimientos.Select(s => s.Codigo)));
        }

        [TestMethod]
        public void MbeYUps_MismoEnvio_LaFechaLaDaUps()
        {
            CorreoCompras mbe = Correo("recogidas061@mbe.es", "Sus envíos con MBE 12/11/2026", MBE_WEELKO, JUEVES_12);
            CorreoCompras ups = Correo("mcinfo@ups.com", "Tu paquete UPS está de camino", UPS_ESTIMADA, JUEVES_12.AddHours(3));
            CorreoSeguimientoFila filaMbe = ServicioCorreosSeguimientoCompras.Fila(mbe, Analizar(mbe), null);
            CorreoSeguimientoFila filaUps = ServicioCorreosSeguimientoCompras.Fila(ups, Analizar(ups), null);
            filaMbe.Id = 1;
            filaUps.Id = 2;

            List<List<CorreoSeguimientoFila>> envios = MismoEnvio.Agrupar(new[] { filaMbe, filaUps });

            Assert.AreEqual(1, envios.Count, "Comparten el 1Z de UPS");
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaFechaRecepcionPorCorreo.Calcular(envios[0], JUEVES_12.Date));
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, ReglaFechaRecepcionPorCorreo.Origen(envios[0]));
            Assert.AreEqual("MBE", filaMbe.Agencia);
            Assert.AreEqual("UPS", filaUps.Agencia);
        }

        #endregion

        #region Weelko: confirmación de pedido

        [TestMethod]
        public void Weelko_Confirmacion_NoEsUnEnvio_PeroDaElPedidoDelProveedorYLaAgencia()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("noreply@weelko.com", "Pedido SO253360", WEELKO_CONFIRMACION, new DateTime(2026, 11, 19, 7, 29, 0)));

            Assert.IsFalse(a.Guardar, "No se guarda como correo de seguimiento ni pone fecha");
            Assert.IsNotNull(a.Confirmacion);
            Assert.AreEqual(FormatoAvisoSalidaWeelko.NOMBRE, a.Confirmacion.Formato);
            Assert.AreEqual("SO253360", a.Confirmacion.PedidoProveedor);
            Assert.AreEqual("21325", a.Confirmacion.ReferenciaCliente);
            Assert.AreEqual("MBE", a.Confirmacion.Agencia?.Nombre);
        }

        [TestMethod]
        public void Weelko_NiLaFacturaNiLaPublicidadSonConfirmaciones_NiSeGuardan()
        {
            AnalisisCorreoSeguimiento factura = Analizar(Correo("noreply@weelko.com", "Factura BCN/2026/27757 (con referencia SO252231)", WEELKO_FACTURA, JUEVES_12));
            AnalisisCorreoSeguimiento publicidad = Analizar(Correo("info@weelko.com", "COT02: frío y compresión · 455 €", WEELKO_PUBLICIDAD_HTML, JUEVES_12, true));

            Assert.IsNull(factura.Confirmacion);
            Assert.IsFalse(factura.Guardar);
            Assert.IsNull(publicidad.Confirmacion);
            Assert.IsFalse(publicidad.Guardar);
        }

        [TestMethod]
        public void ReglaPedidoProveedor_LaReferenciaEmpiezaPorElPedido_YLoQueSigueNoEsUnaCifra()
        {
            Assert.IsTrue(ReglaPedidoProveedor.Casa("SO252231WKOUT77626", "SO252231"));
            Assert.IsTrue(ReglaPedidoProveedor.Casa("so252231", "SO252231"));
            Assert.IsFalse(ReglaPedidoProveedor.Casa("SO2522310", "SO252231"));
            Assert.IsFalse(ReglaPedidoProveedor.Casa("SO253360WKOUT1", "SO252231"));
            Assert.IsFalse(ReglaPedidoProveedor.Casa("SO1", "SO1"), "Demasiado corto");
        }

        [TestMethod]
        public void ReglaPedidoConfirmado_ElQueSeLeMandoJustoAntes_SiSoloEsUno()
        {
            DateTime recibido = new DateTime(2026, 11, 19, 7, 29, 0);
            var enAire = new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 220873, Proveedor = "1050", FechaEnvio = recibido.AddMinutes(-20) },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 220181, Proveedor = "1050", FechaEnvio = recibido.AddDays(-9) }
            };
            var candidatos = enAire.Select(p => new CandidatoPedidoCompra { Empresa = "1", Pedido = p.Pedido, Proveedor = "1050", Motivo = AsignadorPedidosCompra.MOTIVO_DOMINIO }).ToList();

            CollectionAssert.AreEqual(new[] { 220873 }, ReglaPedidoConfirmado.Elegir(candidatos, enAire, "21325", recibido).Select(c => c.Pedido).ToList());
            CollectionAssert.AreEqual(new[] { 220181 }, ReglaPedidoConfirmado.Elegir(candidatos, enAire, "220181", recibido).Select(c => c.Pedido).ToList(),
                "Si la referencia es nuestro pedido, ese");
            enAire[1].FechaEnvio = recibido.AddHours(-3);
            Assert.AreEqual(2, ReglaPedidoConfirmado.Elegir(candidatos, enAire, null, recibido).Count, "Dos recientes: no se elige a ciegas");
        }

        [TestMethod]
        public async Task Weelko_LaConfirmacionEnlazaElAvisoDeMbe_ConNuestroPedido()
        {
            var repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            DateTime confirmada = new DateTime(2026, 11, 19, 7, 29, 0);
            repositorio.Pedidos.Add(new PedidoCompraEnAire
            {
                Empresa = "1", Pedido = 220873, Proveedor = "1050", NombreProveedor = "WEELKO BARCELONA, S.L.", Web = "www.weelko.com", FechaEnvio = confirmada.AddMinutes(-20)
            });
            repositorio.Pedidos.Add(new PedidoCompraEnAire
            {
                Empresa = "1", Pedido = 220181, Proveedor = "1050", NombreProveedor = "WEELKO BARCELONA, S.L.", Web = "www.weelko.com", FechaEnvio = confirmada.AddDays(-9)
            });
            var servicio = new ServicioCorreosSeguimientoCompras(null, repositorio);
            var resultado = new ResultadoPasadaCorreosSeguimiento();
            CorreoCompras confirmacion = Correo("noreply@weelko.com", "Pedido SO253360", WEELKO_CONFIRMACION, confirmada);
            CorreoCompras mbe = Correo("mbe225@mbe.es", "Sus envíos con MBE 20/11/2026",
                MBE_WEELKO.Replace("SO252231WKOUT77626", "SO253360WKOUT80001"), confirmada.AddDays(1));

            bool todo = await servicio.GuardarCorreos(new List<CorreoCompras> { confirmacion, mbe }, resultado,
                _ => Task.FromResult(repositorio.Pedidos.ToList()));

            Assert.IsTrue(todo);
            ConfirmacionPedidoProveedorFila guardada = repositorio.Confirmaciones.Single();
            Assert.AreEqual("SO253360", guardada.PedidoProveedor);
            Assert.AreEqual("MBE", guardada.Agencia);
            CollectionAssert.AreEqual(new[] { 220873 }, AsignadorPedidosCompra.Deserializar(guardada.PedidosCandidatos).Select(c => c.Pedido).ToList(),
                "El que se le mandó 20 minutos antes");
            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual("MBE", fila.Agencia, "La confirmación no se guarda como correo de seguimiento");
            List<CandidatoPedidoCompra> candidatos = AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos);
            Assert.AreEqual(220873, candidatos.Single().Pedido, "Por la referencia SO253360 del resumen de MBE");
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR, candidatos.Single().Motivo);
        }

        [TestMethod]
        public async Task Reanalizar_GuardaLaConfirmacion_YRehaceElCorreoDeMbeComoIntermediario()
        {
            var repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            DateTime confirmada = new DateTime(2026, 11, 19, 7, 29, 0);
            repositorio.Pedidos.Add(new PedidoCompraEnAire
            {
                Empresa = "1", Pedido = 220873, Proveedor = "1050", NombreProveedor = "WEELKO BARCELONA, S.L.", Web = "www.weelko.com", FechaEnvio = confirmada.AddMinutes(-20)
            });
            repositorio.Pedidos.Add(new PedidoCompraEnAire
            {
                Empresa = "1", Pedido = 220181, Proveedor = "1050", NombreProveedor = "WEELKO BARCELONA, S.L.", Web = "www.weelko.com", FechaEnvio = confirmada.AddDays(-9)
            });
            foreach (PedidoCompraEnAire p in repositorio.Pedidos)
            {
                repositorio.Lineas[p.Pedido] = new List<LineaCompraCorreo> { new LineaCompraCorreo { NumeroOrden = 1, Producto = "P02", Estado = 1, Enviado = true, Cantidad = 3 } };
            }
            CorreoCompras mbe = Correo("mbe225@mbe.es", "Sus envíos con MBE 20/11/2026", MBE_WEELKO.Replace("SO252231WKOUT77626", "SO253360WKOUT80001"), confirmada.AddDays(1));
            // Como estaba guardado antes de #857g: de UPS y con los dos pedidos de Weelko
            await repositorio.Insertar(new CorreoSeguimientoFila
            {
                MensajeId = mbe.Clave(), Recibido = mbe.Recibido, RemitenteDireccion = "mbe225@mbe.es", Agencia = "UPS", Codigo = "1Z21E7X76890915153",
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(repositorio.Pedidos.Select(p => new CandidatoPedidoCompra
                {
                    Empresa = "1", Pedido = p.Pedido, Proveedor = "1050", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE
                }).ToList())
            });
            var lector = FakeItEasy.A.Fake<ILectorBuzonCompras>();
            FakeItEasy.A.CallTo(() => lector.LeerPagina(FakeItEasy.A<string>._, FakeItEasy.A<DateTime>._, FakeItEasy.A<DateTime>._))
                .Returns(new LecturaBuzonCompras { Correos = { Correo("noreply@weelko.com", "Pedido SO253360", WEELKO_CONFIRMACION, confirmada), mbe } });

            ResultadoReanalisisCorreos resultado = await new ServicioCorreosSeguimientoCompras(lector, repositorio).Reanalizar(7, confirmada.AddDays(2));

            Assert.AreEqual(1, repositorio.Confirmaciones.Count);
            Assert.AreEqual(1, resultado.Actualizados);
            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual("MBE", fila.Agencia);
            Assert.AreEqual(220873, AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos).Single().Pedido);
        }

        [TestMethod]
        public async Task Weelko_SinLaTablaDeConfirmaciones_ComoAntes()
        {
            var repositorio = new RepositorioCorreosSeguimientoEnMemoria { ConConfirmaciones = false };
            var servicio = new ServicioCorreosSeguimientoCompras(null, repositorio);

            bool todo = await servicio.GuardarCorreos(new List<CorreoCompras>
            {
                Correo("noreply@weelko.com", "Pedido SO253360", WEELKO_CONFIRMACION, new DateTime(2026, 11, 19, 7, 29, 0))
            }, new ResultadoPasadaCorreosSeguimiento(), _ => Task.FromResult(new List<PedidoCompraEnAire>()));

            Assert.IsTrue(todo);
            Assert.AreEqual(0, repositorio.Confirmaciones.Count);
            Assert.AreEqual(0, repositorio.Correos.Count);
        }

        #endregion

        #region DB Schenker: plazo en laborables

        [TestMethod]
        public void Schenker_TresLaborables_DeMiercolesALunes()
        {
            DateTime miercoles18 = new DateTime(2026, 11, 18, 18, 0, 0);

            Assert.AreEqual(new DateTime(2026, 11, 23), PlazoAgenciaEntrada.DiaDeLlegada(miercoles18, "DB Schenker"), "El caso real: aviso 18/11, llegó el 23/11");
            Assert.AreEqual(new DateTime(2026, 11, 25), PlazoAgenciaEntrada.DiaDeLlegada(new DateTime(2026, 11, 20, 10, 0, 0), "DB Schenker"), "Viernes → miércoles");
            Assert.AreEqual(new DateTime(2026, 11, 25), PlazoAgenciaEntrada.DiaDeLlegada(new DateTime(2026, 11, 21, 10, 0, 0), "DB Schenker"), "Sábado → miércoles");
        }

        [TestMethod]
        public void Schenker_ConUnFestivoDeAlgete_UnDiaMas()
        {
            DateTime lunes23 = new DateTime(2026, 11, 23);
            Func<DateTime, bool> festivo = d => d.Date == lunes23;

            Assert.AreEqual(new DateTime(2026, 11, 24), PlazoAgenciaEntrada.DiaDeLlegada(new DateTime(2026, 11, 18, 18, 0, 0), "DB Schenker", festivo));
            Assert.AreEqual(new DateTime(2026, 11, 24), ReglaFechaRecepcionPorCorreo.Calcular(
                new CorreoSeguimientoFila { Recibido = new DateTime(2026, 11, 18, 18, 0, 0), Agencia = "DB Schenker" }, new DateTime(2026, 11, 18), festivo));
            Assert.AreEqual(new DateTime(2026, 11, 24), ReglaFechaRecepcionPorCorreo.Calcular(
                new[] { new CorreoSeguimientoFila { Id = 1, Recibido = new DateTime(2026, 11, 18, 18, 0, 0), Agencia = "DB Schenker", Codigo = "ESMNR264773770" } },
                new DateTime(2026, 11, 18), festivo));
        }

        [TestMethod]
        public void LasDemasAgencias_SiguenEnHorasNaturales()
        {
            DateTime viernes = new DateTime(2026, 11, 20, 10, 0, 0);

            Assert.AreEqual(new DateTime(2026, 11, 21), PlazoAgenciaEntrada.DiaDeLlegada(viernes, "GLS", d => true), "GLS: + 24 h, aunque el sábado no se trabaje");
            Assert.AreEqual(new DateTime(2026, 11, 22), PlazoAgenciaEntrada.DiaDeLlegada(viernes, "Dachser"), "Dachser: + 48 h");
            Assert.AreEqual(new DateTime(2026, 11, 22), PlazoAgenciaEntrada.DiaDeLlegada(viernes, "Transportes inventados"), "Sin agencia conocida: + 48 h");
        }

        #endregion
    }
}
