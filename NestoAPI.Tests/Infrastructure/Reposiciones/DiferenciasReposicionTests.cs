using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#634 (corte 1): diferencias de reposición. Caso real: traspaso 80915 (ALG → ALC), Andre preparó 1 ud. de la
    /// 38631 que no fue en la caja y Paloma recibió 0: Algete se quedó con una unidad menos en el sistema de las que tenía.
    /// Y al revés, 80906 (ALC → ALG): 6 enviadas, 7 leídas.
    /// </summary>
    [TestClass]
    public class DiferenciasReposicionTests
    {
        private ITransaccionDiferenciasReposicion transaccion;
        private RepositorioFalso repositorio;
        private bool interruptor;
        private Dictionary<string, string> almacenes;

        private class RepositorioFalso : IRepositorioDiferenciasReposicion
        {
            private readonly ITransaccionDiferenciasReposicion transaccion;
            public int Transacciones { get; private set; }
            public List<DiferenciaReposicionDTO> Filas { get; } = new List<DiferenciaReposicionDTO>();
            public string UltimoOrigenListado { get; private set; } = "sin llamar";

            public RepositorioFalso(ITransaccionDiferenciasReposicion transaccion)
            {
                this.transaccion = transaccion;
            }

            public Task<T> EnTransaccion<T>(Func<ITransaccionDiferenciasReposicion, Task<T>> trabajo)
            {
                Transacciones++;
                return trabajo(transaccion);
            }

            public Task<List<DiferenciaReposicionDTO>> Listar(string empresa, string estado, string origen)
            {
                UltimoOrigenListado = origen;
                return Task.FromResult(Filas.Where(f => (estado == null || f.Estado == estado) && (origen == null || f.Origen == origen)).ToList());
            }

            public Task<DiferenciaReposicionDTO> Leer(string empresa, int id) => Task.FromResult(Filas.FirstOrDefault(f => f.Id == id));

            public Task<int> ContarPendientes(string empresa, string origen) =>
                Task.FromResult(Filas.Count(f => f.Estado == EstadosDiferenciaReposicion.PENDIENTE && (origen == null || f.Origen == origen)));
        }

        [TestInitialize]
        public void Inicializar()
        {
            transaccion = A.Fake<ITransaccionDiferenciasReposicion>();
            repositorio = new RepositorioFalso(transaccion);
            interruptor = true;
            almacenes = new Dictionary<string, string> { ["Paloma"] = "ALC", ["Reina"] = "REI" };
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).Returns(new ResultadoMovimientoStock { NumeroOrdenExtracto = 4700001 });
        }

        private ServicioDiferenciasReposicion Servicio()
        {
            return new ServicioDiferenciasReposicion(repositorio, () => interruptor, (e, u) => almacenes.TryGetValue(u, out string a) ? a : null);
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        /// <summary>80915: enviado 1, leído 0 de la 38631, de ALG (con control de ubicaciones) a ALC.</summary>
        private static DiferenciaReposicionDTO Caso80915(string ajuste = null)
        {
            return new DiferenciaReposicionDTO
            {
                Id = 1, Empresa = "1", Traspaso = 80915, Origen = "ALG", Destino = "ALC", Producto = "38631",
                Enviado = 1, Leido = 0, Diferencia = -1, Estado = EstadosDiferenciaReposicion.PENDIENTE, Ajuste = ajuste,
                OrigenConControlUbicaciones = true, HuecoSalida = "005/005/004", UsuarioRecepcion = "NUEVAVISION\\Paloma", Preparador = "NUEVAVISION\\Andre"
            };
        }

        /// <summary>80906: enviadas 6, leídas 7 de la 39631, de ALC (sin control de ubicaciones) a ALG.</summary>
        private static DiferenciaReposicionDTO Caso80906(string ajuste = null)
        {
            return new DiferenciaReposicionDTO
            {
                Id = 2, Empresa = "1", Traspaso = 80906, Origen = "ALC", Destino = "ALG", Producto = "39631",
                Enviado = 6, Leido = 7, Diferencia = 1, Estado = EstadosDiferenciaReposicion.PENDIENTE, Ajuste = ajuste
            };
        }

        private void LaTransaccionLee(DiferenciaReposicionDTO fila)
        {
            A.CallTo(() => transaccion.Leer("1", fila.Id)).Returns(fila);
        }

        // ---------------------------------------------------------------- ajuste al recibir

        [TestMethod]
        public async Task Ajustar_Caso38631_80915_RecibidoDeMenos_LaDiferenciaVuelveAlStockDeAlgete()
        {
            // Regresión del caso: Algete se quedaba con −1 sin que nadie lo corrigiera
            LaTransaccionLee(Caso80915());

            List<ResultadoAjusteDiferencia> resultado = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 1 }, "NUEVAVISION\\Paloma");

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m =>
                m.Almacen == "ALG" && m.Producto == "38631" && m.Cantidad == 1 && m.Traspaso == 80915 && !m.PrimeroPendienteDeUbicar &&
                m.Texto == "Diferencia de reposición 80915 (recibido en ALC)"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.GuardarAjuste(1, AjustesDiferenciaReposicion.HECHO, 4700001, null)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(AjustesDiferenciaReposicion.HECHO, resultado.Single().Ajuste);
        }

        [TestMethod]
        public async Task Ajustar_RecibidoDeMas_SeDescuentaDelOrigenEmpezandoPorElHuecoDelTraspaso()
        {
            LaTransaccionLee(Caso80906());

            _ = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 2 }, "NUEVAVISION\\Alfredo");

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m =>
                m.Almacen == "ALC" && m.Producto == "39631" && m.Cantidad == -1 && !m.PrimeroPendienteDeUbicar))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ajustar_InterruptorApagado_NoTocaElStockYQuedaApagado()
        {
            interruptor = false;
            LaTransaccionLee(Caso80915());

            List<ResultadoAjusteDiferencia> resultado = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 1 }, "x");

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.GuardarAjuste(1, AjustesDiferenciaReposicion.APAGADO, null, null)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(AjustesDiferenciaReposicion.APAGADO, resultado.Single().Ajuste);
        }

        [TestMethod]
        public async Task Ajustar_YaAjustada_NoAjustaDosVeces()
        {
            LaTransaccionLee(Caso80915(AjustesDiferenciaReposicion.HECHO));

            _ = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 1 }, "x");

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.GuardarAjuste(A<int>._, A<string>._, A<int?>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ajustar_SiFallaPrdExtrProducto_QuedaFallidoConElMotivoYNoLanza()
        {
            LaTransaccionLee(Caso80906());
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).Throws(new Exception("No se puede realizar la acción. Se quedaría un stock negativo."));

            List<ResultadoAjusteDiferencia> resultado = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 2 }, "x");

            Assert.AreEqual(AjustesDiferenciaReposicion.FALLIDO, resultado.Single().Ajuste);
            StringAssert.Contains(resultado.Single().Mensaje, "stock negativo");
            A.CallTo(() => transaccion.GuardarAjuste(2, AjustesDiferenciaReposicion.FALLIDO, null, A<string>.That.Contains("stock negativo")))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, repositorio.Transacciones, "El fallo se apunta en otra transacción (la del ajuste se ha deshecho)");
        }

        [TestMethod]
        public async Task Ajustar_SinOrigenConocido_QuedaFallidoSinTocarStock()
        {
            DiferenciaReposicionDTO fila = Caso80915();
            fila.Origen = null;
            LaTransaccionLee(fila);

            List<ResultadoAjusteDiferencia> resultado = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 1 }, "x");

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            Assert.AreEqual(AjustesDiferenciaReposicion.FALLIDO, resultado.Single().Ajuste);
        }

        [TestMethod]
        public async Task Ajustar_SalidaQueNoEstaEnNingunHueco_LoDiceEnElMensaje()
        {
            LaTransaccionLee(Caso80906());
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).Returns(new ResultadoMovimientoStock { NumeroOrdenExtracto = 9, UnidadesSinHueco = 1 });

            List<ResultadoAjusteDiferencia> resultado = await Servicio().AjustarOrigenTrasRecibir("1", new[] { 2 }, "x");

            Assert.AreEqual(AjustesDiferenciaReposicion.HECHO, resultado.Single().Ajuste);
            StringAssert.Contains(resultado.Single().Mensaje, "ningún hueco");
        }

        // ---------------------------------------------------------------- resolver: qué se mueve

        // (diferencia, ¿ajustada al recibir?, resolución) → (movimiento en el origen, movimiento en el destino)
        [DataTestMethod]
        [DataRow(-1, true, "Confirmada", 0, 0)]
        [DataRow(-1, true, "NoEstaba", -1, 0)]
        [DataRow(-1, true, "ErrorRecepcion", -1, 1)]
        [DataRow(1, true, "Confirmada", 0, 0)]
        [DataRow(1, true, "Sobraba", 1, 0)]
        [DataRow(1, true, "ErrorRecepcion", 1, -1)]
        [DataRow(-1, false, "Confirmada", 1, 0)]
        [DataRow(-1, false, "NoEstaba", 0, 0)]
        [DataRow(1, false, "Confirmada", -1, 0)]
        [DataRow(-2, false, "ErrorRecepcion", 0, 2)]
        public void Planificar_LoQueQuedaPorMover(int diferencia, bool ajustada, string resolucion, int origen, int destino)
        {
            var fila = new DiferenciaReposicionDTO { Diferencia = diferencia, Ajuste = ajustada ? AjustesDiferenciaReposicion.HECHO : null };

            ServicioDiferenciasReposicion.PlanResolucion plan = ServicioDiferenciasReposicion.Planificar(fila, resolucion);

            Assert.AreEqual(origen, plan.Origen, "origen");
            Assert.AreEqual(destino, plan.Destino, "destino");
        }

        [TestMethod]
        public async Task Resolver_Confirmada_TrasElAjuste_SoloCierraConQuienYComentario()
        {
            LaTransaccionLee(Caso80915(AjustesDiferenciaReposicion.HECHO));

            ResultadoResolverDiferenciaReposicionDTO resultado = await Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "confirmada", Comentario = " Estaba en 005/005/004 " }, Usuario("Santiago", "Almacén"));

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.GuardarResolucion(1, "Confirmada", "Santiago", "Estaba en 005/005/004", null, null)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(resultado.Mensaje, "No se ha tocado el stock");
        }

        [TestMethod]
        public async Task Resolver_NoEstaba_BajaEnElOrigenQuitandoPrimeroLoPendienteDeUbicar()
        {
            LaTransaccionLee(Caso80915(AjustesDiferenciaReposicion.HECHO));

            ResultadoResolverDiferenciaReposicionDTO resultado = await Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "NoEstaba" }, Usuario("Santiago", "Almacén"));

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m =>
                m.Almacen == "ALG" && m.Cantidad == -1 && m.PrimeroPendienteDeUbicar && m.Texto.Contains("no estaba en ALG")))).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.GuardarResolucion(1, "NoEstaba", "Santiago", null, 4700001, null)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(resultado.Mensaje, "Stock corregido");
        }

        [TestMethod]
        public async Task Resolver_ErrorRecepcion_DeshaceElOrigenYCorrigeElDestino()
        {
            LaTransaccionLee(Caso80906(AjustesDiferenciaReposicion.HECHO));
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Almacen == "ALC")))
                .Returns(new ResultadoMovimientoStock { NumeroOrdenExtracto = 11 });
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Almacen == "ALG")))
                .Returns(new ResultadoMovimientoStock { NumeroOrdenExtracto = 12 });

            _ = await Servicio().Resolver("1", 2, new ResolverDiferenciaReposicionDTO { Resolucion = "ErrorRecepcion" }, Usuario("Carlos", "Dirección"));

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Almacen == "ALC" && m.Cantidad == 1))).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Almacen == "ALG" && m.Cantidad == -1))).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.GuardarResolucion(2, "ErrorRecepcion", "Carlos", null, 11, 12)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Resolver_InterruptorApagado_SoloApuntaYDiceQueNoSeHaTocadoElStock()
        {
            interruptor = false;
            LaTransaccionLee(Caso80915(AjustesDiferenciaReposicion.APAGADO));

            ResultadoResolverDiferenciaReposicionDTO resultado = await Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" }, Usuario("Santiago", "Almacén"));

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.GuardarResolucion(1, "Confirmada", "Santiago", null, null, null)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(resultado.Mensaje, "apagado");
        }

        [TestMethod]
        public async Task Resolver_SobrabaEnUnaFalta_400SinTocarNada()
        {
            LaTransaccionLee(Caso80915(AjustesDiferenciaReposicion.HECHO));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "Sobraba" }, Usuario("Santiago", "Almacén")));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            A.CallTo(() => transaccion.GuardarResolucion(A<int>._, A<string>._, A<string>._, A<string>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Resolver_ResolucionQueNoExiste_400()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "Perdida" }, Usuario("Santiago", "Almacén")));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public async Task Resolver_YaResuelta_409()
        {
            DiferenciaReposicionDTO fila = Caso80915(AjustesDiferenciaReposicion.HECHO);
            fila.Estado = EstadosDiferenciaReposicion.RESUELTA;
            fila.Resolucion = "Confirmada";
            LaTransaccionLee(fila);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "NoEstaba" }, Usuario("Santiago", "Almacén")));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Resolver_NoExiste_404()
        {
            A.CallTo(() => transaccion.Leer("1", 99)).Returns((DiferenciaReposicionDTO)null);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Resolver("1", 99,
                new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" }, Usuario("Santiago", "Almacén")));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        // ---------------------------------------------------------------- permisos

        [TestMethod]
        public async Task Resolver_LaTiendaResuelveLasDeSuAlmacen()
        {
            LaTransaccionLee(Caso80906(AjustesDiferenciaReposicion.HECHO));

            _ = await Servicio().Resolver("1", 2, new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" }, Usuario("NUEVAVISION\\Paloma", "Tiendas"));

            A.CallTo(() => transaccion.GuardarResolucion(2, "Confirmada", "NUEVAVISION\\Paloma", null, null, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Resolver_LaTiendaNoResuelveLasDeAlgeteNiLasDeOtraTienda()
        {
            LaTransaccionLee(Caso80915(AjustesDiferenciaReposicion.HECHO));
            LaTransaccionLee(Caso80906(AjustesDiferenciaReposicion.HECHO));

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => Servicio().Resolver("1", 1,
                new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" }, Usuario("Paloma", "Tiendas")));
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => Servicio().Resolver("1", 2,
                new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" }, Usuario("Reina", "Tiendas")));

            A.CallTo(() => transaccion.GuardarResolucion(A<int>._, A<string>._, A<string>._, A<string>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Listar_AlmacenVeLasDelAlmacenQuePideYTodasSiNoPide()
        {
            repositorio.Filas.Add(Caso80915());
            repositorio.Filas.Add(Caso80906());

            List<DiferenciaReposicionDTO> deAlgete = await Servicio().Listar("1", "Pendiente", "alg", Usuario("Andre", "Almacén"));
            List<DiferenciaReposicionDTO> todas = await Servicio().Listar("1", null, null, Usuario("Andre", "Almacén"));

            Assert.AreEqual("38631", deAlgete.Single().Producto);
            Assert.AreEqual(2, todas.Count);
            StringAssert.Contains(deAlgete.Single().QueComprobar, "sigue en ALG");
            CollectionAssert.AreEqual(new[] { "Confirmada", "NoEstaba", "ErrorRecepcion" }, deAlgete.Single().ResolucionesPosibles);
        }

        [TestMethod]
        public async Task Listar_LaTiendaSoloVeLasSuyas()
        {
            repositorio.Filas.Add(Caso80915());
            repositorio.Filas.Add(Caso80906());

            List<DiferenciaReposicionDTO> suyas = await Servicio().Listar("1", "Pendiente", null, Usuario("NUEVAVISION\\Paloma", "Tiendas"));

            Assert.AreEqual("ALC", repositorio.UltimoOrigenListado);
            Assert.AreEqual("39631", suyas.Single().Producto);
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => Servicio().Listar("1", "Pendiente", "ALG", Usuario("Paloma", "Tiendas")));
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => Servicio().Listar("1", "Pendiente", null, Usuario("SinAlmacen")));
        }

        [TestMethod]
        public async Task Listar_EstadoQueNoExiste_400()
        {
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Listar("1", "Abierta", null, Usuario("Andre", "Almacén")));
        }

        [TestMethod]
        public void Completar_ExplicaQuePasoYQueComprobar()
        {
            DiferenciaReposicionDTO falta = Caso80915();
            DiferenciaReposicionDTO sobra = Caso80906();

            ServicioDiferenciasReposicion.Completar(falta);
            ServicioDiferenciasReposicion.Completar(sobra);

            Assert.AreEqual("En ALC se recibieron 0 de 1.", falta.QuePaso);
            StringAssert.Contains(falta.QueComprobar, "005/005/004");
            Assert.AreEqual("En ALG se recibieron 7 de 6.", sobra.QuePaso);
            StringAssert.Contains(sobra.QueComprobar, "Sobraba");
            CollectionAssert.AreEqual(new[] { "Confirmada", "Sobraba", "ErrorRecepcion" }, sobra.ResolucionesPosibles);
        }

        // ---------------------------------------------------------------- endpoints

        private static ReposicionesController Controlador(IServicioDiferenciasReposicion diferencias, IPrincipal usuario = null)
        {
            var controlador = new ReposicionesController(null, A.Fake<IServicioPreparacionReposicion>(), diferencias: diferencias)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controlador.User = usuario ?? Usuario("Andre", "Almacén");
            return controlador;
        }

        [TestMethod]
        public async Task GetDiferencias_PendientesPorDefecto()
        {
            IServicioDiferenciasReposicion servicio = A.Fake<IServicioDiferenciasReposicion>();
            A.CallTo(() => servicio.Listar("1", "Pendiente", "ALG", A<IPrincipal>._)).Returns(new List<DiferenciaReposicionDTO> { Caso80915() });

            IHttpActionResult respuesta = await Controlador(servicio).GetDiferencias(almacen: "ALG");

            Assert.AreEqual(1, ((OkNegotiatedContentResult<List<DiferenciaReposicionDTO>>)respuesta).Content.Count);
        }

        [TestMethod]
        public async Task GetDiferencias_SinPermiso_403()
        {
            IServicioDiferenciasReposicion servicio = A.Fake<IServicioDiferenciasReposicion>();
            A.CallTo(() => servicio.Listar(A<string>._, A<string>._, A<string>._, A<IPrincipal>._)).Throws(new UnauthorizedAccessException("no"));

            IHttpActionResult respuesta = await Controlador(servicio).GetDiferencias(almacen: "ALG");

            HttpResponseMessage mensaje = await respuesta.ExecuteAsync(default);
            Assert.AreEqual(HttpStatusCode.Forbidden, mensaje.StatusCode);
        }

        [TestMethod]
        public async Task GetDiferencia_NoExiste_404()
        {
            IServicioDiferenciasReposicion servicio = A.Fake<IServicioDiferenciasReposicion>();
            A.CallTo(() => servicio.Leer("1", 7, A<IPrincipal>._)).Returns((DiferenciaReposicionDTO)null);

            IHttpActionResult respuesta = await Controlador(servicio).GetDiferencia(7);

            Assert.IsInstanceOfType(respuesta, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetContadorDiferencias_DevuelveLasPendientes()
        {
            IServicioDiferenciasReposicion servicio = A.Fake<IServicioDiferenciasReposicion>();
            A.CallTo(() => servicio.ContarPendientes("1", "ALG", A<IPrincipal>._)).Returns(new ContadorDiferenciasReposicionDTO { Almacen = "ALG", Pendientes = 3 });

            IHttpActionResult respuesta = await Controlador(servicio).GetContadorDiferencias("ALG");

            Assert.AreEqual(3, ((OkNegotiatedContentResult<ContadorDiferenciasReposicionDTO>)respuesta).Content.Pendientes);
        }

        [TestMethod]
        public async Task PostResolverDiferencia_SinResolucion_400YNoLlamaAlServicio()
        {
            IServicioDiferenciasReposicion servicio = A.Fake<IServicioDiferenciasReposicion>();

            IHttpActionResult respuesta = await Controlador(servicio).PostResolverDiferencia(1, new ResolverDiferenciaReposicionDTO());

            Assert.IsInstanceOfType(respuesta, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.Resolver(A<string>._, A<int>._, A<ResolverDiferenciaReposicionDTO>._, A<IPrincipal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostResolverDiferencia_PasaLaPeticionYElUsuario()
        {
            IServicioDiferenciasReposicion servicio = A.Fake<IServicioDiferenciasReposicion>();
            var peticion = new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" };
            A.CallTo(() => servicio.Resolver("1", 1, peticion, A<IPrincipal>.That.Matches(u => u.Identity.Name == "Andre")))
                .Returns(new ResultadoResolverDiferenciaReposicionDTO { Mensaje = "ok" });

            IHttpActionResult respuesta = await Controlador(servicio).PostResolverDiferencia(1, peticion);

            Assert.AreEqual("ok", ((OkNegotiatedContentResult<ResultadoResolverDiferenciaReposicionDTO>)respuesta).Content.Mensaje);
        }

        // ---------------------------------------------------------------- Ubicaciones (puerta)

        private static FilaUbicacionDiferencia Fila(int orden, int estado, string pasillo, int cantidad, bool delTraspaso = false, int minutos = 0)
        {
            return new FilaUbicacionDiferencia
            {
                NumeroOrden = orden, Estado = estado, Pasillo = pasillo, Fila = pasillo == null ? null : "005", Columna = pasillo == null ? null : "004",
                Cantidad = cantidad, DelTraspaso = delTraspaso, FechaCreacion = new DateTime(2026, 10, 8).AddMinutes(minutos), Producto = "38631"
            };
        }

        [TestMethod]
        public void Planificador_AlRecibirDeMas_PrimeroElHuecoDelTraspaso()
        {
            var filas = new[] { Fila(1, 2, null, 1), Fila(2, 0, "005", 2, delTraspaso: true) };

            List<TomaDeHueco> tomas = PlanificadorDiferenciaUbicaciones.Repartir(filas, 1, primeroPendienteDeUbicar: false);

            Assert.AreEqual(2, tomas.Single().Libre.NumeroOrden);
            Assert.IsFalse(tomas.Single().Entera);
        }

        [TestMethod]
        public void Planificador_AlResolver_PrimeroLoPendienteDeUbicarMasNuevo()
        {
            var filas = new[] { Fila(1, 2, null, 1, minutos: 0), Fila(3, 2, null, 1, minutos: 10), Fila(2, 0, "005", 2, delTraspaso: true) };

            List<TomaDeHueco> tomas = PlanificadorDiferenciaUbicaciones.Repartir(filas, 2, primeroPendienteDeUbicar: true);

            CollectionAssert.AreEqual(new[] { 3, 1 }, tomas.Select(t => t.Libre.NumeroOrden).ToList());
            Assert.IsTrue(tomas.All(t => t.Entera));
        }

        [TestMethod]
        public void Planificador_SinHuecoDelTraspaso_SoloSiElProductoEstaEnUnHueco()
        {
            var unHueco = new[] { Fila(5, 0, "007", 3) };
            var dosHuecos = new[] { Fila(5, 0, "007", 3), Fila(6, 0, "008", 3) };

            Assert.AreEqual(1, PlanificadorDiferenciaUbicaciones.Repartir(unHueco, 1, false).Sum(t => t.Cantidad));
            Assert.AreEqual(0, PlanificadorDiferenciaUbicaciones.Repartir(dosHuecos, 1, false).Count, "Con varios huecos no se sabe de cuál: no se toca");
        }

        [TestMethod]
        public void Planificador_NoLlega_CogeLoQueHay()
        {
            var filas = new[] { Fila(2, 0, "005", 1, delTraspaso: true) };

            List<TomaDeHueco> tomas = PlanificadorDiferenciaUbicaciones.Repartir(filas, 3, false);

            Assert.AreEqual(1, tomas.Sum(t => t.Cantidad));
        }

        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_ElAjusteVaPorElDiarioDeErroresDeReposicion()
        {
            Assert.AreEqual("_ErrRepo", TransaccionDiferenciasReposicionSql.DIARIO);
            StringAssert.Contains(DiferenciasReposicionSql.SQL_INSERTAR, "OBJECT_ID(N'dbo.DiferenciasReposiciones', N'U') IS NULL");
            StringAssert.Contains(DiferenciasReposicionSql.SQL_INSERTAR, "IF NOT EXISTS", "Una por traspaso y producto");
            StringAssert.Contains(DiferenciasReposicionSql.SQL_LEER_BLOQUEANDO, "WITH (UPDLOCK, HOLDLOCK)");
            StringAssert.Contains(DiferenciasReposicionSql.SQL_GUARDAR_RESOLUCION, "AND Estado = 'Pendiente'");
        }

        [TestMethod]
        public void Sql_LaSalidaPorDiferenciaSoloCogeLoLibreYSinReservar()
        {
            string sql = PuertaUbicacionesSql.SQL_FILAS_PARA_DIFERENCIA;
            StringAssert.Contains(sql, "u.Estado IN (0, 2)");
            StringAssert.Contains(sql, "u.[NºOrdenVta] IS NULL");
            StringAssert.Contains(sql, "u.[NºOrdenRepo] IS NULL");
            StringAssert.Contains(sql, "s.[NºTraspasoRepo] = @p4 AND s.Estado = -4");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_ENTRADA_PENDIENTE_DE_UBICAR, "Cantidad, Estado, Usuario) VALUES (@p0, @p1, @p2, @p3, 2, @p4)");
        }
    }
}
