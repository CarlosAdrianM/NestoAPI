using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Tests.Infrastructure.Productos
{
    /// <summary>
    /// NestoAPI#501: quién ve las familias de venta presencial. La regla creció el mismo día que
    /// nació: empezó siendo «solo vendedores presenciales» y al crear los productos se vio que
    /// Carlos y Manuel, que son quienes pueden saltarse la denegación, son vendedores «mini» y no
    /// veían lo que sí podían vender; y que el almacén las necesita para inventarios y abonos.
    /// </summary>
    [TestClass]
    public class FamiliasRestringidasTests
    {
        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            foreach (string grupo in grupos)
            {
                claims.Add(new Claim(ClaimTypes.Role, "NUEVAVISION\\" + grupo));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        private static IPrincipal Anonimo()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        [TestMethod]
        public void SinIdentificar_NoLasVe()
        {
            // El buscador es anónimo y lo llama la tienda de PrestaShop: el defecto es ocultarlas.
            Assert.IsFalse(FamiliasRestringidas.PuedeVerlas(Anonimo(), null, A.Fake<IServicioUsuarioVendedor>()));
            Assert.IsFalse(FamiliasRestringidas.PuedeVerlas(null, null, A.Fake<IServicioUsuarioVendedor>()));
        }

        [TestMethod]
        public void DireccionYAlmacen_LasVenSinSerVendedores()
        {
            // db a null a propósito: si llegara a consultar el vendedor, reventaría. No debe.
            Assert.IsTrue(FamiliasRestringidas.PuedeVerlas(
                Usuario("NUEVAVISION\\Carlos", Constantes.GruposSeguridad.DIRECCION), null, A.Fake<IServicioUsuarioVendedor>()));
            Assert.IsTrue(FamiliasRestringidas.PuedeVerlas(
                Usuario("NUEVAVISION\\Alfredo", Constantes.GruposSeguridad.ALMACEN), null, A.Fake<IServicioUsuarioVendedor>()));
        }

        [TestMethod]
        public void OtroGrupoCualquiera_NoBastaPorSiSolo()
        {
            // Sin grupo que valga y sin vendedor asociado, no las ve (el servicio devuelve null).
            IServicioUsuarioVendedor servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario(A<string>._)).Returns(null);
            ILectorParametrosUsuario sinPermiso = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => sinPermiso.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);

            Assert.IsFalse(FamiliasRestringidas.PuedeVerlas(
                Usuario("NUEVAVISION\\Paloma", Constantes.GruposSeguridad.ADMINISTRACION), null, servicio, sinPermiso));
        }

        [TestMethod]
        public void UsuarioSinDominio_QuitaElDominioParaBuscarElParametro()
        {
            Assert.AreEqual("Carlos", FamiliasRestringidas.UsuarioSinDominio("NUEVAVISION\\Carlos"));
            Assert.AreEqual("Carlos", FamiliasRestringidas.UsuarioSinDominio("Carlos"));
            Assert.IsNull(FamiliasRestringidas.UsuarioSinDominio(null));
            Assert.IsNull(FamiliasRestringidas.UsuarioSinDominio("   "));
        }

        [TestMethod]
        public void VendedorDelUsuario_PrefiereElClaimYSiNoTiraDeUsuarioVendedor()
        {
            IServicioUsuarioVendedor servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario("Jesus")).Returns("JE");

            ClaimsIdentity conClaim = new ClaimsIdentity(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "NUEVAVISION\\Jesus"),
                new Claim("Vendedor", "OTRO")
            }, "TestAuth");

            Assert.AreEqual("OTRO", FamiliasRestringidas.VendedorDelUsuario(new ClaimsPrincipal(conClaim), servicio));
            Assert.AreEqual("JE", FamiliasRestringidas.VendedorDelUsuario(Usuario("NUEVAVISION\\Jesus"), servicio));
        }

        /// <summary>
        /// La cuarta puerta: quien puede saltarse la denegación al vender (Carlos y Manuel, que son
        /// vendedores «mini») tiene que VER la familia o no podría crear el pedido de la excepción.
        /// </summary>
        [TestMethod]
        public void ConElPermisoDeVenta_LaVeAunqueNoSeaPresencialNiDeDireccion()
        {
            IServicioUsuarioVendedor servicio = A.Fake<IServicioUsuarioVendedor>();
            ILectorParametrosUsuario conPermiso = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => conPermiso.LeerParametro("1", "Manuel",
                Constantes.ParametrosUsuario.PERMITIR_VENDER_FAMILIAS_RESTRINGIDAS)).Returns("1");

            Assert.IsTrue(FamiliasRestringidas.PuedeVerlas(
                Usuario("NUEVAVISION\\Manuel"), null, servicio, conPermiso));
        }

        [TestMethod]
        public void SiLaLecturaDelParametroFalla_NoLaVe()
        {
            IServicioUsuarioVendedor servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario(A<string>._)).Returns(null);
            ILectorParametrosUsuario roto = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => roto.LeerParametro(A<string>._, A<string>._, A<string>._))
                .Throws(new System.InvalidOperationException("sin conexion"));

            Assert.IsFalse(FamiliasRestringidas.PuedeVerlas(Usuario("NUEVAVISION\\Manuel"), null, servicio, roto));
        }

        [TestMethod]
        public void VaciarCache_DejaLaListaListaParaReleerse()
        {
            // No revienta y deja el estado limpio para el resto de tests de la suite.
            FamiliasRestringidas.VaciarCache();
            FamiliasRestringidas.VaciarCache();
        }
    }
}
