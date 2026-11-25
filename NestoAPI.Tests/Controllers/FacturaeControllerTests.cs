using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Facturae;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Tests.Infrastructure.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using System.Xml.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#903: GET api/Facturas/Facturae y GET/PUT api/Clientes/FacturaElectronica.</summary>
    [TestClass]
    public class FacturaeControllerTests
    {
        private const string FACTURA = "NV2616075";
        private IServicioFacturas servicioFacturas;
        private IGestorFacturas gestor;
        private IRepositorioClientesFacturaElectronica repositorio;
        private List<VerifactuFacturaRectificada> rectificadas;

        [TestInitialize]
        public void Preparar()
        {
            servicioFacturas = A.Fake<IServicioFacturas>();
            gestor = A.Fake<IGestorFacturas>();
            repositorio = A.Fake<IRepositorioClientesFacturaElectronica>();
            rectificadas = new List<VerifactuFacturaRectificada>();

            DatosOrigenFacturae datos = FacturaeTests.Datos(new List<LinPedidoVta> { FacturaeTests.Linea(1, 2, 10M, 0, 21) });
            A.CallTo(() => servicioFacturas.CargarCabFactura("1", FACTURA)).Returns(new CabFacturaVta
            {
                Empresa = "1",
                Número = FACTURA,
                Serie = "NV",
                Nº_Cliente = "33895     ",
                Contacto = "0  ",
                LinPedidoVtas = datos.Lineas
            });
            A.CallTo(() => gestor.LeerFactura("1", FACTURA)).Returns(datos.Factura);
            A.CallTo(() => servicioFacturas.CargarEmpresa("1")).Returns(new Empresa { NIF = "B99999999" });
            A.CallTo(() => repositorio.Leer(A<string>._, A<string>._, A<string>._)).Returns(new LecturaClienteFacturaElectronica { HayTabla = true });
        }

        private FacturaeController Controlador(params string[] roles)
        {
            var servicio = new ServicioFacturae(gestor, servicioFacturas, repositorio, (e, n) => Task.FromResult(rectificadas));
            return new FacturaeController(servicio, repositorio)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Carlos", "Bearer"), roles),
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        private void Dir3Guardados() => A.CallTo(() => repositorio.Leer("1", "33895", "0")).Returns(new LecturaClienteFacturaElectronica
        {
            HayTabla = true,
            Fila = new FilaClienteFacturaElectronica
            {
                Empresa = "1", Cliente = "33895", Contacto = "0",
                OficinaContable = "L01280796", OrganoGestor = "L01280796", UnidadTramitadora = "LA0007357", NombreUnidadTramitadora = "Compras"
            }
        });

        [TestMethod]
        public void Endpoints_PidenToken()
        {
            foreach (string metodo in new[] { nameof(FacturaeController.GetFacturae), nameof(FacturaeController.GetFacturaElectronica), nameof(FacturaeController.PutFacturaElectronica) })
            {
                Assert.IsTrue(typeof(FacturaeController).GetMethod(metodo).GetCustomAttributes(typeof(AuthorizeAttribute), true).Any(), metodo);
            }
        }

        [TestMethod]
        public async Task GetFacturae_SinGrupo_403()
        {
            HttpResponseMessage respuesta = await Controlador("Almacén").GetFacturae("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(FacturaeController.MENSAJE_SIN_PERMISO, await respuesta.Content.ReadAsStringAsync());
            A.CallTo(() => gestor.LeerFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFacturae_ClienteDeLaTienda_403()
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "cliente@x.es"), new Claim("cliente", "33895"), new Claim(ClaimTypes.Role, "Administración") }, "Bearer");
            FacturaeController controlador = Controlador();
            controlador.User = new ClaimsPrincipal(identidad);

            HttpResponseMessage respuesta = await controlador.GetFacturae("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetFacturae_NoExiste_404()
        {
            A.CallTo(() => servicioFacturas.CargarCabFactura("1", "NV0000001")).Returns(null);

            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", "NV0000001");

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        // Carlos 25/11/26: «No se puede mandar por FACe uno de la empresa 3» (ReglaDocumentoNoDescargable)
        [DataTestMethod]
        [DataRow("3", FACTURA, false)]
        [DataRow("3", FACTURA, true)]
        [DataRow("3  ", FACTURA, true)]
        [DataRow("1", "GB2600012", false)]
        [DataRow("1", "GB2600012", true)]
        public async Task GetFacturae_EmpresaEspejoOSerieSinDescarga_403ConElMensajeUnico(string empresa, string factura, bool comprobar)
        {
            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae(empresa, factura, comprobar);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(ReglaDocumentoNoDescargable.Mensaje(factura), await respuesta.Content.ReadAsStringAsync());
            A.CallTo(() => servicioFacturas.CargarCabFactura(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => gestor.LeerFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFacturae_EmpresaEspejo_SinGrupo_SigueDando403DePermiso()
        {
            HttpResponseMessage respuesta = await Controlador("Almacén").GetFacturae("3", FACTURA);

            Assert.AreEqual(FacturaeController.MENSAJE_SIN_PERMISO, await respuesta.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task GetFacturae_SinDir3_400ConElMensaje()
        {
            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.BadRequest, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), ConstructorModeloFacturae.FALTAN_DIR3);
        }

        [TestMethod]
        public async Task GetFacturae_ConDir3Guardados_DevuelveElXml()
        {
            Dir3Guardados();

            HttpResponseMessage respuesta = await Controlador("Dirección").GetFacturae("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("application/xml", respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("utf-8", respuesta.Content.Headers.ContentType.CharSet);
            Assert.AreEqual("Facturae_NV2616075.xml", respuesta.Content.Headers.ContentDisposition.FileName);
            XDocument doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(await respuesta.Content.ReadAsByteArrayAsync()));
            XElement comprador = doc.Root.Element("Parties").Element("BuyerParty");
            Assert.AreEqual("LA0007357", comprador.Element("AdministrativeCentres").Elements("AdministrativeCentre").Last().Element("CentreCode").Value);
            Assert.AreEqual("PO-2026/118", doc.Root.Descendants("ReceiverTransactionReference").First().Value, "«Su pedido» de la factura");
        }

        [TestMethod]
        public async Task GetFacturae_Dir3YReferenciasEnLaLlamada_GananSobreLoGuardado()
        {
            Dir3Guardados();

            HttpResponseMessage respuesta = await Controlador("Informática").GetFacturae("1", FACTURA,
                unidadTramitadora: "l01280797", suPedido: "EXP-1", expediente: "2026/55");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            XDocument doc = XDocument.Parse(await respuesta.Content.ReadAsStringAsync());
            XElement unidad = doc.Root.Descendants("AdministrativeCentre").Single(c => c.Element("RoleTypeCode").Value == "03");
            Assert.AreEqual("L01280797", unidad.Element("CentreCode").Value);
            Assert.IsNull(unidad.Element("Name"), "El nombre guardado es del otro código");
            Assert.AreEqual("EXP-1", doc.Root.Descendants("ReceiverTransactionReference").First().Value);
            Assert.AreEqual("2026/55", doc.Root.Descendants("FileReference").First().Value);
        }

        [TestMethod]
        public async Task GetFacturae_SinTablaPeroConDir3EnLaLlamada_Descarga()
        {
            A.CallTo(() => repositorio.Leer(A<string>._, A<string>._, A<string>._)).Returns(new LecturaClienteFacturaElectronica { HayTabla = false });

            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", FACTURA,
                oficinaContable: "L01280796", organoGestor: "L01280796", unidadTramitadora: "LA0007357");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetFacturae_Comprobar_DevuelveJsonConTotalesYFaltas()
        {
            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", FACTURA, comprobar: true);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            var dto = (ComprobacionFacturaeDTO)((ObjectContent)respuesta.Content).Value;
            Assert.IsFalse(dto.Listo);
            CollectionAssert.Contains(dto.Faltas, ConstructorModeloFacturae.FALTAN_DIR3);
            Assert.AreEqual(20M, dto.BaseImponible);
            Assert.AreEqual(4.20M, dto.TotalImpuestos);
            Assert.AreEqual(24.20M, dto.Total);
            Assert.AreEqual(1, dto.Vencimientos.Count);
            Assert.AreEqual("04", dto.Vencimientos[0].MedioPago);
            Assert.AreEqual("ES9121000418450200051332", dto.Vencimientos[0].Iban);
            Assert.AreEqual("33895", dto.Cliente);
            Assert.AreEqual("PO-2026/118", dto.SuPedido);
            Assert.AreEqual(ConstructorModeloFacturae.ORIGEN_PEDIDO_FACTURA, dto.OrigenSuPedido);
            Assert.AreEqual("Facturae_NV2616075.xml", dto.NombreFichero);
        }

        // ---------------------------------------------------------------- Carlos 25/11/26: sin P.O., nuestro número de pedido

        private async Task<ComprobacionFacturaeDTO> Comprobar(string suPedido = null)
        {
            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", FACTURA, comprobar: true, suPedido: suPedido);
            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            return (ComprobacionFacturaeDTO)((ObjectContent)respuesta.Content).Value;
        }

        private void FacturaSinSuPedido(params LinPedidoVta[] lineas)
        {
            DatosOrigenFacturae datos = FacturaeTests.Datos(lineas.ToList());
            datos.Factura.SuPedido = null;
            A.CallTo(() => servicioFacturas.CargarCabFactura("1", FACTURA)).Returns(new CabFacturaVta
            {
                Empresa = "1", Número = FACTURA, Serie = "NV", Nº_Cliente = "33895", Contacto = "0", LinPedidoVtas = datos.Lineas
            });
            A.CallTo(() => gestor.LeerFactura("1", FACTURA)).Returns(datos.Factura);
        }

        [TestMethod]
        public async Task Comprobar_ConSuPedidoEnLaLlamada_GanaYOrigenParametro()
        {
            ComprobacionFacturaeDTO dto = await Comprobar(suPedido: "EXP-1");

            Assert.AreEqual("EXP-1", dto.SuPedido);
            Assert.AreEqual(ConstructorModeloFacturae.ORIGEN_PEDIDO_PARAMETRO, dto.OrigenSuPedido);
            Assert.IsFalse(dto.Avisos.Any(a => a.StartsWith("No consta el pedido del organismo")));
        }

        [TestMethod]
        public async Task Comprobar_SinPoDeUnPedido_VaNuestroNumeroYLoAvisa()
        {
            FacturaSinSuPedido(FacturaeTests.Linea(1, 2, 10M, 0, 21));

            ComprobacionFacturaeDTO dto = await Comprobar();

            Assert.AreEqual("930001", dto.SuPedido);
            Assert.AreEqual(ConstructorModeloFacturae.ORIGEN_PEDIDO_NUESTRO, dto.OrigenSuPedido);
            CollectionAssert.Contains(dto.Avisos, "No consta el pedido del organismo: se pone nuestro número de pedido (930001) como referencia.");
            Assert.IsFalse(dto.Avisos.Any(a => a.StartsWith("La factura no lleva «Su pedido»")), "Ya no avisa de que falta la referencia");
        }

        [TestMethod]
        public async Task Descargar_SinPoDeDosPedidos_CadaLineaLlevaElSuyo()
        {
            LinPedidoVta otra = FacturaeTests.Linea(2, 1, 5M, 0, 21, producto: "40695", texto: "JABÓN");
            otra.Número = 931472;
            FacturaSinSuPedido(FacturaeTests.Linea(1, 2, 10M, 0, 21), otra);
            Dir3Guardados();

            ComprobacionFacturaeDTO dto = await Comprobar();
            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", FACTURA);

            Assert.AreEqual("930001, 931472", dto.SuPedido);
            Assert.AreEqual(ConstructorModeloFacturae.ORIGEN_PEDIDO_NUESTRO, dto.OrigenSuPedido);
            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            XDocument doc = XDocument.Parse(await respuesta.Content.ReadAsStringAsync());
            CollectionAssert.AreEqual(new[] { "930001", "931472" },
                doc.Root.Descendants("InvoiceLine").Select(l => l.Element("ReceiverTransactionReference").Value).ToArray());
        }

        [TestMethod]
        public async Task GetFacturae_LeerFacturaFalla_SaleComoFalta()
        {
            A.CallTo(() => gestor.LeerFactura("1", FACTURA)).Throws(new Exception("No cuadran los vencimientos con el total de la factura."));

            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.BadRequest, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "No cuadran los vencimientos");
        }

        [TestMethod]
        public async Task GetFacturae_Rectificativa_LeeLasVinculaciones()
        {
            A.CallTo(() => servicioFacturas.CargarCabFactura("1", "RV2600189")).Returns(new CabFacturaVta
            {
                Empresa = "1", Número = "RV2600189", Serie = "RV", Nº_Cliente = "33895", Contacto = "0",
                LinPedidoVtas = new List<LinPedidoVta> { FacturaeTests.Linea(1, -2, 10M, 0, 21) }
            });
            DatosOrigenFacturae datos = FacturaeTests.Datos(new List<LinPedidoVta> { FacturaeTests.Linea(1, -2, 10M, 0, 21) });
            datos.Factura.NumeroFactura = "RV2600189";
            datos.Factura.Serie = "RV";
            A.CallTo(() => gestor.LeerFactura("1", "RV2600189")).Returns(datos.Factura);
            rectificadas.Add(new VerifactuFacturaRectificada { Serie = "NV", Numero = "2616075", FechaExpedicion = new DateTime(2026, 11, 24) });
            Dir3Guardados();

            HttpResponseMessage respuesta = await Controlador("Administración").GetFacturae("1", "RV2600189");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync());
            XDocument doc = XDocument.Parse(await respuesta.Content.ReadAsStringAsync());
            Assert.AreEqual("OR", doc.Root.Descendants("InvoiceClass").Single().Value);
            Assert.AreEqual("2616075", doc.Root.Descendants("Corrective").Single().Element("InvoiceNumber").Value);
        }

        [TestMethod]
        public async Task GetFacturaElectronica_SinFila_200Vacio()
        {
            var resultado = await Controlador("Administración").GetFacturaElectronica("33895", "1", "0") as OkNegotiatedContentResult<ClienteFacturaElectronicaDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsFalse(resultado.Content.Guardado);
            Assert.IsTrue(resultado.Content.TablaCreada);
            Assert.IsNull(resultado.Content.OficinaContable);
        }

        [TestMethod]
        public async Task GetFacturaElectronica_ConFila_LaDevuelve()
        {
            Dir3Guardados();

            var resultado = await Controlador("Administración").GetFacturaElectronica("33895", "1", "0") as OkNegotiatedContentResult<ClienteFacturaElectronicaDTO>;

            Assert.IsTrue(resultado.Content.Guardado);
            Assert.AreEqual("LA0007357", resultado.Content.UnidadTramitadora);
        }

        [TestMethod]
        public async Task PutFacturaElectronica_SinGrupo_403()
        {
            var resultado = await Controlador("Vendedores").PutFacturaElectronica(new ClienteFacturaElectronicaDTO { Cliente = "33895" }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Guardar(A<FilaClienteFacturaElectronica>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutFacturaElectronica_CodigoNoValidoOFaltaUno_400()
        {
            var resultado = await Controlador("Administración").PutFacturaElectronica(new ClienteFacturaElectronicaDTO
            {
                Cliente = "33895", Contacto = "0", OficinaContable = "L0128", OrganoGestor = "L01280796"
            }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "El código DIR3 de la oficina contable «L0128» no es válido");
            StringAssert.Contains(resultado.Message, "Falta el código DIR3 de la unidad tramitadora.");
            A.CallTo(() => repositorio.Guardar(A<FilaClienteFacturaElectronica>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutFacturaElectronica_SinTabla_503()
        {
            A.CallTo(() => repositorio.Guardar(A<FilaClienteFacturaElectronica>._, A<string>._)).Returns(ResultadoEscrituraFacturaElectronica.SinTabla);

            var resultado = await Controlador("Administración").PutFacturaElectronica(new ClienteFacturaElectronicaDTO
            {
                Cliente = "33895", Contacto = "0", OficinaContable = "L01280796", OrganoGestor = "L01280796", UnidadTramitadora = "LA0007357"
            }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
        }

        [TestMethod]
        public async Task PutFacturaElectronica_Valido_GuardaNormalizado()
        {
            FilaClienteFacturaElectronica guardada = null;
            A.CallTo(() => repositorio.Guardar(A<FilaClienteFacturaElectronica>._, A<string>._))
                .Invokes((FilaClienteFacturaElectronica f, string u) => guardada = f)
                .Returns(ResultadoEscrituraFacturaElectronica.Creado);

            var resultado = await Controlador("Dirección").PutFacturaElectronica(new ClienteFacturaElectronicaDTO
            {
                Cliente = " 33895 ", Contacto = "0", OficinaContable = "l0128 0796", OrganoGestor = "L01280796", UnidadTramitadora = "LA0007357",
                NombreUnidadTramitadora = "  Compras  "
            }) as OkNegotiatedContentResult<ClienteFacturaElectronicaDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Content.Guardado);
            Assert.AreEqual("1", guardada.Empresa);
            Assert.AreEqual("33895", guardada.Cliente);
            Assert.AreEqual("L01280796", guardada.OficinaContable);
            Assert.AreEqual("Compras", guardada.NombreUnidadTramitadora);
            Assert.IsNull(guardada.OrganoProponente);
        }

        [TestMethod]
        public async Task PutFacturaElectronica_SinCodigos_LosQuita()
        {
            FilaClienteFacturaElectronica guardada = null;
            A.CallTo(() => repositorio.Guardar(A<FilaClienteFacturaElectronica>._, A<string>._))
                .Invokes((FilaClienteFacturaElectronica f, string u) => guardada = f)
                .Returns(ResultadoEscrituraFacturaElectronica.Borrado);

            var resultado = await Controlador("Administración").PutFacturaElectronica(new ClienteFacturaElectronicaDTO { Cliente = "33895", Contacto = "0" })
                as OkNegotiatedContentResult<ClienteFacturaElectronicaDTO>;

            Assert.IsFalse(resultado.Content.Guardado);
            Assert.IsNull(guardada.OficinaContable);
        }

        [TestMethod]
        public void Elegir_SinLaDelContactoYUnaSola_UsaEsa_ConVariasNinguna()
        {
            var una = new List<FilaClienteFacturaElectronica> { new FilaClienteFacturaElectronica { Cliente = "33895", Contacto = "1  ", OficinaContable = "L01280796" } };
            Assert.AreEqual("1", RepositorioClientesFacturaElectronica.Elegir(una, true, "0").Fila.Contacto);

            var dos = new List<FilaClienteFacturaElectronica>
            {
                new FilaClienteFacturaElectronica { Cliente = "33895", Contacto = "1" },
                new FilaClienteFacturaElectronica { Cliente = "33895", Contacto = "2" }
            };
            Assert.IsNull(RepositorioClientesFacturaElectronica.Elegir(dos, true, "0").Fila);
            Assert.AreEqual("2", RepositorioClientesFacturaElectronica.Elegir(dos, true, "2").Fila.Contacto);
        }
    }
}
