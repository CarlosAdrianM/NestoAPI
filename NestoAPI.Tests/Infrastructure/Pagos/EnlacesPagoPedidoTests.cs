using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#679 (pedido 928521, cliente 40026): enlaces de pago y contra reembolso. Los enlaces 891, 894 y 900 quedaron
    /// sin pagar, el envío 249871 salió por CTT contra reembolso de 258,56 € y después se pagó el 905 (252,94 €).
    /// </summary>
    [TestClass]
    public class EnlacesPagoPedidoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 928521;
        private static readonly DateTime SalidaDelEnvio = new DateTime(2026, 10, 19, 11, 0, 0);

        /// <summary>Los espacios duros que pueda poner la cultura antes del «€», como espacios normales.</summary>
        private static string N(string texto) => texto?.Replace(' ', ' ').Replace(' ', ' ');

        private static EnlacePagoPedido Enlace(int id, decimal importe, string estado, DateTime creado) => new EnlacePagoPedido
        {
            Id = id,
            Importe = importe,
            Estado = estado,
            FechaCreacion = creado,
            Usuario = "Javier"
        };

        private static List<EnlacePagoPedido> EnlacesSinPagarDel928521() => new List<EnlacePagoPedido>
        {
            Enlace(891, 152.29m, Constantes.EstadosPagoTPV.PENDIENTE, new DateTime(2026, 10, 15, 9, 54, 7)),
            Enlace(894, 225.51m, Constantes.EstadosPagoTPV.PENDIENTE, new DateTime(2026, 10, 15, 17, 17, 19)),
            Enlace(900, 252.94m, Constantes.EstadosPagoTPV.PENDIENTE, new DateTime(2026, 10, 16, 12, 14, 26))
        };

        // ================================================================ reglas

        [TestMethod]
        public void PedidoDelConcepto_LosConceptosReales()
        {
            Assert.AreEqual(928521, EnlacesPagoPedido.PedidoDelConcepto("Pedido 928521"), "NestoApp");
            Assert.AreEqual(928972, EnlacesPagoPedido.PedidoDelConcepto("Pago pedido 928972 de Nueva Visión"), "Nesto");
            Assert.AreEqual(925979, EnlacesPagoPedido.PedidoDelConcepto("Pago pedido925979"));
            Assert.AreEqual(928426, EnlacesPagoPedido.PedidoDelConcepto("PEDIDO 928426 DE NUEVA VISIÓN"));
            Assert.IsNull(EnlacesPagoPedido.PedidoDelConcepto("Pago pedido 3"), "una prueba, no un pedido");
            Assert.IsNull(EnlacesPagoPedido.PedidoDelConcepto("Pedido camilla draco"));
            Assert.IsNull(EnlacesPagoPedido.PedidoDelConcepto("Pago señal curso quiromasaje 2026"));
            Assert.IsNull(EnlacesPagoPedido.PedidoDelConcepto(null));
        }

        [TestMethod]
        public void EsEnlaceDelPedido_SoloTpvVirtualYElMismoPedido()
        {
            Assert.IsTrue(EnlacesPagoPedido.EsEnlaceDelPedido("TPVVirtual", "Pedido 928521", PEDIDO));
            Assert.IsFalse(EnlacesPagoPedido.EsEnlaceDelPedido("TPVVirtual", "Pedido 9285210", PEDIDO), "otro número que lo contiene");
            Assert.IsFalse(EnlacesPagoPedido.EsEnlaceDelPedido(Constantes.TiposPagoTPV.PEDIDO_APP, "Pedido 928521", PEDIDO),
                "los cobros de la app ya entran como Prepago (#436)");
        }

        [TestMethod]
        public void PendientesVivos_AlSalirElEnvio_LosTresSinPagar()
        {
            List<EnlacePagoPedido> vivos = EnlacesPagoPedido.PendientesVivos(EnlacesSinPagarDel928521(), SalidaDelEnvio);

            CollectionAssert.AreEqual(new[] { 900, 894, 891 }, vivos.Select(e => e.Id).ToArray(), "del más reciente al más antiguo");
        }

        [TestMethod]
        public void PendientesVivos_LosAnterioresAlCobradoYLosViejosNoCuentan()
        {
            List<EnlacePagoPedido> enlaces = EnlacesSinPagarDel928521();
            enlaces.Add(Enlace(905, 252.94m, Constantes.EstadosPagoTPV.AUTORIZADO, new DateTime(2026, 10, 19, 15, 54, 40)));
            Assert.AreEqual(0, EnlacesPagoPedido.PendientesVivos(enlaces, new DateTime(2026, 10, 20)).Count, "sustituidos por el 905");

            var viejo = new List<EnlacePagoPedido> { Enlace(1, 10m, Constantes.EstadosPagoTPV.PENDIENTE, SalidaDelEnvio.AddDays(-31)) };
            Assert.AreEqual(0, EnlacesPagoPedido.PendientesVivos(viejo, SalidaDelEnvio).Count, "abandonado");
        }

        [TestMethod]
        public void ImporteAutorizado_SoloLosCobrados()
        {
            List<EnlacePagoPedido> enlaces = EnlacesSinPagarDel928521();
            enlaces.Add(Enlace(905, 252.94m, Constantes.EstadosPagoTPV.AUTORIZADO, new DateTime(2026, 10, 19, 15, 54, 40)));

            Assert.AreEqual(252.94m, EnlacesPagoPedido.ImporteAutorizado(enlaces));
            Assert.AreEqual(0m, EnlacesPagoPedido.ImporteAutorizado(null));
        }

        [TestMethod]
        public void TextoAvisoPendientes_UnoYVarios()
        {
            List<EnlacePagoPedido> tres = EnlacesPagoPedido.PendientesVivos(EnlacesSinPagarDel928521(), SalidaDelEnvio);

            Assert.AreEqual("Este pedido tiene un enlace de pago pendiente de 252,94 € (enviado el 16/10/2026): ¿seguro que sale contra reembolso?",
                N(EnlacesPagoPedido.TextoAvisoPendientes(tres.Take(1).ToList())));
            Assert.AreEqual("Este pedido tiene 3 enlaces de pago pendientes (el último, de 252,94 €, enviado el 16/10/2026): ¿seguro que sale contra reembolso?",
                N(EnlacesPagoPedido.TextoAvisoPendientes(tres)));
        }

        // ================================================================ punto 1: la propuesta de envío

        private static IDatosPropuestaEnvio DatosPropuesta(decimal reembolso, decimal prepagos, List<EnlacePagoPedido> enlaces)
        {
            var datos = A.Fake<IDatosPropuestaEnvio>();
            A.CallTo(() => datos.LeerPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult(new PedidoParaAgenciaDTO
            {
                Empresa = "1  ",
                Numero = PEDIDO,
                Cliente = "40026     ",
                Contacto = "0  ",
                Vendedor = "JE ",
                ClienteFicha = new ClienteParaAgenciaDTO { Nombre = "CLIENTA", Direccion = "Calle Mayor, 1", CodPostal = "28001", Poblacion = "MADRID", Provincia = "MADRID" }
            }));
            A.CallTo(() => datos.TieneAlgunaLineaConPicking(EMPRESA, PEDIDO)).Returns(Task.FromResult(true));
            A.CallTo(() => datos.LeerEnvioPendiente(A<string>._, A<int>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.LeerEnvioAmpliacion(A<string>._, A<string>._, A<string>._, A<int?>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.ImporteReembolso(EMPRESA, PEDIDO)).Returns(reembolso);
            A.CallTo(() => datos.PrepagosPendientes(EMPRESA, PEDIDO)).Returns(prepagos);
            A.CallTo(() => datos.EnlacesDePago(EMPRESA, PEDIDO)).Returns(enlaces);
            A.CallTo(() => datos.BultosPacking(EMPRESA, PEDIDO)).Returns(Task.FromResult(0));
            A.CallTo(() => datos.FechaPicking(A<string>._)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 19)));
            A.CallTo(() => datos.LeerAgencias()).Returns(Task.FromResult(new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "ASM" }
            }));
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(new ITarifaAgencia[] { new TarifaGLSBusinessParcel() });
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => datos.Comparador()).ReturnsLazily(() => new ComparadorAgencias(registro, fuel));
            return datos;
        }

        [TestMethod]
        public async Task Propuesta_ConEnlacePendienteYContraReembolso_PideConfirmar()
        {
            var enlaces = new List<EnlacePagoPedido> { Enlace(900, 252.94m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now.AddDays(-3)) };
            var servicio = new PropuestaEnvioService(DatosPropuesta(258.56m, 0m, enlaces));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            AvisoPropuestaEnvio aviso = p.AvisosDetalle.SingleOrDefault(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE);
            Assert.IsNotNull(aviso, string.Join(" | ", p.Avisos));
            Assert.AreEqual(TiposAvisoPropuestaEnvio.CONFIRMAR, aviso.Tipo);
            StringAssert.Contains(N(aviso.Texto), "enlace de pago pendiente de 252,94 €");
            StringAssert.Contains(aviso.Texto, "¿seguro que sale contra reembolso?");
            Assert.IsTrue(p.Avisos.Contains(aviso.Texto), "también en Avisos, como el resto");
            Assert.AreEqual(900, p.EnlacesPagoPendientes.Single().Id);
        }

        [TestMethod]
        public async Task Propuesta_ConEnlacePendienteSinReembolso_NoAvisa()
        {
            var enlaces = new List<EnlacePagoPedido> { Enlace(900, 252.94m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now.AddDays(-3)) };
            var servicio = new PropuestaEnvioService(DatosPropuesta(0m, 0m, enlaces));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
        }

        [TestMethod]
        public async Task Propuesta_ConEnlaceCobrado_EsUnPrepagoADescontar()
        {
            // GestorPedidosVenta.ImportePrepagosPendientes ya suma el enlace cobrado (lo que lee datos.PrepagosPendientes)
            var enlaces = new List<EnlacePagoPedido> { Enlace(905, 252.94m, Constantes.EstadosPagoTPV.AUTORIZADO, DateTime.Now.AddDays(-1)) };
            var servicio = new PropuestaEnvioService(DatosPropuesta(258.56m, 252.94m, enlaces));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.AreEqual(258.56m, p.Reembolso, "#569: no se descuenta solo; se elige");
            Assert.AreEqual(252.94m, p.EnlacesPagoAutorizados);
            AvisoPropuestaEnvio aviso = p.AvisosDetalle.Single(a => a.Codigo == CodigosAvisoPropuestaEnvio.PREPAGOS);
            Assert.AreEqual(TiposAvisoPropuestaEnvio.ELEGIR, aviso.Tipo);
            StringAssert.Contains(N(aviso.Texto), "252,94 € pagados por adelantado (252,94 € con enlace de pago)");
            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
        }

        [TestMethod]
        public async Task Propuesta_SiFallaLaLecturaDeEnlaces_SigueSinAviso()
        {
            IDatosPropuestaEnvio datos = DatosPropuesta(258.56m, 0m, null);
            A.CallTo(() => datos.EnlacesDePago(EMPRESA, PEDIDO)).Throws(new InvalidOperationException("caída"));

            PropuestaEnvioDTO p = await new PropuestaEnvioService(datos).Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsNotNull(p);
            Assert.AreEqual(0, p.EnlacesPagoPendientes.Count);
        }

        [TestMethod]
        public void RecalcularAvisosDeDestino_SiLaPantallaQuitaElReembolso_DesapareceElAviso()
        {
            var p = new PropuestaEnvioDTO
            {
                Agencia = 1,
                Reembolso = 258.56m,
                EnlacesPagoPendientes = new List<EnlacePagoPedido> { Enlace(900, 252.94m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now) }
            };
            PropuestaEnvioService.RecalcularAvisosDeDestino(p);
            Assert.AreEqual(1, p.AvisosDetalle.Count(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));

            p.Reembolso = 0m;
            PropuestaEnvioService.RecalcularAvisosDeDestino(p);

            Assert.AreEqual(0, p.AvisosDetalle.Count(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
            Assert.AreEqual(0, p.Avisos.Count);
        }

        [TestMethod]
        public void ImportePrepagosPendientes_SumaLosEnlacesCobrados()
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.CargarPrepagosPedido(EMPRESA, PEDIDO)).Returns(new List<Prepago> { new Prepago { Importe = 10m } });
            A.CallTo(() => servicio.CargarEnlacesPagoPedido(EMPRESA, PEDIDO)).Returns(new List<EnlacePagoPedido>
            {
                Enlace(900, 252.94m, Constantes.EstadosPagoTPV.PENDIENTE, SalidaDelEnvio),
                Enlace(905, 252.94m, Constantes.EstadosPagoTPV.AUTORIZADO, SalidaDelEnvio)
            });

            Assert.AreEqual(262.94m, new GestorPedidosVenta(servicio).ImportePrepagosPendientes(EMPRESA, PEDIDO), "el pendiente no cuenta");
        }

        [TestMethod]
        public void ImportePrepagosPendientes_SiFallanLosEnlaces_SoloLosPrepagos()
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.CargarPrepagosPedido(EMPRESA, PEDIDO)).Returns(new List<Prepago> { new Prepago { Importe = 10m } });
            A.CallTo(() => servicio.CargarEnlacesPagoPedido(EMPRESA, PEDIDO)).Throws(new InvalidOperationException("caída"));

            Assert.AreEqual(10m, new GestorPedidosVenta(servicio).ImportePrepagosPendientes(EMPRESA, PEDIDO));
        }

        // ================================================================ punto 2: al generar el enlace

        private static EnviosAgencia Envio249871(short estado, decimal reembolso = 258.56m)
            => new EnviosAgencia { Numero = 249871, Empresa = "1  ", Pedido = PEDIDO, Agencia = Constantes.Agencias.AGENCIA_CTT, Estado = estado, Reembolso = reembolso, CodigoBarras = "CTT249871" };

        [TestMethod]
        public void AvisosAlGenerar_PedidoContraReembolsoSinSalir_RecomiendaTarjetaPrepago()
        {
            var datos = new DatosPedidoEnlacePago { ContraReembolso = true, TieneLineasSinServir = true };

            AvisoEnlacePago aviso = EnlacesPagoPedido.AvisosAlGenerar(PEDIDO, datos).Single();

            Assert.AreEqual(CodigosAvisoEnlacePago.PEDIDO_CONTRA_REEMBOLSO, aviso.Codigo);
            StringAssert.Contains(aviso.Texto, "El pedido 928521 va contra reembolso");
            StringAssert.Contains(aviso.Texto, "«tarjeta prepago»");
        }

        [TestMethod]
        public void AvisosAlGenerar_EnvioEnCaminoConReembolso_HableConAdministracion()
        {
            foreach (short estado in new[] { (short)Constantes.Agencias.ESTADO_EN_CURSO, Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_INCIDENTADO })
            {
                var datos = new DatosPedidoEnlacePago { ContraReembolso = true, TieneLineasSinServir = true, Envios = new List<EnviosAgencia> { Envio249871(estado) } };

                AvisoEnlacePago aviso = EnlacesPagoPedido.AvisosAlGenerar(PEDIDO, datos).Single();

                Assert.AreEqual(CodigosAvisoEnlacePago.ENVIO_EN_CAMINO_CON_REEMBOLSO, aviso.Codigo, $"estado {estado}");
                Assert.AreEqual("Este pedido ya va en camino con un reembolso de 258,56 €: si el cliente paga por enlace, la agencia intentará " +
                    "cobrarle otra vez. Hable antes con Administración/Logística.", N(aviso.Texto));
            }
        }

        [TestMethod]
        public void AvisosAlGenerar_SinReembolsoOYaServido_NoAvisa()
        {
            Assert.AreEqual(0, EnlacesPagoPedido.AvisosAlGenerar(PEDIDO, new DatosPedidoEnlacePago { ContraReembolso = false, TieneLineasSinServir = true }).Count,
                "transferencia, tarjeta, prepago...");
            Assert.AreEqual(0, EnlacesPagoPedido.AvisosAlGenerar(PEDIDO, new DatosPedidoEnlacePago
            {
                ContraReembolso = true,
                TieneLineasSinServir = false,
                Envios = new List<EnviosAgencia> { Envio249871(Constantes.Agencias.ESTADO_ENTREGADO) }
            }).Count, "ya entregado");
            Assert.AreEqual(0, EnlacesPagoPedido.AvisosAlGenerar(PEDIDO, new DatosPedidoEnlacePago
            {
                ContraReembolso = false,
                TieneLineasSinServir = true,
                Envios = new List<EnviosAgencia> { Envio249871(Constantes.Agencias.ESTADO_TRAMITADO, 0m) }
            }).Count, "en camino sin reembolso");
            Assert.AreEqual(0, EnlacesPagoPedido.AvisosAlGenerar(PEDIDO, null).Count, "el pedido no existe");
        }

        private ILogService log;

        private ServicioPagos ServicioPagos()
        {
            log = A.Fake<ILogService>();
            return new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<IContabilidadService>(), A.Fake<ILectorParametrosUsuario>(),
                A.Fake<IServicioCorreoElectronico>(), log, A.Fake<ITarjetaClienteStore>());
        }

        [TestMethod]
        public async Task ServicioPagos_AvisosAlGenerarEnlace_LeeElPedidoDelConcepto()
        {
            ServicioPagos servicio = ServicioPagos();
            string empresaLeida = null;
            int pedidoLeido = 0;
            servicio.LeerDatosPedidoEnlace = (empresa, pedido) =>
            {
                empresaLeida = empresa;
                pedidoLeido = pedido;
                return Task.FromResult(new DatosPedidoEnlacePago { ContraReembolso = true, TieneLineasSinServir = true });
            };

            List<AvisoEnlacePago> avisos = await servicio.AvisosAlGenerarEnlace("1  ", "Pedido 928521");

            Assert.AreEqual("1", empresaLeida);
            Assert.AreEqual(PEDIDO, pedidoLeido);
            Assert.AreEqual(CodigosAvisoEnlacePago.PEDIDO_CONTRA_REEMBOLSO, avisos.Single().Codigo);
        }

        [TestMethod]
        public async Task ServicioPagos_AvisosAlGenerarEnlace_SinPedidoOConFallo_SinAvisosYSinLanzar()
        {
            ServicioPagos servicio = ServicioPagos();
            bool leido = false;
            servicio.LeerDatosPedidoEnlace = (empresa, pedido) => { leido = true; throw new InvalidOperationException("caída"); };

            Assert.AreEqual(0, (await servicio.AvisosAlGenerarEnlace("1", "Pago señal curso quiromasaje")).Count);
            Assert.IsFalse(leido, "sin pedido en el concepto no se lee nada");

            Assert.AreEqual(0, (await servicio.AvisosAlGenerarEnlace("1", "Pedido 928521")).Count);
            A.CallTo(() => log.LogError(A<string>.That.Contains("#679"), A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        // ================================================================ punto 3: al cobrar el enlace

        private static PagoTPV Pago905(string tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL) => new PagoTPV
        {
            Id = 905,
            Tipo = tipo,
            Empresa = "1",
            Cliente = "40026",
            Importe = 252.94m,
            Descripcion = "Pedido 928521",
            Estado = Constantes.EstadosPagoTPV.AUTORIZADO,
            NumeroOrden = "2610191554AB",
            CodigoAutorizacion = "893530",
            Usuario = "Javier"
        };

        [TestMethod]
        public void ServicioPagos_AlCobrarElEnlaceDeUnPedido_EncolaElAviso()
        {
            ServicioPagos servicio = ServicioPagos();
            var encolados = new List<int>();
            servicio.EncolarAvisoCobroConReembolso = encolados.Add;

            servicio.AvisarSiElEnvioVaContraReembolso(Pago905());
            servicio.AvisarSiElEnvioVaContraReembolso(Pago905(Constantes.TiposPagoTPV.PEDIDO_APP));
            PagoTPV sinPedido = Pago905();
            sinPedido.Descripcion = "Pago señal curso quiromasaje";
            servicio.AvisarSiElEnvioVaContraReembolso(sinPedido);

            CollectionAssert.AreEqual(new[] { 905 }, encolados, "solo el enlace (TPV virtual) que lleva un pedido");
        }

        [TestMethod]
        public void ServicioPagos_SiNoSePuedeEncolar_NoRompeLaNotificacion()
        {
            ServicioPagos servicio = ServicioPagos();
            servicio.EncolarAvisoCobroConReembolso = _ => throw new InvalidOperationException("Hangfire caído");

            servicio.AvisarSiElEnvioVaContraReembolso(Pago905());
            servicio.AvisarSiElEnvioVaContraReembolso(null);

            A.CallTo(() => log.LogError(A<string>.That.Contains("#679"), A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        private static CobroEnlaceConEnvioEnReparto Cobro() => new CobroEnlaceConEnvioEnReparto
        {
            Pago = Pago905(),
            Pedido = PEDIDO,
            Envio = Envio249871(Constantes.Agencias.ESTADO_TRAMITADO),
            NombreAgencia = "CTT Express"
        };

        [TestMethod]
        public async Task Job_ConElEnvioEnReparto_CorreoALogisticaConCopiaAAdministracionYCampana()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) =>
            {
                enviado = new MailMessage { Subject = m.Subject, Body = m.Body };
                foreach (MailAddress a in m.To) enviado.To.Add(a);
                foreach (MailAddress a in m.CC) enviado.CC.Add(a);
            }).Returns(true);
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var registrados = new List<Exception>();

            bool avisado = await AvisoCobroEnlaceConReembolsoJobs.Avisar(905, _ => Task.FromResult(Cobro()), correo, notificaciones, registrados.Add);

            Assert.IsTrue(avisado);
            Assert.IsNotNull(enviado);
            Assert.AreEqual(Constantes.Correos.LOGISTICA, enviado.To.Single().Address);
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, enviado.CC.Single().Address);
            StringAssert.Contains(enviado.Subject, "pedido 928521");
            StringAssert.Contains(enviado.Subject, "envío 249871");
            StringAssert.Contains(enviado.Subject, "258,56 €");
            StringAssert.Contains(enviado.Body, "CTT249871");
            StringAssert.Contains(enviado.Body, "CTT Express");
            StringAssert.Contains(enviado.Body, "252,94");
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Javier", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisoCobroEnlaceConReembolsoJobs.TIPO_NOTIFICACION
                    && n.Titulo.Contains("928521") && N(n.Cuerpo).Contains("258,56") && n.Datos["envio"] == "249871")))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task Job_SinEnvioEnReparto_NoAvisaANadie()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            var notificaciones = A.Fake<IServicioNotificacionesPush>();

            bool avisado = await AvisoCobroEnlaceConReembolsoJobs.Avisar(905, _ => Task.FromResult<CobroEnlaceConEnvioEnReparto>(null), correo, notificaciones, _ => { });

            Assert.IsFalse(avisado);
            A.CallTo(correo).MustNotHaveHappened();
            A.CallTo(notificaciones).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_SiFallaElCorreo_AvisaIgualEnLaCampanaYQuedaEnElmah()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Throws(new InvalidOperationException("SMTP caído"));
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var registrados = new List<Exception>();

            bool avisado = await AvisoCobroEnlaceConReembolsoJobs.Avisar(905, _ => Task.FromResult(Cobro()), correo, notificaciones, registrados.Add);

            Assert.IsTrue(avisado);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Javier", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(registrados.Single().Message, "SMTP caído");
        }

        [TestMethod]
        public async Task Job_SiFallaLaLectura_SoloElmah()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            var registrados = new List<Exception>();

            bool avisado = await AvisoCobroEnlaceConReembolsoJobs.Avisar(905, _ => throw new InvalidOperationException("BD caída"), correo,
                A.Fake<IServicioNotificacionesPush>(), registrados.Add);

            Assert.IsFalse(avisado);
            A.CallTo(correo).MustNotHaveHappened();
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public void EnvioEnRepartoConReembolso_SoloEnCursoTramitadoEIncidentadoConReembolso()
        {
            Assert.IsNull(EnlacesPagoPedido.EnvioEnRepartoConReembolso(new[] { Envio249871(-1) }), "pendiente: no ha salido");
            Assert.IsNull(EnlacesPagoPedido.EnvioEnRepartoConReembolso(new[] { Envio249871(Constantes.Agencias.ESTADO_ENTREGADO) }));
            Assert.IsNull(EnlacesPagoPedido.EnvioEnRepartoConReembolso(new[] { Envio249871(Constantes.Agencias.ESTADO_DEVUELTO) }));
            Assert.IsNull(EnlacesPagoPedido.EnvioEnRepartoConReembolso(new[] { Envio249871(Constantes.Agencias.ESTADO_INCIDENTADO, 0m) }), "ya le quitaron el reembolso");
            Assert.AreEqual(249871, EnlacesPagoPedido.EnvioEnRepartoConReembolso(new[] { Envio249871(Constantes.Agencias.ESTADO_INCIDENTADO) }).Numero);
        }
    }
}
