using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#827 (sugerencia 959, Sancho; Carlos 05/11/26): una sola grafía por persona. El informe de rapports partía a
    /// Daniel en dos bloques porque sus rapports llevaban «NUEVAVISION\dlopez» y «NUEVAVISION\Dlopez» (Windows devuelve el
    /// usuario con la caja con la que se tecleó). La grafía canónica: la del usuario de Identity («DLopez», «MariaJose») o,
    /// si no lo hay, inicial en mayúscula y resto en minúscula («Santiago», «Reina»).
    /// </summary>
    [TestClass]
    public class UsuarioAuditoriaHelperUnaGrafiaTests
    {
        /// <summary>Los gemelos de FechasPermitidas, con la caja de su fila (como en producción).</summary>
        private Dictionary<string, string> gemelos;
        /// <summary>Los usuarios de Identity, con su grafía.</summary>
        private Dictionary<string, string> identity;
        private List<string> consultasIdentity;
        private List<Exception> registrados;
        private DateTime ahora;

        [TestInitialize]
        public void Inicializar()
        {
            gemelos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"NUEVAVISION\DLopez"] = @"NUEVAVISION\dlopez",
                [@"NUEVAVISION\Santiago"] = @"NUEVAVISION\santiago",
                [@"NUEVAVISION\Reina"] = @"NUEVAVISION\REINA"
            };
            identity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DLopez"] = "DLopez",
                ["MariaJose"] = "MariaJose",
                ["Reina"] = "Reina",
                ["carlos"] = "carlos" // en minúsculas en Identity: se pone bonito con la regla general
            };
            consultasIdentity = new List<string>();
            registrados = new List<Exception>();
            ahora = new DateTime(2026, 11, 5, 12, 0, 0);
            UsuarioAuditoriaHelper.Configurar(g => gemelos.TryGetValue(g, out string f) ? f : null, registrados.Add, () => ahora);
            UsuarioAuditoriaHelper.ConfigurarGrafia(BuscarEnIdentity);
        }

        [TestCleanup]
        public void Limpiar() => UsuarioAuditoriaHelper.Reiniciar();

        private string BuscarEnIdentity(string nombre)
        {
            consultasIdentity.Add(nombre);
            return identity.TryGetValue(nombre, out string grafia) ? grafia : null;
        }

        // ---- Persona con dominio ----

        [TestMethod]
        public void ConDominio_LaGrafiaDeIdentity()
        {
            Assert.AreEqual(@"NUEVAVISION\DLopez", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\dlopez"));
            Assert.AreEqual(@"NUEVAVISION\DLopez", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\Dlopez"));
            Assert.AreEqual(@"NUEVAVISION\MariaJose", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\Mariajose"));
            Assert.AreEqual(@"NUEVAVISION\Reina", UsuarioAuditoriaHelper.Persona(@"nuevavision\REINA"), "También el dominio");
        }

        [TestMethod]
        public void ConDominio_SinIdentity_InicialMayusculaYRestoMinuscula()
        {
            Assert.AreEqual(@"NUEVAVISION\Santiago", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\santiago"));
            Assert.AreEqual(@"NUEVAVISION\Santiago", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\SANTIAGO"));
            Assert.AreEqual(@"NUEVAVISION\Aida", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\aida"));
            Assert.AreEqual(@"NUEVAVISION\Carlos", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\CARLOS"),
                "La de Identity está en minúsculas: no es la bonita");
        }

        [TestMethod]
        public void ConDominio_SinBuscadorDeIdentity_LaReglaGeneral()
        {
            UsuarioAuditoriaHelper.ConfigurarGrafia(null);

            Assert.AreEqual(@"NUEVAVISION\Dlopez", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\dlopez"));
            Assert.AreEqual(@"NUEVAVISION\Santiago", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\santiago"));
        }

        [TestMethod]
        public void ConDominio_LoQueNoEsUnaPersonaDelDominio_TalCual()
        {
            Assert.AreEqual(@"NUEVAVISION\RDS2016$", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\RDS2016$"), "Cuenta de máquina");
            Assert.AreEqual(@"NUEVAVISION\reina\Laura", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\reina\Laura"), "Dos barras");
            Assert.AreEqual(@"DOMINIO\pilar", UsuarioAuditoriaHelper.Persona(@"DOMINIO\pilar"), "Otro dominio");
            Assert.AreEqual("cliente@ejemplo.com", UsuarioAuditoriaHelper.Persona("cliente@ejemplo.com"));
            Assert.AreEqual(0, consultasIdentity.Count);
        }

        [TestMethod]
        public void ConDominio_YaCanonico_SeDevuelveLoQueVino()
        {
            const string conEspacios = @"NUEVAVISION\DLopez  ";
            Assert.AreSame(conEspacios, UsuarioAuditoriaHelper.Persona(conEspacios), "Como en #823: si no cambia, tal cual");
        }

        // ---- Persona sin dominio (el gemelo de #823) ----

        [TestMethod]
        public void SinDominioConGemelo_ElGemeloConLaGrafiaCanonica()
        {
            Assert.AreEqual(@"NUEVAVISION\DLopez", UsuarioAuditoriaHelper.Persona("dlopez"), "La fila dice «dlopez», Identity «DLopez»");
            Assert.AreEqual(@"NUEVAVISION\Santiago", UsuarioAuditoriaHelper.Persona("Santiago"), "La fila dice «santiago»");
            Assert.AreEqual(@"NUEVAVISION\Reina", UsuarioAuditoriaHelper.Persona("Reina"), "La fila dice «REINA»");
        }

        [TestMethod]
        public void SinDominioSinGemelo_Igual()
        {
            Assert.AreEqual("Pedro", UsuarioAuditoriaHelper.Persona("Pedro"));
            Assert.AreEqual("pedro", UsuarioAuditoriaHelper.Persona("pedro"), "Sin gemelo no se toca la caja");
        }

        [TestMethod]
        public void ParaAuditoria_YResolver_Canonicos()
        {
            Assert.AreEqual(@"NUEVAVISION\DLopez", UsuarioAuditoriaHelper.ParaAuditoria(@" NUEVAVISION\Dlopez "));
            var token = new System.Security.Principal.GenericPrincipal(new System.Security.Principal.GenericIdentity(@"NUEVAVISION\dlopez"), new string[0]);
            Assert.AreEqual(@"NUEVAVISION\DLopez", UsuarioAuditoriaHelper.Resolver(token, null));
        }

        // ---- Nombres de la persona: siguen el contrato de #823 ----

        [TestMethod]
        public void NombresDeLaPersona_IncluyeElCanonicoElSinDominioYElCrudo()
        {
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\DLopez", "DLopez", @"NUEVAVISION\Dlopez" },
                UsuarioAuditoriaHelper.NombresDeLaPersona(@"NUEVAVISION\Dlopez"));
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Santiago", "Santiago" },
                UsuarioAuditoriaHelper.NombresDeLaPersona("Santiago"));
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Santiago", "Santiago", "santiago" },
                UsuarioAuditoriaHelper.NombresDeLaPersona("santiago"), "El crudo con su caja exacta");
        }

        [TestMethod]
        public void MismaPersona_SinDistinguirMayusculas()
        {
            Assert.IsTrue(UsuarioAuditoriaHelper.MismaPersona(@"NUEVAVISION\dlopez", @"NUEVAVISION\Dlopez"));
            Assert.IsTrue(UsuarioAuditoriaHelper.MismaPersona(@"NUEVAVISION\santiago ", "Santiago"), "El gemelo también");
            Assert.IsFalse(UsuarioAuditoriaHelper.MismaPersona(@"NUEVAVISION\dlopez", @"NUEVAVISION\Aida"));
            Assert.IsFalse(UsuarioAuditoriaHelper.MismaPersona(null, null));
        }

        // ---- Caché y fallos ----

        [TestMethod]
        public void Cache_UnaConsultaAIdentityPorNombreCadaDiezMinutos()
        {
            _ = UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\dlopez");
            _ = UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\Dlopez");
            _ = UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\DLOPEZ");
            Assert.AreEqual(1, consultasIdentity.Count);

            ahora = ahora.AddMinutes(11);
            _ = UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\dlopez");
            Assert.AreEqual(2, consultasIdentity.Count);
        }

        [TestMethod]
        public void FalloDeIdentity_ReglaGeneralYAvisaUnaSolaVez()
        {
            UsuarioAuditoriaHelper.ConfigurarGrafia(_ => throw new InvalidOperationException("sin Identity"));

            Assert.AreEqual(@"NUEVAVISION\Dlopez", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\dlopez"));
            Assert.AreEqual(@"NUEVAVISION\Santiago", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\santiago"));

            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "NestoAPI#827");
        }

        [TestMethod]
        public void SoloConfigurar_LaGrafiaQuedaApagada()
        {
            UsuarioAuditoriaHelper.Configurar(g => gemelos.TryGetValue(g, out string f) ? f : null);

            Assert.AreEqual(@"NUEVAVISION\dlopez", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\dlopez"));
            Assert.AreEqual(@"NUEVAVISION\santiago", UsuarioAuditoriaHelper.Persona("Santiago"), "El #823 de siempre");
        }

        // ---- Solo grafía (pedidos) ----

        [TestMethod]
        public void SoloGrafia_CambiaLaCajaPeroNoResuelveElGemelo()
        {
            Assert.AreEqual(@"NUEVAVISION\DLopez", UsuarioAuditoriaHelper.SoloGrafia(@"NUEVAVISION\Dlopez"));
            Assert.AreEqual("Santiago", UsuarioAuditoriaHelper.SoloGrafia("Santiago"), "Sin dominio, tal cual");
            Assert.AreEqual("cliente@ejemplo.com", UsuarioAuditoriaHelper.SoloGrafia("cliente@ejemplo.com"));
            Assert.IsNull(UsuarioAuditoriaHelper.SoloGrafia(null));
        }

        // ---- Rapports ----

        [TestMethod]
        public void Rapport_SeGrabaConLaGrafiaCanonica()
        {
            Assert.AreEqual(@"NUEVAVISION\DLopez", SeguimientosClientesController.UsuarioParaGrabar(@"NUEVAVISION\Dlopez"),
                "Lo que manda Nesto con la caja del inicio de sesión");
            Assert.AreEqual(@"NUEVAVISION\Aida", SeguimientosClientesController.UsuarioParaGrabar(@"NUEVAVISION\aida"));
            Assert.AreEqual(@"NUEVAVISION\Santiago", SeguimientosClientesController.UsuarioParaGrabar("Santiago"), "Copiar desde Ariadna/NestoApp");
        }

        [TestMethod]
        public void InformeDetalleRapports_DlopezYDlopez_UnSoloBloqueConLaGrafiaCanonica()
        {
            var rapports = new List<DetalleRapportsDTO>
            {
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\dlopez", Cliente = "1" },
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\Aida", Cliente = "2" },
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\Dlopez ", Cliente = "3" },
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\aida", Cliente = "4" }
            };

            var grupos = GeneradorPdfDetalleRapports.AgruparPorUsuario(rapports);

            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Aida", @"NUEVAVISION\DLopez" }, grupos.Select(g => g.Key).ToList());
            CollectionAssert.AreEqual(new[] { "1", "3" }, grupos[1].Select(r => r.Cliente).ToList());
        }

        [TestMethod]
        public void InformeDetalleRapports_SinBD_AgrupaSinCajaYEnsenaLaPrimera()
        {
            UsuarioAuditoriaHelper.Reiniciar();
            var rapports = new List<DetalleRapportsDTO>
            {
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\dlopez", Cliente = "1" },
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\Dlopez", Cliente = "2" },
                new DetalleRapportsDTO { Usuario = @"NUEVAVISION\Carlos", Cliente = "3" },
                new DetalleRapportsDTO { Usuario = null, Cliente = "4" }
            };

            var grupos = GeneradorPdfDetalleRapports.AgruparPorUsuario(rapports);

            CollectionAssert.AreEqual(new[] { "", @"NUEVAVISION\Carlos", @"NUEVAVISION\dlopez" }, grupos.Select(g => g.Key).ToList());
            Assert.AreEqual(2, grupos[2].Count());
            Assert.IsNotNull(new GeneradorPdfDetalleRapports().GenerarPdf(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), rapports));
        }
    }
}
