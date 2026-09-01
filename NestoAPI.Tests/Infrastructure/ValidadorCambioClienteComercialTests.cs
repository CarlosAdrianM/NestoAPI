using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using System.Collections.Generic;
using System.Security.Claims;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#458: la regla de quién puede cambiar el estado y el vendedor de un cliente. Los
    /// casos son la tabla de la issue, con el equipo real de ejemplo: ASH tiene a JE, DV y JGP
    /// (y el jefe cuenta en su propio equipo, como lo devuelve VendedoresEquipo).
    /// </summary>
    [TestClass]
    public class ValidadorCambioClienteComercialTests
    {
        /// <summary>ASH, jefe con equipo, quiere tocar un cliente. Cada test ajusta lo suyo.</summary>
        private static DatosPermisoClienteComercial JefeAsh()
        {
            return new DatosPermisoClienteComercial
            {
                EsOficina = false,
                VendedorDelUsuario = "ASH",
                EquipoDelUsuario = new List<string> { "JE", "DV", "JGP", "ASH" },
                VendedorActual = "JE ",
                VendedorDestino = null,
                CambiaEstado = false
            };
        }

        [TestMethod]
        public void Evaluar_LaTablaDeLaIssue()
        {
            var casos = new[]
            {
                (actual: "JE", destino: "JGP", permitido: true),   // los dos de su equipo
                (actual: "JE", destino: "ASH", permitido: true),   // el jefe cuenta como destino
                (actual: "NV", destino: "JE", permitido: true),    // el genérico como origen
                (actual: "DV", destino: "NV", permitido: true),    // el genérico como destino
                (actual: "MPP", destino: "JE", permitido: false),  // MPP no es suyo: ni tocarlo
                (actual: "JE", destino: "MPP", permitido: false)   // el destino se sale del equipo
            };

            foreach ((string actual, string destino, bool permitido) in casos)
            {
                DatosPermisoClienteComercial datos = JefeAsh();
                datos.VendedorActual = actual;
                datos.VendedorDestino = destino;

                ResultadoPermisoClienteComercial resultado = ValidadorCambioClienteComercial.Evaluar(datos);

                Assert.AreEqual(permitido, resultado.Permitido, $"{actual} -> {destino}");
            }
        }

        [TestMethod]
        public void Evaluar_SoloElEstadoDeUnClienteDeFuera_TampocoSePuede()
        {
            // Lo importante del último par de la issue: no es solo que no pueda ASIGNAR fuera,
            // es que un cliente que no es suyo no lo toca en absoluto — tampoco el estado.
            DatosPermisoClienteComercial datos = JefeAsh();
            datos.VendedorActual = "MPP";
            datos.CambiaEstado = true;

            ResultadoPermisoClienteComercial resultado = ValidadorCambioClienteComercial.Evaluar(datos);

            Assert.IsFalse(resultado.Permitido);
            StringAssert.Contains(resultado.Motivo, "MPP");
        }

        [TestMethod]
        public void Evaluar_ElEstadoDeUnClienteDelEquipo_SePuede()
        {
            DatosPermisoClienteComercial datos = JefeAsh();
            datos.VendedorActual = "DV";
            datos.CambiaEstado = true;

            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(datos).Permitido);
        }

        [TestMethod]
        public void Evaluar_GrupoDeProductoDeFuera_NoSeToca()
        {
            // Decisión 2 de la issue: el PUT también toca el vendedor por grupo de producto, y la
            // regla lo cubre igual — si CUALQUIER vendedor del cliente es de fuera, no se toca.
            DatosPermisoClienteComercial datos = JefeAsh();
            datos.VendedorActual = "JE";
            datos.VendedorDestino = "JGP";
            datos.VendedorGrupoActual = "MPP";

            ResultadoPermisoClienteComercial resultado = ValidadorCambioClienteComercial.Evaluar(datos);

            Assert.IsFalse(resultado.Permitido);
            StringAssert.Contains(resultado.Motivo, "grupo de producto");
        }

        [TestMethod]
        public void Evaluar_AsignarElGrupoFueraDelEquipo_TampocoSePuede()
        {
            DatosPermisoClienteComercial datos = JefeAsh();
            datos.VendedorActual = "JE";
            datos.VendedorGrupoActual = "DV";
            datos.VendedorGrupoDestino = "MPP";

            Assert.IsFalse(ValidadorCambioClienteComercial.Evaluar(datos).Permitido);
        }

        [TestMethod]
        public void Evaluar_OficinaYUsuariosSinVendedor_SinRestriccion()
        {
            DatosPermisoClienteComercial oficina = JefeAsh();
            oficina.EsOficina = true;
            oficina.VendedorActual = "MPP";
            oficina.VendedorDestino = "JE";
            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(oficina).Permitido,
                "Administración sigue como siempre");

            DatosPermisoClienteComercial sinVendedor = JefeAsh();
            sinVendedor.VendedorDelUsuario = null;
            sinVendedor.VendedorActual = "MPP";
            sinVendedor.VendedorDestino = "JE";
            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(sinVendedor).Permitido,
                "Un usuario de oficina sin vendedor asociado no se restringe");
        }

        [TestMethod]
        public void Evaluar_SiNadaCambia_SePermiteAunqueElClienteSeaDeFuera()
        {
            // Un PUT que no cambia vendedor ni estado no debe fallar por permisos: no toca nada.
            DatosPermisoClienteComercial datos = JefeAsh();
            datos.VendedorActual = "MPP";
            datos.VendedorDestino = "MPP";

            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(datos).Permitido);
        }

        [TestMethod]
        public void Evaluar_VendedorSinEquipo_SoloSusClientesYElGenerico()
        {
            DatosPermisoClienteComercial raso = new DatosPermisoClienteComercial
            {
                VendedorDelUsuario = "JE",
                EquipoDelUsuario = new List<string> { "JE" },
                VendedorActual = "JE",
                VendedorDestino = "NV"
            };
            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(raso).Permitido,
                "Su propio cliente al genérico, sí");

            raso.VendedorDestino = "DV";
            Assert.IsFalse(ValidadorCambioClienteComercial.Evaluar(raso).Permitido,
                "A otro vendedor, no: DV no es de su equipo");
        }

        [TestMethod]
        public void Evaluar_ElPaddingDeLosCharNoEngaña()
        {
            // Memoria de la migración EF→API: "JE" y "JE " son el MISMO vendedor.
            DatosPermisoClienteComercial datos = JefeAsh();
            datos.VendedorActual = "JE ";
            datos.VendedorDestino = "jgp  ";
            datos.EquipoDelUsuario = new List<string> { "JE ", "DV", "JGP  ", "ASH" };

            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(datos).Permitido);
        }

        // ----- Resolución de identidad -----

        [TestMethod]
        public void EsUsuarioDeOficina_GrupoDeAdministracionConDominio_True()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "NUEVAVISION\\Aida"),
                new Claim(ClaimTypes.Role, "NUEVAVISION\\Administración")
            }, "JWT");

            Assert.IsTrue(ValidadorCambioClienteComercial.EsUsuarioDeOficina(new ClaimsPrincipal(identity)));
        }

        [TestMethod]
        public void EsUsuarioDeOficina_VendedorSinGruposDeOficina_False()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "NUEVAVISION\\Ash"),
                new Claim(ClaimTypes.Role, "NUEVAVISION\\Usuarios del dominio")
            }, "JWT");

            Assert.IsFalse(ValidadorCambioClienteComercial.EsUsuarioDeOficina(new ClaimsPrincipal(identity)));
        }

        [TestMethod]
        public void VendedorDelUsuario_ConClaimDeNestoApp_NoVaALaBaseDeDatos()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "anaisdelhoyo"),
                new Claim("Vendedor", "AH")
            }, "JWT");

            string vendedor = ValidadorCambioClienteComercial.VendedorDelUsuario(
                new ClaimsPrincipal(identity), servicio: null);

            Assert.AreEqual("AH", vendedor);
        }
    }
}
