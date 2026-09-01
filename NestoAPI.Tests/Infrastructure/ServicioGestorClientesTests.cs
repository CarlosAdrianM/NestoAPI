using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

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
        // ----- Issue #285: NormalizarNif con NIF de solo guiones/espacios -----

        [TestMethod]
        public void NormalizarNif_SoloGuionesOEspacios_DevuelveVacioSinLanzar()
        {
            // Regresión #285: un NIF almacenado como "-" (datos legacy sucios) quedaba en cadena
            // vacía tras la limpieza y nif.First() lanzaba 'Sequence contains no elements',
            // rompiendo el login por email+NIF de TiendasNuevaVision para cualquier email cuyo
            // recorrido pasara por ese cliente.
            Assert.AreEqual(string.Empty, ServicioGestorClientes.NormalizarNif("-"));
            Assert.AreEqual(string.Empty, ServicioGestorClientes.NormalizarNif(" - - "));
        }

        [TestMethod]
        public void NormalizarNif_NifNormal_QuitaGuionesEspaciosYCerosIniciales()
        {
            Assert.AreEqual("B12345678", ServicioGestorClientes.NormalizarNif("b-1234 5678"));
            Assert.AreEqual("123456X", ServicioGestorClientes.NormalizarNif("00123456-X"));
        }

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

        // ----- NestoAPI#428 (punto 5) y #429 (punto 3): el DTO del login de la tienda -----

        private static Cliente FichaCompleta()
        {
            return new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "15191     ",
                Contacto = "0  ",
                ClientePrincipal = true,
                CIF_NIF = "B12345678",
                Nombre = "PELUQUERIA EJEMPLO ",
                Dirección = "CALLE MAYOR 1",
                Teléfono = "911234567",
                Población = "MADRID",
                CodPostal = "28001",
                Provincia = "MADRID",
                Estado = 0,
                PersonasContactoClientes = new List<PersonaContactoCliente>
                {
                    new PersonaContactoCliente
                    {
                        Nombre = "PERSONA QUE SE LOGUEA",
                        CorreoElectrónico = "duena@peluqueria.com ",
                        Cargo = Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                    },
                    new PersonaContactoCliente
                    {
                        Nombre = "OTRA PERSONA (TERCERO)",
                        CorreoElectrónico = "empleada@otrocorreo.com",
                        Cargo = 1
                    }
                }
            };
        }

        [TestMethod]
        public void MapearClienteParaLogin_SoloViajaLaPersonaDelEmailConsultado()
        {
            // Antes viajaban nombre y correo de TODAS las personas de contacto del cliente por un
            // endpoint anónimo: datos personales de terceros. Ahora solo la del email que el
            // llamante ya conoce, con su flag de facturación electrónica (PermitirVerFacturas).
            var dto = ServicioGestorClientes.MapearClienteParaLogin(FichaCompleta(), "Duena@peluqueria.com");

            PersonaContactoDTO persona = dto.PersonasContacto.Single();
            Assert.AreEqual("duena@peluqueria.com", persona.CorreoElectronico);
            Assert.IsTrue(persona.FacturacionElectronica);
            Assert.IsNull(persona.Nombre, "El nombre de la persona no debe viajar");
        }

        [TestMethod]
        public void MapearClienteParaLogin_NoViajanVendedorNiEmpresa()
        {
            var dto = ServicioGestorClientes.MapearClienteParaLogin(FichaCompleta(), "duena@peluqueria.com");

            Assert.IsNull(dto.vendedor, "El nombre del vendedor es dato de un empleado: fuera del endpoint anónimo");
            Assert.IsNull(dto.empresa);
            Assert.AreEqual("15191", dto.cliente);
            Assert.AreEqual("PELUQUERIA EJEMPLO", dto.nombre);
        }

        [TestMethod]
        public void MapearClienteParaLogin_FichaConNulos_NoRevienta()
        {
            // Regresión #429 (punto 3): Nombre, Dirección o Vendedore a null (datos legítimos pero
            // incompletos) reventaban con NullReferenceException el login de la tienda.
            var cliente = new Cliente { Nº_Cliente = "15191", Estado = 0 };

            var dto = ServicioGestorClientes.MapearClienteParaLogin(cliente, "duena@peluqueria.com");

            Assert.AreEqual("15191", dto.cliente);
            Assert.IsNull(dto.nombre);
            Assert.AreEqual(0, dto.PersonasContacto.Count);
        }

        [TestMethod]
        public void MapearClienteParaLogin_SinCliente_DevuelveDtoVacio()
        {
            var dto = ServicioGestorClientes.MapearClienteParaLogin(null, "duena@peluqueria.com");

            Assert.IsNull(dto.cliente);
        }

        [TestMethod]
        public void ElegirFichaParaLogin_ConVariasFichas_GanaLaPrincipal()
        {
            // Regresión #429 (punto 3): antes FirstOrDefault sin OrderBy — con varias fichas del
            // mismo email+NIF elegía SQL Server, de forma no determinista.
            var secundaria = new Cliente { Nº_Cliente = "15191", Contacto = "1", ClientePrincipal = false, CIF_NIF = "B12345678" };
            var principal = new Cliente { Nº_Cliente = "15191", Contacto = "0", ClientePrincipal = true, CIF_NIF = "B12345678" };

            var elegida = ServicioGestorClientes.ElegirFichaParaLogin(
                new List<Cliente> { secundaria, principal }, "B12345678");

            Assert.AreSame(principal, elegida);
        }

        [TestMethod]
        public void ElegirFichaParaLogin_NifQueNoCoincide_DevuelveNull()
        {
            var ficha = new Cliente { Nº_Cliente = "15191", Contacto = "0", ClientePrincipal = true, CIF_NIF = "B12345678" };

            var elegida = ServicioGestorClientes.ElegirFichaParaLogin(
                new List<Cliente> { ficha }, "OTRO");

            Assert.IsNull(elegida);
        }
    }
}
