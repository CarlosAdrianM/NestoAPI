using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#552 (fase 1, sombra): la API construye la petición GrabaServicios de GLS igual que
    /// AgenciaASM.construirXMLdeSalida de Nesto, sin enviarla. La referencia es una petición REAL guardada en
    /// AgenciasLlamadasWeb (01/10/26), con el destinatario cambiado por datos inventados.
    /// </summary>
    [TestClass]
    public class PeticionGlsTests
    {
        // Tal cual la genera Nesto (incluido el comentario de Referencias y el formato de fecha dd/MM/yy).
        private const string PETICION_NESTO = @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><GrabaServicios xmlns=""http://www.asmred.com/""><docIn><Servicios xmlns=""http://www.asmred.com/"" uidcliente=""6fb665f2-15a2-4478-9804-c1556fc1f272"">
  <Envio codbarras=""61197140249365"">
    <Fecha>01/10/26</Fecha>
    <Portes>P</Portes>
    <Servicio>96</Servicio>
    <Horario>18</Horario>
    <Bultos>2</Bultos>
    <Peso>1</Peso>
    <Retorno>0</Retorno>
    <Pod>N</Pod>
    <Remite>
      <Plaza></Plaza>
      <Nombre>Nueva Visión, S.A.</Nombre>
      <Direccion>C/ Río Tiétar, 11 - Nave 22</Direccion>
      <Poblacion>ALGETE</Poblacion>
      <Provincia>Madrid</Provincia>
      <Pais>34</Pais>
      <CP>28119</CP>
      <Telefono>916281914</Telefono>
      <Movil></Movil>
      <Email>nuevavision@nuevavision.es</Email>
      <Observaciones></Observaciones>
    </Remite>
    <Destinatario>
      <Codigo></Codigo>
      <Plaza></Plaza>
      <Nombre>CENTRO DE ESTÉTICA PRUEBA SL</Nombre>
      <Direccion>C/ INVENTADA, 1, BAJO</Direccion>
      <Poblacion>MADRID</Poblacion>
      <Provincia>MADRID</Provincia>
      <Pais>34</Pais>
      <CP>28043</CP>
      <Telefono>910000000</Telefono>
      <Movil>600000000</Movil>
      <Email>prueba@example.com</Email>
      <Observaciones></Observaciones>
      <ATT>CENTRO DE ESTÉTICA PRUEBA SL</ATT>
    </Destinatario>
    <Referencias>
      <!-- cualquier numero, siempre distinto a cada prueba-->
      <Referencia tipo=""C"">37653/927525</Referencia>
    </Referencias>
    <Importes>
      <Debidos>0</Debidos>
      <Reembolso>0.0000</Reembolso>
    </Importes>
    <Seguro tipo="""">
      <Descripcion></Descripcion>
      <Importe></Importe>
    </Seguro>
    <DevuelveAdicionales>
      <PlazaDestino />
    </DevuelveAdicionales>
  </Envio>
</Servicios></docIn></GrabaServicios></soap:Body></soap:Envelope>";

        private static EnviosAgencia Envio() => new EnviosAgencia
        {
            Numero = 249365,
            Empresa = "1  ",
            Agencia = 1,
            Cliente = "37653     ",
            Pedido = 927525,
            Fecha = new DateTime(2026, 10, 1, 13, 4, 0),
            Servicio = 96,
            Horario = 18,
            Bultos = 2,
            Retorno = 0,
            Nombre = "CENTRO DE ESTÉTICA PRUEBA SL",
            Direccion = "C/ INVENTADA, 1, BAJO",
            Poblacion = "MADRID",
            Provincia = "MADRID",
            Pais = 34,
            CodPostal = "28043",
            Telefono = "910000000",
            Movil = "600000000",
            Email = "prueba@example.com",
            Observaciones = "",
            Atencion = "CENTRO DE ESTÉTICA PRUEBA SL",
            Reembolso = 0.0000M,
            CodigoBarras = "61197140249365"
        };

        private static Empresa EmpresaNuevaVision() => new Empresa
        {
            Número = "1  ",
            Nombre = "Nueva Visión, S.A.            ",
            Dirección = "C/ Río Tiétar, 11 - Nave 22                       ",
            Población = "ALGETE                        ",
            Provincia = "Madrid                        ",
            CodPostal = "28119          ",
            Teléfono = "916281914                    ",
            Email = "nuevavision@nuevavision.es                        "
        };

        private static XElement Construir(EnviosAgencia envio, Empresa empresa = null, string identificadorAgencia = "9ABAEE33-4D94-4D58-A7C5-902E824313C9")
            => PeticionGls.ConstruirServicios(envio, empresa ?? EmpresaNuevaVision(), identificadorAgencia);

        [TestMethod]
        public void ConstruirServicios_EnvioReal_EsIgualQueLaDeNesto()
        {
            XElement nesto = PeticionGls.ServiciosDeLaPeticion(PETICION_NESTO);

            List<DiferenciaPeticionGls> diferencias = ComparadorPeticionesGls.Comparar(nesto, Construir(Envio()));

            Assert.AreEqual(0, diferencias.Count, string.Join(Environment.NewLine, diferencias.Select(d => d.ToString())));
        }

        [TestMethod]
        public void ConstruirServicios_FechaComoLaMandaNesto_DiaMesAnoDeDosCifras()
        {
            XElement api = Construir(Envio());

            Assert.AreEqual("01/10/26", api.Descendants(PeticionGls.Ns + "Fecha").Single().Value);
        }

        [TestMethod]
        public void ConstruirServicios_BusinessParcel_UsaSuIdentificadorYNoElDeLaAgencia()
        {
            XElement api = Construir(Envio(), identificadorAgencia: "OTRO");

            Assert.AreEqual(PeticionGls.IDENTIFICADOR_BUSINESSPARCEL, api.Attribute("uidcliente").Value);
        }

        [TestMethod]
        public void ConstruirServicios_OtroServicio_UsaElIdentificadorDeLaAgencia()
        {
            EnviosAgencia envio = Envio();
            envio.Servicio = 1;

            XElement api = Construir(envio, identificadorAgencia: "9ABAEE33-4D94-4D58-A7C5-902E824313C9");

            Assert.AreEqual("9ABAEE33-4D94-4D58-A7C5-902E824313C9", api.Attribute("uidcliente").Value);
        }

        [TestMethod]
        public void ConstruirServicios_EmpresaEspejo_MandaComoRemitenteANuevaVisionEnAlgete()
        {
            Empresa espejo = new Empresa { Número = "3  ", Nombre = "Global                        " };

            XElement remite = Construir(Envio(), espejo).Descendants(PeticionGls.Ns + "Remite").Single();

            Assert.AreEqual("Nueva Visión", remite.Element(PeticionGls.Ns + "Nombre").Value);
            Assert.AreEqual("c/ Río Tiétar, 11", remite.Element(PeticionGls.Ns + "Direccion").Value);
            Assert.AreEqual("Algete", remite.Element(PeticionGls.Ns + "Poblacion").Value);
            Assert.AreEqual("28119", remite.Element(PeticionGls.Ns + "CP").Value);
            Assert.AreEqual("logistica@nuevavision.es", remite.Element(PeticionGls.Ns + "Email").Value);
        }

        [TestMethod]
        public void ConstruirSoap_EnvuelveLosServiciosEnGrabaServicios()
        {
            string soap = PeticionGls.ConstruirSoap(Construir(Envio()));

            StringAssert.StartsWith(soap, @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope");
            Assert.AreEqual(0, ComparadorPeticionesGls.Comparar(PeticionGls.ServiciosDeLaPeticion(soap), Construir(Envio())).Count);
        }

        [TestMethod]
        public void ServiciosDeLaPeticion_CuerpoQueNoEsUnaPeticion_DevuelveNull()
        {
            Assert.IsNull(PeticionGls.ServiciosDeLaPeticion("Consultar seguimiento envío 249315 (pedido 927329)"));
            Assert.IsNull(PeticionGls.ServiciosDeLaPeticion(null));
        }

        [TestMethod]
        public void Comparar_SeñalaCadaCampoDistintoConLosDosValores()
        {
            EnviosAgencia cambiado = Envio();
            cambiado.Direccion = "C/ OTRA, 2";
            cambiado.Bultos = 3;

            List<DiferenciaPeticionGls> diferencias = ComparadorPeticionesGls.Comparar(Construir(Envio()), Construir(cambiado));

            CollectionAssert.AreEquivalent(new[] { "Envio/Bultos", "Envio/Destinatario/Direccion" }, diferencias.Select(d => d.Campo).ToList());
            DiferenciaPeticionGls direccion = diferencias.Single(d => d.Campo == "Envio/Destinatario/Direccion");
            Assert.AreEqual("C/ INVENTADA, 1, BAJO", direccion.Nesto);
            Assert.AreEqual("C/ OTRA, 2", direccion.Api);
        }

        [TestMethod]
        public void Comparar_FechaConAnoDeCuatroCifras_EsLaMismaFecha()
        {
            // Algún puesto de Nesto con otra configuración regional manda dd/MM/yyyy (71 de 20.621 peticiones).
            XElement nesto = Construir(Envio());
            nesto.Descendants(PeticionGls.Ns + "Fecha").Single().Value = "01/10/2026";

            Assert.AreEqual(0, ComparadorPeticionesGls.Comparar(nesto, Construir(Envio())).Count);
        }

        [TestMethod]
        public void Comparar_ReembolsoConOtrosDecimales_EsElMismoImporte()
        {
            XElement nesto = Construir(Envio());
            nesto.Descendants(PeticionGls.Ns + "Reembolso").Single().Value = "0";

            Assert.AreEqual(0, ComparadorPeticionesGls.Comparar(nesto, Construir(Envio())).Count);
        }

        [TestMethod]
        public void Comparar_CampoQueSoloEstaEnUnaDeLasDos_EsUnaDiferencia()
        {
            XElement nesto = Construir(Envio());
            nesto.Element(PeticionGls.Ns + "Envio").Add(new XElement(PeticionGls.Ns + "Nuevo", "x"));

            DiferenciaPeticionGls diferencia = ComparadorPeticionesGls.Comparar(nesto, Construir(Envio())).Single();

            Assert.AreEqual("Envio/Nuevo", diferencia.Campo);
            Assert.AreEqual("x", diferencia.Nesto);
            Assert.IsNull(diferencia.Api);
        }
    }
}
