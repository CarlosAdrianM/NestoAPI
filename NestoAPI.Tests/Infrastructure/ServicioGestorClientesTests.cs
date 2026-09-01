using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
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

        // ----- NestoAPI#438: copiar personas de contacto y CCC del principal a otro contacto -----

        private static readonly DateTime AHORA = new DateTime(2026, 9, 1, 17, 0, 0);

        private static Cliente PrincipalConDatos()
        {
            return new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "15191     ",
                Contacto = "0  ",
                ClientePrincipal = true,
                CCC = "1  ",
                PersonasContactoClientes = new List<PersonaContactoCliente>
                {
                    new PersonaContactoCliente { Número = "1", Nombre = "LA DUEÑA", Cargo = 22,
                        CorreoElectrónico = "facturas@peluqueria.com", Teléfono = "911111111", EnviarBoletin = true },
                    new PersonaContactoCliente { Número = "2", Nombre = "LA ENCARGADA", Cargo = 1,
                        CorreoElectrónico = "cobros@peluqueria.com" }
                },
                CCCs = new List<CCC>
                {
                    new CCC { Número = "1", Pais = "ES", DC_IBAN = "21", Entidad = "0049", Oficina = "2605",
                        DC = "95", Nº_Cuenta = "1234567890", Estado = 1, Secuencia = "RCUR" }
                }
            };
        }

        private static Cliente DestinoVacio()
        {
            return new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "15191     ",
                Contacto = "1  ",
                ClientePrincipal = false,
                PersonasContactoClientes = new List<PersonaContactoCliente>(),
                CCCs = new List<CCC>()
            };
        }

        [TestMethod]
        public void PrepararCopiaDelPrincipal_DestinoVacio_CopiaTodoYAsignaElCccDeLaFicha()
        {
            var resultado = ServicioGestorClientes.PrepararCopiaDelPrincipal(
                PrincipalConDatos(), DestinoVacio(), "NUEVAVISION\\Vendedor", AHORA);

            Assert.IsNull(resultado.Error);
            Assert.AreEqual(2, resultado.PersonasCopiadas);
            Assert.AreEqual(1, resultado.CccsCopiados);
            PersonaContactoCliente facturas = resultado.NuevasPersonas.Single(p => p.Cargo == 22);
            Assert.AreEqual("facturas@peluqueria.com", facturas.CorreoElectrónico);
            Assert.AreEqual("1  ", facturas.Contacto, "La copia es para el contacto de DESTINO");
            Assert.AreEqual("NUEVAVISION\\Vendedor", facturas.Usuario);
            CCC cuenta = resultado.NuevosCccs.Single();
            Assert.AreEqual("1234567890", cuenta.Nº_Cuenta);
            Assert.AreEqual("RCUR", cuenta.Secuencia, "El mandato viaja tal cual: el deudor es el mismo cliente");
            Assert.AreEqual("1", resultado.CccAsignado, "La ficha del destino apunta al equivalente del predeterminado");
        }

        [TestMethod]
        public void PrepararCopiaDelPrincipal_LoQueYaTiene_NoSeDuplica()
        {
            Cliente destino = DestinoVacio();
            destino.PersonasContactoClientes.Add(new PersonaContactoCliente
            {
                Número = "1", Nombre = "la dueña ", Cargo = 22, CorreoElectrónico = "FACTURAS@peluqueria.com "
            });
            destino.CCCs.Add(new CCC
            {
                Número = "7", Pais = "ES ", DC_IBAN = "21", Entidad = "0049 ", Oficina = "2605",
                DC = "95", Nº_Cuenta = "1234567890"
            });

            var resultado = ServicioGestorClientes.PrepararCopiaDelPrincipal(
                PrincipalConDatos(), destino, "u", AHORA);

            Assert.AreEqual(1, resultado.PersonasCopiadas, "Solo la persona que faltaba (cobros)");
            Assert.AreEqual(0, resultado.CccsCopiados, "La cuenta ya estaba: mismos dígitos");
            Assert.AreEqual("7", resultado.CccAsignado, "La ficha apunta a la cuenta EXISTENTE equivalente");
        }

        [TestMethod]
        public void PrepararCopiaDelPrincipal_LaNumeracionSigueLaDelDestino()
        {
            Cliente destino = DestinoVacio();
            destino.PersonasContactoClientes.Add(new PersonaContactoCliente
            {
                Número = "3", Nombre = "OTRA PERSONA", Cargo = 14, CorreoElectrónico = "otra@x.com"
            });

            var resultado = ServicioGestorClientes.PrepararCopiaDelPrincipal(
                PrincipalConDatos(), destino, "u", AHORA);

            Assert.IsTrue(resultado.NuevasPersonas.All(p => int.Parse(p.Número) >= 4),
                "Los números nuevos siguen a los que ya hay, sin pisar ninguno");
        }

        [TestMethod]
        public void PrepararCopiaDelPrincipal_DestinoConCccEnFicha_NoSeLePisa()
        {
            Cliente destino = DestinoVacio();
            destino.CCC = "2  ";

            var resultado = ServicioGestorClientes.PrepararCopiaDelPrincipal(
                PrincipalConDatos(), destino, "u", AHORA);

            Assert.IsNull(resultado.CccAsignado, "Lo que alguien eligió en la ficha no se toca");
        }

        [TestMethod]
        public void PrepararCopiaDelPrincipal_SinPrincipalODestinoInvalido_DevuelveError()
        {
            Assert.IsNotNull(ServicioGestorClientes.PrepararCopiaDelPrincipal(null, DestinoVacio(), "u", AHORA).Error);
            Assert.IsNotNull(ServicioGestorClientes.PrepararCopiaDelPrincipal(PrincipalConDatos(), null, "u", AHORA).Error);
            Assert.IsNotNull(ServicioGestorClientes.PrepararCopiaDelPrincipal(PrincipalConDatos(), PrincipalConDatos(), "u", AHORA).Error,
                "Copiar el principal sobre sí mismo no tiene sentido");
        }
    }
}
