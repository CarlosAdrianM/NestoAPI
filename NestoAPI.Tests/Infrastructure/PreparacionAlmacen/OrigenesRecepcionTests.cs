using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#559: la estrategia de compras (permisos y lo que pasa al terminar) y la de reposiciones (#553).
    [TestClass]
    public class OrigenesRecepcionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 5);

        private IRepositorioRecepcionCompras repositorio;
        private ITransaccionRecepcionCompra transaccion;
        private IAvisadorCompras avisador;
        private OrigenRecepcionCompras compras;
        private List<string> llamadas;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestInitialize]
        public void Inicializar()
        {
            llamadas = new List<string>();
            repositorio = A.Fake<IRepositorioRecepcionCompras>();
            transaccion = A.Fake<ITransaccionRecepcionCompra>();
            avisador = A.Fake<IAvisadorCompras>();
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>>>.Ignored))
                .ReturnsLazily((Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>> trabajo) => trabajo(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.CambiarVistoBueno(A<string>.Ignored, A<IEnumerable<int>>.Ignored, A<bool>.Ignored))
                .Invokes((string e, IEnumerable<int> o, bool vb) => llamadas.Add(vb ? "devolver visto bueno" : "apartar"));
            A.CallTo(() => transaccion.CrearAlbaran(A<int>.Ignored, A<string>.Ignored))
                .ReturnsLazily((int pedido, string u) => { llamadas.Add("albarán " + pedido); return 9000 + pedido % 100; });
            compras = new OrigenRecepcionCompras(repositorio, avisador, () => HOY);
        }

        private static LineaCompraPendiente Linea(int pedido, int orden, string producto, int cantidad, bool control = true)
        {
            return new LineaCompraPendiente
            {
                Pedido = pedido, FechaPedido = new DateTime(2026, 9, 1), NumeroOrden = orden, Producto = producto,
                Cantidad = cantidad, FechaRecepcion = HOY.AddDays(-1), VistoBueno = true, ControlPendientes = control
            };
        }

        private SolicitudTerminarRecepcion Solicitud(IPrincipal usuario, params (string producto, int cantidad)[] lecturas)
        {
            return new SolicitudTerminarRecepcion
            {
                Empresa = "1", Almacen = "ALG", Documento = "65", IdRecepcion = Guid.NewGuid(),
                Lecturas = lecturas.ToDictionary(l => l.producto, l => l.cantidad),
                Usuario = usuario.Identity.Name, Principal = usuario
            };
        }

        private void LineasDelProveedor(params LineaCompraPendiente[] lineas)
        {
            A.CallTo(() => transaccion.LeerLineasBloqueando("1", "ALG", "65")).Returns(lineas.ToList());
        }

        [DataTestMethod]
        [DataRow("Almacén", true)]
        [DataRow("Compras", true)]
        [DataRow("Dirección", true)]
        [DataRow("Tiendas", false)]
        [DataRow("Administración", false)]
        public void Compras_QuienPuedeTerminar(string grupo, bool puede)
        {
            Assert.AreEqual(puede, compras.PuedeTerminar(Usuario("x", grupo)));
        }

        [DataTestMethod]
        [DataRow("Almacén", true)]
        [DataRow("Compras", true)]
        [DataRow("Dirección", true)]
        [DataRow("Tiendas", true)]
        [DataRow("Administración", false)]
        public void Reposiciones_QuienPuedeTerminar_TambienLasTiendas(string grupo, bool puede)
        {
            var reposiciones = new OrigenRecepcionReposiciones(A.Fake<IServicioRecepcionReposiciones>());

            Assert.AreEqual(puede, reposiciones.PuedeTerminar(Usuario("x", grupo)));
        }

        [TestMethod]
        public void Reposiciones_TodaviaNoSeTerminanDesdeAqui()
        {
            Assert.IsFalse(new OrigenRecepcionReposiciones(A.Fake<IServicioRecepcionReposiciones>()).SeTerminaDesdeAqui);
            Assert.IsTrue(compras.SeTerminaDesdeAqui);
        }

        [TestMethod]
        public async Task Terminar_ApartaLoQueSigue_CreaElAlbaranYDevuelveElVistoBueno_EnEseOrden()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5), Linea(100, 2, "B", 3));

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5)));

            CollectionAssert.AreEqual(new[] { "apartar", "albarán 100", "devolver visto bueno" }, llamadas);
            Assert.AreEqual(100, resultado.Documentos.Single().Pedido);
            Assert.AreEqual(9000, resultado.Documentos.Single().Albaran);
            A.CallTo(() => transaccion.RecibirLinea("1", A<LineaCompraPendiente>.That.Matches(l => l.NumeroOrden == 1),
                A<LineaRecibida>.That.Matches(r => r.Recibido == 5 && r.Resto == 0), HOY, "Pedro")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_SinControlDePendientes_AnulaLoQueNoHaLlegado()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5, control: false), Linea(100, 2, "B", 3, control: false));

            _ = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5)));

            A.CallTo(() => transaccion.Anular("1", 2, "Pedro")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_GuardaLaEvidenciaDeLoLeido()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));

            _ = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5), ("Z", 1)));

            A.CallTo(() => transaccion.RegistrarEvidencia("1", A<IEnumerable<EvidenciaRecepcion>>.That.Matches(e =>
                e.Count() == 2 && e.Single(x => x.Producto == "A").NumeroOrigen == 100 && e.Single(x => x.Producto == "Z").NumeroOrigen == 0)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_ReenvioDeLaMismaRecepcion_NoHaceNadaYLoDice()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(true);

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5)));

            Assert.IsTrue(resultado.YaEstabaTerminada);
            A.CallTo(() => transaccion.CrearAlbaran(A<int>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => transaccion.RecibirLinea(A<string>.Ignored, A<LineaCompraPendiente>.Ignored, A<LineaRecibida>.Ignored, A<DateTime>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void IdsDeEvidencia_SonLosMismosParaLaMismaRecepcionYProducto()
        {
            Guid id = Guid.NewGuid();

            Assert.AreEqual(OrigenRecepcionCompras.IdEvidencia(id, "A"), OrigenRecepcionCompras.IdEvidencia(id, "A"));
            Assert.AreNotEqual(OrigenRecepcionCompras.IdEvidencia(id, "A"), OrigenRecepcionCompras.IdEvidencia(id, "B"));
            Assert.AreNotEqual(OrigenRecepcionCompras.IdEvidencia(id, "A"), OrigenRecepcionCompras.IdEvidencia(Guid.NewGuid(), "A"));
        }

        [TestMethod]
        public async Task Terminar_ElProveedorNoTieneNadaPendiente_ErrorClaro()
        {
            LineasDelProveedor();

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5))));

            StringAssert.Contains(ex.Message, "65");
        }

        [TestMethod]
        public async Task Terminar_NadaDeLoLeidoEstaPedido_NoSeTermina()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("Z", 5))));

            StringAssert.Contains(ex.Message, "Z");
        }

        [TestMethod]
        public async Task Terminar_ExcesoRecibidoPorAlmacen_SeAvisaACompras()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 7)));

            A.CallTo(() => transaccion.CrearExceso("1", A<LineaCompraPendiente>.That.Matches(l => l.NumeroOrden == 1),
                A<ExcesoRecepcion>.That.Matches(e => e.Cantidad == 2 && !e.VistoBueno), HOY, "Pedro")).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(A<string>.Ignored, A<IEnumerable<string>>.That.Matches(a => a.Count() == 1)))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Avisos.Count);
        }

        [TestMethod]
        public async Task Terminar_ExcesoRecibidoPorCompras_EntraConVistoBuenoYNoSeAvisaANadie()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));

            _ = await compras.Terminar(Solicitud(Usuario("Andre", "Compras"), ("A", 7)));

            A.CallTo(() => transaccion.CrearExceso("1", A<LineaCompraPendiente>.Ignored,
                A<ExcesoRecepcion>.That.Matches(e => e.VistoBueno), HOY, "Andre")).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(A<string>.Ignored, A<IEnumerable<string>>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_SiFallaElAvisoACompras_LaRecepcionYaEstaHecha()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));
            A.CallTo(() => avisador.Avisar(A<string>.Ignored, A<IEnumerable<string>>.Ignored)).Throws(new Exception("sin dominio"));

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 7)));

            Assert.AreEqual(1, resultado.Documentos.Count);
        }

        [TestMethod]
        public async Task Terminar_NoPedido_SeDevuelveYNoEntra()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5), ("Z", 2)));

            Assert.AreEqual("Z", resultado.NoEsperados.Single().Producto);
            Assert.AreEqual(2, resultado.NoEsperados.Single().Leido);
            Assert.IsTrue(resultado.NoEsperados.Single().Ajeno);
        }

        [TestMethod]
        public async Task LeerPendientes_UnaRecepcionPorProveedorConSusPedidos()
        {
            A.CallTo(() => repositorio.LeerPedidosPendientes("1", "ALG")).Returns(new List<PedidoCompraPendienteDTO>
            {
                new PedidoCompraPendienteDTO { Pedido = 100, Proveedor = "65", NombreProveedor = "MAYSTAR", FechaRecepcion = HOY, Lineas = 2, Unidades = 8 },
                new PedidoCompraPendienteDTO { Pedido = 101, Proveedor = "65", NombreProveedor = "MAYSTAR", FechaRecepcion = HOY.AddDays(-3), Lineas = 1, Unidades = 4 },
                new PedidoCompraPendienteDTO { Pedido = 102, Proveedor = "16", NombreProveedor = "DRV", FechaRecepcion = HOY, Lineas = 1, Unidades = 1 }
            });

            List<RecepcionPendienteDTO> pendientes = await compras.LeerPendientes("1", "ALG");

            RecepcionPendienteDTO maystar = pendientes.Single(p => p.Documento == "65");
            Assert.AreEqual("COMP", maystar.Tipo);
            Assert.AreEqual("MAYSTAR", maystar.Titulo);
            CollectionAssert.AreEquivalent(new[] { 100, 101 }, maystar.Pedidos);
            Assert.AreEqual(12, maystar.Unidades);
            Assert.AreEqual(HOY.AddDays(-3), maystar.Fecha);
        }

        [TestMethod]
        public async Task LeerEsperado_SumaElProductoDeTodosLosPedidosDelProveedor()
        {
            A.CallTo(() => repositorio.LeerLineasPendientesProveedor("1", "ALG", "65")).Returns(new List<FilaRecepcionCompra>
            {
                new FilaRecepcionCompra { Pedido = 100, LineaPedido = 1, Proveedor = "65", NombreProveedor = "MAYSTAR", Producto = "A", CodigoBarras = "111", Cantidad = 2 },
                new FilaRecepcionCompra { Pedido = 101, LineaPedido = 7, Proveedor = "65", NombreProveedor = "MAYSTAR", Producto = "A", CodigoBarras = "111", Cantidad = 3 },
                new FilaRecepcionCompra { Pedido = 101, LineaPedido = 8, Proveedor = "65", NombreProveedor = "MAYSTAR", Producto = "B", Cantidad = 1 }
            });

            RecepcionDTO recepcion = await compras.LeerEsperado("1", "ALG", "65");

            Assert.AreEqual("MAYSTAR", recepcion.Titulo);
            Assert.AreEqual(5, recepcion.Lineas.Single(l => l.Producto == "A").Cantidad);
            Assert.IsFalse(recepcion.Lineas.Single(l => l.Producto == "A").CodigoDuplicado, "El mismo producto en dos pedidos no es un código duplicado");
            Assert.IsTrue(recepcion.Lineas.Single(l => l.Producto == "B").SinCodigo);
        }

        [TestMethod]
        public async Task LeerEsperado_SinNadaPendiente_Null()
        {
            A.CallTo(() => repositorio.LeerLineasPendientesProveedor("1", "ALG", "65")).Returns(new List<FilaRecepcionCompra>());

            Assert.IsNull(await compras.LeerEsperado("1", "ALG", "65"));
        }
    }
}
