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
    /// NestoAPI#552 (fase 5, decisión de Carlos del 27/10/26): con -33 («ya existe el código de barras») el envío se da por
    /// tramitado, pero si sus datos cambiaron desde que se grabó en GLS se avisa para que Almacén lo revise con GLS.
    /// </summary>
    [TestClass]
    public class CambiosGrabacionGlsTests
    {
        private static XElement Servicios(Action<EnviosAgencia> cambiar = null, DateTime? fecha = null, Empresa empresa = null)
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio();
            cambiar?.Invoke(envio);
            return PeticionGls.ConstruirServicios(envio, empresa ?? TramitadorGlsTests.EmpresaNuevaVision(), "9ABAEE33", fecha ?? new DateTime(2026, 10, 20));
        }

        private static string Soap(XElement servicios) => PeticionGls.ConstruirSoap(servicios);

        [TestMethod]
        public void Detectar_MismosDatos_OtraFecha_NoHayCambios()
        {
            List<CambioGrabacionGls> cambios = CambiosGrabacionGls.Detectar(Servicios(), Servicios(fecha: new DateTime(2026, 10, 27)));

            Assert.AreEqual(0, cambios.Count, "La fecha de tramitación siempre cambia: no cuenta");
        }

        [TestMethod]
        public void Detectar_OtroRemitente_NoCuenta()
        {
            Empresa otra = TramitadorGlsTests.EmpresaNuevaVision();
            otra.Teléfono = "910000001";

            Assert.AreEqual(0, CambiosGrabacionGls.Detectar(Servicios(), Servicios(empresa: otra)).Count);
        }

        [TestMethod]
        public void Detectar_DireccionBultosYReembolso_SalenConSuNombreYLoDeAntesYAhora()
        {
            XElement grabado = Servicios();
            XElement actual = Servicios(e => { e.Direccion = "C/ NUEVA, 2"; e.Bultos = 3; e.Reembolso = 40.25M; });

            List<CambioGrabacionGls> cambios = CambiosGrabacionGls.Detectar(grabado, actual);

            Assert.AreEqual(3, cambios.Count);
            CambioGrabacionGls direccion = cambios.Single(c => c.Nombre == "Dirección");
            Assert.AreEqual("C/ INVENTADA, 1", direccion.Antes);
            Assert.AreEqual("C/ NUEVA, 2", direccion.Ahora);
            Assert.AreEqual("Bultos: «2» → «3»", cambios.Single(c => c.Campo == "Envio/Bultos").ToString());
            Assert.AreEqual("Reembolso: «0» → «40.25»", cambios.Single(c => c.Campo == "Envio/Importes/Reembolso").ToString());
        }

        [TestMethod]
        public void Detectar_ReembolsoConOtrosDecimales_NoEsUnCambio()
        {
            XElement grabado = Servicios(e => e.Reembolso = 25.5M);
            XElement actual = Servicios(e => e.Reembolso = 25.50M);

            Assert.AreEqual(0, CambiosGrabacionGls.Detectar(grabado, actual).Count);
        }

        [TestMethod]
        public void Detectar_RellenoDeLosChar_NoEsUnCambio()
        {
            XElement grabado = Servicios(e => e.CodPostal = "28043     ");
            XElement actual = Servicios(e => e.CodPostal = "28043");

            Assert.AreEqual(0, CambiosGrabacionGls.Detectar(grabado, actual).Count);
        }

        [TestMethod]
        public void Detectar_ConLaPeticionGuardadaPorNesto_ConSangria()
        {
            // Nesto guarda el XML con sangría (literal XML de VB): los nodos con hijos llevan espacios, que no son datos.
            XElement grabado = PeticionGls.ServiciosDeLaPeticion(XDocument.Parse(Soap(Servicios())).ToString());
            XElement actual = PeticionGls.ServiciosDeLaPeticion(Soap(Servicios(e => e.CodPostal = "28044")));

            List<CambioGrabacionGls> cambios = CambiosGrabacionGls.Detectar(grabado, actual);

            Assert.AreEqual("Código postal: «28043» → «28044»", cambios.Single().ToString());
        }

        [TestMethod]
        public void Aviso_SinCambios_Null_ConCambios_DeUsted()
        {
            Assert.IsNull(CambiosGrabacionGls.Aviso(928379, new List<CambioGrabacionGls>()));

            string aviso = CambiosGrabacionGls.Aviso(928379, CambiosGrabacionGls.Detectar(Servicios(), Servicios(e => e.Bultos = 1)));

            Assert.AreEqual("GLS ya tenía grabado el envío del pedido 928379, pero con otros datos: Bultos: «2» → «1». " +
                "Se ha dado por tramitado: revíselo con GLS para que lo entreguen con los datos de ahora.", aviso);
        }

        [TestMethod]
        public void GrabacionAceptada_LaUltimaQueGlsAcepto_SinContarLas33NiOtrosCodigos()
        {
            string codigo = "61197140249723";
            XElement aceptada = Servicios(e => e.Bultos = 2);
            XElement rechazada33 = Servicios(e => e.Bultos = 5);
            EnviosAgencia otro = TramitadorGlsTests.Envio();
            otro.CodigoBarras = "61197140249999";
            var llamadas = new List<(string, string)>
            {
                // Las más recientes primero, como las devuelve la consulta.
                (Soap(PeticionGls.ConstruirServicios(otro, TramitadorGlsTests.EmpresaNuevaVision(), "9ABAEE33")), TramitadorGlsTests.RESPUESTA_OK),
                (Soap(rechazada33), TramitadorGlsTests.Respuesta("-33")),
                (Soap(aceptada), TramitadorGlsTests.RESPUESTA_OK),
            };

            XElement elegida = CambiosGrabacionGls.GrabacionAceptada(llamadas, codigo + "  ");

            Assert.IsNotNull(elegida);
            Assert.AreEqual("2", elegida.Descendants(PeticionGls.Ns + "Bultos").Single().Value);
        }

        [TestMethod]
        public void GrabacionAceptada_SinNingunaAceptada_Null()
        {
            var llamadas = new List<(string, string)> { (Soap(Servicios()), TramitadorGlsTests.Respuesta("-33")) };

            Assert.IsNull(CambiosGrabacionGls.GrabacionAceptada(llamadas, "61197140249723"));
            Assert.IsNull(CambiosGrabacionGls.GrabacionAceptada(llamadas, null));
        }

        [DataTestMethod]
        [DataRow(1, true)]
        [DataRow(5, true)]
        [DataRow(13, false)]
        [DataRow(12, false)]
        public void AgenciasGls_LaUnoYLaDeLaEmpresaEspejo(int agencia, bool esperado)
        {
            Assert.AreEqual(esperado, AgenciasGls.Es(agencia));
        }
    }
}
