using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using System.Xml.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.Agencias.PruebasEntrega;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): un endpoint y una interfaz para todas las agencias (reparto propio, CTT, GLS).
    /// Sin llamadas reales: los clientes de las agencias van con fakes y los XML/JSON son inventados (sin datos reales).
    /// </summary>
    [TestClass]
    public class PruebasEntregaTests
    {
        private static EnviosAgencia Envio(int agencia, short estado = Constantes.Agencias.ESTADO_ENTREGADO, string codigo = "61197140000001")
            => new EnviosAgencia { Numero = 251500, Agencia = agencia, Estado = estado, CodigoBarras = codigo + "   ", CodPostal = "28001", Pais = 34, Pedido = 930001 };

        // ------------------------------------------------------------ resolución por agencia

        [TestMethod]
        public void Registro_LasAgenciasConPruebaDeEntrega_SonRepartoPropioCttYGls()
        {
            List<int> agencias = RegistroAgencias.PorReflexionSinPuerta().ConCapacidad<IPerfilConPruebaEntrega>().OrderBy(a => a).ToList();

            CollectionAssert.AreEqual(new[] { Constantes.Agencias.AGENCIA_GLS, Constantes.Agencias.AGENCIA_CTT, Constantes.Agencias.AGENCIA_REPARTO_PROPIO }, agencias);
        }

        [TestMethod]
        public void ProveedorDe_ResuelvePorLaAgenciaDelPerfil()
        {
            var proveedor = A.Fake<IProveedorPruebaEntrega>();
            var perfil = new PerfilFalso(77, proveedor);
            var registro = new RegistroAgencias(new IPerfilAgencia[] { perfil, new PerfilSinPrueba(78) }, new PuertaAbierta());

            Assert.AreSame(proveedor, ServicioPruebasEntrega.ProveedorDe(registro, 77, null));
            Assert.IsNull(ServicioPruebasEntrega.ProveedorDe(registro, 78, null), "Un perfil sin la capacidad no da prueba de entrega");
            Assert.IsNull(ServicioPruebasEntrega.ProveedorDe(registro, 79, null), "Una agencia sin perfil tampoco");
        }

        [TestMethod]
        public void ProveedorDe_GlsYReparto_ConstruyenSuProveedorSinTocarLaBD()
        {
            RegistroAgencias registro = RegistroAgencias.PorReflexionSinPuerta();

            Assert.IsInstanceOfType(ServicioPruebasEntrega.ProveedorDe(registro, Constantes.Agencias.AGENCIA_GLS, null), typeof(PruebaEntregaGls));
            Assert.IsNull(ServicioPruebasEntrega.ProveedorDe(registro, Constantes.Agencias.AGENCIA_INNOVATRANS, null));
        }

        [TestMethod]
        public async Task Obtener_AgenciaSinProveedor_DiceQueNoDaPruebaDeEntrega()
        {
            var servicio = new ServicioPruebasEntrega(_ => null);

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_INNOVATRANS), "Innovatrans");

            Assert.IsFalse(prueba.Disponible);
            Assert.AreEqual(ServicioPruebasEntrega.MENSAJE_AGENCIA_SIN_PRUEBA, prueba.Mensaje);
            Assert.AreEqual(251500, prueba.Numero);
            Assert.AreEqual(930001, prueba.Pedido);
        }

        [TestMethod]
        public async Task Obtener_SinEntregar_NoPreguntaALaAgencia()
        {
            var proveedor = A.Fake<IProveedorPruebaEntrega>();
            var servicio = new ServicioPruebasEntrega(_ => proveedor);

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_CTT, estado: 1), "CTT");

            Assert.IsFalse(prueba.Disponible);
            Assert.AreEqual(ServicioPruebasEntrega.MENSAJE_NO_ENTREGADO, prueba.Mensaje);
            A.CallTo(() => proveedor.ObtenerAsync(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Obtener_LaAgenciaFalla_MensajeYElmahNunca500()
        {
            var proveedor = A.Fake<IProveedorPruebaEntrega>();
            A.CallTo(() => proveedor.ObtenerAsync(A<EnviosAgencia>._)).Throws(new AgenciaRemotaException("CTT respondió 400: hash incorrecto"));
            var errores = new List<Exception>();
            var servicio = new ServicioPruebasEntrega(_ => proveedor, errores.Add);

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_CTT), "CTT");

            Assert.IsFalse(prueba.Disponible);
            StringAssert.Contains(prueba.Mensaje, "No se ha podido consultar la prueba de entrega en CTT");
            StringAssert.Contains(prueba.Mensaje, "hash incorrecto");
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "251500");
        }

        [TestMethod]
        public async Task Obtener_FallaAlCrearElProveedor_MensajeNoExcepcion()
        {
            var servicio = new ServicioPruebasEntrega(_ => throw new ArgumentException("Falta CTT:Url en la configuración."));

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_CTT), null);

            Assert.IsFalse(prueba.Disponible);
            StringAssert.Contains(prueba.Mensaje, "la agencia");
        }

        [TestMethod]
        public async Task Obtener_Disponible_CopiaLaFormaComun()
        {
            var proveedor = A.Fake<IProveedorPruebaEntrega>();
            var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            A.CallTo(() => proveedor.ObtenerAsync(A<EnviosAgencia>._)).Returns(new PruebaEntregaAgencia
            {
                Disponible = true,
                FechaHora = new DateTime(2026, 11, 10, 9, 27, 0),
                Receptor = "Receptor de prueba",
                DniReceptor = "00000000T",
                Documento = pdf,
                TipoDocumento = "application/pdf"
            });
            var servicio = new ServicioPruebasEntrega(_ => proveedor);

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_CTT), "CTT ");

            Assert.IsTrue(prueba.Disponible);
            Assert.AreEqual("CTT", prueba.NombreAgencia);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_CTT, prueba.Agencia);
            Assert.AreSame(pdf, prueba.Documento);
            Assert.AreEqual("application/pdf", prueba.TipoDocumento);
            Assert.AreEqual("Receptor de prueba", prueba.Receptor);
            Assert.AreEqual("00000000T", prueba.DniReceptor);
            Assert.IsNull(prueba.Mensaje);
        }

        // ------------------------------------------------------------ endpoint

        [TestMethod]
        public async Task Endpoint_SinPermisoDeDatosPersonales_403()
        {
            var controller = new PruebasEntregaController(null, new ServicioPruebasEntrega(_ => null))
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new GenericPrincipal(new GenericIdentity("Informatico", "Bearer"), new[] { Constantes.GruposSeguridad.INFORMATICA })
            };

            IHttpActionResult resultado = await controller.GetPruebaEntrega(251500);

            HttpResponseMessage respuesta = await resultado.ExecuteAsync(System.Threading.CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "permiso");
        }

        // ------------------------------------------------------------ CTT

        [TestMethod]
        public void Ctt_Hash_MD5HexMinusculasDeCodigoCentroYCp()
        {
            Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", PruebaEntregaCTT.Hash("a", "b", "c"), "MD5 conocido de «abc»");
            Assert.AreEqual("2acc69f05ace37c3eff2fc7f97db9d65", PruebaEntregaCTT.Hash("0082800012345678901234", "9990001", "28001"));
        }

        [TestMethod]
        public void Ctt_CodigoPostalDestino_EnRecogidaEsElDelAlmacen()
        {
            ConfiguracionCTT config = ConfigCtt();
            var envio = Envio(Constantes.Agencias.AGENCIA_CTT);

            Assert.AreEqual("28001", PruebaEntregaCTT.CodigoPostalDestino(envio, config));
            envio.Retorno = AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN;
            Assert.AreEqual("28110", PruebaEntregaCTT.CodigoPostalDestino(envio, config));
        }

        [TestMethod]
        public async Task Ctt_Pdf_LoPideConElHashYLeeQuienFirmaDelSeguimiento()
        {
            var descarga = A.Fake<IDescargaBinariaCTT>();
            string ruta = null;
            A.CallTo(() => descarga.DescargarAsync(A<string>._, A<string>._))
                .Invokes((string r, string o) => ruta = r)
                .Returns(new RespuestaBinariaCTT { Codigo = 200, TipoContenido = "application/pdf", Contenido = new byte[] { 1, 2, 3 } });
            var cliente = A.Fake<IClienteRestCTT>();
            A.CallTo(() => cliente.EnviarAsync(HttpMethod.Get, A<string>._, null, A<string>._)).Returns(new RespuestaCTT
            {
                Codigo = 200,
                Cuerpo = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["shipping_history"] = new JObject
                        {
                            ["events"] = new JArray(
                                new JObject { ["code"] = "1500", ["type"] = "STATUS", ["event_date"] = "2026-11-10T07:00:00.000+01:00" },
                                new JObject
                                {
                                    ["code"] = "2100", ["type"] = "STATUS", ["event_date"] = "2026-11-10T09:27:35.393+01:00",
                                    ["detail"] = new JObject { ["signee_name"] = " Receptor Inventado ", ["signee_id"] = "" }
                                })
                        }
                    }
                }.ToString()
            });
            var proveedor = new PruebaEntregaCTT(descarga, cliente, ConfigCtt());

            PruebaEntregaAgencia prueba = await proveedor.ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_CTT, codigo: "0082800012345678901234"));

            Assert.IsTrue(prueba.Disponible);
            Assert.AreEqual("cls/pods/0082800012345678901234?client_center_code=9990001&hash=2acc69f05ace37c3eff2fc7f97db9d65", ruta);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, prueba.Documento);
            Assert.AreEqual("application/pdf", prueba.TipoDocumento);
            Assert.AreEqual("Receptor Inventado", prueba.Receptor);
            Assert.IsNull(prueba.DniReceptor, "signee_id vacío = sin DNI");
            Assert.AreEqual(new DateTimeOffset(2026, 11, 10, 9, 27, 35, 393, TimeSpan.FromHours(1)).LocalDateTime, prueba.FechaHora);
        }

        [TestMethod]
        public async Task Ctt_SinPodTodavia_404_EsUnMensaje()
        {
            var descarga = A.Fake<IDescargaBinariaCTT>();
            A.CallTo(() => descarga.DescargarAsync(A<string>._, A<string>._)).Returns(new RespuestaBinariaCTT { Codigo = 404, Contenido = new byte[0] });
            var proveedor = new PruebaEntregaCTT(descarga, null, ConfigCtt());

            PruebaEntregaAgencia prueba = await proveedor.ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_CTT));

            Assert.IsFalse(prueba.Disponible);
            Assert.AreEqual(PruebaEntregaCTT.MENSAJE_AUN_NO, prueba.Mensaje);
        }

        [TestMethod]
        public async Task Ctt_400_LanzaConElMotivoYElServicioLoConvierteEnMensaje()
        {
            var descarga = A.Fake<IDescargaBinariaCTT>();
            A.CallTo(() => descarga.DescargarAsync(A<string>._, A<string>._)).Returns(new RespuestaBinariaCTT
            {
                Codigo = 400,
                Contenido = System.Text.Encoding.UTF8.GetBytes("{\"error\":\"Invalid hash\"}")
            });
            var proveedor = new PruebaEntregaCTT(descarga, null, ConfigCtt());
            var servicio = new ServicioPruebasEntrega(_ => proveedor);

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_CTT), "CTT");

            Assert.IsFalse(prueba.Disponible);
            StringAssert.Contains(prueba.Mensaje, "Invalid hash");
        }

        [TestMethod]
        public async Task Ctt_SiFallaElSeguimiento_DaElPdfIgual()
        {
            var descarga = A.Fake<IDescargaBinariaCTT>();
            A.CallTo(() => descarga.DescargarAsync(A<string>._, A<string>._))
                .Returns(new RespuestaBinariaCTT { Codigo = 200, TipoContenido = "application/pdf", Contenido = new byte[] { 9 } });
            var cliente = A.Fake<IClienteRestCTT>();
            A.CallTo(() => cliente.EnviarAsync(A<HttpMethod>._, A<string>._, A<object>._, A<string>._)).Throws(new AgenciaRemotaException("429"));
            var envio = Envio(Constantes.Agencias.AGENCIA_CTT);
            envio.FechaEntrega = new DateTime(2026, 11, 10);

            PruebaEntregaAgencia prueba = await new PruebaEntregaCTT(descarga, cliente, ConfigCtt()).ObtenerAsync(envio);

            Assert.IsTrue(prueba.Disponible);
            Assert.AreEqual(new DateTime(2026, 11, 10), prueba.FechaHora);
            Assert.IsNull(prueba.Receptor);
        }

        [TestMethod]
        public async Task Ctt_SinCodigo_NoLlamaACtt()
        {
            var descarga = A.Fake<IDescargaBinariaCTT>();
            var envio = Envio(Constantes.Agencias.AGENCIA_CTT);
            envio.CodigoBarras = "  ";

            PruebaEntregaAgencia prueba = await new PruebaEntregaCTT(descarga, null, ConfigCtt()).ObtenerAsync(envio);

            Assert.IsFalse(prueba.Disponible);
            A.CallTo(() => descarga.DescargarAsync(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        private static ConfiguracionCTT ConfigCtt()
            => new ConfiguracionCTT("https://ctt.invalid/integrations/", "id", "secreto", "9990001", new RemitenteCTT { CodigoPostal = "28110", Pais = "ES" });

        // ------------------------------------------------------------ GLS

        private const string XML_GLS_ENTREGADO = @"<?xml version=""1.0"" encoding=""utf-8""?>
<expediciones>
  <exp>
    <albaran>61197140000001</albaran>
    <estado>ENTREGADO</estado>
    <incidencia>SIN INCIDENCIA</incidencia>
    <pod>10/11/2026 9:27:35</pod>
    <NombreEntrega>Receptor Inventado</NombreEntrega>
    <DniEntrega>00000000T</DniEntrega>
    <firma></firma>
    <digitalizaciones>
      <digitalizacion>
        <codtipo>1</codtipo>
        <tipo>ALBARAN</tipo>
        <fecha>09/11/2026 18:00:00</fecha>
        <imagen>https://pods.gls-spain.es/albaran.aspx?x=1</imagen>
        <observaciones></observaciones>
      </digitalizacion>
      <digitalizacion>
        <codtipo>9</codtipo>
        <tipo>POD (APP)</tipo>
        <fecha>10/11/2026 9:28:00</fecha>
        <imagen>https://pods.gls-spain.es/AlbaranDesdeFirma.aspx?codexp=1</imagen>
        <observaciones>Prueba</observaciones>
      </digitalizacion>
    </digitalizaciones>
  </exp>
</expediciones>";

        [TestMethod]
        public void Gls_LeerEntrega_SacaFechaReceptorDniYLaImagenDelPod()
        {
            PruebaEntregaGls.DatosEntregaGls datos = PruebaEntregaGls.LeerEntrega(XDocument.Parse(XML_GLS_ENTREGADO));

            Assert.IsTrue(datos.Entregado);
            Assert.AreEqual(new DateTime(2026, 11, 10, 9, 27, 35), datos.FechaHora);
            Assert.AreEqual("Receptor Inventado", datos.Receptor);
            Assert.AreEqual("00000000T", datos.Dni);
            Assert.AreEqual("https://pods.gls-spain.es/AlbaranDesdeFirma.aspx?codexp=1", datos.UrlImagen, "La del POD, no la del albarán");
        }

        [TestMethod]
        public void Gls_LeerEntrega_SinExp_EsNull()
        {
            Assert.IsNull(PruebaEntregaGls.LeerEntrega(XDocument.Parse("<expediciones><Error>No se encuentra la expedición</Error></expediciones>")));
        }

        [TestMethod]
        public async Task Gls_ImagenJpeg_SeTraeDesdeElServidor()
        {
            var descargador = A.Fake<IDescargadorDocumentosPruebaEntrega>();
            A.CallTo(() => descargador.DescargarAsync("https://pods.gls-spain.es/AlbaranDesdeFirma.aspx?codexp=1"))
                .Returns(new DocumentoDescargado { Contenido = new byte[] { 0xFF, 0xD8 }, Tipo = "image/jpeg" });

            PruebaEntregaAgencia prueba = await new PruebaEntregaGls(ClienteGls(XML_GLS_ENTREGADO), descargador).ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_GLS));

            Assert.IsTrue(prueba.Disponible);
            Assert.AreEqual("image/jpeg", prueba.TipoDocumento);
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8 }, prueba.Documento);
            Assert.IsNull(prueba.UrlDocumento);
            Assert.AreEqual("Receptor Inventado", prueba.Receptor);
        }

        [TestMethod]
        public async Task Gls_SiNoEsPdfNiImagen_DaElEnlace()
        {
            var descargador = A.Fake<IDescargadorDocumentosPruebaEntrega>();
            A.CallTo(() => descargador.DescargarAsync(A<string>._)).Returns(Task.FromResult<DocumentoDescargado>(null));

            PruebaEntregaAgencia prueba = await new PruebaEntregaGls(ClienteGls(XML_GLS_ENTREGADO), descargador).ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_GLS));

            Assert.IsTrue(prueba.Disponible);
            Assert.IsNull(prueba.Documento);
            Assert.AreEqual("https://pods.gls-spain.es/AlbaranDesdeFirma.aspx?codexp=1", prueba.UrlDocumento);
        }

        [TestMethod]
        public async Task Gls_NoEntregadoEnGls_EsUnMensaje()
        {
            string xml = "<expediciones><exp><estado>EN REPARTO</estado><pod></pod></exp></expediciones>";
            var descargador = A.Fake<IDescargadorDocumentosPruebaEntrega>();

            PruebaEntregaAgencia prueba = await new PruebaEntregaGls(ClienteGls(xml), descargador).ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_GLS));

            Assert.IsFalse(prueba.Disponible);
            Assert.AreEqual(PruebaEntregaGls.MENSAJE_NO_ENTREGADO, prueba.Mensaje);
            A.CallTo(() => descargador.DescargarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Gls_CaidaDelWebService_ElServicioDaMensaje()
        {
            var cliente = A.Fake<IClienteTrackingGls>();
            A.CallTo(() => cliente.ConsultarAsync(A<string>._)).Throws(new HttpRequestException("500 (Internal Server Error)"));
            var servicio = new ServicioPruebasEntrega(_ => new PruebaEntregaGls(cliente, A.Fake<IDescargadorDocumentosPruebaEntrega>()));

            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(Envio(Constantes.Agencias.AGENCIA_GLS), "ASM");

            Assert.IsFalse(prueba.Disponible);
            StringAssert.Contains(prueba.Mensaje, "ASM");
        }

        // Revisión 10/11/26: la URL viene del XML de GLS; solo https de gls-spain.es (o subdominio) se baja o se da al cliente.
        [TestMethod]
        public void Descargador_EsUrlDeGls_SoloHttpsDeGlsSpain()
        {
            Assert.IsTrue(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://pods.gls-spain.es/Extranet/public/AlbaranDesdeFirma.aspx?codexp=1"));
            Assert.IsTrue(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://gls-spain.es/x"));
            Assert.IsTrue(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://PODS.GLS-SPAIN.ES/x"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("http://pods.gls-spain.es/x"), "Sin https no");
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://gls-spain.es.malo.com/x"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://falsogls-spain.es/x"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://pods.gls-spain.es@192.168.1.1/x"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("https://192.168.1.1/x"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("file:///C:/Windows/win.ini"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls("/relativa"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsUrlDeGls(null));
        }

        [TestMethod]
        public async Task Descargador_UrlQueNoEsDeGls_NiSiquieraPide()
        {
            var manejador = new ManejadorQueCuenta();
            var descargador = new DescargadorDocumentosPruebaEntrega(new HttpClient(manejador));

            Assert.IsNull(await descargador.DescargarAsync("http://intranet.local/secreto"));
            Assert.AreEqual(0, manejador.Llamadas);
        }

        [TestMethod]
        public async Task Descargador_CuerpoMayorQueElMaximoSinContentLength_EsNull()
        {
            var cuerpo = new System.IO.MemoryStream(new byte[DescargadorDocumentosPruebaEntrega.MAXIMO_BYTES + 1]);
            Assert.IsNull(await DescargadorDocumentosPruebaEntrega.LeerHasta(cuerpo, DescargadorDocumentosPruebaEntrega.MAXIMO_BYTES));
            var justo = new System.IO.MemoryStream(new byte[] { 1, 2, 3 });
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await DescargadorDocumentosPruebaEntrega.LeerHasta(justo, 3));
        }

        [TestMethod]
        public async Task Gls_UrlQueNoEsDeGls_NiSeBajaNiSeDaAlCliente()
        {
            string xml = XML_GLS_ENTREGADO.Replace("https://pods.gls-spain.es/AlbaranDesdeFirma.aspx?codexp=1", "http://10.0.0.1/interno");
            var descargador = A.Fake<IDescargadorDocumentosPruebaEntrega>();

            PruebaEntregaAgencia prueba = await new PruebaEntregaGls(ClienteGls(xml), descargador).ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_GLS));

            Assert.IsTrue(prueba.Disponible);
            Assert.IsNull(prueba.UrlDocumento);
            Assert.IsNull(prueba.Documento);
            Assert.AreEqual(PruebaEntregaGls.MENSAJE_SIN_IMAGEN, prueba.Mensaje);
            A.CallTo(() => descargador.DescargarAsync(A<string>._)).MustNotHaveHappened();
        }

        private class ManejadorQueCuenta : HttpMessageHandler
        {
            public int Llamadas { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                Llamadas++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }

        [TestMethod]
        public void Descargador_SoloPdfEImagenes()
        {
            Assert.IsTrue(DescargadorDocumentosPruebaEntrega.EsDocumentoAdmitido("application/pdf"));
            Assert.IsTrue(DescargadorDocumentosPruebaEntrega.EsDocumentoAdmitido("image/jpeg"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsDocumentoAdmitido("text/html"));
            Assert.IsFalse(DescargadorDocumentosPruebaEntrega.EsDocumentoAdmitido(null));
        }

        private static IClienteTrackingGls ClienteGls(string xml)
        {
            var cliente = A.Fake<IClienteTrackingGls>();
            A.CallTo(() => cliente.ConsultarAsync(A<string>._)).Returns(XDocument.Parse(xml));
            return cliente;
        }

        // ------------------------------------------------------------ reparto propio

        [TestMethod]
        public async Task RepartoPropio_LaMismaPruebaYLaFirmaComoDocumento()
        {
            var repositorio = A.Fake<IRepositorioRepartoPropio>();
            A.CallTo(() => repositorio.ScriptAplicado()).Returns(true);
            A.CallTo(() => repositorio.LeerUltimaEntrega(251500)).Returns(new PruebaEntregaReparto
            {
                EnvioId = 251500, FechaHora = new DateTime(2026, 10, 29, 12, 34, 0), Receptor = "Receptor Inventado", DniReceptor = "00000000T",
                CobroReembolso = PruebaEntregaReparto.COBRO_EFECTIVO, ImporteCobrado = 80m, Usuario = "Pedro", TieneFirma = true
            });
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            A.CallTo(() => repositorio.LeerFirma(251500)).Returns(new FirmaGuardadaReparto { Id = 1, EnvioId = 251500, Firma = png });
            var proveedor = new PruebaEntregaRepartoPropio(new ServicioRepartoPropio(repositorio));

            PruebaEntregaAgencia prueba = await proveedor.ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_REPARTO_PROPIO));

            Assert.IsTrue(prueba.Disponible);
            Assert.AreEqual("Receptor Inventado", prueba.Receptor);
            Assert.AreEqual("00000000T", prueba.DniReceptor);
            Assert.AreEqual(PruebaEntregaReparto.COBRO_EFECTIVO, prueba.CobroReembolso);
            Assert.AreEqual(80m, prueba.ImporteCobrado);
            Assert.AreEqual("Pedro", prueba.Usuario);
            Assert.AreSame(png, prueba.Documento);
            Assert.AreEqual("image/png", prueba.TipoDocumento);
        }

        [TestMethod]
        public async Task RepartoPropio_SinFirma_LoDiceYNoLaPide()
        {
            var repositorio = A.Fake<IRepositorioRepartoPropio>();
            A.CallTo(() => repositorio.ScriptAplicado()).Returns(true);
            A.CallTo(() => repositorio.LeerUltimaEntrega(251500)).Returns(new PruebaEntregaReparto { EnvioId = 251500, TieneFirma = false });

            PruebaEntregaAgencia prueba = await new PruebaEntregaRepartoPropio(new ServicioRepartoPropio(repositorio)).ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_REPARTO_PROPIO));

            Assert.IsTrue(prueba.Disponible);
            Assert.AreEqual(PruebaEntregaRepartoPropio.MENSAJE_SIN_FIRMA, prueba.Mensaje);
            A.CallTo(() => repositorio.LeerFirma(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RepartoPropio_SinPrueba_NoDisponible()
        {
            var repositorio = A.Fake<IRepositorioRepartoPropio>();
            A.CallTo(() => repositorio.ScriptAplicado()).Returns(true);
            A.CallTo(() => repositorio.LeerUltimaEntrega(251500)).Returns(Task.FromResult<PruebaEntregaReparto>(null));

            PruebaEntregaAgencia prueba = await new PruebaEntregaRepartoPropio(new ServicioRepartoPropio(repositorio)).ObtenerAsync(Envio(Constantes.Agencias.AGENCIA_REPARTO_PROPIO));

            Assert.IsFalse(prueba.Disponible);
            Assert.AreEqual(PruebaEntregaRepartoPropio.MENSAJE_SIN_PRUEBA, prueba.Mensaje);
        }

        // ------------------------------------------------------------ dobles

        private class PuertaAbierta : IGateAgenciasActivas
        {
            public bool EstaActiva(int agenciaId) => true;
        }

        private class PerfilFalso : IPerfilConPruebaEntrega
        {
            private readonly IProveedorPruebaEntrega proveedor;
            public PerfilFalso(int agencia, IProveedorPruebaEntrega proveedor) { AgenciaId = agencia; this.proveedor = proveedor; }
            public int AgenciaId { get; }
            public IProveedorPruebaEntrega CrearPruebaEntrega(NVEntities db) => proveedor;
        }

        private class PerfilSinPrueba : IPerfilAgencia
        {
            public PerfilSinPrueba(int agencia) { AgenciaId = agencia; }
            public int AgenciaId { get; }
        }
    }
}
