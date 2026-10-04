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
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>> trabajo, bool deshacer) => trabajo(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.Aplazar(A<string>.Ignored, A<IEnumerable<int>>.Ignored, A<DateTime>.Ignored))
                .Invokes((string e, IEnumerable<int> o, DateTime f) => llamadas.Add("aplazar al " + f.ToString("dd/MM")));
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
            Assert.AreEqual(puede, compras.PuedeTerminar(Usuario("x", grupo), "1", "ALG"));
        }

        // Reposiciones: hoy da la entrada quien tiene el almacén de destino en AlmacénPedidoVta
        // (60 días: Reina → REI, Paloma → ALC, Andre/Alfredo/Santiago → ALG)
        private static OrigenRecepcionReposiciones Reposiciones(IRepositorioCierreReposiciones cierre = null)
        {
            var almacenes = new Dictionary<string, string> { ["Reina"] = "REI", ["Paloma"] = "ALC", ["Andre"] = "ALG" };
            return new OrigenRecepcionReposiciones(A.Fake<IServicioRecepcionReposiciones>(), cierre ?? A.Fake<IRepositorioCierreReposiciones>(),
                (empresa, usuario) => almacenes.TryGetValue(usuario, out string a) ? a : null);
        }

        private static (IRepositorioCierreReposiciones Cierre, ITransaccionCierreReposicion Transaccion) CierreFalso(params int[] traspasosEnDiario)
        {
            var cierre = A.Fake<IRepositorioCierreReposiciones>();
            var transaccion = A.Fake<ITransaccionCierreReposicion>();
            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> t, bool deshacer) => t(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.DiarioDeEntrada("1", "REI")).Returns("PendRepo");
            A.CallTo(() => transaccion.TraspasosEnDiario("1", "REI", "PendRepo")).Returns(traspasosEnDiario.ToList());
            // Lo enviado en la 80862 es justo lo que lee SolicitudReposicion (2 de A)
            A.CallTo(() => transaccion.LeerLineas("1", "REI", 80862)).Returns(new List<FilaReposicion>
            {
                new FilaReposicion { Producto = "A", Descripcion = "Producto A", Cantidad = 2 }
            });
            return (cierre, transaccion);
        }

        private static SolicitudTerminarRecepcion SolicitudReposicionLeyendo(params (string producto, int cantidad)[] lecturas)
        {
            SolicitudTerminarRecepcion solicitud = SolicitudReposicion();
            solicitud.Lecturas = lecturas.ToDictionary(l => l.producto, l => l.cantidad);
            return solicitud;
        }

        // Decisión provisional (04/10/26, #553): hasta que Carlos decida qué se hace con las diferencias, una reposición solo
        // se termina desde aquí si lo leído coincide EXACTAMENTE con lo enviado; si no, 409 y se hace en Nesto viejo
        [DataTestMethod]
        [DataRow("A", 1, "falta 1 de A")]
        [DataRow("A", 3, "sobra 1 de A")]
        public async Task Reposiciones_Terminar_LoLeidoNoCoincideConLoEnviado_409YNoSeContabilizaNada(string producto, int cantidad, string motivo)
        {
            var (cierre, transaccion) = CierreFalso(80862);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Reposiciones(cierre).Terminar(SolicitudReposicionLeyendo((producto, cantidad))));

            Assert.AreEqual(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, motivo);
            StringAssert.Contains(ex.Message, "Nesto");
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => transaccion.RegistrarEvidencia(A<string>.Ignored, A<IEnumerable<EvidenciaRecepcion>>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_ProductoQueNoVeniaEnLaReposicion_409()
        {
            var (cierre, transaccion) = CierreFalso(80862);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Reposiciones(cierre).Terminar(SolicitudReposicionLeyendo(("A", 2), ("Z", 1))));

            Assert.AreEqual(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, "Z");
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_DiarioConFilasQueSoloTrataBienNestoViejo_409()
        {
            // Con hueco o en negativo: prdExtrProducto llamado desde la API no hace con ellas lo que hace desde Nesto viejo
            var (cierre, transaccion) = CierreFalso(80862);
            A.CallTo(() => transaccion.FilasQueSoloSabeNestoViejo("1", "PendRepo")).Returns(1);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Reposiciones(cierre).Terminar(SolicitudReposicion()));

            Assert.AreEqual(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_LoQueEntraQuedaPendienteDeUbicar_LeidoAntesYPuestoDespuesDeContabilizar()
        {
            // Regresión 04/10/26: desde la API prdExtrProducto no encuentra el almacén del usuario (busca el de SYSTEM_USER,
            // RDS2016$) y se salta el INSERT en Ubicaciones (estado 2, NºTraspasoRepo): nada quedaba «pendiente de ubicar»
            var (cierre, transaccion) = CierreFalso(80862);
            var llamadasCierre = new List<string>();
            var pendientes = new PendientesDeUbicarEntrada { UltimaUbicacion = 322200201 };
            A.CallTo(() => transaccion.LeerPendientesDeUbicar("1", "PendRepo")).Invokes(() => llamadasCierre.Add("leer pendientes")).Returns(pendientes);
            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", A<string>.Ignored)).Invokes(() => llamadasCierre.Add("contabilizar"));
            A.CallTo(() => transaccion.DejarPendientesDeUbicar("1", pendientes, "NUEVAVISION\\Reina")).Invokes(() => llamadasCierre.Add("dejar pendientes"));

            _ = await Reposiciones(cierre).Terminar(SolicitudReposicion());

            CollectionAssert.AreEqual(new[] { "leer pendientes", "contabilizar", "dejar pendientes" }, llamadasCierre);
        }

        private static SolicitudTerminarRecepcion SolicitudReposicion(string documento = "80862")
        {
            return new SolicitudTerminarRecepcion
            {
                Empresa = "1", Almacen = "REI", Documento = documento, IdRecepcion = Guid.NewGuid(),
                Lecturas = new Dictionary<string, int> { ["A"] = 2 }, Usuario = "NUEVAVISION\\Reina", Principal = Usuario("NUEVAVISION\\Reina", "Tiendas")
            };
        }

        [DataTestMethod]
        [DataRow("NUEVAVISION\\Reina", "REI", true)]
        [DataRow("Reina", "rei ", true)]
        [DataRow("NUEVAVISION\\Reina", "ALG", false)]
        [DataRow("NUEVAVISION\\Paloma", "ALC", true)]
        [DataRow("Andre", "REI", false)]
        [DataRow("Desconocido", "ALG", false)]
        public void Reposiciones_SoloDaEntradaQuienTieneElAlmacenDeDestino(string usuario, string almacen, bool puede)
        {
            Assert.AreEqual(puede, Reposiciones().PuedeTerminar(Usuario(usuario, "Tiendas"), "1", almacen));
        }

        [TestMethod]
        public void Reposiciones_SeTerminanDesdeAqui()
        {
            Assert.IsTrue(Reposiciones().SeTerminaDesdeAqui);
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_ContabilizaElDiarioDeEntradaDelDestinoConElUsuario()
        {
            var (cierre, transaccion) = CierreFalso(80862);

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones(cierre).Terminar(SolicitudReposicion());

            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", "NUEVAVISION\\Reina")).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.RegistrarEvidencia("1", A<IEnumerable<EvidenciaRecepcion>>.That.Matches(e => e.Single().NumeroOrigen == 80862)))
                .MustHaveHappenedOnceExactly();
            Assert.IsFalse(resultado.YaEstabaTerminada);
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_OtrasDelMismoDiarioEntranTambienYSeDice()
        {
            // prdExtrProducto contabiliza el diario entero: hoy a veces entran dos traspasos juntos (lo leído se compara
            // solo con el que se recibe)
            var (cierre, _) = CierreFalso(80862, 80863);

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones(cierre).Terminar(SolicitudReposicion());

            StringAssert.Contains(string.Join(" ", resultado.Avisos), "80863");
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_YaNoEstaEnElDiario_ErrorYNoSeContabilizaNada()
        {
            var (cierre, transaccion) = CierreFalso(80863);

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Reposiciones(cierre).Terminar(SolicitudReposicion()));

            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reposiciones_Terminar_Reenvio_NoContabilizaOtraVez()
        {
            var (cierre, transaccion) = CierreFalso(80862);
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(true);

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones(cierre).Terminar(SolicitudReposicion());

            Assert.IsTrue(resultado.YaEstabaTerminada);
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        private static Func<Task<List<FilaEnsayoDTO>>> FotoQueCuenta(List<string> llamadas)
        {
            return () =>
            {
                llamadas.Add("foto");
                return Task.FromResult(new List<FilaEnsayoDTO>());
            };
        }

        [TestMethod]
        public async Task Reposiciones_DeVerdad_SeGuarda()
        {
            var (cierre, _) = CierreFalso(80862);

            _ = await Reposiciones(cierre).Terminar(SolicitudReposicion());

            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, false))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reposiciones_Ensayo_DeshaceSiempreYFotografiaAntesYDespuesDeContabilizar()
        {
            var (cierre, transaccion) = CierreFalso(80862);
            var pasos = new List<string>();
            A.CallTo(() => transaccion.PrepararFoto("1", "PendRepo", A<IReadOnlyCollection<int>>.That.Contains(80862)))
                .Returns(FotoQueCuenta(pasos));
            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", A<string>.Ignored)).Invokes(() => pasos.Add("contabilizar"));
            SolicitudTerminarRecepcion solicitud = SolicitudReposicion();
            solicitud.Ensayo = new RegistroEnsayoRecepcion();

            _ = await Reposiciones(cierre).Terminar(solicitud);

            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, true))
                .MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "foto", "contabilizar", "foto" }, pasos);
            Assert.IsNotNull(solicitud.Ensayo.Despues);
        }

        [TestMethod]
        public async Task Compras_DeVerdad_SeGuarda()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));

            _ = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5)));

            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, false))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Compras_Ensayo_DeshaceSiempreFotografiaYNoAvisaACompras()
        {
            LineasDelProveedor(Linea(100, 1, "A", 5));
            var pasos = new List<string>();
            A.CallTo(() => transaccion.PrepararFoto("1", A<IReadOnlyCollection<int>>.That.Contains(100))).Returns(FotoQueCuenta(pasos));
            SolicitudTerminarRecepcion solicitud = Solicitud(Usuario("Pedro", "Almacén"), ("A", 7), ("Z", 1));
            solicitud.Ensayo = new RegistroEnsayoRecepcion();

            _ = await compras.Terminar(solicitud);

            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, true))
                .MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "foto", "foto" }, pasos);
            A.CallTo(() => avisador.Avisar(A<string>.Ignored, A<IEnumerable<string>>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Compras_BuscarPorCodigo_ElProveedorDeLoQueTienePendiente()
        {
            // 02/10/26: los 441 productos pendientes de recibir en ALG tienen un solo proveedor cada uno
            A.CallTo(() => repositorio.ProveedoresConPendiente("1", "ALG", "8436620930427")).Returns(new List<string> { "65" });
            A.CallTo(() => repositorio.LeerPedidosPendientes("1", "ALG")).Returns(new List<PedidoCompraPendienteDTO>
            {
                new PedidoCompraPendienteDTO { Pedido = 100, Proveedor = "65", NombreProveedor = "MAYSTAR", Lineas = 1, Unidades = 4 },
                new PedidoCompraPendienteDTO { Pedido = 102, Proveedor = "16", NombreProveedor = "DRV", Lineas = 1, Unidades = 1 }
            });

            List<RecepcionPendienteDTO> encontradas = await compras.BuscarPorCodigo("1", "ALG", "8436620930427");

            Assert.AreEqual("MAYSTAR", encontradas.Single().Titulo);
        }

        [TestMethod]
        public async Task Terminar_LoQueSiguePasaAMananaAntesDelAlbaran_SinTocarElVistoBueno()
        {
            // Lo de siempre (prdInsertarLineaCmp): lo que no se recibe hoy se mueve de fecha; el visto bueno no se toca
            LineasDelProveedor(Linea(100, 1, "A", 5), Linea(100, 2, "B", 3));

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 5)));

            CollectionAssert.AreEqual(new[] { "aplazar al 06/10", "albarán 100" }, llamadas);
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
        public async Task Terminar_Recuperada_SeReactivaAntesDeRecibirlaYSeDevuelveSinAvisarACompras()
        {
            LineaCompraPendiente anulada = Linea(150, 9, "A", 3, control: false);
            anulada.Estado = PlanificadorRecepcionCompra.ESTADO_ANULADA;
            anulada.FechaRecepcion = HOY.AddDays(-6);
            LineasDelProveedor(anulada);
            A.CallTo(() => transaccion.Reactivar("1", 9, "Pedro")).Invokes(() => llamadas.Add("reactivar 9"));
            A.CallTo(() => transaccion.RecibirLinea("1", A<LineaCompraPendiente>.Ignored, A<LineaRecibida>.Ignored, HOY, "Pedro"))
                .Invokes(() => llamadas.Add("recibir"));

            ResultadoTerminarRecepcionDTO resultado = await compras.Terminar(Solicitud(Usuario("Pedro", "Almacén"), ("A", 3)));

            CollectionAssert.AreEqual(new[] { "reactivar 9", "recibir", "albarán 150" }, llamadas);
            LineaRecuperadaDTO recuperada = resultado.Recuperadas.Single();
            Assert.AreEqual(150, recuperada.Pedido);
            StringAssert.Contains(recuperada.Texto, "no servido");
            A.CallTo(() => avisador.Avisar(A<string>.Ignored, A<IEnumerable<string>>.Ignored)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Avisos.Count);
        }

        [TestMethod]
        public async Task LeerEsperado_Las99RecientesSeVenComoRecuperables()
        {
            A.CallTo(() => repositorio.LeerLineasPendientesProveedor("1", "ALG", "65")).Returns(new List<FilaRecepcionCompra>
            {
                new FilaRecepcionCompra { Pedido = 200, LineaPedido = 5, Proveedor = "65", Producto = "A", Cantidad = 5, Estado = 1, FechaRecepcion = HOY },
                new FilaRecepcionCompra { Pedido = 150, LineaPedido = 9, Proveedor = "65", Producto = "A", Cantidad = 3, Estado = -99, FechaRecepcion = HOY.AddDays(-6) },
                new FilaRecepcionCompra { Pedido = 150, LineaPedido = 10, Proveedor = "65", Producto = "Q", Cantidad = 2, Estado = -99, FechaRecepcion = HOY.AddDays(-6) }
            });

            RecepcionDTO recepcion = await compras.LeerEsperado("1", "ALG", "65");

            LineaRecepcionDTO a = recepcion.Lineas.Single(l => l.Producto == "A");
            Assert.AreEqual(5, a.Cantidad, "Lo -99 no es lo esperado: se puede recuperar si llega");
            Assert.AreEqual(3, a.Recuperables.Single().Cantidad);
            Assert.AreEqual(150, a.Recuperables.Single().Pedido);
            Assert.AreEqual(HOY.AddDays(-6), a.Recuperables.Single().FechaNoServido);
            LineaRecepcionDTO q = recepcion.Lineas.Single(l => l.Producto == "Q");
            Assert.AreEqual(0, q.Cantidad);
            Assert.AreEqual(2, q.Recuperables.Single().Cantidad);
        }

        [TestMethod]
        public async Task LeerPendientes_UnProveedorConSoloLineas99_NoSaleComoPendiente()
        {
            A.CallTo(() => repositorio.LeerPedidosPendientes("1", "ALG")).Returns(new List<PedidoCompraPendienteDTO>());

            Assert.AreEqual(0, (await compras.LeerPendientes("1", "ALG")).Count);
        }

        [TestMethod]
        public async Task LeerEsperado_SinNadaPendiente_Null()
        {
            A.CallTo(() => repositorio.LeerLineasPendientesProveedor("1", "ALG", "65")).Returns(new List<FilaRecepcionCompra>());

            Assert.IsNull(await compras.LeerEsperado("1", "ALG", "65"));
        }
    }
}
