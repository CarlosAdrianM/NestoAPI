using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>NestoAPI#595 (slice 5, Ariadna#13): la etiqueta de agencia desde el packing, un camino por prueba.</summary>
    [TestClass]
    public class ServicioEtiquetaPackingTests
    {
        private const int PEDIDO = 926940;
        private IPasosEtiquetaPacking pasos;
        private ILectorParametrosUsuario parametros;
        private ServicioEtiquetaPacking servicio;
        private IPrincipal usuario;
        private List<Exception> registrados;

        [TestInitialize]
        public void Preparar()
        {
            pasos = A.Fake<IPasosEtiquetaPacking>();
            parametros = A.Fake<ILectorParametrosUsuario>();
            registrados = new List<Exception>();
            usuario = new GenericPrincipal(new GenericIdentity("Andre", "Bearer"), new[] { "Almacén" });
            servicio = new ServicioEtiquetaPacking(_ => pasos, parametros, ex => registrados.Add(ex))
            {
                MotivoPedidoRetenido = (e, p) => Task.FromResult<string>(null)
            };

            A.CallTo(() => pasos.LeerEnvioEnCurso(A<string>._, A<int>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 2 });
            A.CallTo(() => pasos.NombreAgencia(Constantes.Agencias.AGENCIA_CTT)).Returns("CTT");
            A.CallTo(() => pasos.NombreAgencia(Constantes.Agencias.AGENCIA_GLS)).Returns("GLS");
            A.CallTo(() => pasos.NombreAgencia(Constantes.Agencias.AGENCIA_CANTERAS)).Returns("Canteras");
            A.CallTo(() => pasos.Tramitar(A<int>._)).Returns(PasoEtiquetaAgencia.Bien("CTT123", 2));
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).ReturnsLazily((int _, string impresora) =>
                Task.FromResult(PasoEtiquetaAgencia.Bien("CTT123", 2, impresora ?? "ZEBRA PACKING")));
        }

        private static PeticionEtiquetaPackingDTO Peticion(decimal peso = 3.5m, string impresora = null, params string[] confirmaciones)
            => new PeticionEtiquetaPackingDTO { Empresa = "1", Peso = peso, Impresora = impresora, Confirmaciones = confirmaciones.ToList() };

        private static EnviosAgencia Envio(int agencia, string codigo = "", short bultos = 2, int numero = 5001)
            => new EnviosAgencia { Numero = numero, Empresa = "1  ", Agencia = agencia, Pedido = PEDIDO, Bultos = bultos, CodigoBarras = codigo,
                Estado = Constantes.Agencias.ESTADO_EN_CURSO };

        private void CreaElEnvio(int agencia, List<string> avisos = null)
        {
            EnviosAgencia envio = Envio(agencia);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(ResultadoEnvioDesdePedido.Creado(
                new EnvioDesdePedidoDTO { Envio = envio, Origen = PropuestaEnvioDTO.ORIGEN_NUEVO, Flujo = ServicioEnvioDesdePedido.FlujoDe(agencia),
                    EtiquetaEnNesto = ServicioEnvioDesdePedido.FlujoDe(agencia) != FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR,
                    Avisos = avisos ?? new List<string>() }));
        }

        private void FacturarActivado(string valor = "1")
            => A.CallTo(() => parametros.LeerParametro("1", "Andre", ServicioEtiquetaPacking.PARAMETRO_FACTURAR)).Returns(valor);

        // ------------------------------------------------------------ CTT nuevo: graba, registra e imprime

        [TestMethod]
        public async Task Ctt_Nuevo_GrabaConLasCajasDeLaEntregaRegistraEImprime()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(3.5m, null, "SinPicking"), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, resultado.Tipo);
            EtiquetaPackingDTO etiqueta = resultado.Etiqueta;
            Assert.IsTrue(etiqueta.Impresa);
            Assert.IsTrue(etiqueta.EnvioNuevo);
            Assert.IsFalse(etiqueta.Reimpresion);
            Assert.IsFalse(etiqueta.EtiquetaEnNesto);
            Assert.AreEqual(5001, etiqueta.Envio);
            Assert.AreEqual("CTT123", etiqueta.CodigoBarras);
            Assert.AreEqual("ZEBRA PACKING", etiqueta.Impresora);
            Assert.AreEqual("Etiqueta de CTT impresa en ZEBRA PACKING (2 bultos).", etiqueta.Message);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p =>
                p.Empresa == "1" && p.Pedido == PEDIDO && p.Bultos == 2 && p.Peso == 3.5m && p.Confirmaciones.Single() == "SinPicking"),
                "Andre")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => pasos.Tramitar(5001)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => pasos.Imprimir(5001, null)).MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task Ctt_ConImpresora_LaPasaAImprimir()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(2m, " ZEBRA2 "), usuario);

            A.CallTo(() => pasos.Imprimir(5001, "ZEBRA2")).MustHaveHappenedOnceExactly();
            Assert.AreEqual("ZEBRA2", resultado.Etiqueta.Impresora);
        }

        [TestMethod]
        public async Task Ctt_LosAvisosDeLaGrabacionYLasCajasSinFotoVanEnLaRespuesta()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 3, BultosSinFoto = 1, OtrosPedidos = new List<int> { 926941 } });
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT, new List<string> { "La agencia no ha dado la plaza." });

            EtiquetaPackingDTO etiqueta = (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Etiqueta;

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Bultos == 3), A<string>._)).MustHaveHappened();
            CollectionAssert.Contains(etiqueta.Avisos, "La agencia no ha dado la plaza.");
            CollectionAssert.Contains(etiqueta.Avisos, "Hay 1 caja sin foto.");
            Assert.IsTrue(etiqueta.Avisos.Any(a => a.Contains("926941")));
        }

        // ------------------------------------------------------------ lo que dice DesdePedido pasa tal cual

        [TestMethod]
        public async Task ConPendientes_DevuelveElConflictoYNoTramitaNiImprime()
        {
            var conflicto = new ConflictoEnvioDesdePedidoDTO
            {
                Pendientes = new List<AvisoPropuestaEnvio> { new AvisoPropuestaEnvio { Codigo = CodigosAvisoPropuestaEnvio.SIN_PICKING, Tipo = TiposAvisoPropuestaEnvio.CONFIRMAR, Texto = "Sin picking." } }
            };
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(ResultadoEnvioDesdePedido.ConPendientes(conflicto));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Conflicto, resultado.Tipo);
            Assert.AreSame(conflicto, resultado.Conflicto);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ampliacion_NoSeConfirmaDesdeElPacking_400ParaHacerlaEnNesto()
        {
            // Los bultos de una ampliación son los del envío entero; el packing solo sabe los de esta entrega
            var conflicto = new ConflictoEnvioDesdePedidoDTO
            {
                Pendientes = new List<AvisoPropuestaEnvio>
                {
                    new AvisoPropuestaEnvio { Codigo = CodigosAvisoPropuestaEnvio.AMPLIACION, Tipo = TiposAvisoPropuestaEnvio.CONFIRMAR,
                        Texto = "Este pedido es una ampliación del envío 4999: se actualizarían los datos de ese envío y el nº total de bultos pasaría a ser 2." }
                }
            };
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(ResultadoEnvioDesdePedido.ConPendientes(conflicto));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(3m, null, "Ampliacion", "SinPicking"), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            Assert.AreEqual(CodigosEtiquetaPacking.AMPLIACION, resultado.Error.Codigo);
            StringAssert.Contains(resultado.Error.Message, "Agencias de Nesto");
            // Aunque llegue confirmada, no se pasa a DesdePedido
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p =>
                p.Confirmaciones.Count == 1 && p.Confirmaciones[0] == "SinPicking"), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Invalido_DevuelveElErrorDeDesdePedido()
        {
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(
                ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.PESO_OBLIGATORIO, "Indique el peso."));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(0m), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            Assert.AreEqual(CodigosErrorEnvioDesdePedido.PESO_OBLIGATORIO, resultado.Error.Codigo);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PedidoQueNoExiste_NoEncontrado()
        {
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(ResultadoEnvioDesdePedido.NoEncontrado());

            Assert.AreEqual(TipoResultadoEtiquetaPacking.NoEncontrado, (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Tipo);
        }

        [TestMethod]
        public async Task SinCajasEnElPacking_SinBultosYNoGrabaNada()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 0 });

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            Assert.AreEqual(CodigosEtiquetaPacking.SIN_BULTOS, resultado.Error.Codigo);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------ NestoAPI#740: los bultos se dicen al imprimir

        [TestMethod]
        public async Task BultosTecleados_SinCajasCerradas_GrabaElEnvioConEsosBultos()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 0 });
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Bultos = 3;

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, peticion, usuario);

            Assert.AreNotEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Bultos == 3), A<string>._)).MustHaveHappened();
        }

        [TestMethod]
        public async Task BultosTecleados_SinPackingEnCurso_TambienValen()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(Task.FromResult<EntregaParaEtiqueta>(null));
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Bultos = 1;

            await servicio.Imprimir(PEDIDO, peticion, usuario);

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Bultos == 1), A<string>._)).MustHaveHappened();
        }

        [TestMethod]
        public async Task BultosTecleados_MandanSobreLasCajas_YSinAvisoDeCajasSinFoto()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 2, BultosSinFoto = 2, OtrosPedidos = new List<int> { 926941 } });
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Bultos = 4;

            EtiquetaPackingDTO etiqueta = (await servicio.Imprimir(PEDIDO, peticion, usuario)).Etiqueta;

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Bultos == 4), A<string>._)).MustHaveHappened();
            Assert.IsFalse(etiqueta.Avisos.Any(a => a.Contains("sin foto")));
            Assert.IsTrue(etiqueta.Avisos.Any(a => a.Contains("926941")), "El aviso de otros pedidos de la entrega sigue saliendo");
        }

        [TestMethod]
        public async Task BultosACero_ComoSiempre_LasCajasCerradas()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 2 });
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Bultos = 0;

            await servicio.Imprimir(PEDIDO, peticion, usuario);

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Bultos == 2), A<string>._)).MustHaveHappened();
        }

        [TestMethod]
        [DataRow(-1)]
        [DataRow(ServicioEtiquetaPacking.MAXIMO_BULTOS + 1)]
        public async Task BultosFueraDeRango_400SinGrabarNada(int bultos)
        {
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Bultos = bultos;

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, peticion, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinPacking_SinBultos()
        {
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(Task.FromResult<EntregaParaEtiqueta>(null));

            Assert.AreEqual(CodigosEtiquetaPacking.SIN_BULTOS, (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Error.Codigo);
        }

        [TestMethod]
        public async Task SinEmpresa_LaDeDefecto()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);

            _ = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO { Peso = 1m }, usuario);

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        // ------------------------------------------------------------ GLS y Canteras: la etiqueta sigue en Nesto

        [TestMethod]
        public async Task Gls_GrabaElEnvioYDiceQueLaEtiquetaSeSacaEnNesto()
        {
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_GLS);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, resultado.Tipo);
            Assert.IsTrue(resultado.Etiqueta.EtiquetaEnNesto);
            Assert.IsFalse(resultado.Etiqueta.Impresa);
            Assert.AreEqual("La etiqueta de GLS hay que sacarla en Nesto.", resultado.Etiqueta.Message);
            Assert.IsTrue(resultado.Etiqueta.Avisos[0].StartsWith("El envío 5001 está grabado (2 bultos)"));
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).MustNotHaveHappened();
            // Se factura en Nesto al sacar la etiqueta, como hoy
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Gls_ConElEnvioYaGrabado_NoGrabaOtro()
        {
            A.CallTo(() => pasos.LeerEnvioEnCurso("1", PEDIDO)).Returns(Envio(Constantes.Agencias.AGENCIA_GLS, "61771234567890"));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsTrue(resultado.Etiqueta.EtiquetaEnNesto);
            Assert.IsFalse(resultado.Etiqueta.EnvioNuevo);
            Assert.IsTrue(resultado.Etiqueta.Avisos[0].StartsWith("El envío 5001 ya estaba grabado"));
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------ NestoAPI#552 (fase 4): GLS por la API

        [TestMethod]
        public async Task Gls_ConElInterruptorEncendido_ImprimeEnElServidorSinRegistrarEnGls()
        {
            servicio.GlsEtiquetaPorApi = () => true;
            CreaElEnvio(Constantes.Agencias.AGENCIA_GLS);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, resultado.Tipo);
            Assert.IsFalse(resultado.Etiqueta.EtiquetaEnNesto);
            Assert.IsTrue(resultado.Etiqueta.Impresa);
            Assert.IsFalse(resultado.Etiqueta.Reimpresion);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Imprimir(5001, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Gls_ConElInterruptorEncendidoYElEnvioYaGrabado_EsReimpresion()
        {
            servicio.GlsEtiquetaPorApi = () => true;
            A.CallTo(() => pasos.LeerEnvioEnCurso("1", PEDIDO)).Returns(Envio(Constantes.Agencias.AGENCIA_GLS, "61771234567890"));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsTrue(resultado.Etiqueta.Impresa);
            Assert.IsTrue(resultado.Etiqueta.Reimpresion);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GlsEmpresaEspejo_ConElInterruptorEncendido_TambienSeImprimeEnElServidor()
        {
            // NestoAPI#552 (fase 5, Carlos 27/10/26): la agencia 5 pasa a la API igual que la 1.
            servicio.GlsEtiquetaPorApi = () => true;
            CreaElEnvio(Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsFalse(resultado.Etiqueta.EtiquetaEnNesto);
            Assert.IsTrue(resultado.Etiqueta.Impresa);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Imprimir(5001, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GlsEmpresaEspejo_ConElInterruptorApagado_SigueEnNesto()
        {
            servicio.GlsEtiquetaPorApi = () => false;
            CreaElEnvio(Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsTrue(resultado.Etiqueta.EtiquetaEnNesto);
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Canteras_TambienEnNesto()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CANTERAS);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsTrue(resultado.Etiqueta.EtiquetaEnNesto);
            Assert.AreEqual("La etiqueta de Canteras hay que sacarla en Nesto.", resultado.Etiqueta.Message);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------ fallos: el envío queda grabado

        [TestMethod]
        public async Task FallaTramitar_ElEnvioQuedaGrabadoYNoSeImprime()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => pasos.Tramitar(5001)).Returns(PasoEtiquetaAgencia.Mal(HttpStatusCode.BadGateway, "CTT no responde"));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.FalloAgencia, resultado.Tipo);
            Assert.AreEqual(CodigosEtiquetaPacking.TRAMITAR_FALLIDO, resultado.Etiqueta.Codigo);
            Assert.AreEqual(5001, resultado.Etiqueta.Envio);
            Assert.IsFalse(resultado.Etiqueta.Impresa);
            StringAssert.Contains(resultado.Etiqueta.Message, "CTT no responde.");
            StringAssert.Contains(resultado.Etiqueta.Message, "El envío está grabado");
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FallaImprimir_ElEnvioQuedaRegistradoYNoSeFactura()
        {
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => pasos.Imprimir(5001, A<string>._)).Returns(PasoEtiquetaAgencia.Mal(HttpStatusCode.BadGateway, "La impresora ZEBRA no existe"));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.FalloAgencia, resultado.Tipo);
            Assert.AreEqual(CodigosEtiquetaPacking.IMPRIMIR_FALLIDO, resultado.Etiqueta.Codigo);
            Assert.AreEqual("CTT123", resultado.Etiqueta.CodigoBarras);
            StringAssert.Contains(resultado.Etiqueta.Message, "La impresora ZEBRA no existe.");
            StringAssert.Contains(resultado.Etiqueta.Message, "registrado en CTT (albarán CTT123)");
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------ reintentos: sin duplicar

        [TestMethod]
        public async Task Reintento_EnvioEnCursoSinAlbaran_NoGrabaOtroYLoRegistra()
        {
            A.CallTo(() => pasos.LeerEnvioEnCurso("1", PEDIDO)).Returns(Envio(Constantes.Agencias.AGENCIA_CTT, ""));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, resultado.Tipo);
            Assert.IsFalse(resultado.Etiqueta.EnvioNuevo);
            Assert.IsFalse(resultado.Etiqueta.Reimpresion);
            A.CallTo(() => pasos.LeerEntrega(A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Tramitar(5001)).MustHaveHappenedOnceExactly();
            A.CallTo(() => pasos.Imprimir(5001, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reimpresion_EnvioConAlbaran_NoTramitaYSoloImprime()
        {
            A.CallTo(() => pasos.LeerEnvioEnCurso("1", PEDIDO)).Returns(Envio(Constantes.Agencias.AGENCIA_CTT, "CTT123 "));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsTrue(resultado.Etiqueta.Reimpresion);
            Assert.IsTrue(resultado.Etiqueta.Impresa);
            Assert.AreEqual("Etiqueta de CTT reimpresa en ZEBRA PACKING (2 bultos).", resultado.Etiqueta.Message);
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Innovatrans_MismoCaminoQueCtt()
        {
            A.CallTo(() => pasos.NombreAgencia(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns("Innovatrans");
            CreaElEnvio(Constantes.Agencias.AGENCIA_INNOVATRANS);

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.IsTrue(resultado.Etiqueta.Impresa);
            StringAssert.StartsWith(resultado.Etiqueta.Message, "Etiqueta de Innovatrans impresa");
            A.CallTo(() => pasos.Tramitar(5001)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LosPasosSeLiberanAlAcabar()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);

            _ = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            A.CallTo(() => pasos.Dispose()).MustHaveHappenedOnceExactly();
        }

        // ------------------------------------------------------------ facturar al imprimir (#592)

        [TestMethod]
        public async Task FacturarActivado_FacturaDespuesDeImprimir()
        {
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO { NumeroFactura = "NV26/001234", Avisos = new List<string> { "El NIF no está en la AEAT." } });
            respuesta.Albaranes.Add(new AlbaranCreadoDTO { NumeroAlbaran = 777 });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            EtiquetaPackingDTO etiqueta = (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Etiqueta;

            Assert.IsTrue(etiqueta.Facturado);
            CollectionAssert.Contains(etiqueta.Avisos, $"Pedido {PEDIDO} facturado en la factura NV26/001234 (albarán 777).");
            CollectionAssert.Contains(etiqueta.Avisos, "El NIF no está en la AEAT.");
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).MustHaveHappened()
                .Then(A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task FacturarDesactivadoOSinParametro_NoFactura()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => parametros.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns("0");

            EtiquetaPackingDTO etiqueta = (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Etiqueta;

            Assert.IsFalse(etiqueta.Facturado);
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarFalla_LaEtiquetaSigueHechaYSeAvisa()
        {
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).ThrowsAsync(new InvalidOperationException("Su usuario no tiene permiso para facturar."));

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, resultado.Tipo);
            Assert.IsTrue(resultado.Etiqueta.Impresa);
            Assert.IsFalse(resultado.Etiqueta.Facturado);
            Assert.IsTrue(resultado.Etiqueta.Avisos.Any(a => a.StartsWith($"No se ha podido facturar el pedido {PEDIDO}: Su usuario no tiene permiso")));
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public void FacturarAlImprimir_AceptaUnoTrueYSi_ConOSinDominio()
        {
            A.CallTo(() => parametros.LeerParametro("1", "Andre", ServicioEtiquetaPacking.PARAMETRO_FACTURAR)).Returns("sí");
            A.CallTo(() => parametros.LeerParametro("1", "Pedro", ServicioEtiquetaPacking.PARAMETRO_FACTURAR)).Returns(null);

            Assert.IsTrue(servicio.FacturarAlImprimir("NUEVAVISION\\Andre"));
            Assert.IsTrue(servicio.FacturarAlImprimir("Andre"));
            Assert.IsFalse(servicio.FacturarAlImprimir("Pedro"));
            Assert.IsFalse(servicio.FacturarAlImprimir(null));
        }

        [TestMethod]
        public void ResumenFacturacion_ErroresDocumentoEnPapelYReimpresion()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.PedidosConErrores.Add(new PedidoConErrorDTO { TipoError = ServicioEtiquetaPacking.TIPO_ERROR_SIN_LINEAS, MensajeError = "No tiene líneas.", Severidad = NivelSeveridad.Error });
            respuesta.PedidosConErrores.Add(new PedidoConErrorDTO { TipoError = "MantenerJunto", MensajeError = "Queda pendiente por MantenerJunto.", Severidad = NivelSeveridad.Warning });
            respuesta.Albaranes.Add(new AlbaranCreadoDTO { NumeroAlbaran = 5, DatosImpresion = new DocumentoParaImprimir() });

            List<string> primera = ServicioEtiquetaPacking.ResumenFacturacion(respuesta, PEDIDO, reimpresion: false, out bool facturado);
            List<string> reimpresion = ServicioEtiquetaPacking.ResumenFacturacion(respuesta, PEDIDO, reimpresion: true, out _);

            Assert.IsTrue(facturado);
            CollectionAssert.Contains(primera, $"Albarán 5 del pedido {PEDIDO} creado.");
            CollectionAssert.Contains(primera, "No se ha facturado: Sin líneas para facturar: No tiene líneas.");
            CollectionAssert.Contains(primera, "Queda pendiente por MantenerJunto.");
            CollectionAssert.Contains(primera, $"El pedido {PEDIDO} lleva el documento en papel: imprímalo desde Nesto.");
            Assert.IsFalse(reimpresion.Any(a => a.Contains("Sin líneas")));
        }

        // ------------------------------------------------------------ las cajas de la entrega

        [TestMethod]
        public void EntregaParaEtiqueta_UnaCajaCompartidaCuentaUnaVezYDiceLosOtrosPedidos()
        {
            var packing = new PackingAlmacenDTO
            {
                Picking = 99633,
                Entregas = new List<EntregaPackingAlmacenDTO>
                {
                    new EntregaPackingAlmacenDTO
                    {
                        Pedidos = new List<PedidoPackingAlmacenDTO> { new PedidoPackingAlmacenDTO { Pedido = PEDIDO }, new PedidoPackingAlmacenDTO { Pedido = 926941 } },
                        Bultos = new List<BultoAlmacenDTO>
                        {
                            // caja 1 compartida por los dos pedidos (la misma foto, una fila por pedido)
                            new BultoAlmacenDTO { Pedido = PEDIDO, Picking = 99633, Bulto = 1, TieneFoto = true, RutaBlob = "1/926940/a.jpg" },
                            new BultoAlmacenDTO { Pedido = 926941, Picking = 99633, Bulto = 1, TieneFoto = true, RutaBlob = "1/926940/a.jpg" },
                            // caja 2 solo del otro pedido
                            new BultoAlmacenDTO { Pedido = 926941, Picking = 99633, Bulto = 2, TieneFoto = true, RutaBlob = "1/926941/b.jpg" },
                            // caja 3 sin foto
                            new BultoAlmacenDTO { Pedido = PEDIDO, Picking = 99633, Bulto = 3, TieneFoto = false }
                        }
                    },
                    new EntregaPackingAlmacenDTO { Pedidos = new List<PedidoPackingAlmacenDTO> { new PedidoPackingAlmacenDTO { Pedido = 1 } } }
                }
            };

            EntregaParaEtiqueta entrega = EntregaParaEtiqueta.De(packing, PEDIDO);

            Assert.AreEqual(3, entrega.Bultos);
            Assert.AreEqual(1, entrega.BultosSinFoto);
            CollectionAssert.AreEqual(new List<int> { 926941 }, entrega.OtrosPedidos);
            Assert.IsNull(EntregaParaEtiqueta.De(packing, 12345));
        }

        [TestMethod]
        public void EntregaParaEtiqueta_SinPacking_LosBultosDelPedido()
        {
            EntregaParaEtiqueta entrega = EntregaParaEtiqueta.De(new[]
            {
                new BultoAlmacenDTO { Pedido = PEDIDO, Picking = 5, Bulto = 1, TieneFoto = true, RutaBlob = "x" },
                new BultoAlmacenDTO { Pedido = PEDIDO, Picking = 5, Bulto = 2, TieneFoto = true, RutaBlob = "y" }
            });

            Assert.AreEqual(2, entrega.Bultos);
            Assert.AreEqual(0, entrega.BultosSinFoto);
        }

        [TestMethod]
        public void MensajeDe_HttpErrorTextoOCuerpo()
        {
            Assert.AreEqual("CTT no responde", PasosEtiquetaPackingApi.MensajeDe("{\"Message\":\"CTT no responde\",\"Codigo\":\"AgenciaNoResponde\"}"));
            Assert.AreEqual("Primero hay que tramitar el envío.", PasosEtiquetaPackingApi.MensajeDe("\"Primero hay que tramitar el envío.\""));
            Assert.AreEqual("texto suelto", PasosEtiquetaPackingApi.MensajeDe(" texto suelto "));
            Assert.IsNull(PasosEtiquetaPackingApi.MensajeDe(""));
        }
    
        /// <summary>NestoAPI#698/#734 (29/10/26): un pedido retenido no saca etiqueta desde el packing ni graba envío.</summary>
        [TestMethod]
        public async Task Imprimir_PedidoRetenido_NoGrabaEnvioYDiceElMotivo()
        {
            servicio.MotivoPedidoRetenido = (e, p) => Task.FromResult("El pedido está retenido: esperando a Administración.");

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO(), usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            Assert.AreEqual(CodigosEtiquetaPacking.PEDIDO_RETENIDO, resultado.Error.Codigo);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Tramitar(A<int>._)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------ NestoAPI#765: notas de entrega

        [TestMethod]
        public async Task Reembolso_DelPacking_VaEnLosCambiosDelEnvio()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Reembolso = 45.5m;

            await servicio.Imprimir(PEDIDO, peticion, usuario);

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Cambios != null && p.Cambios.Reembolso == 45.5m),
                A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reembolso_Cero_TambienSeManda_LaNotaNoCobraNada()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Reembolso = 0m;

            await servicio.Imprimir(PEDIDO, peticion, usuario);

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Cambios != null && p.Cambios.Reembolso == 0m),
                A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reembolso_SinIndicar_ElDeLaPropuestaComoSiempre()
        {
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);

            await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Cambios == null), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reembolso_Negativo_400SinGrabarNada()
        {
            PeticionEtiquetaPackingDTO peticion = Peticion();
            peticion.Reembolso = -1m;

            ResultadoEtiquetaPacking resultado = await servicio.Imprimir(PEDIDO, peticion, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Invalida, resultado.Tipo);
            StringAssert.Contains(resultado.Error.Message, "reembolso");
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarAlImprimir_NotaDeRepartoPropio_NoSeProcesa_LaProcesaFacturarRutasConSuDocumentoNE()
        {
            // Si se procesara aquí, Facturar rutas ya no la vería: ni parada (#758) ni documento NE (Ariadna no imprime documentos)
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => pasos.EsNotaDeRepartoPropio("1", PEDIDO)).Returns(true);

            EtiquetaPackingDTO etiqueta = (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Etiqueta;

            Assert.IsTrue(etiqueta.Impresa);
            Assert.IsFalse(etiqueta.Facturado);
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.IsTrue(etiqueta.Avisos.Any(a => a.Contains("la procesa Facturar rutas")));
        }

        [TestMethod]
        public async Task FacturarAlImprimir_SiNoEsNotaDeRepartoPropio_SeFacturaComoSiempre()
        {
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => pasos.EsNotaDeRepartoPropio("1", PEDIDO)).Returns(false);
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(new FacturarRutasResponseDTO());

            await servicio.Imprimir(PEDIDO, Peticion(), usuario);

            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturarAlImprimir_SiNoSePuedeSaberSiEsNota_NoSeFactura_YQuedaEnElmah()
        {
            FacturarActivado();
            CreaElEnvio(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => pasos.EsNotaDeRepartoPropio("1", PEDIDO)).Throws(new InvalidOperationException("sin BD"));

            EtiquetaPackingDTO etiqueta = (await servicio.Imprimir(PEDIDO, Peticion(), usuario)).Etiqueta;

            Assert.IsTrue(etiqueta.Impresa);
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.IsTrue(registrados.Any(e => e.Message.Contains("#765")));
        }
    }
}
