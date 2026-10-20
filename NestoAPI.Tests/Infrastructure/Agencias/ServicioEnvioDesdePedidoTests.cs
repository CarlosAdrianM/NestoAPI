using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 2, cortes 4 a 6): POST api/EnviosAgencias/DesdePedido. La propuesta se fakea (sus reglas tienen
    /// sus propios tests en PropuestaEnvioServiceTests); la BD es un doble en memoria con su transacción; la plaza y el
    /// código de barras son los reales, con el transporte de GLS fakeado.
    /// </summary>
    [TestClass]
    public class ServicioEnvioDesdePedidoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 927700;
        private const int GLS = Constantes.Agencias.AGENCIA_GLS;
        private const int CTT = Constantes.Agencias.AGENCIA_CTT;
        private const int CANTERAS = Constantes.Agencias.AGENCIA_CANTERAS;

        [TestMethod]
        public async Task NotificadorCorreo_SoloEncolaElNumero_SinEsperarAlCorreo()
        {
            // Carlos (20/10/26): quien inserta el envío no espera a que se genere el PDF y salga el correo
            var encolados = new List<int>();
            var notificador = new NotificadorEntregaAgenciaCorreo(numero => encolados.Add(numero));

            Task tarea = notificador.Notificar(new EnviosAgencia { Numero = 249871, Email = "cliente@ejemplo.es" });

            Assert.IsTrue(tarea.IsCompleted);
            await tarea;
            CollectionAssert.AreEqual(new[] { 249871 }, encolados);
        }

        [TestMethod]
        public async Task CorreoEntregaJob_SiSale_NoAvisaANadie()
        {
            var notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var envio = new EnviosAgencia { Numero = 1, Pedido = PEDIDO, Email = "c@ejemplo.es", Usuario = "NUEVAVISION\\Alfredo" };

            await CorreoEntregaAgenciaJobs.Enviar(1, _ => envio, _ => Task.CompletedTask, notificaciones, _ => { });

            A.CallTo(notificaciones).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CorreoEntregaJob_SiFalla_AvisaEnLaCampanaAQuienInsertoElEnvio()
        {
            var notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var registrados = new List<Exception>();
            var envio = new EnviosAgencia { Numero = 249871, Empresa = "1", Pedido = PEDIDO, Cliente = "15191", Email = "c@ejemplo.es", Usuario = "alfredo", CodigoBarras = "61197140249871" };

            await CorreoEntregaAgenciaJobs.Enviar(249871, _ => envio, _ => throw new InvalidOperationException("SMTP caído"), notificaciones, registrados.Add);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\alfredo", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == CorreoEntregaAgenciaJobs.TIPO_NOTIFICACION
                    && n.Titulo.Contains(PEDIDO.ToString()) && n.Cuerpo.Contains("SMTP caído") && n.Cuerpo.Contains("Avise usted"))))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public async Task CorreoEntregaJob_SinCorreo_AvisaSinIntentarMandarlo()
        {
            var notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            bool mandado = false;
            var envio = new EnviosAgencia { Numero = 2, Pedido = PEDIDO, Email = " ", Usuario = "NUEVAVISION\\Alfredo" };

            await CorreoEntregaAgenciaJobs.Enviar(2, _ => envio, _ => { mandado = true; return Task.CompletedTask; }, notificaciones, _ => { });

            Assert.IsFalse(mandado);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("no tiene correo")))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CorreoEntregaJob_SinUsuarioAQuienAvisar_SoloElmah()
        {
            var notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var registrados = new List<Exception>();
            var envio = new EnviosAgencia { Numero = 3, Pedido = PEDIDO, Email = "", Usuario = "NestoAPI" };

            await CorreoEntregaAgenciaJobs.Enviar(3, _ => envio, _ => Task.CompletedTask, notificaciones, registrados.Add);

            A.CallTo(notificaciones).MustNotHaveHappened();
            Assert.AreEqual(1, registrados.Count);
        }

        private const string RespuestaPlazaGls =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            "<soap:Body><GetPlazaXCPResponse xmlns=\"http://www.asmred.com/\"><GetPlazaXCPResult>" +
            "<Plaza xmlns=\"\"><Nemonico>MAD-ALC</Nemonico><Nombre>GLS MADRID ALCOBENDAS</Nombre>" +
            "<Telefono>916 620 000</Telefono><Mail>alcobendas@gls-spain.es</Mail></Plaza>" +
            "</GetPlazaXCPResult></GetPlazaXCPResponse></soap:Body></soap:Envelope>";

        private IPropuestaEnvioService propuestas;
        private IClientePlazaGls clienteGls;
        private INotificadorEntregaAgencia notificador;
        private DatosFalsos datos;
        private List<Exception> registrados;
        private ServicioEnvioDesdePedido servicio;
        private PropuestaEnvioDTO propuesta;

        [TestInitialize]
        public void Setup()
        {
            propuesta = PropuestaNueva();
            propuestas = A.Fake<IPropuestaEnvioService>();
            A.CallTo(() => propuestas.Calcular(EMPRESA, PEDIDO, A<short?>._, A<decimal?>._, A<int?>._))
                .ReturnsLazily(() => Task.FromResult(propuesta));
            clienteGls = A.Fake<IClientePlazaGls>();
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(RespuestaPlazaGls));
            notificador = A.Fake<INotificadorEntregaAgencia>();
            datos = new DatosFalsos();
            registrados = new List<Exception>();
            servicio = new ServicioEnvioDesdePedido(propuestas, datos, RegistroPlazasAgencias.PorDefecto(clienteGls, ex => registrados.Add(ex)),
                notificador, ex => registrados.Add(ex));
        }

        private static PropuestaEnvioDTO PropuestaNueva() => new PropuestaEnvioDTO
        {
            Empresa = EMPRESA,
            Pedido = PEDIDO,
            Cliente = "15191",
            Contacto = "0",
            Agencia = GLS,
            AgenciaNombre = "ASM",
            Servicio = 96,
            Horario = 18,
            Retorno = 0,
            Bultos = 2,
            Peso = 3.5m,
            Reembolso = 120m,
            Nombre = "PELUQUERÍA LA BONITA",
            Direccion = "Calle Mayor, 1",
            CodPostal = "28001",
            Poblacion = "MADRID",
            Provincia = "MADRID",
            Pais = 34,
            PaisIso = "ES",
            Telefono = "916281914",
            Movil = "600123456",
            Email = "agencia@bonita.es",
            Atencion = "PELUQUERÍA LA BONITA",
            Observaciones = "Entregar por la mañana",
            Vendedor = "JE",
            Fecha = new DateTime(2026, 10, 20),
            FechaEntrega = new DateTime(2026, 10, 21),
            ImporteGasto = 4.25m,
            Origen = PropuestaEnvioDTO.ORIGEN_NUEVO
        };

        private static CrearEnvioDesdePedidoDTO Peticion(params string[] confirmaciones) => new CrearEnvioDesdePedidoDTO
        {
            Empresa = EMPRESA,
            Pedido = PEDIDO,
            Bultos = 2,
            Peso = 3.5m,
            Confirmaciones = confirmaciones.ToList()
        };

        private void ConAviso(string codigo, string tipo, string texto)
        {
            propuesta.Avisos.Add(texto);
            propuesta.AvisosDetalle.Add(new AvisoPropuestaEnvio { Codigo = codigo, Tipo = tipo, Texto = texto });
        }

        private Task<ResultadoEnvioDesdePedido> Crear(CrearEnvioDesdePedidoDTO peticion) => servicio.Crear(peticion, "Alfredo");

        // ---------------------------------------------------------------- nuevo

        [TestMethod]
        public async Task Nuevo_Gls_GrabaConPlazaYCodigoDelServidorYAvisaAlCliente()
        {
            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            EnviosAgencia envio = resultado.Envio.Envio;
            Assert.AreSame(datos.Anadidos.Single(), envio);
            Assert.AreEqual(250000, envio.Numero);
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado);
            Assert.AreEqual("1  ", envio.Empresa, "la empresa es la de la agencia (VB 3520)");
            Assert.AreEqual(GLS, envio.Agencia);
            Assert.AreEqual(PEDIDO, envio.Pedido);
            Assert.AreEqual((short)2, envio.Bultos);
            Assert.AreEqual(3.5m, envio.Peso);
            Assert.AreEqual(120m, envio.Reembolso);
            Assert.AreEqual(4.25m, envio.ImporteGasto);
            Assert.AreEqual("MAD-ALC", envio.Nemonico);
            Assert.AreEqual("GLS MADRID ALCOBENDAS", envio.NombrePlaza);
            Assert.AreEqual("916620000", envio.TelefonoPlaza);
            Assert.AreEqual("alcobendas@gls-spain.es", envio.EmailPlaza);
            Assert.AreEqual("61197140250000", envio.CodigoBarras, "BusinessParcel: su prefijo + el Numero con 7 cifras");
            Assert.AreEqual("Alfredo", envio.Usuario);
            Assert.AreEqual(2, datos.Guardados, "grabar, código de barras con el Numero y grabar otra vez");
            Assert.IsTrue(datos.Confirmada);
            Assert.AreEqual(FlujosEnvioDesdePedido.TRAMITAR_AL_CERRAR, resultado.Envio.Flujo);
            Assert.IsTrue(resultado.Envio.EtiquetaEnNesto);
            Assert.IsTrue(resultado.Envio.HayQueImprimir);
            Assert.AreEqual(PropuestaEnvioDTO.ORIGEN_NUEVO, resultado.Envio.Origen);
            Assert.IsTrue(resultado.Envio.CorreoEnviado);
            A.CallTo(() => notificador.Notificar(envio)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Nuevo_CambiosDeLaPantalla_SeAplicanYSeRecalculaElCoste()
        {
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Cambios = new CambiosEnvioDesdePedidoDTO
            {
                Agencia = CTT, Servicio = 48, Horario = 0, Retorno = 0, Reembolso = 100m,
                Nombre = "Otra \"peluquería\"", Direccion = "Calle Otra, 2", CodPostal = "28002", Poblacion = "MADRID", Provincia = "MADRID",
                Pais = 34, Telefono = "911111111", Movil = "622222222", Email = "otra@peluqueria.es", Atencion = "Ana",
                Observaciones = new string('o', 100)
            };
            datos.Coste = agencia => agencia == CTT ? 3.10m : (decimal?)null;

            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            EnviosAgencia envio = resultado.Envio.Envio;
            Assert.AreEqual(CTT, envio.Agencia);
            Assert.AreEqual("1  ", envio.Empresa);
            Assert.AreEqual((short)48, envio.Servicio);
            Assert.AreEqual(100m, envio.Reembolso);
            Assert.AreEqual("Otra peluquería", envio.Nombre, "sin comillas, como VB 3531");
            Assert.AreEqual("28002", envio.CodPostal);
            Assert.AreEqual("Ana", envio.Atencion);
            Assert.AreEqual(80, envio.Observaciones.Length);
            Assert.AreEqual(3.10m, envio.ImporteGasto, "el coste de la agencia elegida en la pantalla");
            Assert.IsTrue(datos.CostesPedidos.Any(c => c.Agencia == CTT && c.Reembolso == 100m && c.Peso == 3.5m));
            Assert.AreEqual("CT", envio.Nemonico);
            Assert.AreEqual(string.Empty, envio.CodigoBarras, "CTT: el albarán lo da /Tramitar");
            Assert.AreEqual(FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR, resultado.Envio.Flujo);
            Assert.IsFalse(resultado.Envio.EtiquetaEnNesto);
            Assert.IsFalse(resultado.Envio.CorreoEnviado, "CTT avisa al cliente al registrar, no aquí");
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- pendiente reutilizado

        [TestMethod]
        public async Task Pendiente_ReutilizaElEnvio_ConSuDestinoYSinAvisarAlCliente()
        {
            EnviosAgencia pendiente = EnvioExistente(249500, Constantes.Agencias.ESTADO_PENDIENTE, CTT, PEDIDO);
            pendiente.Reembolso = -1m;
            pendiente.ImporteGasto = 2m;
            propuesta.Origen = PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO;
            propuesta.EnvioOrigen = 249500;
            propuesta.Agencia = CTT;
            propuesta.Servicio = 48;
            propuesta.Horario = 0;
            propuesta.Reembolso = 0m;
            // La propuesta real ya trae los bultos y el peso de la petición.
            propuesta.Bultos = 3;
            propuesta.Peso = 7m;
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Bultos = 3;
            peticion.Peso = 7m;
            peticion.Cambios = new CambiosEnvioDesdePedidoDTO { Agencia = CTT, Servicio = 24, Retorno = 0, Direccion = "Calle Tecleada, 9", Reembolso = 50m };
            datos.Coste = agencia => 0m;

            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            Assert.AreSame(pendiente, resultado.Envio.Envio);
            Assert.AreEqual(0, datos.Anadidos.Count, "se reutiliza, no se inserta otro");
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, pendiente.Estado);
            Assert.AreEqual((short)3, pendiente.Bultos);
            Assert.AreEqual(7m, pendiente.Peso);
            Assert.AreEqual((short)24, pendiente.Servicio, "CTT: el servicio de la pantalla (Nesto#495)");
            Assert.AreEqual("Calle Pendiente, 5", pendiente.Direccion, "el destino es el del pendiente (Nesto#395)");
            Assert.AreEqual(-1m, pendiente.Reembolso, "el reembolso del pendiente no cambia");
            Assert.AreEqual(2m, pendiente.ImporteGasto, "sin coste se queda el que tenía");
            Assert.IsTrue(resultado.Envio.Avisos.Any(a => a.Contains("conserva su destino")));
            Assert.IsTrue(resultado.Envio.HayQueImprimir);
            Assert.IsFalse(resultado.Envio.CorreoEnviado);
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pendiente_DeGls_NoAvisaAlClienteAunqueTengaCodigo()
        {
            EnvioExistente(249501, Constantes.Agencias.ESTADO_PENDIENTE, GLS, PEDIDO);
            propuesta.Origen = PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO;
            propuesta.EnvioOrigen = 249501;

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            Assert.AreEqual("61197140249501", resultado.Envio.Envio.CodigoBarras);
            Assert.IsFalse(resultado.Envio.CorreoEnviado, "decisión de Carlos: el pendiente reutilizado no avisa, como hoy");
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- ampliación

        private EnviosAgencia PrepararAmpliacion(int agencia, int pedidoDelEnvio, short bultosDelEnvio)
        {
            EnviosAgencia ampliado = EnvioExistente(249600, Constantes.Agencias.ESTADO_EN_CURSO, agencia, pedidoDelEnvio);
            ampliado.Bultos = bultosDelEnvio;
            ampliado.Reembolso = 30m;
            propuesta.Origen = PropuestaEnvioDTO.ORIGEN_AMPLIACION;
            propuesta.EnvioOrigen = 249600;
            propuesta.Agencia = agencia;
            // La propuesta ya suma el reembolso del envío si es de otro pedido (VB 3542).
            propuesta.Reembolso = pedidoDelEnvio != PEDIDO ? 30m + 120m : 120m;
            ConAviso(CodigosAvisoPropuestaEnvio.AMPLIACION, TiposAvisoPropuestaEnvio.CONFIRMAR, "Este pedido es una ampliación del envío 249600.");
            return ampliado;
        }

        [TestMethod]
        public async Task Ampliacion_SinConfirmar_409_YConfirmada_SumaElReembolsoDeOtroPedido()
        {
            EnviosAgencia ampliado = PrepararAmpliacion(GLS, 927600, bultosDelEnvio: 1);
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Cambios = new CambiosEnvioDesdePedidoDTO { Agencia = CTT, Reembolso = 120m, Servicio = 96, Horario = 18, Retorno = 0 };

            ResultadoEnvioDesdePedido sinConfirmar = await Crear(peticion);
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Conflicto, sinConfirmar.Tipo);
            Assert.AreEqual(CodigosAvisoPropuestaEnvio.AMPLIACION, sinConfirmar.Conflicto.Pendientes.Single().Codigo);
            Assert.AreEqual(0, datos.Guardados, "con un 409 no se graba nada");

            peticion.Confirmaciones = new List<string> { CodigosAvisoPropuestaEnvio.AMPLIACION };
            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            Assert.AreSame(ampliado, resultado.Envio.Envio);
            Assert.AreEqual(0, datos.Anadidos.Count);
            Assert.AreEqual(150m, ampliado.Reembolso, "30 € del envío + 120 € de este pedido");
            Assert.AreEqual(PEDIDO, ampliado.Pedido, "VB pone el pedido nuevo en el envío ampliado");
            Assert.AreEqual(GLS, ampliado.Agencia, "la agencia es la del envío que se amplía (Nesto#412)");
            Assert.IsTrue(resultado.Envio.Avisos.Any(a => a.Contains("el cambio de agencia no se aplica")));
            Assert.AreEqual((short)2, ampliado.Bultos);
            Assert.IsTrue(resultado.Envio.HayQueImprimir, "de 1 a 2 bultos: etiqueta nueva");
            Assert.IsFalse(resultado.Envio.CorreoEnviado, "la ampliación no avisa al cliente");
        }

        [TestMethod]
        public async Task Ampliacion_DelMismoPedidoConLosMismosBultos_NoHayQueImprimirNiSeSuma()
        {
            EnviosAgencia ampliado = PrepararAmpliacion(GLS, PEDIDO, bultosDelEnvio: 2);

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion(CodigosAvisoPropuestaEnvio.AMPLIACION));

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            Assert.AreEqual(120m, ampliado.Reembolso);
            Assert.IsFalse(resultado.Envio.HayQueImprimir);
        }

        [TestMethod]
        public async Task Ampliacion_DeCttYaRegistrado_400AnuleYVuelvaAInsertar()
        {
            EnviosAgencia ampliado = PrepararAmpliacion(CTT, PEDIDO, bultosDelEnvio: 1);
            ampliado.CodigoBarras = "0082800000123456";
            propuesta.Servicio = 48;
            propuesta.Horario = 0;

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion(CodigosAvisoPropuestaEnvio.AMPLIACION));

            AssertInvalido(resultado, CodigosErrorEnvioDesdePedido.AMPLIACION_REGISTRADA);
            StringAssert.Contains(resultado.Error.Message, "anule el envío y vuelva a insertarlo");
            Assert.AreEqual("0082800000123456", ampliado.CodigoBarras);
        }

        // ---------------------------------------------------------------- 409 y confirmaciones

        [TestMethod]
        public async Task SinPicking_409_YConfirmado_200()
        {
            ConAviso(CodigosAvisoPropuestaEnvio.SIN_PICKING, TiposAvisoPropuestaEnvio.CONFIRMAR, "Este pedido no tiene ninguna línea con picking.");

            ResultadoEnvioDesdePedido sinConfirmar = await Crear(Peticion());
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Conflicto, sinConfirmar.Tipo);
            AvisoPropuestaEnvio pendiente = sinConfirmar.Conflicto.Pendientes.Single();
            Assert.AreEqual(CodigosAvisoPropuestaEnvio.SIN_PICKING, pendiente.Codigo);
            Assert.AreEqual(TiposAvisoPropuestaEnvio.CONFIRMAR, pendiente.Tipo);
            Assert.IsNotNull(sinConfirmar.Conflicto.Propuesta);

            ResultadoEnvioDesdePedido confirmado = await Crear(Peticion("sinpicking"));
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, confirmado.Tipo, "el código se compara sin distinguir mayúsculas");
        }

        [TestMethod]
        public async Task DireccionPropia_DeLaPantalla_409AntesDeGrabar_YConfirmada_200()
        {
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Cambios = new CambiosEnvioDesdePedidoDTO { Direccion = "C/ Río Tiétar, 11", CodPostal = "28110", Poblacion = "ALGETE" };

            ResultadoEnvioDesdePedido sinConfirmar = await Crear(peticion);
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Conflicto, sinConfirmar.Tipo);
            Assert.AreEqual(CodigosAvisoPropuestaEnvio.DIRECCION_PROPIA, sinConfirmar.Conflicto.Pendientes.Single().Codigo);
            Assert.AreEqual(0, datos.Guardados);

            peticion.Confirmaciones = new List<string> { CodigosAvisoPropuestaEnvio.DIRECCION_PROPIA };
            ResultadoEnvioDesdePedido confirmado = await Crear(peticion);
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, confirmado.Tipo, confirmado.Error?.Message);
            Assert.AreEqual("28110", confirmado.Envio.Envio.CodPostal);
        }

        [TestMethod]
        public async Task Prepagos_SinDecidir_409Elegir()
        {
            propuesta.PrepagosPendientes = 50m;

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Conflicto, resultado.Tipo);
            AvisoPropuestaEnvio pendiente = resultado.Conflicto.Pendientes.Single();
            Assert.AreEqual(CodigosAvisoPropuestaEnvio.PREPAGOS, pendiente.Codigo);
            Assert.AreEqual(TiposAvisoPropuestaEnvio.ELEGIR, pendiente.Tipo);
        }

        [TestMethod]
        public async Task Prepagos_Descontarlos_ReembolsoMenosPrepagosYSeRecalculaElCoste()
        {
            propuesta.PrepagosPendientes = 50m;
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.DescontarPrepagos = true;
            datos.Coste = agencia => 4.00m;

            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            Assert.AreEqual(70m, resultado.Envio.Envio.Reembolso);
            Assert.AreEqual(4.00m, resultado.Envio.Envio.ImporteGasto);
            Assert.IsTrue(datos.CostesPedidos.Any(c => c.Reembolso == 70m));
        }

        [TestMethod]
        public async Task Prepagos_NoDescontarlos_ReembolsoDeLaPantalla()
        {
            propuesta.PrepagosPendientes = 50m;
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.DescontarPrepagos = false;

            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            Assert.AreEqual(120m, resultado.Envio.Envio.Reembolso);
            Assert.AreEqual(4.25m, resultado.Envio.Envio.ImporteGasto, "nada cambia el coste: el de la propuesta");
            Assert.AreEqual(0, datos.CostesPedidos.Count);
        }

        [TestMethod]
        public async Task Canteras_SinDimensiones_409Dato_YConDimensiones_VanALasObservaciones()
        {
            propuesta.Agencia = CANTERAS;
            propuesta.Servicio = 0;
            propuesta.Horario = 0;
            propuesta.Reembolso = 0m;
            propuesta.CodPostal = "35001";
            propuesta.Observaciones = new string('o', 70);
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Dimensiones = "3";

            ResultadoEnvioDesdePedido sinDimensiones = await Crear(peticion);
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Conflicto, sinDimensiones.Tipo);
            AvisoPropuestaEnvio pendiente = sinDimensiones.Conflicto.Pendientes.Single();
            Assert.AreEqual(CodigosAvisoPropuestaEnvio.DIMENSIONES, pendiente.Codigo);
            Assert.AreEqual(TiposAvisoPropuestaEnvio.DATO, pendiente.Tipo);

            peticion.Dimensiones = " 30x20x15 ";
            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            EnviosAgencia envio = resultado.Envio.Envio;
            Assert.AreEqual(new string('o', 70) + " - 30x20x1", envio.Observaciones, "recortadas a 80");
            Assert.AreEqual("CN", envio.Nemonico);
            Assert.AreEqual(string.Empty, envio.CodigoBarras, "Canteras: el nº llega por correo");
            Assert.AreEqual(FlujosEnvioDesdePedido.MANUAL, resultado.Envio.Flujo);
            Assert.IsTrue(resultado.Envio.EtiquetaEnNesto);
            Assert.IsFalse(resultado.Envio.CorreoEnviado, "sin código no se avisa");
        }

        // ---------------------------------------------------------------- 400 y 404

        private static void AssertInvalido(ResultadoEnvioDesdePedido resultado, string codigo)
        {
            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Invalido, resultado.Tipo);
            Assert.AreEqual(codigo, resultado.Error.Codigo, resultado.Error.Message);
        }

        [TestMethod]
        public async Task SinPeso_400()
        {
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Peso = 0m;

            AssertInvalido(await Crear(peticion), CodigosErrorEnvioDesdePedido.PESO_OBLIGATORIO);
            A.CallTo(() => propuestas.Calcular(A<string>._, A<int>._, A<short?>._, A<decimal?>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EtiquetaDeRecogida_400HagalaDesdeNesto()
        {
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.EtiquetaRecogida = true;

            ResultadoEnvioDesdePedido resultado = await Crear(peticion);

            AssertInvalido(resultado, CodigosErrorEnvioDesdePedido.ETIQUETA_RECOGIDA);
            Assert.AreEqual("Haga la etiqueta de recogida desde Nesto.", resultado.Error.Message);
        }

        [TestMethod]
        public async Task SinAgenciaConTarifa_400()
        {
            propuesta.Agencia = 0;
            ConAviso(CodigosAvisoPropuestaEnvio.SIN_AGENCIA, TiposAvisoPropuestaEnvio.BLOQUEANTE, "Ninguna agencia tiene tarifa.");

            AssertInvalido(await Crear(Peticion()), CodigosErrorEnvioDesdePedido.SIN_AGENCIA);
        }

        [TestMethod]
        public async Task ServicioQueNoEsDeLaAgencia_400Catalogo()
        {
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Cambios = new CambiosEnvioDesdePedidoDTO { Servicio = 48, Horario = 0 };

            AssertInvalido(await Crear(peticion), CodigosErrorEnvioDesdePedido.CATALOGO_INVALIDO);
            Assert.AreEqual(0, datos.Guardados);
        }

        [TestMethod]
        public async Task AgenciaNoSoportada_400_CorreosExpressYGlovo()
        {
            foreach (int agencia in new[] { Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, Constantes.Agencias.AGENCIA_GLOVO })
            {
                CrearEnvioDesdePedidoDTO peticion = Peticion();
                peticion.Cambios = new CambiosEnvioDesdePedidoDTO { Agencia = agencia };
                propuesta = PropuestaNueva();

                AssertInvalido(await Crear(peticion), CodigosErrorEnvioDesdePedido.AGENCIA_NO_SOPORTADA);
            }
            Assert.AreEqual(0, datos.Guardados);
        }

        [TestMethod]
        public async Task CambioAUnaAgenciaSinCoberturaParaElDestino_400()
        {
            CrearEnvioDesdePedidoDTO peticion = Peticion();
            peticion.Cambios = new CambiosEnvioDesdePedidoDTO { Agencia = CTT, Servicio = 48, Horario = 0 };
            datos.Coste = agencia => null;

            AssertInvalido(await Crear(peticion), CodigosErrorEnvioDesdePedido.SIN_COBERTURA);
            Assert.AreEqual(0, datos.Guardados);
        }

        [TestMethod]
        public async Task PedidoQueNoExiste_404()
        {
            propuesta = null;

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.NoEncontrado, resultado.Tipo);
        }

        // ---------------------------------------------------------------- GLS sin plaza

        [TestMethod]
        public async Task Gls_PlazaCaida_SeGrabaSinPlazaYConAviso()
        {
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._)).Throws(new TimeoutException("GLS no contesta"));

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo, resultado.Error?.Message);
            EnviosAgencia envio = resultado.Envio.Envio;
            Assert.IsNull(envio.Nemonico);
            Assert.IsNull(envio.NombrePlaza);
            Assert.AreEqual("61197140250000", envio.CodigoBarras);
            Assert.IsTrue(resultado.Envio.Avisos.Any(a => a.Contains("sin plaza")));
            Assert.IsTrue(registrados.Any(e => e.Message.Contains("[Plaza GLS #595]")), "el fallo de GLS va a ELMAH");
        }

        // ---------------------------------------------------------------- aviso al cliente

        [TestMethod]
        public async Task Correo_TodoElPedidoOnline_NoSeAvisa()
        {
            datos.TodoOnline = true;

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo);
            Assert.IsFalse(resultado.Envio.CorreoEnviado);
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_QueFalla_NoRompeLaRespuesta()
        {
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).Throws(new InvalidOperationException("SMTP caído"));

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            Assert.AreEqual(TipoResultadoEnvioDesdePedido.Creado, resultado.Tipo);
            Assert.IsFalse(resultado.Envio.CorreoEnviado);
            Assert.IsTrue(datos.Confirmada, "el envío ya estaba grabado");
            Assert.IsTrue(registrados.Any(e => e.Message.Contains("no se pudo avisar al cliente")));
        }

        // ---------------------------------------------------------------- transacción

        [TestMethod]
        public async Task FalloAlGrabarElCodigo_Rollback_YNiCorreoNiConfirmacion()
        {
            datos.FallarEnElGuardado = 2;

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Crear(Peticion()));

            Assert.IsTrue(datos.Deshecha, "rollback de la transacción");
            Assert.IsFalse(datos.Confirmada);
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinPrefijoParaElCodigo_400_YRollback()
        {
            // La GLS de la empresa espejo (5) sin prefijo y con un servicio que no es BusinessParcel: Nesto daría null y compensaría.
            propuesta.Agencia = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO;
            propuesta.Servicio = 1;

            ResultadoEnvioDesdePedido resultado = await Crear(Peticion());

            AssertInvalido(resultado, CodigosErrorEnvioDesdePedido.SIN_CODIGO_BARRAS);
            Assert.IsTrue(datos.Deshecha);
            Assert.IsFalse(datos.Confirmada);
            A.CallTo(() => notificador.Notificar(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- piezas

        [TestMethod]
        public void Flujos_PorAgencia()
        {
            Assert.AreEqual(FlujosEnvioDesdePedido.TRAMITAR_AL_CERRAR, ServicioEnvioDesdePedido.FlujoDe(GLS));
            Assert.AreEqual(FlujosEnvioDesdePedido.TRAMITAR_AL_CERRAR, ServicioEnvioDesdePedido.FlujoDe(Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO));
            Assert.AreEqual(FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR, ServicioEnvioDesdePedido.FlujoDe(CTT));
            Assert.AreEqual(FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR, ServicioEnvioDesdePedido.FlujoDe(Constantes.Agencias.AGENCIA_INNOVATRANS));
            Assert.AreEqual(FlujosEnvioDesdePedido.MANUAL, ServicioEnvioDesdePedido.FlujoDe(CANTERAS));
        }

        private EnviosAgencia EnvioExistente(int numero, int estado, int agencia, int pedido)
        {
            var envio = new EnviosAgencia
            {
                Numero = numero, Empresa = "1  ", Agencia = agencia, Estado = (short)estado, Pedido = pedido, Cliente = "15191", Contacto = "0",
                Servicio = 96, Horario = 18, Bultos = 1, Nombre = "PELUQUERÍA LA BONITA", Direccion = "Calle Pendiente, 5", CodPostal = "28001",
                Poblacion = "MADRID", Provincia = "MADRID", Pais = 34, Email = "agencia@bonita.es", Vendedor = "JE"
            };
            datos.Envios[numero] = envio;
            return envio;
        }

        /// <summary>La BD en memoria: identity desde 250000, transacción con commit/rollback y el comparador como función.</summary>
        private class DatosFalsos : IDatosEnvioDesdePedido
        {
            private int siguienteNumero = 250000;

            public Dictionary<int, AgenciaTransporte> Agencias { get; } = new Dictionary<int, AgenciaTransporte>
            {
                { GLS, new AgenciaTransporte { Numero = GLS, Empresa = "1  ", Nombre = "ASM", PrefijoCodigoBarras = "6108352" } },
                { Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO, new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO, Empresa = "3  ", Nombre = "ASM" } },
                { CTT, new AgenciaTransporte { Numero = CTT, Empresa = "1  ", Nombre = "CTT" } },
                { CANTERAS, new AgenciaTransporte { Numero = CANTERAS, Empresa = "1  ", Nombre = "Canteras" } },
                { Constantes.Agencias.AGENCIA_GLOVO, new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLOVO, Empresa = "1  ", Nombre = "Glovo" } },
                { Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, Empresa = "1  ", Nombre = "Correos Express" } }
            };
            public Dictionary<int, EnviosAgencia> Envios { get; } = new Dictionary<int, EnviosAgencia>();
            public List<EnviosAgencia> Anadidos { get; } = new List<EnviosAgencia>();
            public int Guardados { get; private set; }
            public int? FallarEnElGuardado { get; set; }
            public bool Confirmada { get; private set; }
            public bool Deshecha { get; private set; }
            public bool TodoOnline { get; set; }
            public Func<int, decimal?> Coste { get; set; } = agencia => 5m;
            public List<(int Agencia, decimal Peso, decimal Reembolso)> CostesPedidos { get; } = new List<(int, decimal, decimal)>();

            public Task<AgenciaTransporte> LeerAgencia(int numero)
                => Task.FromResult(Agencias.TryGetValue(numero, out AgenciaTransporte a) ? a : null);

            public Task<EnviosAgencia> LeerEnvioParaModificar(int numero)
                => Task.FromResult(Envios.TryGetValue(numero, out EnviosAgencia e) ? e : null);

            public void Anadir(EnviosAgencia envio) => Anadidos.Add(envio);

            public Task GuardarCambios()
            {
                Guardados++;
                if (FallarEnElGuardado == Guardados)
                {
                    throw new InvalidOperationException("La BD no contesta");
                }
                foreach (EnviosAgencia envio in Anadidos.Where(e => e.Numero == 0))
                {
                    envio.Numero = siguienteNumero++;
                }
                return Task.CompletedTask;
            }

            public async Task<T> EnTransaccion<T>(Func<Task<T>> cuerpo)
            {
                try
                {
                    T resultado = await cuerpo();
                    Confirmada = true;
                    return resultado;
                }
                catch
                {
                    Deshecha = true;
                    throw;
                }
            }

            public Task<bool> EsTodoElPedidoOnline(string empresa, int pedido) => Task.FromResult(TodoOnline);

            public OpcionEnvioAgencia CosteDeAgencia(string empresa, string codPostal, decimal peso, decimal reembolso, int agencia, byte? servicio, string paisIso)
            {
                if (peso > 0m)
                {
                    CostesPedidos.Add((agencia, peso, reembolso));
                }
                decimal? coste = Coste(agencia);
                return coste.HasValue ? new OpcionEnvioAgencia { AgenciaId = agencia, Coste = coste.Value } : null;
            }

            public bool EsAgenciaDelComparador(int agencia)
                => agencia == GLS || agencia == CTT || agencia == Constantes.Agencias.AGENCIA_INNOVATRANS;
        }
    }
}
