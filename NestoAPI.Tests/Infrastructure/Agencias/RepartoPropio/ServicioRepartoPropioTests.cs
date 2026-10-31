using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Informes;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#739: las reglas de api/RepartoPropio (contrato de la issue + cambios de Carlos del 29/10/26: entrega opcional salvo
    /// la clave, reembolso contabilizado AL ENTREGAR). La caja es la del REPARTIDOR que entrega (su parámetro CajaDefecto), no la
    /// CuentaReembolsos de la agencia 14 (Carlos 29/10/26: con dos repartidores, cada uno en su caja). Sin datáfono: Efectivo,
    /// Enlace (enlace de pago al correo, sin contabilizar) o NoCobrado.
    /// </summary>
    [TestClass]
    public class ServicioRepartoPropioTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 29, 12, 30, 0);
        private const string USUARIO = @"NUEVAVISION\Repartidor";
        /// <summary>La CuentaReembolsos de la agencia 14: NO se usa (la caja es del repartidor).</summary>
        private const string CUENTA_AGENCIA = "57000014";
        private const string CAJA_REPARTIDOR = "57000001";
        private const string URL_ENLACE = "https://api.nuevavision.es/pago/abc";

        private RepositorioFalso repositorio;
        private ParametrosFalsos parametros;
        private IServicioPagos pagos;
        private IInformesService informes;
        private List<EnviosAgencia> avisados;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new RepositorioFalso();
            parametros = new ParametrosFalsos();
            parametros.Caja("Repartidor", CAJA_REPARTIDOR);
            pagos = A.Fake<IServicioPagos>();
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._))
                .Returns(new RespuestaIniciarPago { IdPago = 4321, UrlPaginaPago = URL_ENLACE });
            informes = A.Fake<IInformesService>();
            avisados = new List<EnviosAgencia>();
            servicio = new ServicioRepartoPropio(repositorio, () => AHORA, e => { avisados.Add(e); return Task.CompletedTask; }, parametros,
                () => pagos, () => informes);
        }

        internal static EnviosAgencia Envio(int numero = 251300, short estado = Constantes.Agencias.ESTADO_TRAMITADO, decimal reembolso = 0m,
            string cuenta = CUENTA_AGENCIA, int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, short bultos = 3) => new EnviosAgencia
            {
                Numero = numero,
                Empresa = "1  ",
                Agencia = agencia,
                Cliente = "15191     ",
                Contacto = "0  ",
                Pedido = 928000,
                Estado = estado,
                Fecha = AHORA.Date,
                Bultos = bultos,
                Nombre = "PELUQUERÍA LA BONITA   ",
                Direccion = "Calle Mayor, 1   ",
                CodPostal = "28110",
                Poblacion = "ALGETE   ",
                Telefono = "916281914",
                Movil = "600123456",
                Observaciones = "Por la mañana",
                Reembolso = reembolso,
                CodigoBarras = "RP" + numero,
                AgenciasTransporte = new AgenciaTransporte { Numero = agencia, Nombre = "Reparto propio", CuentaReembolsos = cuenta }
            };

        private static EntregaRepartoDTO Entrega(Guid? clave = null) => new EntregaRepartoDTO { ClaveIdempotencia = clave ?? Guid.NewGuid() };

        private static IncidenciaRepartoDTO Incidencia(string motivo = IncidenciaRepartoDTO.AUSENTE, string texto = null, Guid? clave = null)
            => new IncidenciaRepartoDTO { ClaveIdempotencia = clave ?? Guid.NewGuid(), Motivo = motivo, Texto = texto };

        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        // ================================================================ sin script

        [TestMethod]
        public async Task SinScript_TodosLosEndpointsDan400FaltaElScript_YNoSeTocaNada()
        {
            repositorio.Script = false;
            EnviosAgencia envio = repositorio.Con(Envio());

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Envios(null)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Bulto("RP251300-1")).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.PruebaEntrega(251300)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Firma(251300)).Tipo);
            var entregar = await servicio.Entregar(251300, Entrega(), USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Invalido, entregar.Tipo);
            StringAssert.Contains(entregar.Mensaje, "Issue739_RepartoPropio.sql");
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Incidencia(251300, Incidencia(), USUARIO)).Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        // ================================================================ entregar: validaciones 400

        [TestMethod]
        public async Task Entregar_SinCuerpoOSinClave_400()
        {
            repositorio.Con(Envio());
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Entregar(251300, null, USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Entregar(251300, new EntregaRepartoDTO(), USUARIO)).Tipo);
        }

        [TestMethod]
        public async Task Entregar_SoloConLaClave_Entregado()
        {
            // Carlos 29/10/26: registrar la entrega es opcional; ni receptor ni firma ni DNI son obligatorios por ahora.
            EnviosAgencia envio = repositorio.Con(Envio());

            var resultado = await servicio.Entregar(251300, Entrega(), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(251300, resultado.Contenido.EnvioId);
            Assert.AreEqual("Entregado", resultado.Contenido.Estado);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            Assert.AreEqual(AHORA, envio.FechaEntrega, "Sin FechaHora, ahora");
            Assert.AreEqual(ServicioRepartoPropio.DETALLE_ENTREGADO, envio.DetalleEstado);
            Assert.AreEqual(1, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_CobroDesconocido_ImporteNegativo_FirmaQueNoEsPng_TextosLargos_400()
        {
            repositorio.Con(Envio());
            async Task<TipoResultadoReparto> Con(Action<EntregaRepartoDTO> cambio)
            {
                EntregaRepartoDTO p = Entrega();
                cambio(p);
                return (await servicio.Entregar(251300, p, USUARIO)).Tipo;
            }

            Assert.AreEqual(TipoResultadoReparto.Invalido, await Con(p => p.CobroReembolso = "Bizum"));
            Assert.AreEqual(TipoResultadoReparto.Invalido, await Con(p => p.ImporteCobrado = -1m));
            Assert.AreEqual(TipoResultadoReparto.Invalido, await Con(p => p.FirmaPng = "esto no es base64!"));
            Assert.AreEqual(TipoResultadoReparto.Invalido, await Con(p => p.FirmaPng = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5 })));
            Assert.AreEqual(TipoResultadoReparto.Invalido, await Con(p => p.Receptor = new string('x', 101)));
            Assert.AreEqual(TipoResultadoReparto.Invalido, await Con(p => p.DniReceptor = new string('1', 21)));
            Assert.AreEqual(0, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_ConFirmaYDni_SeGuardanEnLaPrueba_PeroNuncaEnLaHistoriaNiEnElEstado()
        {
            EnviosAgencia envio = repositorio.Con(Envio());
            EntregaRepartoDTO p = Entrega();
            p.Receptor = " María Pérez ";
            p.DniReceptor = "12345678Z";
            p.FirmaPng = "data:image/png;base64," + Convert.ToBase64String(Png);
            p.FechaHora = new DateTime(2026, 10, 29, 10, 15, 0);
            p.Latitud = 40.5970m;
            p.Longitud = -3.4970m;

            var resultado = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Registro r = repositorio.Registros.Single();
            Assert.AreEqual("María Pérez", r.Prueba.Receptor);
            Assert.AreEqual("12345678Z", r.Prueba.DniReceptor);
            CollectionAssert.AreEqual(Png, r.Firma);
            Assert.IsTrue(r.Prueba.TieneFirma);
            Assert.AreEqual(PruebaEntregaReparto.TIPO_ENTREGA, r.Prueba.Tipo);
            Assert.AreEqual(USUARIO, r.Prueba.Usuario);
            Assert.AreEqual(40.5970m, r.Prueba.Latitud);
            Assert.AreEqual(new DateTime(2026, 10, 29, 10, 15, 0), envio.FechaEntrega, "La hora de la PDA, no la de llegada");
            Assert.AreEqual(2, r.Historia.Count);
            foreach (EnvioHistoria h in r.Historia)
            {
                Assert.IsFalse((h.Observaciones + h.ValorAnterior).Contains("12345678Z"), "El DNI nunca va a la historia");
                Assert.AreEqual(USUARIO, h.Usuario);
            }
            Assert.AreEqual("1", r.Historia.Single(h => h.Campo == "Estado").ValorAnterior);
            Assert.IsFalse((envio.DetalleEstado ?? string.Empty).Contains("12345678Z"));
        }

        // ================================================================ entregar: 404 y 409

        [TestMethod]
        public async Task Entregar_EnvioQueNoExisteODeOtraAgencia_404()
        {
            repositorio.Con(Envio(numero: 1, agencia: Constantes.Agencias.AGENCIA_INNOVATRANS));
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Entregar(999, Entrega(), USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Entregar(1, Entrega(), USUARIO)).Tipo);
        }

        [TestMethod]
        public async Task Entregar_MismaClave_MismaRespuestaSinRegistrarDosVeces()
        {
            repositorio.Con(Envio(reembolso: 50m));
            Guid clave = Guid.NewGuid();
            EntregaRepartoDTO p = Entrega(clave);
            p.CobroReembolso = "Efectivo";

            var primera = await servicio.Entregar(251300, p, USUARIO);
            var segunda = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, primera.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Ok, segunda.Tipo, "Reintento de la cola de Ariadna: 200, no 409");
            Assert.AreEqual("Entregado", segunda.Contenido.Estado);
            Assert.AreEqual(1, repositorio.Registros.Count, "Ni otra prueba ni otro asiento");
            Assert.AreEqual(1, repositorio.Registros.Count(r => r.ImporteAContabilizar.HasValue));
        }

        [TestMethod]
        public async Task Entregar_YaEntregadoConOtraClave_409()
        {
            repositorio.Con(Envio());
            _ = await servicio.Entregar(251300, Entrega(), USUARIO);

            var otra = await servicio.Entregar(251300, Entrega(), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, otra.Tipo);
            StringAssert.Contains(otra.Mensaje, "ya está entregado");
            Assert.AreEqual(1, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_ClaveDeOtroEnvioODeUnaIncidencia_409()
        {
            repositorio.Con(Envio(numero: 1));
            repositorio.Con(Envio(numero: 2));
            Guid clave = Guid.NewGuid();
            _ = await servicio.Incidencia(1, Incidencia(clave: clave), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Entregar(2, Entrega(clave), USUARIO)).Tipo, "Otro envío");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Entregar(1, Entrega(clave), USUARIO)).Tipo, "La clave era de una incidencia");
        }

        [TestMethod]
        public async Task Entregar_EnvioSinTramitarODevuelto_409()
        {
            repositorio.Con(Envio(numero: 1, estado: Constantes.Agencias.ESTADO_EN_CURSO));
            repositorio.Con(Envio(numero: 2, estado: Constantes.Agencias.ESTADO_DEVUELTO));
            repositorio.Con(Envio(numero: 3, estado: (short)Constantes.Agencias.ESTADO_PENDIENTE));

            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Entregar(1, Entrega(), USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Entregar(2, Entrega(), USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Entregar(3, Entrega(), USUARIO)).Tipo);
        }

        [TestMethod]
        public async Task Entregar_DespuesDeUnaIncidencia_SePuedeEntregar()
        {
            EnviosAgencia envio = repositorio.Con(Envio());
            _ = await servicio.Incidencia(251300, Incidencia(), USUARIO);

            var resultado = await servicio.Entregar(251300, Entrega(), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
        }

        [TestMethod]
        public async Task Entregar_DosPeticionesALaVezConLaMismaClave_LaPerdedoraDevuelveLoMismo()
        {
            repositorio.Con(Envio());
            Guid clave = Guid.NewGuid();
            repositorio.SimularCarreraCon = new PruebaEntregaReparto { EnvioId = 251300, ClaveIdempotencia = clave, Tipo = PruebaEntregaReparto.TIPO_ENTREGA };

            var resultado = await servicio.Entregar(251300, Entrega(clave), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.AreEqual("Entregado", resultado.Contenido.Estado);
        }

        // ================================================================ entregar: reembolso (se contabiliza AL ENTREGAR)

        [TestMethod]
        public async Task Entregar_ConReembolsoSinDecirElCobro_NoCobrado_YNoSeContabiliza()
        {
            repositorio.Con(Envio(reembolso: 85.40m));

            var resultado = await servicio.Entregar(251300, Entrega(), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, "Carlos 29/10/26: no es 400");
            Registro r = repositorio.Registros.Single();
            Assert.AreEqual(PruebaEntregaReparto.COBRO_NO_COBRADO, r.Prueba.CobroReembolso);
            Assert.AreEqual(0m, r.Prueba.ImporteCobrado);
            Assert.IsNull(r.ImporteAContabilizar);
        }

        [TestMethod]
        public async Task Entregar_CobradoEnEfectivoSinImporte_ContabilizaElReembolsoDelEnvio()
        {
            repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "efectivo";

            var resultado = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Registro r = repositorio.Registros.Single();
            Assert.AreEqual(PruebaEntregaReparto.COBRO_EFECTIVO, r.Prueba.CobroReembolso, "Normalizado");
            Assert.AreEqual(85.40m, r.Prueba.ImporteCobrado);
            Assert.AreEqual(85.40m, r.ImporteAContabilizar, "Al entregar, en la misma transacción que el cambio de estado");
            Assert.AreEqual(USUARIO, r.Usuario);
            Assert.AreEqual(7001, r.Prueba.Asiento);
            Assert.AreEqual(CAJA_REPARTIDOR, r.Prueba.CuentaCaja, "La caja del repartidor, no la CuentaReembolsos de la agencia");
            Assert.IsNull(resultado.Contenido.UrlPago);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_CobradoEnEfectivoOtroImporte_ContabilizaLoCobrado()
        {
            repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Efectivo";
            p.ImporteCobrado = 80m;

            _ = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(80m, repositorio.Registros.Single().ImporteAContabilizar);
        }

        [TestMethod]
        public async Task Entregar_EfectivoConCajaDelUsuario_SeContabilizaEnSuCaja_AunqueLaAgenciaNoTengaCuenta()
        {
            repositorio.Con(Envio(reembolso: 85.40m, cuenta: null));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Efectivo";

            var resultado = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Registro r = repositorio.Registros.Single();
            Assert.AreEqual(85.40m, r.ImporteAContabilizar);
            Assert.AreEqual(CAJA_REPARTIDOR, r.Prueba.CuentaCaja);
            Assert.AreEqual("1", parametros.UltimaEmpresa, "Empresa 1");
            Assert.AreEqual("Repartidor", parametros.UltimoUsuario, "El usuario sin dominio");
            Assert.AreEqual("CajaDefecto", ServicioRepartoPropio.PARAMETRO_CAJA, "El mismo parámetro que la ventana de Cajas de Nesto");
            Assert.AreEqual(ServicioRepartoPropio.PARAMETRO_CAJA, parametros.UltimoParametro);
        }

        [TestMethod]
        public async Task Entregar_DosRepartidores_CadaUnoCobraEnSuCaja()
        {
            parametros.Caja("Andre", "57000002");
            parametros.Caja("Miguel", "57000003");
            repositorio.Con(Envio(numero: 251301, reembolso: 50m));
            repositorio.Con(Envio(numero: 251302, reembolso: 70m));
            EntregaRepartoDTO p1 = Entrega();
            p1.CobroReembolso = "Efectivo";
            EntregaRepartoDTO p2 = Entrega();
            p2.CobroReembolso = "Efectivo";

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251301, p1, @"NUEVAVISION\Andre")).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251302, p2, @"NUEVAVISION\Miguel")).Tipo);

            Assert.AreEqual("57000002", repositorio.Registros.Single(r => r.Prueba.EnvioId == 251301).Prueba.CuentaCaja);
            Assert.AreEqual("57000003", repositorio.Registros.Single(r => r.Prueba.EnvioId == 251302).Prueba.CuentaCaja);
        }

        [TestMethod]
        public async Task Entregar_EfectivoSinCajaDelUsuario_400DeUstedYNadaRegistrado_AunqueLaAgenciaTengaCuenta()
        {
            EnviosAgencia envio = repositorio.Con(Envio(reembolso: 85.40m, cuenta: CUENTA_AGENCIA));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Efectivo";

            var resultado = await servicio.Entregar(251300, p, @"NUEVAVISION\SinCaja");

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.MENSAJE_SIN_CAJA, resultado.Mensaje);
            StringAssert.Contains(resultado.Mensaje, "No tiene caja asignada (parámetro CajaDefecto)");
            Assert.AreEqual(0, repositorio.Registros.Count);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado, "Sin registro no cambia el estado");
            Assert.IsNull(envio.FechaEntrega);
        }

        [TestMethod]
        public async Task Entregar_EfectivoConCajaVaciaEnSuFila_400()
        {
            parametros.Caja("Vacio", "   ");
            repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Efectivo";

            var resultado = await servicio.Entregar(251300, p, @"NUEVAVISION\Vacio");

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_NoCobradoSinCajaDelUsuario_SeEntrega_YNiSiquieraSeMiraLaCaja()
        {
            repositorio.Con(Envio(reembolso: 85.40m, cuenta: null));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "NoCobrado";

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, p, @"NUEVAVISION\SinCaja")).Tipo);
            Registro r = repositorio.Registros.Single();
            Assert.IsNull(r.ImporteAContabilizar);
            Assert.IsNull(r.Prueba.CuentaCaja);
            Assert.AreEqual(0, parametros.Lecturas);
        }

        // ================================================================ entregar: sin datáfono y cobro por enlace

        [TestMethod]
        public async Task Entregar_Tarjeta_400SinDatafono_YNadaRegistrado()
        {
            EnviosAgencia envio = repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "tarjeta";

            var resultado = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual("Sin datáfono: use Efectivo o Enlace.", resultado.Mensaje);
            Assert.AreEqual(0, repositorio.Registros.Count);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        [TestMethod]
        public async Task Entregar_EnlaceSinCorreoOConCorreoMal_400_YNoSeCreaEnlace()
        {
            repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO sinCorreo = Entrega();
            sinCorreo.CobroReembolso = "Enlace";
            EntregaRepartoDTO correoMal = Entrega();
            correoMal.CobroReembolso = "Enlace";
            correoMal.CorreoEnlace = "no-es-un-correo";

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Entregar(251300, sinCorreo, USUARIO)).Tipo);
            var mal = await servicio.Entregar(251300, correoMal, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Invalido, mal.Tipo);
            StringAssert.Contains(mal.Mensaje, "no es válido");
            Assert.AreEqual(0, repositorio.Registros.Count);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_Enlace_CreaElEnlaceDelPedidoPorElReembolso_NoContabiliza_YDevuelveLaUrl()
        {
            EnviosAgencia envio = repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "enlace";
            p.CorreoEnlace = " cliente@ejemplo.com ";

            // Sin caja: con enlace no hace falta.
            var resultado = await servicio.Entregar(251300, p, @"NUEVAVISION\SinCaja");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(URL_ENLACE, resultado.Contenido.UrlPago);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>.That.Matches(s =>
                    s.Importe == 85.40m
                    && s.Descripcion == "Pago pedido 928000 de Nueva Visión"
                    && s.Correo == "cliente@ejemplo.com"
                    && s.Cliente == "15191"
                    && s.Contacto == "0"
                    && s.Empresa == "1"
                    && s.Pedido == null), @"NUEVAVISION\SinCaja"))
                .MustHaveHappenedOnceExactly();
            Registro r = repositorio.Registros.Single();
            Assert.IsNull(r.ImporteAContabilizar, "El cobro entra por la notificación de Redsys");
            Assert.IsNull(r.Prueba.CuentaCaja);
            Assert.AreEqual(PruebaEntregaReparto.COBRO_ENLACE, r.Prueba.CobroReembolso);
            Assert.AreEqual(85.40m, r.Prueba.ImporteCobrado, "El importe del enlace");
            Assert.AreEqual(4321, r.Prueba.PagoTPVId);
            Assert.AreEqual(URL_ENLACE, r.Prueba.UrlPago);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            Assert.AreEqual(0, parametros.Lecturas);
        }

        [TestMethod]
        public async Task Entregar_EnlaceConOtroImporte_ElEnlaceVaPorEseImporte()
        {
            repositorio.Con(Envio(reembolso: 85.40m));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Enlace";
            p.CorreoEnlace = "cliente@ejemplo.com";
            p.ImporteCobrado = 60m;

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, p, USUARIO)).Tipo);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>.That.Matches(s => s.Importe == 60m), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Entregar_EnlaceSinImporte_400()
        {
            repositorio.Con(Envio());
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Enlace";
            p.CorreoEnlace = "cliente@ejemplo.com";

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Entregar(251300, p, USUARIO)).Tipo);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_EnlaceMismaClave_NoCreaDosEnlaces_YDevuelveLaMismaUrl()
        {
            repositorio.Con(Envio(reembolso: 85.40m));
            Guid clave = Guid.NewGuid();
            EntregaRepartoDTO p = Entrega(clave);
            p.CobroReembolso = "Enlace";
            p.CorreoEnlace = "cliente@ejemplo.com";

            var primera = await servicio.Entregar(251300, p, USUARIO);
            var segunda = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, segunda.Tipo);
            Assert.AreEqual(URL_ENLACE, primera.Contenido.UrlPago);
            Assert.AreEqual(URL_ENLACE, segunda.Contenido.UrlPago);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_FallaElEnlace_400YElEnvioSigueComoEstaba()
        {
            EnviosAgencia envio = repositorio.Con(Envio(reembolso: 85.40m));
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).Throws(new ArgumentException("El importe debe ser mayor que cero"));
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Enlace";
            p.CorreoEnlace = "cliente@ejemplo.com";

            var resultado = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        [TestMethod]
        public void ConceptoEnlace_ElPedidoQueReconocenLosEnlacesDelPedido()
        {
            EnviosAgencia envio = Envio();
            Assert.AreEqual(928000, NestoAPI.Infraestructure.Pagos.EnlacesPagoPedido.PedidoDelConcepto(ServicioRepartoPropio.ConceptoEnlace(envio)));
            envio.Pedido = null;
            Assert.AreEqual("Pago envío 251300 de Nueva Visión", ServicioRepartoPropio.ConceptoEnlace(envio));
        }

        [TestMethod]
        public async Task EnviosYBulto_LlevanElCorreoDelCliente_ONullSiNoHayONoEsValido()
        {
            EnviosAgencia conCorreo = repositorio.Con(Envio(numero: 251301));
            conCorreo.Email = " cliente@ejemplo.com  ";
            EnviosAgencia sinCorreo = repositorio.Con(Envio(numero: 251302));
            sinCorreo.Email = null;
            EnviosAgencia correoMal = repositorio.Con(Envio(numero: 251303));
            correoMal.Email = "no es correo";

            List<EnvioRepartoDTO> envios = (await servicio.Envios(null)).Contenido;

            Assert.AreEqual("cliente@ejemplo.com", envios.Single(e => e.EnvioId == 251301).Correo);
            Assert.IsNull(envios.Single(e => e.EnvioId == 251302).Correo);
            Assert.IsNull(envios.Single(e => e.EnvioId == 251303).Correo);
            Assert.AreEqual("cliente@ejemplo.com", (await servicio.Bulto("RP251301-1")).Contenido.Correo);
        }

        // ================================================================ mis cobros

        [TestMethod]
        public async Task MisCobros_ElExtractoDelDiaDeSuCaja()
        {
            DateTime dia = new DateTime(2026, 10, 28);
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("1", CAJA_REPARTIDOR, dia)).Returns(100m);
            A.CallTo(() => informes.LeerExtractoContableAsync("1", CAJA_REPARTIDOR, dia, dia)).Returns(new List<ExtractoContableDTO>
            {
                new ExtractoContableDTO { Id = 2, Fecha = dia, Concepto = "Arqueo  ", Debe = 0m, Haber = 50m, Asiento = 902, Documento = "  " },
                new ExtractoContableDTO { Id = 1, Fecha = dia, Concepto = "Reembolso envío 251300  ", Debe = 85.40m, Haber = 0m, Asiento = 901, Documento = "928000  " }
            });

            var resultado = await servicio.MisCobros(dia.AddHours(15), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            MisCobrosRepartoDTO c = resultado.Contenido;
            Assert.AreEqual(CAJA_REPARTIDOR, c.Cuenta);
            Assert.AreEqual(dia, c.Fecha);
            Assert.AreEqual(100m, c.SaldoAnterior);
            Assert.AreEqual(2, c.Apuntes.Count);
            Assert.AreEqual(901, c.Apuntes[0].Asiento, "En orden");
            Assert.AreEqual(85.40m, c.Apuntes[0].Importe, "Debe de la caja = entra");
            Assert.AreEqual("Reembolso envío 251300", c.Apuntes[0].Concepto);
            Assert.AreEqual("928000", c.Apuntes[0].Documento);
            Assert.AreEqual(-50m, c.Apuntes[1].Importe, "Haber de la caja = sale");
            Assert.AreEqual(35.40m, c.TotalDia);
            Assert.AreEqual(135.40m, c.SaldoFinal);
        }

        [TestMethod]
        public async Task MisCobros_SinFecha_Hoy_YSinApuntes_SaldoIgual()
        {
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).Returns(20m);
            A.CallTo(() => informes.LeerExtractoContableAsync(A<string>._, A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new List<ExtractoContableDTO>());

            var resultado = await servicio.MisCobros(null, USUARIO);

            Assert.AreEqual(AHORA.Date, resultado.Contenido.Fecha);
            Assert.AreEqual(0, resultado.Contenido.Apuntes.Count);
            Assert.AreEqual(0m, resultado.Contenido.TotalDia);
            Assert.AreEqual(20m, resultado.Contenido.SaldoFinal);
            A.CallTo(() => informes.LeerExtractoContableAsync("1", CAJA_REPARTIDOR, AHORA.Date, AHORA.Date)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task MisCobros_SinCaja_400ElMismoMensaje()
        {
            var resultado = await servicio.MisCobros(null, @"NUEVAVISION\SinCaja");

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.MENSAJE_SIN_CAJA, resultado.Mensaje);
            A.CallTo(() => informes.LeerExtractoContableAsync(A<string>._, A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MisCobros_DosRepartidores_CadaUnoSuCaja()
        {
            parametros.Caja("Andre", "57000002");
            parametros.Caja("Miguel", "57000003");
            A.CallTo(() => informes.LeerExtractoContableAsync(A<string>._, A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new List<ExtractoContableDTO>());

            Assert.AreEqual("57000002", (await servicio.MisCobros(null, @"NUEVAVISION\Andre")).Contenido.Cuenta);
            Assert.AreEqual("57000003", (await servicio.MisCobros(null, @"NUEVAVISION\Miguel")).Contenido.Cuenta);
        }

        [TestMethod]
        public async Task Entregar_FallaLaContabilizacion_400YNadaRegistrado()
        {
            repositorio.Con(Envio(reembolso: 85.40m));
            repositorio.FalloContabilizar = new NestoBusinessException("No se ha podido contabilizar.");
            EntregaRepartoDTO p = Entrega();
            p.CobroReembolso = "Efectivo";

            var resultado = await servicio.Entregar(251300, p, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count, "O todo o nada");
        }

        // ================================================================ incidencia

        [TestMethod]
        public async Task Incidencia_MotivoDesconocidoUOtroSinTexto_400()
        {
            repositorio.Con(Envio());
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Incidencia(251300, Incidencia("Lluvia"), USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Incidencia(251300, Incidencia(null), USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Incidencia(251300, Incidencia("Otro", "  "), USUARIO)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Incidencia(251300, new IncidenciaRepartoDTO { Motivo = "Ausente" }, USUARIO)).Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Incidencia_PasaAIncidentadoConElMotivo_YAvisaComoLasAgencias()
        {
            EnviosAgencia envio = repositorio.Con(Envio());

            var resultado = await servicio.Incidencia(251300, Incidencia("faltanbultos", "Falta la caja 2"), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual("Incidentado", resultado.Contenido.Estado);
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado);
            Assert.AreEqual("Reparto propio: Faltan bultos. Falta la caja 2", envio.DetalleEstado);
            Registro r = repositorio.Registros.Single();
            Assert.AreEqual("FaltanBultos", r.Prueba.Motivo, "Normalizado al valor del contrato (CHECK de la tabla)");
            Assert.AreEqual(PruebaEntregaReparto.TIPO_INCIDENCIA, r.Prueba.Tipo);
            Assert.IsNull(r.ImporteAContabilizar);
            Assert.AreEqual(1, avisados.Count, "#666: aviso a los comerciales");
        }

        [TestMethod]
        public async Task Incidencia_SegundaIncidencia_NoVuelveAAvisar_YMismaClaveEsIdempotente()
        {
            repositorio.Con(Envio());
            Guid clave = Guid.NewGuid();
            _ = await servicio.Incidencia(251300, Incidencia(clave: clave), USUARIO);
            var repetida = await servicio.Incidencia(251300, Incidencia(clave: clave), USUARIO);
            var otra = await servicio.Incidencia(251300, Incidencia(IncidenciaRepartoDTO.OTRO, "Cerrado por vacaciones"), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, repetida.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Ok, otra.Tipo, "Un segundo intento fallido es otra incidencia");
            Assert.AreEqual(2, repositorio.Registros.Count);
            Assert.AreEqual(1, avisados.Count, "Ya estaba incidentado: no se avisa otra vez");
        }

        [TestMethod]
        public async Task Incidencia_EnvioEntregado_409()
        {
            repositorio.Con(Envio(estado: Constantes.Agencias.ESTADO_ENTREGADO));
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Incidencia(251300, Incidencia(), USUARIO)).Tipo);
        }

        // ================================================================ lecturas

        [TestMethod]
        public async Task Envios_LosDeLaAgenciaEnReparto_HastaLaFechaYTreintaDiasAtras()
        {
            repositorio.Con(Envio(numero: 1, reembolso: -1m));
            repositorio.Con(Envio(numero: 2, estado: Constantes.Agencias.ESTADO_INCIDENTADO, reembolso: 40m));

            var resultado = await servicio.Envios(new DateTime(2026, 10, 29));

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.AreEqual(new DateTime(2026, 9, 29), repositorio.Desde);
            Assert.AreEqual(new DateTime(2026, 10, 30), repositorio.Hasta, "Hasta el final del día pedido");
            Assert.AreEqual(2, resultado.Contenido.Count);
            EnvioRepartoDTO primero = resultado.Contenido[0];
            Assert.AreEqual(1, primero.EnvioId);
            Assert.AreEqual("Tramitado", primero.Estado);
            Assert.AreEqual(0m, primero.Reembolso, "El centinela -1 no es un reembolso");
            Assert.AreEqual("PELUQUERÍA LA BONITA", primero.Nombre);
            Assert.AreEqual("28110", primero.CodigoPostal);
            Assert.AreEqual("1", primero.Empresa);
            Assert.AreEqual(3, primero.Bultos);
            Assert.AreEqual("Incidentado", resultado.Contenido[1].Estado);
            Assert.AreEqual(40m, resultado.Contenido[1].Reembolso);
        }

        [TestMethod]
        public async Task Envios_SinFecha_Hoy()
        {
            _ = await servicio.Envios(null);
            Assert.AreEqual(AHORA.Date.AddDays(1), repositorio.Hasta);
        }

        [TestMethod]
        public async Task Bulto_ResuelveElCodigo()
        {
            repositorio.Con(Envio(numero: 251300, bultos: 3));

            var resultado = await servicio.Bulto(" rp251300-2 ");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(251300, resultado.Contenido.EnvioId);
            Assert.AreEqual(2, resultado.Contenido.Bulto);
            Assert.AreEqual("Tramitado", resultado.Contenido.Estado);
        }

        [TestMethod]
        public async Task Bulto_CodigoMalOEnvioDeOtraAgenciaOBultoQueNoExiste_404DeUsted()
        {
            repositorio.Con(Envio(numero: 251300, bultos: 3));
            repositorio.Con(Envio(numero: 5, agencia: Constantes.Agencias.AGENCIA_CTT));

            foreach (string codigo in new[] { "251300-1", "RP251300", "RPX-1", "RP251300-0", "RP251300-4", "RP5-1", "RP9999-1", "" })
            {
                var resultado = await servicio.Bulto(codigo);
                Assert.AreEqual(TipoResultadoReparto.NoEncontrado, resultado.Tipo, codigo);
                Assert.IsFalse(string.IsNullOrWhiteSpace(resultado.Mensaje), codigo);
            }
        }

        [TestMethod]
        public async Task Bulto_EntregadoSeVe_SinTramitar409()
        {
            repositorio.Con(Envio(numero: 1, estado: Constantes.Agencias.ESTADO_ENTREGADO));
            repositorio.Con(Envio(numero: 2, estado: Constantes.Agencias.ESTADO_EN_CURSO));

            Assert.AreEqual("Entregado", (await servicio.Bulto("RP1-1")).Contenido.Estado);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.Bulto("RP2-1")).Tipo);
        }

        [TestMethod]
        public async Task PruebaEntrega_SinEntrega204_ConEntregaLaUltima()
        {
            repositorio.Con(Envio());
            Assert.AreEqual(TipoResultadoReparto.SinContenido, (await servicio.PruebaEntrega(251300)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.SinContenido, (await servicio.Firma(251300)).Tipo);

            EntregaRepartoDTO p = Entrega();
            p.Receptor = "María";
            p.DniReceptor = "12345678Z";
            p.FirmaPng = Convert.ToBase64String(Png);
            _ = await servicio.Entregar(251300, p, USUARIO);

            var prueba = await servicio.PruebaEntrega(251300);
            Assert.AreEqual(TipoResultadoReparto.Ok, prueba.Tipo);
            Assert.AreEqual("María", prueba.Contenido.Receptor);
            Assert.AreEqual("12345678Z", prueba.Contenido.DniReceptor);
            Assert.IsTrue(prueba.Contenido.TieneFirma);
            Assert.AreEqual(USUARIO, prueba.Contenido.Usuario);
            CollectionAssert.AreEqual(Png, (await servicio.Firma(251300)).Contenido);
        }

        // ================================================================ doble en memoria

        /// <summary>Solo las filas PROPIAS del usuario (LeerParametroSinDefecto); LeerParametro (con «(defecto)») no se debe usar.</summary>
        internal class ParametrosFalsos : NestoAPI.Infraestructure.ILectorParametrosUsuario
        {
            private readonly Dictionary<string, string> cajas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int Lecturas;
            public string UltimaEmpresa, UltimoUsuario, UltimoParametro;

            public void Caja(string usuario, string cuenta) => cajas[usuario] = cuenta;

            public string LeerParametro(string empresa, string usuario, string parametro)
                => throw new AssertFailedException("La caja no puede salir de «(defecto)»: hay que usar LeerParametroSinDefecto.");

            public string LeerParametroSinDefecto(string empresa, string usuario, string parametro)
            {
                Lecturas++;
                UltimaEmpresa = empresa;
                UltimoUsuario = usuario;
                UltimoParametro = parametro;
                return parametro == "CajaDefecto" && cajas.TryGetValue(usuario, out string cuenta) ? cuenta : null;
            }
        }

        internal class Registro
        {
            public PruebaEntregaReparto Prueba;
            public byte[] Firma;
            public IList<EnvioHistoria> Historia;
            public decimal? ImporteAContabilizar;
            public string Usuario;
            /// <summary>NestoAPI#770: la firma ya está en el blob.</summary>
            public string RutaBlob;
        }

        internal class RepositorioFalso : IRepositorioRepartoPropio
        {
            public bool Script = true;
            public readonly Dictionary<int, EnviosAgencia> Envios = new Dictionary<int, EnviosAgencia>();
            public readonly List<Registro> Registros = new List<Registro>();
            public DateTime Desde, Hasta;
            public PruebaEntregaReparto SimularCarreraCon;
            public Exception FalloContabilizar;

            public EnviosAgencia Con(EnviosAgencia envio)
            {
                Envios[envio.Numero] = envio;
                return envio;
            }

            public bool ScriptAplicado() => Script;

            public Task<List<EnviosAgencia>> LeerEnviosEnReparto(DateTime desde, DateTime hasta)
            {
                Desde = desde;
                Hasta = hasta;
                return Task.FromResult(Envios.Values
                    .Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                        && (e.Estado == Constantes.Agencias.ESTADO_TRAMITADO || e.Estado == Constantes.Agencias.ESTADO_INCIDENTADO)
                        && e.Fecha >= desde && e.Fecha < hasta)
                    .ToList());
            }

            public Task<EnviosAgencia> LeerEnvio(int numero) => Task.FromResult(Envios.TryGetValue(numero, out EnviosAgencia e) ? e : null);

            public Task<PruebaEntregaReparto> LeerPorClave(Guid clave)
                => Task.FromResult(Registros.Select(r => r.Prueba).FirstOrDefault(p => p.ClaveIdempotencia == clave));

            public Task<PruebaEntregaReparto> LeerUltimaEntrega(int envio)
                => Task.FromResult(Registros.Select(r => r.Prueba).LastOrDefault(p => p.EnvioId == envio && p.Tipo == PruebaEntregaReparto.TIPO_ENTREGA));

            /// <summary>NestoAPI#770: sin la columna RutaBlob (script sin lanzar), false.</summary>
            public bool ColumnaRutaBlob = true;
            public Exception FalloGuardarRutaBlob;
            private int ultimoId;

            public Task<FirmaGuardadaReparto> LeerFirma(int envio)
            {
                Registro r = Registros.LastOrDefault(x => x.Prueba.EnvioId == envio && x.Prueba.Tipo == PruebaEntregaReparto.TIPO_ENTREGA && (x.Firma != null || x.RutaBlob != null));
                return Task.FromResult(r == null ? null : AFirma(r));
            }

            private static FirmaGuardadaReparto AFirma(Registro r) => new FirmaGuardadaReparto
            {
                Id = r.Prueba.Id,
                EnvioId = r.Prueba.EnvioId,
                Empresa = "1  ",
                FechaHora = r.Prueba.FechaHora,
                RutaBlob = r.RutaBlob,
                Firma = r.Firma,
                Bytes = r.Firma?.Length ?? 0
            };

            public bool TieneRutaBlob() => ColumnaRutaBlob;

            public Task GuardarRutaBlob(int idPrueba, string rutaBlob)
            {
                if (FalloGuardarRutaBlob != null)
                {
                    throw FalloGuardarRutaBlob;
                }
                Registro r = Registros.Single(x => x.Prueba.Id == idPrueba);
                r.RutaBlob = rutaBlob;
                r.Firma = null;
                return Task.CompletedTask;
            }

            public Task<int> ContarFirmasEnColumna() => Task.FromResult(Registros.Count(r => r.Firma != null));

            public Task<List<FirmaGuardadaReparto>> LeerFirmasEnColumna(int maximo)
                => Task.FromResult(Registros.Where(r => r.Firma != null).OrderBy(r => r.Prueba.Id).Take(maximo).Select(AFirma).ToList());

            /// <summary>Una prueba vieja (de antes de #770) con la firma en la columna.</summary>
            public Registro ConFirmaVieja(int envio, byte[] firma, DateTime fechaHora)
            {
                var registro = new Registro
                {
                    Prueba = new PruebaEntregaReparto { Id = ++ultimoId, EnvioId = envio, Tipo = PruebaEntregaReparto.TIPO_ENTREGA, FechaHora = fechaHora, ClaveIdempotencia = Guid.NewGuid(), TieneFirma = true },
                    Firma = firma
                };
                Registros.Add(registro);
                return registro;
            }

            public Task<int> Registrar(PruebaEntregaReparto prueba, byte[] firma, EnviosAgencia envio, IList<EnvioHistoria> historia,
                decimal? importeAContabilizar, string usuario)
            {
                if (SimularCarreraCon != null)
                {
                    Registros.Add(new Registro { Prueba = SimularCarreraCon });
                    SimularCarreraCon = null;
                    throw new ClaveIdempotenciaRepetidaException(null);
                }
                if (importeAContabilizar.HasValue && FalloContabilizar != null)
                {
                    throw FalloContabilizar;
                }
                int asiento = importeAContabilizar.HasValue ? 7001 : 0;
                prueba.Asiento = asiento > 0 ? asiento : (int?)null;
                prueba.Id = ++ultimoId;
                Registros.Add(new Registro { Prueba = prueba, Firma = firma, Historia = historia, ImporteAContabilizar = importeAContabilizar, Usuario = usuario });
                return Task.FromResult(asiento);
            }
        }
    }
}
