using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Ariadna para tiendas (Carlos 09/10/26): GET api/Almacen/Perfil.</summary>
    [TestClass]
    public class PerfilAlmacenTests
    {
        [TestMethod]
        public void Deducir_AlmacenDeReina_EsTienda()
        {
            PerfilAlmacenDTO perfil = ReglasPerfilAlmacen.Deducir("Reina", "REI");

            Assert.AreEqual("REI", perfil.Almacen);
            Assert.IsTrue(perfil.EsTienda);
            Assert.AreEqual(ReglasPerfilAlmacen.PERFIL_TIENDA, perfil.Perfil);
        }

        [TestMethod]
        public void Deducir_AlmacenDeAlcobendasConRellenoYMinusculas_EsTienda()
        {
            // ParámetrosUsuario.Valor es char con relleno
            PerfilAlmacenDTO perfil = ReglasPerfilAlmacen.Deducir("Paloma", "alc   ");

            Assert.AreEqual("ALC", perfil.Almacen);
            Assert.IsTrue(perfil.EsTienda);
        }

        [TestMethod]
        public void Deducir_Algete_EsAlmacen()
        {
            PerfilAlmacenDTO perfil = ReglasPerfilAlmacen.Deducir("Andre", "ALG");

            Assert.AreEqual("ALG", perfil.Almacen);
            Assert.IsFalse(perfil.EsTienda);
            Assert.AreEqual(ReglasPerfilAlmacen.PERFIL_ALMACEN, perfil.Perfil);
        }

        [TestMethod]
        public void Deducir_SinAlmacen_EsAlgeteComoHastaAhora()
        {
            PerfilAlmacenDTO perfil = ReglasPerfilAlmacen.Deducir("RevisionGoogle", null);

            Assert.AreEqual("ALG", perfil.Almacen);
            Assert.IsFalse(perfil.EsTienda);
        }

        [TestMethod]
        public void Deducir_UsuarioConDominio_SeQuitaElDominio()
        {
            Assert.AreEqual("Reina", ReglasPerfilAlmacen.Deducir("NUEVAVISION\\Reina", "REI").Usuario);
        }

        [TestMethod]
        public void ElControlador_ExigeAutenticacion()
        {
            Assert.IsTrue(typeof(PerfilAlmacenController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task GetPerfil_LeeElAlmacenDelUsuarioDelTokenSinDominioYSuNombre()
        {
            string empresaLeida = null;
            string usuarioLeido = null;
            var controller = new PerfilAlmacenController(
                (empresa, usuario) => { empresaLeida = empresa; usuarioLeido = usuario; return "REI"; },
                (empresa, almacen) => Task.FromResult(almacen == "REI" ? "Reina" : null))
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Reina", "Bearer"), new string[0]),
                Request = new HttpRequestMessage()
            };

            var resultado = (OkNegotiatedContentResult<PerfilAlmacenDTO>)await controller.GetPerfil();

            Assert.AreEqual("1", empresaLeida);
            Assert.AreEqual("Reina", usuarioLeido);
            Assert.AreEqual("REI", resultado.Content.Almacen);
            Assert.AreEqual("Reina", resultado.Content.NombreAlmacen);
            Assert.IsTrue(resultado.Content.EsTienda);
        }

        [TestMethod]
        public async Task GetPerfil_MozoDeAlgete_PerfilAlmacen()
        {
            var controller = new PerfilAlmacenController((empresa, usuario) => "ALG", (empresa, almacen) => Task.FromResult("Algete"))
            {
                User = new GenericPrincipal(new GenericIdentity("Andre", "Bearer"), new[] { "Almacén" }),
                Request = new HttpRequestMessage()
            };

            var resultado = (OkNegotiatedContentResult<PerfilAlmacenDTO>)await controller.GetPerfil();

            Assert.IsFalse(resultado.Content.EsTienda);
            Assert.AreEqual("Algete", resultado.Content.NombreAlmacen);
        }
    }
}
