using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
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

        // ================================================================ NestoAPI#824 (929852, 05/11/26): por cliente, no por pedido
        // Cliente 18522: el 929852 (EFC, factura NV2617824 de 40,03 €) salió «con este pedido» en el envío 250527 del 929323 (ASM,
        // reembolso 40,03 €). El enlace 998 (40,02 €) se creó a las 09:41, antes de la etiqueta, y se pagó a las 17:00.

        private const int PEDIDO_929852 = 929852;
        private const int PEDIDO_929323 = 929323;

        private static EnviosAgencia Envio250527(short estado = Constantes.Agencias.ESTADO_INCIDENTADO, int agencia = Constantes.Agencias.AGENCIA_GLS)
            => new EnviosAgencia { Numero = 250527, Empresa = "1  ", Pedido = PEDIDO_929323, Cliente = "18522     ", Contacto = "0  ", Agencia = agencia, Estado = estado, Reembolso = 40.03m, CodigoBarras = "61771234567890" };

        private static PagoTPV Pago998() => new PagoTPV
        {
            Id = 998,
            Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL,
            Empresa = "1",
            Cliente = "18522",
            Importe = 40.02m,
            Descripcion = "Pago pedido 929852 de Nueva Visión",
            Estado = Constantes.EstadosPagoTPV.AUTORIZADO,
            NumeroOrden = "2611041700AB",
            Usuario = "NUEVAVISION\\MariaJose"
        };

        private static ExtractoCliente EfectoNV2617824(decimal pendiente = 40.03m, int? remesa = null) => new ExtractoCliente
        {
            Empresa = "1  ", Nº_Orden = 3062417, Número = "18522     ", Contacto = "0  ", TipoApunte = "2  ", Nº_Documento = "NV2617824 ", Efecto = "1 ",
            Importe = 40.03m, ImportePdte = pendiente, Delegación = "ALG", FormaVenta = "TEL", Vendedor = "MJ ", Remesa = remesa
        };

        [TestMethod]
        public async Task Issue824_Job_EnlaceDeUnPedidoConElEnvioVivoDeOtroPedidoDelCliente_AvisaConLosDosNumeros()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            string asunto = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => asunto = m.Subject).Returns(true);
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var cobro = new CobroEnlaceConEnvioEnReparto
            {
                Pago = Pago998(),
                Pedido = PEDIDO_929852,
                Envios = new List<EnvioVivoConReembolso> { new EnvioVivoConReembolso { Envio = Envio250527(), NombreAgencia = "ASM" } }
            };

            bool avisado = await AvisoCobroEnlaceConReembolsoJobs.Avisar(998, _ => Task.FromResult(cobro), correo, notificaciones, _ => { });

            Assert.IsTrue(avisado);
            Assert.AreEqual("URGENTE: pedido 929852 pagado por enlace con el envío 250527 (pedido 929323) en reparto contra reembolso de 40,03 €", N(asunto));
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\MariaJose", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo.Contains("929852") && n.Cuerpo.Contains("envío 250527 (pedido 929323)")
                    && n.Datos["envio"] == "250527" && n.Datos["pedidoEnvio"] == "929323" && n.Datos["pedido"] == "929852")))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("El envío 250527 (pedido 929323) lleva reembolso de 40,03 € y el cliente acaba de pagar el pedido 929852 por enlace. " +
                "Llame a la agencia para que entreguen sin cobrar.", N(AvisoCobroEnlaceConReembolsoJobs.TextoEnvio(Envio250527(), PEDIDO_929852)));
        }

        [TestMethod]
        public void Issue824_AgenciaSinCambioPorApi_ElAvisoDiceLlameALaAgencia()
        {
            foreach (int agencia in new[] { Constantes.Agencias.AGENCIA_GLS, Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO, Constantes.Agencias.AGENCIA_CTT, Constantes.Agencias.AGENCIA_INNOVATRANS })
            {
                Assert.IsFalse(CambioReembolsoEnReparto.AdmiteQuitarPorApi(agencia), $"Agencia {agencia}: hoy ninguna (TODO #726)");
                Assert.AreEqual("Llame a la agencia para que entreguen sin cobrar.", CambioReembolsoEnReparto.QueHacer(agencia));
                StringAssert.Contains(AvisoCobroEnlaceConReembolsoJobs.TextoEnvio(Envio250527(agencia: agencia), PEDIDO_929852), "Llame a la agencia para que entreguen sin cobrar.");
            }
            Assert.AreEqual("Avise al repartidor para que entregue sin cobrar.", CambioReembolsoEnReparto.QueHacer(Constantes.Agencias.AGENCIA_REPARTO_PROPIO));
        }

        [TestMethod]
        public void Issue824_Job_ElMismoPedido_ElCorreoSigueComoAntes_YConVariosEnviosLosCuenta()
        {
            var cobro = new CobroEnlaceConEnvioEnReparto
            {
                Pago = Pago998(),
                Pedido = PEDIDO_929852,
                Envios = new List<EnvioVivoConReembolso>
                {
                    new EnvioVivoConReembolso { Envio = new EnviosAgencia { Numero = 250600, Pedido = PEDIDO_929852, Agencia = 1, Estado = 1, Reembolso = 40.03m } },
                    new EnvioVivoConReembolso { Envio = Envio250527(), NombreAgencia = "ASM" }
                }
            };

            using (MailMessage mail = AvisoCobroEnlaceConReembolsoJobs.ComponerCorreo(cobro))
            {
                Assert.AreEqual("URGENTE: pedido 929852 pagado por enlace con el envío 250600 y 1 más en reparto contra reembolso de 40,03 €", N(mail.Subject));
                StringAssert.Contains(mail.Body, "250527");
                StringAssert.Contains(mail.Body, "929323");
                StringAssert.Contains(mail.Body, "<strong>Llame a la agencia para que entreguen sin cobrar.</strong>");
            }
        }

        [TestMethod]
        public void Issue824_EnviosEnRepartoConReembolso_DelClienteConElDelPedidoPrimero()
        {
            var envios = new[]
            {
                Envio250527(),
                new EnviosAgencia { Numero = 250415, Pedido = PEDIDO_929323, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Reembolso = 0m },
                new EnviosAgencia { Numero = 250400, Pedido = PEDIDO_929852, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Reembolso = 12m },
                new EnviosAgencia { Numero = 250700, Pedido = 929999, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Reembolso = 50m }
            };

            List<EnviosAgencia> vivos = EnlacesPagoPedido.EnviosEnRepartoConReembolso(envios, PEDIDO_929852);

            CollectionAssert.AreEqual(new[] { 250400, 250527 }, vivos.Select(e => e.Numero).ToArray(), "el del pedido primero; entregados y sin reembolso, fuera");
            Assert.AreEqual(0, EnlacesPagoPedido.EnviosEnRepartoConReembolso(null).Count);
        }

        [TestMethod]
        public async Task Issue824_Propuesta_ConEnlacePendienteDeOtroPedidoDelCliente_AvisaYDiceDeQuePedidoEs()
        {
            IDatosPropuestaEnvio datos = DatosPropuesta(258.56m, 0m, new List<EnlacePagoPedido>());
            var delOtro = Enlace(998, 40.02m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now.AddHours(-1));
            delOtro.Pedido = PEDIDO_929852;
            A.CallTo(() => datos.EnlacesDePagoDeOtrosPedidos(EMPRESA, A<string>._, A<string>._, PEDIDO)).Returns(new List<EnlacePagoPedido> { delOtro });

            PropuestaEnvioDTO p = await new PropuestaEnvioService(datos).Calcular(EMPRESA, PEDIDO, 1, 1m);

            AvisoPropuestaEnvio aviso = p.AvisosDetalle.SingleOrDefault(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE);
            Assert.IsNotNull(aviso, string.Join(" | ", p.Avisos));
            Assert.AreEqual(TiposAvisoPropuestaEnvio.CONFIRMAR, aviso.Tipo);
            Assert.AreEqual($"El cliente tiene un enlace de pago pendiente de 40,02 € del pedido 929852 (enviado el {delOtro.FechaCreacion.ToString("dd/MM/yyyy", new System.Globalization.CultureInfo("es-ES"))}): " +
                "¿seguro que sale contra reembolso?", N(aviso.Texto));
            Assert.AreEqual(PEDIDO_929852, p.EnlacesPagoPendientes.Single().Pedido);
            Assert.AreEqual(0m, p.EnlacesPagoAutorizados, "lo cobrado de otro pedido no es un prepago de este");
            A.CallTo(() => datos.EnlacesDePagoDeOtrosPedidos(EMPRESA, "40026     ", "0  ", PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Issue824_Propuesta_ElOtroPedidoYaPagoSuUltimoEnlace_OSiFallaLaLectura_SinAviso()
        {
            IDatosPropuestaEnvio datos = DatosPropuesta(258.56m, 0m, new List<EnlacePagoPedido>());
            var viejo = Enlace(990, 40.02m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now.AddDays(-2));
            var pagado = Enlace(998, 40.02m, Constantes.EstadosPagoTPV.AUTORIZADO, DateTime.Now.AddDays(-1));
            viejo.Pedido = pagado.Pedido = PEDIDO_929852;
            A.CallTo(() => datos.EnlacesDePagoDeOtrosPedidos(EMPRESA, A<string>._, A<string>._, PEDIDO)).Returns(new List<EnlacePagoPedido> { viejo, pagado });

            PropuestaEnvioDTO p = await new PropuestaEnvioService(datos).Calcular(EMPRESA, PEDIDO, 1, 1m);
            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE), "el pendiente anterior al cobrado se da por sustituido");

            A.CallTo(() => datos.EnlacesDePagoDeOtrosPedidos(EMPRESA, A<string>._, A<string>._, PEDIDO)).Throws(new InvalidOperationException("caída"));
            p = await new PropuestaEnvioService(datos).Calcular(EMPRESA, PEDIDO, 1, 1m);
            Assert.IsNotNull(p);
            Assert.AreEqual(0, p.EnlacesPagoPendientes.Count);
        }

        [TestMethod]
        public void Issue824_TextoAvisoPendientes_DelPedidoYDeOtro_DiceDeCualEsCadaUno()
        {
            var propio = Enlace(990, 12m, Constantes.EstadosPagoTPV.PENDIENTE, new DateTime(2026, 11, 3, 10, 0, 0));
            propio.Pedido = PEDIDO_929323;
            var otro = Enlace(998, 40.02m, Constantes.EstadosPagoTPV.PENDIENTE, new DateTime(2026, 11, 4, 9, 41, 0));
            otro.Pedido = PEDIDO_929852;

            Assert.AreEqual("El cliente tiene 2 enlaces de pago pendientes: 40,02 € del pedido 929852 (enviado el 04/11/2026) y 12,00 € de este pedido " +
                "(enviado el 03/11/2026): ¿seguro que sale contra reembolso?",
                N(EnlacesPagoPedido.TextoAvisoPendientes(new List<EnlacePagoPedido> { propio, otro }, PEDIDO_929323)));
            Assert.AreEqual("Este pedido tiene un enlace de pago pendiente de 12,00 € (enviado el 03/11/2026): ¿seguro que sale contra reembolso?",
                N(EnlacesPagoPedido.TextoAvisoPendientes(new List<EnlacePagoPedido> { propio }, PEDIDO_929323)), "solo los suyos: como antes");
        }

        // ================================================================ 09/11/26 (930167): el otro pedido ya está pagado
        // Cliente 38515/2: el enlace 867 (428,92 €, «Pago pedido 928286») seguía Pendiente aunque el 928286 se cobró por reembolso
        // con CTT (factura NV2616687, todo a 0). El aviso bloqueaba la etiqueta del 930167.

        [TestMethod]
        public async Task Pedido930167_Propuesta_EnlaceDeOtroPedidoYaPagado_SinAviso()
        {
            IDatosPropuestaEnvio datos = DatosPropuesta(258.56m, 0m, new List<EnlacePagoPedido>());
            var enlace867 = Enlace(867, 428.92m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now.AddDays(-27));
            enlace867.Pedido = 928286;
            enlace867.PedidoPagado = true;
            A.CallTo(() => datos.EnlacesDePagoDeOtrosPedidos(EMPRESA, A<string>._, A<string>._, PEDIDO)).Returns(new List<EnlacePagoPedido> { enlace867 });

            PropuestaEnvioDTO p = await new PropuestaEnvioService(datos).Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE), string.Join(" | ", p.Avisos));
            Assert.AreEqual(0, p.EnlacesPagoPendientes.Count);
        }

        [TestMethod]
        public async Task Pedido930167_Propuesta_EnlaceDeOtroPedidoNoPagado_Avisa()
        {
            IDatosPropuestaEnvio datos = DatosPropuesta(258.56m, 0m, new List<EnlacePagoPedido>());
            var enlace867 = Enlace(867, 428.92m, Constantes.EstadosPagoTPV.PENDIENTE, DateTime.Now.AddDays(-27));
            enlace867.Pedido = 928286;
            A.CallTo(() => datos.EnlacesDePagoDeOtrosPedidos(EMPRESA, A<string>._, A<string>._, PEDIDO)).Returns(new List<EnlacePagoPedido> { enlace867 });

            PropuestaEnvioDTO p = await new PropuestaEnvioService(datos).Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsTrue(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
            Assert.AreEqual(867, p.EnlacesPagoPendientes.Single().Id);
        }

        [TestMethod]
        public void PendientesDelPedidoYDelCliente_JuntaLosDelPedidoYLosDeOtrosNoPagados()
        {
            DateTime ahora = new DateTime(2026, 11, 9, 12, 0, 0);
            var propio = Enlace(900, 12m, Constantes.EstadosPagoTPV.PENDIENTE, ahora.AddDays(-1));
            propio.Pedido = PEDIDO;
            var otroPagado = Enlace(867, 428.92m, Constantes.EstadosPagoTPV.PENDIENTE, ahora.AddDays(-27));
            otroPagado.Pedido = 928286;
            otroPagado.PedidoPagado = true;
            var otroSinPagar = Enlace(868, 40m, Constantes.EstadosPagoTPV.PENDIENTE, ahora.AddDays(-2));
            otroSinPagar.Pedido = 928300;
            var otroViejo = Enlace(700, 40m, Constantes.EstadosPagoTPV.PENDIENTE, ahora.AddDays(-31));
            otroViejo.Pedido = 928301;

            List<EnlacePagoPedido> r = EnlacesPagoPedido.PendientesDelPedidoYDelCliente(new[] { propio }, new[] { otroPagado, otroSinPagar, otroViejo }, ahora);

            CollectionAssert.AreEqual(new[] { 900, 868 }, r.Select(e => e.Id).ToArray());
            Assert.AreEqual(0, EnlacesPagoPedido.PendientesDelPedidoYDelCliente(null, null, ahora).Count);
        }

        [TestMethod]
        public void EstaPagado_FacturadoEnteroYSinPendiente()
        {
            short factura = Constantes.EstadosLineaVenta.FACTURA;
            short albaran = Constantes.EstadosLineaVenta.ALBARAN;
            short pendiente = Constantes.EstadosLineaVenta.PENDIENTE;
            // El 928286: todo facturado en NV2616687 y sus cuatro apuntes a 0.
            Assert.IsTrue(EnlacesPagoPedido.EstaPagado(new[] { factura, factura }, new[] { 0m, 0m, 0m, 0m }));
            Assert.IsFalse(EnlacesPagoPedido.EstaPagado(new[] { factura }, new[] { 0m, 428.92m }), "la factura debe algo");
            Assert.IsFalse(EnlacesPagoPedido.EstaPagado(new[] { factura, albaran }, new[] { 0m }), "queda un albarán sin facturar");
            Assert.IsFalse(EnlacesPagoPedido.EstaPagado(new[] { factura, pendiente }, new[] { 0m }), "queda una línea pendiente");
            Assert.IsFalse(EnlacesPagoPedido.EstaPagado(new[] { factura }, new decimal[0]), "sin apuntes no se sabe: se avisa");
            Assert.IsFalse(EnlacesPagoPedido.EstaPagado(new[] { pendiente }, new[] { 0m }), "sin facturar");
            Assert.IsFalse(EnlacesPagoPedido.EstaPagado(null, null));
        }

        // ---------------------------------------------------------------- punto 3: el céntimo

        [TestMethod]
        public void Issue824_Liquidacion_UnCentimoDeDiferencia_LiquidaElEfectoEnteroYElCentimoARegularizacion()
        {
            List<PagoTPV_Efecto> efectos = ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824() }, 40.02m);

            Assert.IsNotNull(efectos, "40,02 € contra 40,03 €: redondeo, se liquida");
            Assert.AreEqual(3062417, efectos.Single().ExtractoClienteId);
            Assert.AreEqual(40.03m, efectos.Single().Importe, "el efecto entero");
            Assert.AreEqual("NV2617824", efectos.Single().Documento);
            Assert.IsTrue(NestoAPI.Infraestructure.Pagos.ServicioPagos.SeLiquidanEnteros(efectos, new[] { EfectoNV2617824() }));

            List<PreContabilidad> lineas = NestoAPI.Infraestructure.Pagos.ServicioPagos.ConstruirLineasCobro(Pago998(), "57200013", "1", "Pago TPV Pago pedido 929852", efectos, efectosEnteros: true);

            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "el asiento cuadra");
            Assert.AreEqual(40.02m, lineas.Single(l => l.Nº_Cuenta == "57200013").Debe);
            PreContabilidad cliente = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE);
            Assert.AreEqual(3062417, cliente.Liquidado);
            Assert.AreEqual(40.03m, cliente.Haber);
            PreContabilidad ajuste = lineas.Single(l => l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO);
            Assert.AreEqual(Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE, ajuste.TipoCuenta);
            Assert.AreEqual(0.01m, ajuste.Debe);
            Assert.AreEqual(0m, ajuste.Haber);
            Assert.IsFalse(lineas.Any(l => l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO_INGRESO), "de menos: gasto, no ingreso");
            Assert.AreEqual(3, lineas.Count, "ni a cuenta ni céntimo pendiente en el cliente");
        }

        [TestMethod]
        public void Issue824_Liquidacion_ClientePagaDeMas_LiquidaElEfectoEnteroYLaDiferenciaAlHaberDe75900004()
        {
            List<PagoTPV_Efecto> efectos = ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824() }, 40.05m);
            PagoTPV pago = Pago998();
            pago.Importe = 40.05m;

            List<PreContabilidad> lineas = NestoAPI.Infraestructure.Pagos.ServicioPagos.ConstruirLineasCobro(pago, "57200013", "1", "Pago TPV Pago pedido 929852", efectos, efectosEnteros: true);

            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "el asiento cuadra");
            Assert.AreEqual(40.05m, lineas.Single(l => l.Nº_Cuenta == "57200013").Debe);
            Assert.AreEqual(40.03m, lineas.Single(l => l.Liquidado == 3062417).Haber, "el efecto entero");
            PreContabilidad ajuste = lineas.Single(l => l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO_INGRESO);
            Assert.AreEqual("75900004", ajuste.Nº_Cuenta);
            Assert.AreEqual(Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE, ajuste.TipoCuenta);
            Assert.AreEqual(0.02m, ajuste.Haber);
            Assert.AreEqual(0m, ajuste.Debe);
            Assert.IsFalse(lineas.Any(l => l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO), "de más: ingreso, no gasto");
            Assert.IsFalse(lineas.Any(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE && l.Liquidado == null), "nada a favor del cliente");
            Assert.AreEqual(3, lineas.Count);
        }

        [TestMethod]
        public void Issue824_Liquidacion_SinDiferencia_ComoHoy_BancoYEfecto()
        {
            List<PagoTPV_Efecto> efectos = ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824() }, 40.03m);
            PagoTPV pago = Pago998();
            pago.Importe = 40.03m;

            List<PreContabilidad> lineas = NestoAPI.Infraestructure.Pagos.ServicioPagos.ConstruirLineasCobro(pago, "57200013", "1", "Pago TPV Pago pedido 929852", efectos, efectosEnteros: true);

            Assert.AreEqual(2, lineas.Count, "banco + efecto, sin ajuste");
            Assert.IsFalse(lineas.Any(l => l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO || l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO_INGRESO));
            Assert.AreEqual(40.03m, lineas.Single(l => l.Liquidado == 3062417).Haber);
        }

        [TestMethod]
        public void Issue824_Liquidacion_FueraDeLaTolerancia_SinFacturar_ORemesado_QuedaACuenta()
        {
            Assert.IsNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824() }, 39.97m), "6 céntimos: ya no es redondeo");
            Assert.IsNotNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824() }, 39.98m), "5 céntimos: todavía sí");
            Assert.IsNotNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824() }, 40.08m), "y por arriba igual");
            Assert.IsNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new ExtractoCliente[0], 40.02m), "sin facturar: a cuenta");
            Assert.IsNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(null, 40.02m));
            Assert.IsNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824(remesa: 10950) }, 40.02m), "remesado: lo cobra el banco");
            Assert.IsNull(ToleranciaRedondeoCobro.EfectosDelEnlaceDePedido(new[] { EfectoNV2617824(pendiente: 0m) }, 40.02m), "ya liquidado");
        }

        [TestMethod]
        public void Issue824_Liquidacion_CobroParcialElegidoAMano_LaDiferenciaSigueEnElCliente()
        {
            // Un efecto elegido en «Reclamar deuda» por menos de su pendiente: la diferencia no es un redondeo nuestro
            var pago = Pago998();
            pago.PagosTPV_Efectos.Add(new PagoTPV_Efecto { ExtractoClienteId = 3062417, Importe = 40.00m, Documento = "NV2617824", Contacto = "0" });

            Assert.IsFalse(NestoAPI.Infraestructure.Pagos.ServicioPagos.SeLiquidanEnteros(pago.PagosTPV_Efectos, new[] { EfectoNV2617824() }));
            List<PreContabilidad> lineas = NestoAPI.Infraestructure.Pagos.ServicioPagos.ConstruirLineasCobro(pago, "57200013", "1", "Pago TPV", efectosEnteros: false);

            Assert.IsFalse(lineas.Any(l => l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO || l.Nº_Cuenta == Constantes.Cuentas.REGULARIZACION_SALDO_INGRESO));
            Assert.AreEqual(0.02m, lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE && l.Liquidado == null).Haber);
        }

        [TestMethod]
        public async Task Issue824_ServicioPagos_EfectosDelEnlaceDePedido_BuscaLaFacturaDelPedidoDelConcepto()
        {
            ServicioPagos servicio = ServicioPagos();
            string leido = null;
            servicio.LeerEfectosFacturaPedido = (empresa, cliente, pedido) =>
            {
                leido = $"{empresa}/{cliente}/{pedido}";
                return Task.FromResult(new List<ExtractoCliente> { EfectoNV2617824() });
            };

            List<PagoTPV_Efecto> efectos = await servicio.EfectosDelEnlaceDePedido(Pago998(), "1");

            Assert.AreEqual("1/18522/929852", leido);
            Assert.AreEqual(40.03m, efectos.Single().Importe);

            // Con sus propios efectos (los de «Reclamar deuda» o del reparto) no se busca nada
            leido = null;
            PagoTPV conEfectos = Pago998();
            conEfectos.PagosTPV_Efectos.Add(new PagoTPV_Efecto { ExtractoClienteId = 1, Importe = 40.02m });
            Assert.IsNull(await servicio.EfectosDelEnlaceDePedido(conEfectos, "1"));
            PagoTPV sinPedido = Pago998();
            sinPedido.Descripcion = "Pago señal curso quiromasaje";
            Assert.IsNull(await servicio.EfectosDelEnlaceDePedido(sinPedido, "1"));
            Assert.IsNull(leido);

            // Si falla la lectura: a cuenta, como antes, y a ELMAH
            servicio.LeerEfectosFacturaPedido = (e, c, p) => throw new InvalidOperationException("BD caída");
            Assert.IsNull(await servicio.EfectosDelEnlaceDePedido(Pago998(), "1"));
            A.CallTo(() => log.LogError(A<string>.That.Contains("#824"), A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Issue824_EnlaceDelReparto_UnCentimoDeDiferencia_LiquidaElEfectoEntero()
        {
            var factura = new EfectoCobroReparto
            {
                Id = 3062417, Empresa = "1  ", Cliente = "18522     ", Contacto = "0  ", TipoApunte = "2  ", Documento = "NV2617824 ", Efecto = "1 ",
                Importe = 40.03m, ImportePendiente = 40.03m, Delegacion = "ALG", FormaVenta = "TEL"
            };

            LiquidacionReembolsoTarjeta conCentimo = ServicioCobroTarjetaReparto.EfectoALiquidar(factura, 40.02m);
            Assert.IsNull(conCentimo.MotivoACuenta, conCentimo.MotivoACuenta);
            Assert.AreEqual(40.03m, conCentimo.Efecto.Importe, "el efecto entero; el céntimo, a Regularización saldo al contabilizar");

            Assert.AreEqual(40.03m, ServicioCobroTarjetaReparto.EfectoALiquidar(factura, 40.03m).Efecto.Importe, "sin diferencia: como hoy");
            StringAssert.Contains(N(ServicioCobroTarjetaReparto.EfectoALiquidar(factura, 39.90m).MotivoACuenta), "es de 40,03 €, no de 39,90 €");
        }
    }
}
