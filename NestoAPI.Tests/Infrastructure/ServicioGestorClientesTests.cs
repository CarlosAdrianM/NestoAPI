using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests de la interpretación del resultado que devuelve la AEAT en ComprobarCifNombre
    /// (NestoAPI#166). Cubren la lógica pura de ConstruirRespuestaCifNombre; la llamada
    /// SOAP a Hacienda queda fuera del alcance de tests unitarios.
    /// </summary>
    [TestClass]
    public class ServicioGestorClientesTests
    {
        [TestMethod]
        public void ConstruirRespuestaCifNombre_Identificado_MarcaValidadoSinPrefijo()
        {
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", "ACME SL", "IDENTIFICADO");

            Assert.IsTrue(resp.NifValidado);
            Assert.AreEqual("ACME SL", resp.NombreFormateado);
        }

        [TestMethod]
        public void ConstruirRespuestaCifNombre_NoIdentificadoSimilar_MarcaValidadoSinPrefijo()
        {
            // AEAT devuelve coincidencia parcial del nombre; lo consideramos válido.
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", "ACME SL", "NO IDENTIFICADO-SIMILAR");

            Assert.IsTrue(resp.NifValidado);
            Assert.AreEqual("ACME SL", resp.NombreFormateado);
        }

        [TestMethod]
        public void ConstruirRespuestaCifNombre_IdentificadoBaja_AnadePrefijoDeBaja()
        {
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", "ACME SL", "IDENTIFICADO-BAJA");

            Assert.IsTrue(resp.NifValidado);
            Assert.IsTrue(resp.NombreFormateado.StartsWith("¡EMPRESA DE BAJA!"));
            Assert.IsTrue(resp.NombreFormateado.Contains("ACME SL"));
        }

        [TestMethod]
        public void ConstruirRespuestaCifNombre_IdentificadoRevocado_AnadePrefijoDeRevocado()
        {
            // NestoAPI#166: NIF revocado por AEAT. El cliente debe solicitar
            // rehabilitación; lo avisamos por prefijo del nombre como con BAJA.
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", "ACME SL", "IDENTIFICADO-REVOCADO");

            Assert.IsTrue(resp.NifValidado);
            Assert.IsTrue(resp.NombreFormateado.StartsWith("¡NIF REVOCADO!"));
            Assert.IsTrue(resp.NombreFormateado.Contains("ACME SL"));
        }

        [TestMethod]
        public void ConstruirRespuestaCifNombre_NoIdentificado_NoMarcaValidado()
        {
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", "ACME SL", "NO IDENTIFICADO");

            Assert.IsFalse(resp.NifValidado);
        }

        [TestMethod]
        public void ConstruirRespuestaCifNombre_NombreConPrefijoPasaDe50_Trunca()
        {
            var nombreLargo = "A VERY LONG COMPANY NAME THAT EXCEEDS THE FIFTY CHARS";
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", nombreLargo, "IDENTIFICADO-REVOCADO");

            Assert.AreEqual(50, resp.NombreFormateado.Length);
            Assert.IsTrue(resp.NombreFormateado.StartsWith("¡NIF REVOCADO!"));
        }

        [TestMethod]
        public void ConstruirRespuestaCifNombre_ResultadoCaseInsensitive_DetectaCorrectamente()
        {
            var resp = ServicioGestorClientes.ConstruirRespuestaCifNombre(
                "B12345678", "ACME SL", "identificado-revocado");

            Assert.IsTrue(resp.NifValidado);
            Assert.IsTrue(resp.NombreFormateado.StartsWith("¡NIF REVOCADO!"));
        }
    }
}
