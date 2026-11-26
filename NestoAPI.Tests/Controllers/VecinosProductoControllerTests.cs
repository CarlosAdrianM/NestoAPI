using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>Sugerencia 1478 (Manuel, 26/11/26): referencia anterior y siguiente para las flechas de la ficha de producto.</summary>
    [TestClass]
    public class VecinosProductoControllerTests
    {
        private static VecinosProductoController Controlador(ILectorVecinosProducto lector, IPrincipal usuario = null)
        {
            return new VecinosProductoController(() => lector)
            {
                User = usuario ?? new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Manuel"), new string[0])
            };
        }

        [TestMethod]
        public void Regla_SoloCifrasDeUnaANueve_EsNumerica()
        {
            Assert.IsTrue(ReglaVecinosProducto.EsNumerica("45957"));
            Assert.IsTrue(ReglaVecinosProducto.EsNumerica("45957   "));
            Assert.IsTrue(ReglaVecinosProducto.EsNumerica("7"));
            Assert.IsTrue(ReglaVecinosProducto.EsNumerica("123456789"));
        }

        [TestMethod]
        public void Regla_LetrasSignosVaciaODemasiadoLarga_NoEsNumerica()
        {
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica("MF0001"));
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica("45957A"));
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica("-5"));
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica("12 34"));
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica(""));
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica(null));
            Assert.IsFalse(ReglaVecinosProducto.EsNumerica("1234567890"));
        }

        [TestMethod]
        public void Consultas_NoUsanTryConvert_QueLaBaseDeDatosNoTiene()
        {
            // 26/11/26: la BD va con un nivel de compatibilidad sin TRY_CONVERT («no es un nombre de función integrada reconocido»).
            Assert.IsFalse(LectorVecinosProductoSql.SQL_NUMERICA.Contains("TRY_CONVERT"));
            Assert.IsFalse(LectorVecinosProductoSql.SQL_NO_NUMERICA.Contains("TRY_CONVERT"));
        }

        [TestMethod]
        public void GetVecinos_SinProducto_400()
        {
            var r = Controlador(A.Fake<ILectorVecinosProducto>()).GetVecinos("  ");

            Assert.IsInstanceOfType(r, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void GetVecinos_ProductoQueNoExiste_404()
        {
            ILectorVecinosProducto lector = A.Fake<ILectorVecinosProducto>();
            A.CallTo(() => lector.Leer("1", "NOEXISTE")).Returns(null);

            Assert.IsInstanceOfType(Controlador(lector).GetVecinos("NOEXISTE"), typeof(NotFoundResult));
        }

        [TestMethod]
        public void GetVecinos_Existe_DevuelveAnteriorYSiguienteConLaEmpresaPorDefecto()
        {
            ILectorVecinosProducto lector = A.Fake<ILectorVecinosProducto>();
            A.CallTo(() => lector.Leer("1", "45957")).Returns(new VecinosProductoDTO { Producto = "45957", Anterior = "45956", Siguiente = "45958" });

            var r = Controlador(lector).GetVecinos("45957") as OkNegotiatedContentResult<VecinosProductoDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual("45956", r.Content.Anterior);
            Assert.AreEqual("45958", r.Content.Siguiente);
        }

        [TestMethod]
        public void GetVecinos_ClienteDeLaTienda_403SinLeerNada()
        {
            ILectorVecinosProducto lector = A.Fake<ILectorVecinosProducto>();
            var identidad = new ClaimsIdentity(new[] { new Claim("cliente", "15191") }, "JWT");

            var r = Controlador(lector, new ClaimsPrincipal(identidad)).GetVecinos("45957") as NegotiatedContentResult<string>;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            A.CallTo(() => lector.Leer(A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
