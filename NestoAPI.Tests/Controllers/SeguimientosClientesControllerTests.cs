using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Rapports;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class CopiarSeguimientosRequestTests
    {
        [TestMethod]
        public void CopiarSeguimientosRequest_MismoClienteYContacto_EsInvalido()
        {
            var request = new CopiarSeguimientosRequest
            {
                Empresa = "1",
                ClienteOrigen = "12345",
                ContactoOrigen = "0",
                ClienteDestino = "12345",
                ContactoDestino = "0"
            };

            bool mismoCliente = request.ClienteOrigen.Trim() == request.ClienteDestino.Trim();
            bool mismoContacto = request.ContactoOrigen?.Trim() == request.ContactoDestino?.Trim();

            Assert.IsTrue(mismoCliente && mismoContacto);
        }

        [TestMethod]
        public void CopiarSeguimientosRequest_MismoClienteDistintoContacto_EsValido()
        {
            var request = new CopiarSeguimientosRequest
            {
                Empresa = "1",
                ClienteOrigen = "12345",
                ContactoOrigen = "0",
                ClienteDestino = "12345",
                ContactoDestino = "1"
            };

            bool mismoCliente = request.ClienteOrigen.Trim() == request.ClienteDestino.Trim();
            bool distintoContacto = request.ContactoOrigen?.Trim() != request.ContactoDestino?.Trim();

            Assert.IsTrue(mismoCliente && distintoContacto);
        }

        [TestMethod]
        public void CopiarSeguimientosRequest_DistintoCliente_EsValido()
        {
            var request = new CopiarSeguimientosRequest
            {
                Empresa = "1",
                ClienteOrigen = "12345",
                ClienteDestino = "67890"
            };

            Assert.AreNotEqual(request.ClienteOrigen.Trim(), request.ClienteDestino.Trim());
        }

        [TestMethod]
        public void CopiarSeguimientosRequest_ComentarioSinContacto_FormatoCorrecto()
        {
            string clienteOrigen = "12345     ";
            string contactoOrigen = null;
            string comentarioOriginal = "Visita comercial exitosa";

            string prefijo = string.IsNullOrEmpty(contactoOrigen?.Trim())
                ? $"[Viene del c/ {clienteOrigen.Trim()}]"
                : $"[Viene del c/ {clienteOrigen.Trim()}/{contactoOrigen.Trim()}]";

            string comentarioCopia = $"{prefijo} {comentarioOriginal}";

            Assert.AreEqual("[Viene del c/ 12345] Visita comercial exitosa", comentarioCopia);
        }

        [TestMethod]
        public void CopiarSeguimientosRequest_ComentarioConContacto_FormatoCorrecto()
        {
            string clienteOrigen = "12345     ";
            string contactoOrigen = "2  ";
            string comentarioOriginal = "Visita comercial exitosa";

            string prefijo = string.IsNullOrEmpty(contactoOrigen?.Trim())
                ? $"[Viene del c/ {clienteOrigen.Trim()}]"
                : $"[Viene del c/ {clienteOrigen.Trim()}/{contactoOrigen.Trim()}]";

            string comentarioCopia = $"{prefijo} {comentarioOriginal}";

            Assert.AreEqual("[Viene del c/ 12345/2] Visita comercial exitosa", comentarioCopia);
        }

        [TestMethod]
        public void CopiarSeguimientosRequest_EliminarOrigenPorDefecto_EsFalse()
        {
            var request = new CopiarSeguimientosRequest
            {
                Empresa = "1",
                ClienteOrigen = "12345",
                ClienteDestino = "67890"
            };

            Assert.IsFalse(request.EliminarOrigen);
        }

        [TestMethod]
        public void SqlBorrarMarcasLeido_BorraDeSeguimientosLeidosPorNumeroOrden()
        {
            // Regresión NestoAPI#267: al mover seguimientos (EliminarOrigen=true), las marcas de
            // "leído" (SeguimientosLeidos, FK hacia SeguimientoCliente, sin mapear en el EDMX) deben
            // borrarse ANTES que los padres o el DELETE viola FK_SeguimientosLeidos_SeguimientoCliente.
            string sql = NestoAPI.Controllers.SeguimientosClientesController
                .SqlBorrarMarcasLeidoDeSeguimientos(new[] { 5, 9, 12 });

            Assert.AreEqual("DELETE FROM SeguimientosLeidos WHERE NºOrden IN (5,9,12)", sql);
        }
    }

    [TestClass]
    public class FormatearSeguimientoResumenTests
    {
        // NestoAPI#374: un seguimiento con Tipo o Contacto null tumbaba el resumen diario con NRE

        [TestMethod]
        public void FormatearSeguimientoResumen_TipoYContactoNull_NoRevienta()
        {
            string texto = NestoAPI.Controllers.SeguimientosClientesController.FormatearSeguimientoResumen(
                null, "JE", "12345", null, "Comentario suficientemente largo", false);

            StringAssert.Contains(texto, "Tipo: Desconocido");
            StringAssert.Contains(texto, "Cliente: 12345/");
            StringAssert.Contains(texto, "Terminó en pedido: No");
        }

        [TestMethod]
        public void FormatearSeguimientoResumen_DatosCompletos_FormateaComoSiempre()
        {
            string texto = NestoAPI.Controllers.SeguimientosClientesController.FormatearSeguimientoResumen(
                "V", "JE", "12345", "0", "Visita al cliente para enseñar novedades", true);

            StringAssert.Contains(texto, "Tipo: Visita");
            StringAssert.Contains(texto, "Cliente: 12345/0");
            StringAssert.Contains(texto, "Terminó en pedido: Sí");
        }
    }
}
