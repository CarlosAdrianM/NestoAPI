using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#773 (Carlos 31/10/26): cobro con tarjeta desde la PDA (enlace Redsys por WhatsApp o correo) y autorización para entregar
    /// un reembolso sin cobrar.
    /// </summary>
    [TestClass]
    public class CobroTarjetaRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 31, 10, 15, 0);
        private const string USUARIO = @"NUEVAVISION\Repartidor";
        private const string URL = "https://api.nuevavision.es/pago/abc";

        private ServicioRepartoPropioTests.RepositorioFalso reparto;
        private TarjetasFalsas tarjetas;
        private AutorizacionesFalsas autorizacionesRepo;
        private IServicioPagos pagos;
        private IAvisadorAutorizacionesReembolso avisador;
        private ServicioCobroTarjetaReparto servicioTarjeta;
        private ServicioAutorizacionesReembolso servicioAutorizaciones;
        private ServicioRepartoPropio servicioReparto;
        private int siguientePago;
        private SolicitudPagoTPV solicitud;
        /// <summary>NestoAPI#775: los escalados a Dirección programados (en vez de Hangfire).</summary>
        private readonly List<int> escalados = new List<int>();

        [TestInitialize]
        public void Setup()
        {
            reparto = new ServicioRepartoPropioTests.RepositorioFalso();
            tarjetas = new TarjetasFalsas();
            autorizacionesRepo = new AutorizacionesFalsas();
            pagos = A.Fake<IServicioPagos>();
            siguientePago = 4321;
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).ReturnsLazily((SolicitudPagoTPV s, string u) =>
            {
                solicitud = s;
                int id = siguientePago++;
                tarjetas.Pagos.Add(new PagoCadenaReparto { Id = id, Estado = Constantes.EstadosPagoTPV.PENDIENTE, Importe = 45.30m, FechaCreacion = AHORA, TokenAcceso = Guid.NewGuid() });
                return new RespuestaIniciarPago { IdPago = id, UrlPaginaPago = URL };
            });
            avisador = A.Fake<IAvisadorAutorizacionesReembolso>();
            servicioTarjeta = new ServicioCobroTarjetaReparto(reparto, tarjetas, () => pagos, () => AHORA);
            servicioAutorizaciones = new ServicioAutorizacionesReembolso(reparto, autorizacionesRepo, avisador, () => AHORA, id => escalados.Add(id));
            var parametros = new ServicioRepartoPropioTests.ParametrosFalsos();
            parametros.Caja("Repartidor", "57000001");
            servicioReparto = new ServicioRepartoPropio(reparto, () => AHORA, null, parametros, () => pagos)
            {
                CobroEntrega = new ComprobadorCobroEntrega(tarjetas, autorizacionesRepo, avisador, () => AHORA)
            };
        }

        private EnviosAgencia EnvioConReembolso(string empresa = "1  ", decimal reembolso = 45.30m)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(reembolso: reembolso);
            envio.Empresa = empresa;
            envio.Email = "lucia@peluqueria.es";
            return reparto.Con(envio);
        }

        private void Pagar(int pagoId) => tarjetas.Pagos.Single(p => p.Id == pagoId).Estado = Constantes.EstadosPagoTPV.AUTORIZADO;

        private static EntregaRepartoDTO Entrega(string cobro = null, int? pago = null, string telefonica = null)
            => new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = cobro, PagoTPVId = pago, AutorizacionTelefonica = telefonica };

        // ================================================================ el enlace

        [TestMethod]
        public async Task EnlaceEnvio_Empresa3_SinTarjeta_400_YNoSeCreaEnlace()
        {
            EnvioConReembolso("3  ");

            var resultado = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "empresa 3");
            StringAssert.Contains(resultado.Mensaje, "efectivo");
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            Assert.IsFalse(new EnvioRepartoDTO { Empresa = "3" }.AdmiteTarjeta);
            Assert.IsTrue(new EnvioRepartoDTO { Empresa = "1" }.AdmiteTarjeta);
        }

        [TestMethod]
        public async Task EnlaceEnvio_CreaElEnlaceDelReembolso_ConWhatsAppDeUsted_YLoGuarda()
        {
            EnvioConReembolso();
            tarjetas.Contacto = new DatosContactoCobro { Telefonos = new List<string> { "916281914 / 611222333" } };

            var resultado = await servicioTarjeta.EnlaceEnvio(251300, new PeticionEnlaceTarjetaDTO(), USUARIO, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(4321, resultado.Contenido.PagoTPVId);
            Assert.AreEqual(URL, resultado.Contenido.Url);
            Assert.AreEqual(45.30m, resultado.Contenido.Importe);
            Assert.AreEqual(EstadosEnlaceTarjeta.PENDIENTE, resultado.Contenido.Estado);
            Assert.IsFalse(resultado.Contenido.Reutilizado);
            Assert.AreEqual("PagoTPV:4321", resultado.Contenido.GrupoAvisos);
            CollectionAssert.AreEqual(new[] { "34600123456", "34611222333" }, resultado.Contenido.Moviles, "El del envío primero; los fijos fuera");
            Assert.AreEqual("lucia@peluqueria.es", resultado.Contenido.Correo);
            StringAssert.Contains(resultado.Contenido.Texto, "Buenos días");
            StringAssert.Contains(resultado.Contenido.Texto, "pedido 928000");
            StringAssert.Contains(resultado.Contenido.Texto, "45,30 €");
            StringAssert.Contains(resultado.Contenido.Texto, URL);
            StringAssert.Contains(resultado.Contenido.Texto, "puede usar");
            Assert.AreEqual("Pago pedido 928000 de Nueva Visión", solicitud.Descripcion);
            Assert.AreEqual("1", solicitud.Empresa);
            Assert.IsNull(solicitud.Correo, "Por WhatsApp no se manda correo");
            EnlaceTarjetaReparto guardado = tarjetas.Enlaces.Single();
            Assert.AreEqual(251300, guardado.EnvioId);
            Assert.AreEqual(TiposCobroTarjetaReparto.REEMBOLSO, guardado.Tipo);
            Assert.AreEqual(USUARIO, guardado.Usuario);
            Assert.AreEqual("PDA1", guardado.Dispositivo);
        }

        [TestMethod]
        public async Task EnlaceEnvio_ConUnEnlaceVivo_LoReutiliza_YNoCreaOtroCobro()
        {
            EnvioConReembolso();
            var primero = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            var segundo = await servicioTarjeta.EnlaceEnvio(251300, new PeticionEnlaceTarjetaDTO { Correo = "otro@cliente.es" }, USUARIO, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Ok, segundo.Tipo, segundo.Mensaje);
            Assert.IsTrue(segundo.Contenido.Reutilizado);
            Assert.AreEqual(primero.Contenido.PagoTPVId, segundo.Contenido.PagoTPVId);
            Assert.IsTrue(segundo.Contenido.CorreoEnviado);
            Assert.AreEqual("otro@cliente.es", tarjetas.CorreosMandados.Single().Value);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnlaceEnvio_YaPagado_DiceQueEstaPagado_SinOtroEnlace()
        {
            EnvioConReembolso();
            var primero = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");
            Pagar(primero.Contenido.PagoTPVId);

            var segundo = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            Assert.AreEqual(EstadosEnlaceTarjeta.PAGADO, segundo.Contenido.Estado);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnlaceEnvio_ConOtroImporteOAnulado_CreaUnoNuevo()
        {
            EnviosAgencia envio = EnvioConReembolso();
            var primero = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");
            envio.Reembolso = 50m;

            var segundo = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            Assert.IsFalse(segundo.Contenido.Reutilizado);
            Assert.AreNotEqual(primero.Contenido.PagoTPVId, segundo.Contenido.PagoTPVId);
        }

        [TestMethod]
        public async Task EnlaceEnvio_SinReembolso_SinScript_OEntregado_NoCreaNada()
        {
            EnvioConReembolso(reembolso: 0m);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, null)).Tipo);
            EnvioConReembolso().Estado = Constantes.Agencias.ESTADO_ENTREGADO;
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, null)).Tipo);
            tarjetas.Script = false;
            var sinScript = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, null);
            StringAssert.Contains(sinScript.Mensaje, "Issue773");
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        // ================================================================ el reembolso con tarjeta liquida la factura (como el efectivo)

        private static EfectoCobroReparto FacturaPendiente(decimal pendiente = 45.30m, string empresa = "1  ")
            => new EfectoCobroReparto
            {
                Id = 3040001, Empresa = empresa, Cliente = "15191     ", Contacto = "0  ", TipoApunte = "2  ", Documento = "NV2614001 ", Efecto = "1 ",
                Importe = pendiente, ImportePendiente = pendiente, Delegacion = "ALG", FormaVenta = "VAR", Vendedor = "JE ", Ruta = "AT "
            };

        [TestMethod]
        public async Task EnlaceEnvio_ConLaFacturaPendiente_LaLiquida_ConSuDocumentoYEfecto()
        {
            EnvioConReembolso();
            tarjetas.EfectoDelReembolso = FacturaPendiente();

            var resultado = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            EfectoAPagar efecto = solicitud.Efectos.Single();
            Assert.AreEqual(3040001, efecto.ExtractoClienteId);
            Assert.AreEqual(45.30m, efecto.Importe, "Por el reembolso (lo que se cobra), como el efectivo");
            Assert.AreEqual("NV2614001", efecto.Documento);
            Assert.AreEqual("1", efecto.Efecto);
            Assert.AreEqual("0", efecto.Contacto);
            Assert.AreEqual("ALG", efecto.Delegacion);
            Assert.AreEqual("VAR", efecto.FormaVenta);
            Assert.AreEqual("JE", efecto.Vendedor);
            Assert.AreEqual(45.30m, solicitud.Importe);
            EnvioHistoria historia = tarjetas.Historia.Single();
            Assert.AreEqual(251300, historia.NumeroEnvio);
            StringAssert.Contains(historia.Observaciones, "liquida la factura NV2614001/1");
            Assert.IsTrue(historia.Campo.Length <= 50 && historia.ValorAnterior.Length <= 50);
        }

        [TestMethod]
        public void ContabilizarCobro_DelEnlaceDelReembolso_LiquidaElEfectoConSuDocumento_SinNadaACuenta()
        {
            LiquidacionReembolsoTarjeta liquidacion = ServicioCobroTarjetaReparto.EfectoALiquidar(FacturaPendiente(), 45.30m);
            var pago = new PagoTPV
            {
                Cliente = "15191", Contacto = "0", Importe = 45.30m, NumeroOrden = "260000012345", Descripcion = "PAGO PEDIDO 928000",
                PagosTPV_Efectos = new List<PagoTPV_Efecto>
                {
                    new PagoTPV_Efecto
                    {
                        ExtractoClienteId = liquidacion.Efecto.ExtractoClienteId, Importe = liquidacion.Efecto.Importe, Documento = liquidacion.Efecto.Documento,
                        Efecto = liquidacion.Efecto.Efecto, Contacto = liquidacion.Efecto.Contacto, Vendedor = liquidacion.Efecto.Vendedor,
                        FormaVenta = liquidacion.Efecto.FormaVenta, Delegacion = liquidacion.Efecto.Delegacion, TipoApunte = liquidacion.Efecto.TipoApunte
                    }
                }
            };

            List<PreContabilidad> lineas = ServicioPagos.ConstruirLineasCobro(pago, "57200020", "1", "Pago TPV PAGO PEDIDO 928000");

            Assert.AreEqual(2, lineas.Count, "Banco + la factura: sin línea de diferencia a cuenta");
            PreContabilidad cliente = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE);
            Assert.AreEqual(3040001, cliente.Liquidado);
            Assert.AreEqual(45.30m, cliente.Haber);
            Assert.AreEqual("NV2614001", cliente.Nº_Documento);
            Assert.AreEqual("1", cliente.Efecto);
            Assert.AreEqual("ALG", cliente.Delegación);
            Assert.AreEqual("VAR", cliente.FormaVenta);
        }

        [TestMethod]
        public async Task EnlaceEnvio_SinFactura_ConOtroImporte_DeOtraEmpresa_OSiFallaLaBusqueda_QuedaACuenta_SinBloquear()
        {
            EnvioConReembolso();
            var casos = new List<Action>
            {
                () => tarjetas.EfectoDelReembolso = null,
                () => tarjetas.EfectoDelReembolso = FacturaPendiente(60m),
                () => tarjetas.EfectoDelReembolso = FacturaPendiente(empresa: "3  "),
                () => { tarjetas.EfectoDelReembolso = FacturaPendiente(); tarjetas.FallaEfectoDelReembolso = true; }
            };
            var motivos = new[] { "ninguna factura pendiente", "es de 60,00 €, no de 45,30 €", "empresa 3", "no se ha podido buscar" };
            for (int i = 0; i < casos.Count; i++)
            {
                tarjetas.Enlaces.Clear();
                tarjetas.Historia.Clear();
                tarjetas.FallaEfectoDelReembolso = false;
                casos[i]();

                var resultado = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

                Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, $"Caso {i}: {resultado.Mensaje}");
                Assert.IsFalse(resultado.Contenido.Reutilizado, $"Caso {i}");
                Assert.IsTrue(solicitud.Efectos == null || solicitud.Efectos.Count == 0, $"Caso {i}: sin efectos, a cuenta como antes");
                StringAssert.Contains(tarjetas.Historia.Single().Observaciones, "A CUENTA", $"Caso {i}");
                StringAssert.Contains(tarjetas.Historia.Single().Observaciones, motivos[i], $"Caso {i}");
            }
        }

        [TestMethod]
        public async Task EnlaceEnvio_AlReutilizarElVivo_NoBuscaNiDuplicaEfectos()
        {
            EnvioConReembolso();
            tarjetas.EfectoDelReembolso = FacturaPendiente();
            var primero = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            var segundo = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, "PDA1");

            Assert.IsTrue(segundo.Contenido.Reutilizado);
            Assert.AreEqual(primero.Contenido.PagoTPVId, segundo.Contenido.PagoTPVId);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, tarjetas.BusquedasEfectoDelReembolso, "El efecto solo se busca (y se asocia) al crear el PagoTPV");
            Assert.AreEqual(1, tarjetas.Historia.Count);
        }

        [TestMethod]
        public async Task EnlaceDeuda_NoBuscaLaFacturaDelReembolso_NiAnotaHistoria()
        {
            tarjetas.Efectos.Add(new EfectoCobroReparto { Id = 10, Empresa = "1  ", Cliente = "15191", Contacto = "0", Documento = "A26/100", Efecto = "1", ImportePendiente = 30m });

            var resultado = await servicioTarjeta.EnlaceDeuda(new PeticionEnlaceTarjetaDeudaDTO { Cliente = "15191", Efectos = new List<int> { 10 } }, USUARIO, null);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(10, solicitud.Efectos.Single().ExtractoClienteId);
            Assert.AreEqual(0, tarjetas.BusquedasEfectoDelReembolso);
            Assert.AreEqual(0, tarjetas.Historia.Count);
        }

        [TestMethod]
        public void EstadoDeLaCadena_PagadoSiAlgunoAutorizado_DenegadoSoloSiElUltimo_AnuladoSiCancelado()
        {
            var raiz = new PagoCadenaReparto { Id = 1, Estado = "Denegado", FechaCreacion = AHORA };
            var regenerado = new PagoCadenaReparto { Id = 2, PagoOriginalId = 1, Estado = "Pendiente", FechaCreacion = AHORA.AddMinutes(1) };
            Assert.AreEqual(EstadosEnlaceTarjeta.PENDIENTE, ServicioCobroTarjetaReparto.EstadoDeLaCadena(new[] { raiz, regenerado }));
            regenerado.Estado = "Autorizado";
            Assert.AreEqual(EstadosEnlaceTarjeta.PAGADO, ServicioCobroTarjetaReparto.EstadoDeLaCadena(new[] { raiz, regenerado }));
            Assert.AreEqual(EstadosEnlaceTarjeta.DENEGADO, ServicioCobroTarjetaReparto.EstadoDeLaCadena(new[] { raiz }));
            raiz.Estado = "Cancelado";
            Assert.AreEqual(EstadosEnlaceTarjeta.ANULADO, ServicioCobroTarjetaReparto.EstadoDeLaCadena(new[] { raiz }));
        }

        // ================================================================ deuda (#766)

        [TestMethod]
        public async Task EnlaceDeuda_PorLaSumaDeLosEfectos_ConLosEfectosEnElEnlace()
        {
            tarjetas.Efectos.Add(new EfectoCobroReparto { Id = 10, Empresa = "1  ", Cliente = "15191", Contacto = "0", Documento = "A26/100", Efecto = "1", ImportePendiente = 30m });
            tarjetas.Efectos.Add(new EfectoCobroReparto { Id = 11, Empresa = "1  ", Cliente = "15191", Contacto = "0", Documento = "A26/101", Efecto = "1", ImportePendiente = 15.30m });

            var resultado = await servicioTarjeta.EnlaceDeuda(new PeticionEnlaceTarjetaDeudaDTO { Cliente = "15191", Efectos = new List<int> { 11, 10 } }, USUARIO, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(45.30m, resultado.Contenido.Importe);
            Assert.AreEqual(45.30m, solicitud.Importe);
            CollectionAssert.AreEquivalent(new[] { 10, 11 }, solicitud.Efectos.Select(e => e.ExtractoClienteId).ToList());
            Assert.AreEqual("10,11", tarjetas.Enlaces.Single().Efectos);
            StringAssert.Contains(resultado.Contenido.Texto, "A26/101");

            var otraVez = await servicioTarjeta.EnlaceDeuda(new PeticionEnlaceTarjetaDeudaDTO { Cliente = "15191", Efectos = new List<int> { 10, 11 } }, USUARIO, "PDA1");
            Assert.IsTrue(otraVez.Contenido.Reutilizado, "Los mismos efectos: el mismo enlace");
        }

        [TestMethod]
        public async Task EnlaceDeuda_SinEfectos_DeLaEmpresa3_DeOtroCliente_OAbonos_400()
        {
            tarjetas.Efectos.Add(new EfectoCobroReparto { Id = 10, Empresa = "3  ", Cliente = "15191", ImportePendiente = 30m });
            tarjetas.Efectos.Add(new EfectoCobroReparto { Id = 11, Empresa = "1  ", Cliente = "99999", ImportePendiente = 30m });
            tarjetas.Efectos.Add(new EfectoCobroReparto { Id = 12, Empresa = "1  ", Cliente = "15191", ImportePendiente = -5m });
            foreach (List<int> efectos in new[] { new List<int>(), new List<int> { 10 }, new List<int> { 11 }, new List<int> { 12 } })
            {
                var resultado = await servicioTarjeta.EnlaceDeuda(new PeticionEnlaceTarjetaDeudaDTO { Cliente = "15191", Efectos = efectos }, USUARIO, null);
                Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo, string.Join(",", efectos));
            }
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        // ================================================================ el pago: SignalR a la PDA que lo generó

        [TestMethod]
        public async Task AlPagarse_AvisaAlGrupoDelPagoRaiz_AunqueSeaElRegenerado()
        {
            var avisos = A.Fake<IAvisosCobroReparto>();
            PagoCompletadoRepartoDTO aviso = null;
            A.CallTo(() => avisos.PagoCompletado(A<PagoCompletadoRepartoDTO>._)).Invokes((PagoCompletadoRepartoDTO p) => aviso = p);
            var enlace = new EnlaceTarjetaReparto { PagoTPVId = 4321, EnvioId = 251300, Cliente = "15191", Tipo = TiposCobroTarjetaReparto.REEMBOLSO };
            var servicio = new AvisoPagoReparto(id => Task.FromResult(id == 4321 ? enlace : null), avisos, () => AHORA);

            bool avisado = await servicio.Avisar(new PagoTPV { Id = 4400, PagoOriginalId = 4321, Importe = 45.30m });

            Assert.IsTrue(avisado);
            Assert.AreEqual(4321, aviso.PagoTPVId);
            Assert.AreEqual(251300, aviso.EnvioId);
            Assert.AreEqual(45.30m, aviso.Importe);
            Assert.IsFalse(await servicio.Avisar(new PagoTPV { Id = 1 }), "Un enlace que no es del reparto no avisa");
        }

        [TestMethod]
        public async Task ProcesarNotificacion_LlamaAlAvisoDelReparto_YSiFallaNoRompe()
        {
            var servicio = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<NestoAPI.Infraestructure.Contabilidad.IContabilidadService>(),
                A.Fake<NestoAPI.Infraestructure.ILectorParametrosUsuario>(), A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>(), A.Fake<ITarjetaClienteStore>());
            PagoTPV avisado = null;
            servicio.AvisarCobroReparto = p => { avisado = p; return Task.CompletedTask; };
            var pago = new PagoTPV { Id = 4321, Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL };

            await servicio.AvisarCobroDelReparto(pago);
            Assert.AreSame(pago, avisado);

            servicio.AvisarCobroReparto = p => throw new InvalidOperationException("SignalR caído");
            await servicio.AvisarCobroDelReparto(pago); // no lanza
        }

        [TestMethod]
        public async Task AvisoUrgenteDe679_NoSaleParaLosEnlacesDelReparto()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            bool leido = false;

            bool avisado = await AvisoCobroEnlaceConReembolsoJobs.Avisar(4321, _ => { leido = true; return Task.FromResult(new CobroEnlaceConEnvioEnReparto()); },
                correo, notificaciones, _ => { }, _ => Task.FromResult(true));

            Assert.IsFalse(avisado);
            Assert.IsFalse(leido);
            A.CallTo(correo).MustNotHaveHappened();
            A.CallTo(notificaciones).MustNotHaveHappened();
        }

        [TestMethod]
        public void GruposNuevosDelAlmacenHub_SeNormalizan()
        {
            Assert.AreEqual("PagoTPV:4321", AvisosAlmacen.Normalizar("pagotpv:4321"));
            Assert.AreEqual("AutorizacionReembolso:12", AvisosAlmacen.Normalizar("AutorizacionReembolso:12"));
            Assert.AreEqual("Packing:99633", AvisosAlmacen.Normalizar("Packing:99633"));
            Assert.IsNull(AvisosAlmacen.Normalizar("PagoTPV:abc"));
        }

        // ================================================================ entregar con tarjeta

        [TestMethod]
        public async Task Entregar_Tarjeta_SinPagar_400_YNadaRegistrado()
        {
            EnvioConReembolso();
            var enlace = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, null);

            var resultado = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_TARJETA, enlace.Contenido.PagoTPVId), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "todavía no ha pagado");
            Assert.AreEqual(0, reparto.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_TarjetaPagada_Entrega_SinContabilizarNada_ConElPagoYSuImporte()
        {
            EnviosAgencia envio = EnvioConReembolso();
            var enlace = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, null);
            Pagar(enlace.Contenido.PagoTPVId);

            var resultado = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_TARJETA, enlace.Contenido.PagoTPVId), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            ServicioRepartoPropioTests.Registro registro = reparto.Registros.Single();
            Assert.IsNull(registro.ImporteAContabilizar, "Lo ha contabilizado ServicioPagos al pagarse: aquí, nada (sin doble contabilización)");
            Assert.AreEqual(1, tarjetas.BusquedasEfectoDelReembolso, "Al entregar no se vuelve a buscar ni liquidar la factura");
            Assert.AreEqual(PruebaEntregaReparto.COBRO_TARJETA, registro.Prueba.CobroReembolso);
            Assert.AreEqual(45.30m, registro.Prueba.ImporteCobrado);
            Assert.AreEqual(enlace.Contenido.PagoTPVId, registro.Prueba.PagoTPVId);
            Assert.IsNull(registro.Prueba.CuentaCaja);
            StringAssert.Contains(registro.Historia[0].Observaciones, "pagado con tarjeta");
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
        }

        [TestMethod]
        public async Task Entregar_TarjetaDeOtroEnvio_OSinPago_400()
        {
            EnvioConReembolso();
            reparto.Con(ServicioRepartoPropioTests.Envio(numero: 251301, reembolso: 45.30m));
            var deOtro = await servicioTarjeta.EnlaceEnvio(251301, null, USUARIO, null);
            Pagar(deOtro.Contenido.PagoTPVId);

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_TARJETA, deOtro.Contenido.PagoTPVId), USUARIO)).Tipo);
            var sinPago = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_TARJETA), USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Invalido, sinPago.Tipo);
            StringAssert.Contains(sinPago.Mensaje, "actualícela");
        }

        [TestMethod]
        public async Task Entregar_EfectivoONoCobrado_ConElEnlacePagado_400_NoSeCobraDosVeces()
        {
            EnvioConReembolso();
            var enlace = await servicioTarjeta.EnlaceEnvio(251300, null, USUARIO, null);
            Pagar(enlace.Contenido.PagoTPVId);

            var efectivo = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_EFECTIVO), USUARIO);
            var noCobrado = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO, telefonica: "Autoriza Pepe"), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Invalido, efectivo.Tipo);
            StringAssert.Contains(efectivo.Mensaje, "ya está pagado con tarjeta");
            Assert.AreEqual(TipoResultadoReparto.Invalido, noCobrado.Tipo);
            Assert.AreEqual(0, reparto.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_SinScriptDe773_ComoAntes_TarjetaSinDatafono_YNoCobradoSinAutorizacion()
        {
            EnvioConReembolso();
            tarjetas.Script = false;
            autorizacionesRepo.Script = false;

            var tarjeta = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_TARJETA, 4321), USUARIO);
            var noCobrado = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO), USUARIO);

            Assert.AreEqual(ServicioRepartoPropio.MENSAJE_SIN_DATAFONO, tarjeta.Mensaje);
            Assert.AreEqual(TipoResultadoReparto.Ok, noCobrado.Tipo, noCobrado.Mensaje);
        }

        // ================================================================ sin cobrar: autorización

        [TestMethod]
        public async Task Entregar_NoCobradoSinAutorizacion_400_TambienLaPdaViejaQueNoDiceNada_YElEnlaceAntiguo()
        {
            EnvioConReembolso();

            var noCobrado = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO), USUARIO);
            var sinDecir = await servicioReparto.Entregar(251300, Entrega(), USUARIO);
            var enlace = await servicioReparto.Entregar(251300, new EntregaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(),
                CobroReembolso = PruebaEntregaReparto.COBRO_ENLACE,
                CorreoEnlace = "a@b.es"
            }, USUARIO);

            foreach (var r in new[] { noCobrado, sinDecir, enlace })
            {
                Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
                StringAssert.Contains(r.Mensaje, "pida autorización");
                StringAssert.Contains(r.Mensaje, "actualícela");
            }
            Assert.AreEqual(0, reparto.Registros.Count);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_EfectivoNoNecesitaAutorizacion()
        {
            EnvioConReembolso();
            var resultado = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_EFECTIVO), USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(45.30m, reparto.Registros.Single().ImporteAContabilizar);
        }

        [TestMethod]
        public async Task Autorizacion_PedidaAvisaAAdministracion_Autorizada_LlegaPorSignalR_YDejaEntregarSinCobrar()
        {
            EnvioConReembolso();

            var pedida = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Paga por transferencia" }, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, pedida.Tipo, pedida.Mensaje);
            Assert.AreEqual(EstadosAutorizacionReembolso.PENDIENTE, pedida.Contenido.Estado);
            Assert.AreEqual("Repartidor", pedida.Contenido.Repartidor);
            Assert.AreEqual("AutorizacionReembolso:" + pedida.Contenido.Id, pedida.Contenido.GrupoAvisos);
            A.CallTo(() => avisador.AvisarAdministracion(A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == TextosAutorizacionReembolso.TIPO_NOTIFICACION && n.Datos["autorizacionId"] == pedida.Contenido.Id.ToString()
                && n.Cuerpo.Contains("Paga por transferencia")))).MustHaveHappenedOnceExactly();

            var otraVez = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Otra vez" }, USUARIO);
            Assert.AreEqual(pedida.Contenido.Id, otraVez.Contenido.Id, "Pendiente: la misma, sin volver a avisar");
            A.CallTo(() => avisador.AvisarAdministracion(A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO), USUARIO)).Tipo,
                "Pendiente: todavía no");

            var respondida = await servicioAutorizaciones.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true, Comentario = "Vale" },
                @"NUEVAVISION\Laura");

            Assert.AreEqual(EstadosAutorizacionReembolso.AUTORIZADA, respondida.Contenido.Estado);
            Assert.AreEqual("Laura", respondida.Contenido.Respondio);
            A.CallTo(() => avisador.Respondida(A<AutorizacionReembolsoDTO>.That.Matches(a => a.Id == pedida.Contenido.Id && a.Estado == "Autorizada")))
                .MustHaveHappenedOnceExactly();
            var entrega = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO), USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, entrega.Tipo, entrega.Mensaje);
            Assert.IsNull(reparto.Registros.Single().ImporteAContabilizar);
        }

        [TestMethod]
        public async Task Autorizacion_Denegada_NoDejaEntregarSinCobrar_PeroSiLaIncidenciaReembolsoNoCobrado()
        {
            EnviosAgencia envio = EnvioConReembolso();
            var pedida = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "No tiene dinero" }, USUARIO);
            await servicioAutorizaciones.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = false, Comentario = "Que pague" }, @"NUEVAVISION\Laura");

            var entrega = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO), USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Invalido, entrega.Tipo);
            StringAssert.Contains(entrega.Mensaje, "Laura ha denegado");
            StringAssert.Contains(entrega.Mensaje, "Reembolso no cobrado");

            var incidencia = await servicioReparto.Incidencia(251300, new IncidenciaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(),
                Motivo = IncidenciaRepartoDTO.REEMBOLSO_NO_COBRADO
            }, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, incidencia.Tipo, incidencia.Mensaje);
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado);
            StringAssert.Contains(envio.DetalleEstado, "Reembolso no cobrado");

            var segunda = await servicioAutorizaciones.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true }, @"NUEVAVISION\Carlos");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, segunda.Tipo, "Ya contestada");
        }

        [TestMethod]
        public async Task Incidencia_ReembolsoNoCobrado_SinScriptOSinReembolso_400()
        {
            reparto.Con(ServicioRepartoPropioTests.Envio(reembolso: 0m));
            var sinReembolso = await servicioReparto.Incidencia(251300, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = IncidenciaRepartoDTO.REEMBOLSO_NO_COBRADO }, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Invalido, sinReembolso.Tipo);
            autorizacionesRepo.Script = false;
            EnvioConReembolso();
            var sinScript = await servicioReparto.Incidencia(251300, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = IncidenciaRepartoDTO.REEMBOLSO_NO_COBRADO }, USUARIO);
            StringAssert.Contains(sinScript.Mensaje, "Issue773");
        }

        [TestMethod]
        public async Task Autorizacion_PorTelefono_SinPedirAntes_SeAcepta_SeApuntaTelefonica_YSeAvisaParaRevisar()
        {
            EnvioConReembolso();

            var entrega = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO, telefonica: "Autoriza Pepe por teléfono"), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, entrega.Tipo, entrega.Mensaje);
            AutorizacionReembolsoReparto fila = autorizacionesRepo.Filas.Single();
            Assert.AreEqual(EstadosAutorizacionReembolso.TELEFONICA, fila.Estado);
            Assert.AreEqual("Autoriza Pepe por teléfono", fila.Respondio);
            Assert.AreEqual(45.30m, fila.Importe);
            A.CallTo(() => avisador.AvisarAdministracionYDireccion(A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == TextosAutorizacionReembolso.TIPO_NOTIFICACION_TELEFONICA && n.Cuerpo.Contains("Autoriza Pepe")))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Autorizacion_PorTelefono_ConUnaPendiente_LaPendientePasaATelefonica_YYaNoSePuedeContestar()
        {
            EnvioConReembolso();
            var pedida = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, USUARIO);

            var entrega = await servicioReparto.Entregar(251300, Entrega(PruebaEntregaReparto.COBRO_NO_COBRADO, telefonica: "Me lo ha dicho Laura"), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, entrega.Tipo, entrega.Mensaje);
            AutorizacionReembolsoReparto fila = autorizacionesRepo.Filas.Single();
            Assert.AreEqual(pedida.Contenido.Id, fila.Id);
            Assert.AreEqual(EstadosAutorizacionReembolso.TELEFONICA, fila.Estado);
            var tarde = await servicioAutorizaciones.Responder(fila.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = false }, @"NUEVAVISION\Laura");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, tarde.Tipo);
            StringAssert.Contains(tarde.Mensaje, "por teléfono");
        }

        [TestMethod]
        public async Task Autorizacion_SinMotivo_SinReembolso_O_YaEntregado_NoSePide()
        {
            EnviosAgencia envio = EnvioConReembolso();
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO(), USUARIO)).Tipo);
            envio.Reembolso = 0m;
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "x" }, USUARIO)).Tipo);
            A.CallTo(() => avisador.AvisarAdministracion(A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ================================================================ permisos

        [TestMethod]
        public async Task Responder_SoloAdministracionODireccion()
        {
            EnvioConReembolso();
            var pedida = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, USUARIO);
            RepartoPropioCobrosController Controlador(params string[] roles) => new RepartoPropioCobrosController(null, servicioTarjeta, servicioAutorizaciones)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Alguien", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

            var almacen = await Controlador(Constantes.GruposSeguridad.ALMACEN).PostResponderAutorizacion(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true });
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(almacen));
            Assert.AreEqual(EstadosAutorizacionReembolso.PENDIENTE, autorizacionesRepo.Filas.Single().Estado);

            var direccion = await Controlador(Constantes.GruposSeguridad.DIRECCION).PostResponderAutorizacion(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true });
            Assert.IsInstanceOfType(direccion, typeof(OkNegotiatedContentResult<AutorizacionReembolsoDTO>));
            Assert.AreEqual(EstadosAutorizacionReembolso.AUTORIZADA, autorizacionesRepo.Filas.Single().Estado);
            Assert.IsTrue(RepartoPropioCobrosController.PuedeAutorizar(new GenericPrincipal(new GenericIdentity("x"), new[] { Constantes.GruposSeguridad.ADMINISTRACION })));
            Assert.IsFalse(RepartoPropioCobrosController.PuedeAutorizar(new GenericPrincipal(new GenericIdentity("x"), new[] { Constantes.GruposSeguridad.INFORMATICA })));
        }

        [TestMethod]
        public async Task EnlaceTarjeta_SinPermisoDeReparto_403()
        {
            var controlador = new RepartoPropioCobrosController(null, servicioTarjeta, servicioAutorizaciones)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Vendedor", "Bearer"), new string[0]),
                Request = new HttpRequestMessage()
            };
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controlador.PostEnlaceTarjeta(251300, null)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controlador.PostAutorizacion(251300, null)));
        }

        private static HttpStatusCode Estado(System.Web.Http.IHttpActionResult resultado)
        {
            System.Reflection.PropertyInfo propiedad = resultado.GetType().GetProperty("StatusCode");
            return propiedad == null ? HttpStatusCode.OK : (HttpStatusCode)propiedad.GetValue(resultado);
        }

        // ================================================================ falsos

        internal class TarjetasFalsas : IRepositorioCobrosTarjetaReparto
        {
            public bool Script = true;
            public readonly List<EnlaceTarjetaReparto> Enlaces = new List<EnlaceTarjetaReparto>();
            public readonly List<PagoCadenaReparto> Pagos = new List<PagoCadenaReparto>();
            public readonly List<EfectoCobroReparto> Efectos = new List<EfectoCobroReparto>();
            public readonly List<KeyValuePair<int, string>> CorreosMandados = new List<KeyValuePair<int, string>>();
            public DatosContactoCobro Contacto = new DatosContactoCobro();

            public bool ScriptAplicado() => Script;
            public Task<EnlaceTarjetaReparto> LeerUltimoDelEnvio(int envioId)
                => Task.FromResult(Enlaces.LastOrDefault(e => e.EnvioId == envioId && e.Tipo == TiposCobroTarjetaReparto.REEMBOLSO));
            public Task<EnlaceTarjetaReparto> LeerUltimoDeDeuda(string cliente, string efectos)
                => Task.FromResult(Enlaces.LastOrDefault(e => e.Tipo == TiposCobroTarjetaReparto.DEUDA && e.Cliente == cliente && e.Efectos == efectos));
            public Task<EnlaceTarjetaReparto> LeerPorPago(int pagoTPVId) => Task.FromResult(Enlaces.FirstOrDefault(e => e.PagoTPVId == pagoTPVId));
            public Task<List<PagoCadenaReparto>> LeerCadena(int pagoRaiz)
                => Task.FromResult(Pagos.Where(p => p.Id == pagoRaiz || p.PagoOriginalId == pagoRaiz).ToList());
            public Task Guardar(EnlaceTarjetaReparto enlace)
            {
                enlace.Id = Enlaces.Count + 1;
                Enlaces.Add(enlace);
                return Task.CompletedTask;
            }
            public Task<DatosContactoCobro> LeerContacto(string empresa, string cliente, string contacto) => Task.FromResult(Contacto);
            public Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids) => Task.FromResult(Efectos.Where(e => ids.Contains(e.Id)).ToList());
            public Task<bool> MandarPorCorreo(int pagoTPVId, string correo)
            {
                CorreosMandados.Add(new KeyValuePair<int, string>(pagoTPVId, correo));
                return Task.FromResult(true);
            }
            /// <summary>El efecto que elegiría el efectivo para el reembolso (null = ninguno).</summary>
            public EfectoCobroReparto EfectoDelReembolso;
            public bool FallaEfectoDelReembolso;
            public int BusquedasEfectoDelReembolso;
            public readonly List<EnvioHistoria> Historia = new List<EnvioHistoria>();
            public Task<EfectoCobroReparto> LeerEfectoDelReembolso(EnviosAgencia envio, decimal importe)
            {
                BusquedasEfectoDelReembolso++;
                if (FallaEfectoDelReembolso)
                {
                    throw new InvalidOperationException("Sin conexión");
                }
                return Task.FromResult(EfectoDelReembolso);
            }
            public Task AnotarHistoria(EnvioHistoria fila)
            {
                Historia.Add(fila);
                return Task.CompletedTask;
            }
        }

        internal class AutorizacionesFalsas : IRepositorioAutorizacionesReembolso
        {
            public bool Script = true;
            public readonly List<AutorizacionReembolsoReparto> Filas = new List<AutorizacionReembolsoReparto>();

            public bool ScriptAplicado() => Script;
            public Task<AutorizacionReembolsoReparto> Leer(int id) => Task.FromResult(Filas.FirstOrDefault(f => f.Id == id));
            public Task<AutorizacionReembolsoReparto> LeerUltimaDelEnvio(int envioId) => Task.FromResult(Filas.LastOrDefault(f => f.EnvioId == envioId));
            public Task<int> Crear(AutorizacionReembolsoReparto autorizacion)
            {
                autorizacion.Id = Filas.Count + 1;
                Filas.Add(autorizacion);
                return Task.FromResult(autorizacion.Id);
            }
            public Task<bool> Responder(int id, string estadoEsperado, string estado, string respondio, DateTime fecha, string comentario)
            {
                AutorizacionReembolsoReparto fila = Filas.FirstOrDefault(f => f.Id == id);
                if (fila == null || fila.Estado != estadoEsperado)
                {
                    return Task.FromResult(false);
                }
                fila.Estado = estado;
                fila.Respondio = respondio;
                fila.FechaRespuesta = fecha;
                fila.Comentario = comentario;
                return Task.FromResult(true);
            }
        }
    }
}
