using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>Sugerencia 1482 (Manuel, Compras, 26/11/26): referencia del proveedor y extracto del producto para la ficha de Nesto.</summary>
    [TestClass]
    public class FichaProductoControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);

        private static IPrincipal Empleado(params string[] grupos) =>
            new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Manuel"), grupos.Select(g => "NUEVAVISION\\" + g).ToArray());

        private static IPrincipal ClienteTienda() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", "15191") }, "JWT"));

        private static IPrincipal VendedorNestoApp() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("IsVendedor", "true"), new Claim("Vendedor", "JE") }, "JWT"));

        private static FichaProductoController Controlador(ILectorFichaProducto lector, IPrincipal usuario) =>
            new FichaProductoController(() => lector, () => HOY) { User = usuario, AlmacenDelUsuario = u => "REI" };

        private static MovimientoExtractoProductoDTO Mov(int orden, int cantidad) => new MovimientoExtractoProductoDTO
        {
            NumeroOrden = orden, Empresa = "1", Cantidad = cantidad, Importe = 10m * cantidad, Proveedor = "13", NombreProveedor = "LISAP"
        };

        // ---- Permisos ----

        [TestMethod]
        public void Permisos_Extracto_LosCincoGruposSi()
        {
            foreach (string grupo in new[] { "Compras", "Almacén", "Dirección", "Informática", "Tiendas" })
            {
                Assert.IsTrue(PermisosFichaProducto.PuedeVerExtracto(Empleado(grupo)), grupo);
            }
        }

        [TestMethod]
        public void Permisos_Extracto_AdministracionNo()
        {
            // Carlos 26/11/26: Administración no necesita el extracto de un producto.
            Assert.IsFalse(PermisosFichaProducto.PuedeVerExtracto(Empleado("Administración")));
        }

        [TestMethod]
        public void Permisos_Extracto_SinGrupoVendedorOClienteNo()
        {
            Assert.IsFalse(PermisosFichaProducto.PuedeVerExtracto(Empleado()));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerExtracto(Empleado("Vendedores")));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerExtracto(VendedorNestoApp()));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerExtracto(ClienteTienda()));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerExtracto(null));
        }

        [TestMethod]
        public void Permisos_ProveedoresYCostes_SoloComprasDireccionEInformatica()
        {
            Assert.IsTrue(PermisosFichaProducto.PuedeVerProveedoresYCostes(Empleado("Compras")));
            Assert.IsTrue(PermisosFichaProducto.PuedeVerProveedoresYCostes(Empleado("Dirección")));
            Assert.IsTrue(PermisosFichaProducto.PuedeVerProveedoresYCostes(Empleado("Informática")));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerProveedoresYCostes(Empleado("Almacén")));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerProveedoresYCostes(Empleado("Administración")));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerProveedoresYCostes(Empleado("Tiendas")));
            Assert.IsFalse(PermisosFichaProducto.PuedeVerProveedoresYCostes(ClienteTienda()));
        }

        // ---- Reglas ----

        [TestMethod]
        public void Saldos_DelMasNuevoAlMasAntiguo_PrimeraPagina()
        {
            // Saldo inicial 10; en el periodo +5, -2, +3 (del más antiguo al más nuevo) = 16.
            var pagina = new List<MovimientoExtractoProductoDTO> { Mov(3, 3), Mov(2, -2), Mov(1, 5) };

            ReglaExtractoProducto.CalcularSaldos(10, 6, 0, pagina);

            CollectionAssert.AreEqual(new[] { 16, 13, 15 }, pagina.Select(m => m.Saldo).ToArray());
        }

        [TestMethod]
        public void Saldos_SegundaPagina_DescuentaLoDeLasPaginasAnteriores()
        {
            // Mismo periodo, página de tamaño 1: la segunda es el -2 y los más nuevos suman 3.
            var pagina = new List<MovimientoExtractoProductoDTO> { Mov(2, -2) };

            ReglaExtractoProducto.CalcularSaldos(10, 6, 3, pagina);

            Assert.AreEqual(13, pagina[0].Saldo);
        }

        [TestMethod]
        public void Habitual_ElDeMenorOrden()
        {
            var filas = ReglaExtractoProducto.MarcarHabitual(new[]
            {
                new ReferenciaProveedorProductoDTO { Proveedor = "200", Orden = 2 },
                new ReferenciaProveedorProductoDTO { Proveedor = "13", Orden = 1 },
                new ReferenciaProveedorProductoDTO { Proveedor = "5", Orden = 3 }
            });

            Assert.AreEqual("13", filas[0].Proveedor);
            Assert.IsTrue(filas[0].EsHabitual);
            Assert.AreEqual(1, filas.Count(f => f.EsHabitual));
        }

        [TestMethod]
        public void Periodo_SinFechas_LosUltimosDoceMeses()
        {
            var (desde, hasta) = ReglaExtractoProducto.Periodo(null, null, HOY);

            Assert.AreEqual(new DateTime(2025, 11, 26), desde);
            Assert.AreEqual(HOY, hasta);
        }

        [TestMethod]
        public void Tamano_PorDefecto200YNuncaMasDe500()
        {
            Assert.AreEqual(200, ReglaExtractoProducto.Tamano(null));
            Assert.AreEqual(200, ReglaExtractoProducto.Tamano(0));
            Assert.AreEqual(500, ReglaExtractoProducto.Tamano(10000));
            Assert.AreEqual(50, ReglaExtractoProducto.Tamano(50));
            Assert.AreEqual(1, ReglaExtractoProducto.Pagina(-3));
        }

        [TestMethod]
        public void Usuario_SinDominio()
        {
            Assert.AreEqual("Manuel", ReglaExtractoProducto.SinDominio("NUEVAVISION\\Manuel"));
            Assert.AreEqual("Manuel", ReglaExtractoProducto.SinDominio("Manuel"));
            Assert.IsNull(ReglaExtractoProducto.SinDominio(" "));
        }

        [TestMethod]
        public void Consultas_SinFuncionesQueLaBaseDeDatosNoTiene()
        {
            // Nivel de compatibilidad antiguo: ni TRY_CONVERT, ni OFFSET/FETCH, ni SUM() OVER (ORDER BY).
            foreach (string sql in new[] { LectorFichaProductoSql.SQL_EXTRACTO, LectorFichaProductoSql.SQL_REFERENCIAS })
            {
                Assert.IsFalse(sql.Contains("TRY_CONVERT"));
                Assert.IsFalse(sql.Contains("OFFSET"));
                Assert.IsFalse(sql.Contains("FETCH"));
                Assert.IsTrue(sql.Contains("NOLOCK"));
            }
            Assert.IsTrue(LectorFichaProductoSql.SQL_EXTRACTO.Contains("e.Empresa IN (@empresa, @espejo)"));
            Assert.AreEqual("3", ReglaExtractoProducto.Espejo("1"));
        }

        // ---- Controlador: referencias ----

        [TestMethod]
        public void Referencias_Compras_DevuelveConElHabitual()
        {
            ILectorFichaProducto lector = A.Fake<ILectorFichaProducto>();
            A.CallTo(() => lector.LeerReferenciasProveedor("1", "17404")).Returns(new List<ReferenciaProveedorProductoDTO>
            {
                new ReferenciaProveedorProductoDTO { Proveedor = "13", NombreProveedor = "LISAP", ReferenciaProveedor = "LK-200", Orden = 1 }
            });

            var r = Controlador(lector, Empleado("Compras")).GetReferenciasProveedor(" 17404 ") as OkNegotiatedContentResult<List<ReferenciaProveedorProductoDTO>>;

            Assert.IsNotNull(r);
            Assert.AreEqual("LK-200", r.Content.Single().ReferenciaProveedor);
            Assert.IsTrue(r.Content.Single().EsHabitual);
        }

        [TestMethod]
        public void Referencias_Almacen_403SinLeer()
        {
            ILectorFichaProducto lector = A.Fake<ILectorFichaProducto>();

            var r = Controlador(lector, Empleado("Almacén")).GetReferenciasProveedor("17404") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(PermisosFichaProducto.MENSAJE_SIN_PERMISO_PROVEEDORES, r.Content);
            A.CallTo(() => lector.LeerReferenciasProveedor(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Referencias_ClienteTienda_403()
        {
            var r = Controlador(A.Fake<ILectorFichaProducto>(), ClienteTienda()).GetReferenciasProveedor("17404") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        }

        [TestMethod]
        public void Referencias_NoExiste_404YSinProducto_400()
        {
            ILectorFichaProducto lector = A.Fake<ILectorFichaProducto>();
            A.CallTo(() => lector.LeerReferenciasProveedor("1", "NOEXISTE")).Returns(null);

            Assert.IsInstanceOfType(Controlador(lector, Empleado("Dirección")).GetReferenciasProveedor("NOEXISTE"), typeof(NotFoundResult));
            Assert.IsInstanceOfType(Controlador(lector, Empleado("Dirección")).GetReferenciasProveedor(""), typeof(BadRequestErrorMessageResult));
        }

        // ---- Controlador: extracto ----

        private static ILectorFichaProducto LectorConExtracto()
        {
            ILectorFichaProducto lector = A.Fake<ILectorFichaProducto>();
            A.CallTo(() => lector.LeerExtracto(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<DateTime>._, A<int>._, A<int>._))
                .ReturnsLazily(() => new PaginaExtractoProducto
                {
                    Producto = "17404", Nombre = "CHAMPU", SaldoInicial = 10, SumaPeriodo = 6, Total = 3, SumaMasNuevos = 0,
                    Movimientos = new List<MovimientoExtractoProductoDTO> { Mov(3, 3), Mov(2, -2), Mov(1, 5) }
                });
            return lector;
        }

        [TestMethod]
        public void Extracto_Almacen_SinImportesNiProveedorPeroConSaldos()
        {
            ILectorFichaProducto lector = LectorConExtracto();

            var r = Controlador(lector, Empleado("Almacén")).GetExtracto("17404", almacen: "alg") as OkNegotiatedContentResult<ExtractoProductoFichaDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(10, r.Content.SaldoInicial);
            Assert.AreEqual(16, r.Content.SaldoFinal);
            Assert.AreEqual("ALG", r.Content.Almacen);
            Assert.IsTrue(r.Content.Movimientos.All(m => m.Importe == null && m.Proveedor == null && m.NombreProveedor == null));
            CollectionAssert.AreEqual(new[] { 16, 13, 15 }, r.Content.Movimientos.Select(m => m.Saldo).ToArray());
            A.CallTo(() => lector.LeerExtracto("1", "17404", "ALG", new DateTime(2025, 11, 26), HOY, 1, 200)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Extracto_Tiendas_SoloSuAlmacen_PidaElQuePida()
        {
            // Carlos 26/11/26: una tienda ve el extracto, pero filtrado por su almacén.
            ILectorFichaProducto lector = LectorConExtracto();

            var r = Controlador(lector, Empleado("Tiendas")).GetExtracto("17404", almacen: "alg") as OkNegotiatedContentResult<ExtractoProductoFichaDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual("REI", r.Content.Almacen);
            A.CallTo(() => lector.LeerExtracto("1", "17404", "REI", A<DateTime>._, A<DateTime>._, 1, 200)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Extracto_Tiendas_SinAlmacenConocido_403SinLeer()
        {
            ILectorFichaProducto lector = LectorConExtracto();
            FichaProductoController controlador = Controlador(lector, Empleado("Tiendas"));
            controlador.AlmacenDelUsuario = u => null;

            var r = controlador.GetExtracto("17404") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(PermisosFichaProducto.MENSAJE_SIN_TIENDA, r.Content);
            A.CallTo(lector).MustNotHaveHappened();
        }

        [TestMethod]
        public void Extracto_TiendasQueAdemasEsDeAlmacen_VeTodos()
        {
            Assert.IsTrue(PermisosFichaProducto.SoloSuAlmacen(Empleado("Tiendas")));
            Assert.IsFalse(PermisosFichaProducto.SoloSuAlmacen(Empleado("Tiendas", "Almacén")));
            Assert.IsFalse(PermisosFichaProducto.SoloSuAlmacen(Empleado("Compras")));
        }

        [TestMethod]
        public void Extracto_Compras_ConImportesYProveedor()
        {
            var r = Controlador(LectorConExtracto(), Empleado("Compras")).GetExtracto("17404") as OkNegotiatedContentResult<ExtractoProductoFichaDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(30m, r.Content.Movimientos[0].Importe);
            Assert.AreEqual("LISAP", r.Content.Movimientos[0].NombreProveedor);
            Assert.IsNull(r.Content.Almacen);
        }

        [TestMethod]
        public void Extracto_PaginaYTamanoTopado()
        {
            ILectorFichaProducto lector = LectorConExtracto();

            Controlador(lector, Empleado("Almacén")).GetExtracto("17404", pagina: 3, tamano: 9999);

            A.CallTo(() => lector.LeerExtracto("1", "17404", null, A<DateTime>._, A<DateTime>._, 3, 500)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Extracto_VendedorDeNestoAppOClienteTienda_403SinLeer()
        {
            ILectorFichaProducto lector = A.Fake<ILectorFichaProducto>();

            var vendedor = Controlador(lector, VendedorNestoApp()).GetExtracto("17404") as NegotiatedContentResult<string>;
            var cliente = Controlador(lector, ClienteTienda()).GetExtracto("17404") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, vendedor.StatusCode);
            Assert.AreEqual(PermisosFichaProducto.MENSAJE_SIN_PERMISO_EXTRACTO, vendedor.Content);
            Assert.AreEqual(HttpStatusCode.Forbidden, cliente.StatusCode);
            A.CallTo(() => lector.LeerExtracto(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<DateTime>._, A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Extracto_FechasAlReves_400()
        {
            var r = Controlador(A.Fake<ILectorFichaProducto>(), Empleado("Compras"))
                .GetExtracto("17404", desde: new DateTime(2026, 11, 20), hasta: new DateTime(2026, 11, 1));

            Assert.IsInstanceOfType(r, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Extracto_NoExiste_404()
        {
            ILectorFichaProducto lector = A.Fake<ILectorFichaProducto>();
            A.CallTo(() => lector.LeerExtracto(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<DateTime>._, A<int>._, A<int>._)).Returns(null);

            Assert.IsInstanceOfType(Controlador(lector, Empleado("Compras")).GetExtracto("NOEXISTE"), typeof(NotFoundResult));
        }
    }
}
