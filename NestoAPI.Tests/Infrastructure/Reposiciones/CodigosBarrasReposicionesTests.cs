using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// Ariadna#22 (NestoAPI#605): las líneas de la reposición en preparación traen en CodigosBarras todos los códigos activos
    /// del producto (el principal, el primero), puestos por FichasProductoAlmacen.CompletarCodigos: una consulta por
    /// reposición, no una por línea.
    /// </summary>
    [TestClass]
    public class CodigosBarrasReposicionesTests
    {
        private const string PRINCIPAL_CERA = "8437017506362";
        private const string ALTERNATIVO_CERA = "8437017500001";
        private const string CAJA_CERA = "5901234123457";
        private const string PRINCIPAL_GUANTES = "4006381333931";

        private IRepositorioCodigosBarras codigos;
        private IServicioPreparacionReposicion preparacion;

        [TestInitialize]
        public void Preparar()
        {
            codigos = A.Fake<IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.CodigosActivos(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["22624"] = new List<string> { PRINCIPAL_CERA, ALTERNATIVO_CERA, CAJA_CERA }
                });
            preparacion = A.Fake<IServicioPreparacionReposicion>();
        }

        private ReposicionesController Controlador()
        {
            IPermisoRellenarReposicionManual permiso = A.Fake<IPermisoRellenarReposicionManual>();
            A.CallTo(() => permiso.Puede(A<IPrincipal>._)).Returns(true);
            var fichas = new FichasProductoAlmacen(A.Fake<IRepositorioFichasProducto>(), codigos: codigos);
            return new ReposicionesController(null, preparacion, permisoManual: permiso, fichas: fichas)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre"), new Claim(ClaimTypes.Role, "Almacén") }, "Bearer"))
            };
        }

        private static ReposicionEnPreparacionDTO Reposicion()
        {
            return new ReposicionEnPreparacionDTO
            {
                Empresa = "1",
                Origen = "ALC",
                Destino = "ALG",
                Lineas = new List<LineaReposicionEnPreparacionDTO>
                {
                    new LineaReposicionEnPreparacionDTO { NumeroOrden = 1, Producto = "22624          ", CodigoBarras = PRINCIPAL_CERA, Cantidad = 2 },
                    new LineaReposicionEnPreparacionDTO { NumeroOrden = 2, Producto = "18004", CodigoBarras = PRINCIPAL_GUANTES, Cantidad = 1 },
                    new LineaReposicionEnPreparacionDTO { NumeroOrden = 3, Producto = "22624", CodigoBarras = PRINCIPAL_CERA, Cantidad = 1 }
                }
            };
        }

        [TestMethod]
        public async Task GetEnPreparacion_CadaLineaTraeTodosLosCodigosDelProducto_ElPrincipalElPrimero()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns(Reposicion());

            var ok = (OkNegotiatedContentResult<ReposicionEnPreparacionDTO>)await Controlador().GetEnPreparacion("ALC");

            CollectionAssert.AreEqual(new List<string> { PRINCIPAL_CERA, ALTERNATIVO_CERA, CAJA_CERA }, ok.Content.Lineas[0].CodigosBarras);
            CollectionAssert.AreEqual(new List<string> { PRINCIPAL_CERA, ALTERNATIVO_CERA, CAJA_CERA }, ok.Content.Lineas[2].CodigosBarras);
            // Sin alternativos en la tabla: solo el principal de la ficha
            CollectionAssert.AreEqual(new List<string> { PRINCIPAL_GUANTES }, ok.Content.Lineas[1].CodigosBarras);
            Assert.AreEqual(PRINCIPAL_CERA, ok.Content.Lineas[0].CodigoBarras, "CodigoBarras sigue siendo el principal");
        }

        [TestMethod]
        public async Task GetEnPreparacion_UnaSolaConsultaDeCodigosParaTodasLasLineas()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns(Reposicion());

            await Controlador().GetEnPreparacion("ALC");

            A.CallTo(() => codigos.CodigosActivos("1", A<IReadOnlyCollection<string>>.That.Matches(p => p.Count == 2)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PutCantidad_LaReposicionDevueltaTraeTodosLosCodigos()
        {
            A.CallTo(() => preparacion.CambiarCantidad("1", "ALC", 1, 1, A<IPrincipal>._)).Returns(Reposicion());

            var ok = (OkNegotiatedContentResult<ReposicionEnPreparacionDTO>)await Controlador()
                .PutCantidad(1, new CambiarCantidadReposicionDTO { Cantidad = 1 }, "ALC");

            CollectionAssert.AreEqual(new List<string> { PRINCIPAL_CERA, ALTERNATIVO_CERA, CAJA_CERA }, ok.Content.Lineas[0].CodigosBarras);
        }
    }
}
