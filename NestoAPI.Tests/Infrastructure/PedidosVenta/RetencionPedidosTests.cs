using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Repositorio en memoria de PedidosRetenidos y de las cabeceras, para los tests de NestoAPI#698.</summary>
    internal class RepositorioPedidosRetenidosEnMemoria : IRepositorioPedidosRetenidos
    {
        public bool Existe { get; set; } = true;
        public Dictionary<int, CabeceraPedidoRetencion> Cabeceras { get; } = new Dictionary<int, CabeceraPedidoRetencion>();
        public List<RetencionPedidoDTO> Filas { get; } = new List<RetencionPedidoDTO>();
        public List<(int pedido, byte? modo, string usuario, string anterior, string nuevo)> CambiosModo { get; } = new List<(int, byte?, string, string, string)>();
        private int siguienteId = 1;

        public CabeceraPedidoRetencion Cabecera(int pedido, byte? modo, bool servirJunto = false, bool pendiente = true)
        {
            var cabecera = new CabeceraPedidoRetencion
            {
                Empresa = "1", Pedido = pedido, ModoServicio = modo, ServirJunto = servirJunto, QuedaPendiente = pendiente,
                Cliente = "15191", NombreCliente = "Isabel Hurtado", Vendedor = "PA"
            };
            Cabeceras[pedido] = cabecera;
            return cabecera;
        }

        public bool TablaExiste() => Existe;
        public Task<CabeceraPedidoRetencion> LeerCabecera(string empresa, int pedido)
            => Task.FromResult(Cabeceras.TryGetValue(pedido, out CabeceraPedidoRetencion c) ? c : null);
        public Task<RetencionPedidoDTO> LeerActiva(string empresa, int pedido) => Task.FromResult(LeerActivaSincrono(empresa, pedido));
        public RetencionPedidoDTO LeerActivaSincrono(string empresa, int pedido)
            => Existe ? Filas.Where(f => f.Pedido == pedido && f.Liberado == null).OrderByDescending(f => f.Id).FirstOrDefault() : null;
        public List<RetencionPedidoDTO> LeerActivasSincrono(string empresa, IEnumerable<int> pedidos)
            => Existe ? Filas.Where(f => f.Liberado == null && pedidos.Contains(f.Pedido)).ToList() : new List<RetencionPedidoDTO>();
        public Task<List<PedidoRetenidoListadoDTO>> LeerListado(string empresa)
            => Task.FromResult(Filas.Where(f => f.Liberado == null).Select(f => new PedidoRetenidoListadoDTO { Id = f.Id, Pedido = f.Pedido, Motivo = f.Motivo }).ToList());
        public Task<List<RetencionPedidoDTO>> LeerVencidas(DateTime hoy)
            => Task.FromResult(Filas.Where(f => f.Liberado == null && f.HastaFecha.HasValue && f.HastaFecha.Value.Date <= hoy.Date).ToList());
        public Task<List<RetencionPedidoDTO>> LeerSinFecha()
            => Task.FromResult(Filas.Where(f => f.Liberado == null && !f.HastaFecha.HasValue).ToList());
        public Task<int> Insertar(RetencionPedidoDTO retencion)
        {
            retencion.Id = siguienteId++;
            Filas.Add(retencion);
            return Task.FromResult(retencion.Id);
        }
        public Task<bool> Liberar(int id, DateTime cuando, string usuario, string motivo)
        {
            RetencionPedidoDTO fila = Filas.FirstOrDefault(f => f.Id == id && f.Liberado == null);
            if (fila == null)
            {
                return Task.FromResult(false);
            }
            fila.Liberado = cuando;
            fila.UsuarioLiberacion = usuario;
            fila.MotivoLiberacion = motivo;
            return Task.FromResult(true);
        }
        public Task MarcarRecordatorio(int id, DateTime cuando)
        {
            Filas.Single(f => f.Id == id).UltimoRecordatorio = cuando;
            return Task.CompletedTask;
        }
        public Task CambiarModoServicio(string empresa, int pedido, byte? modo, string usuario, string anterior, string nuevo)
        {
            CambiosModo.Add((pedido, modo, usuario, anterior, nuevo));
            if (Cabeceras.TryGetValue(pedido, out CabeceraPedidoRetencion c))
            {
                c.ModoServicio = modo;
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>NestoAPI#698: retener un pedido «hasta nuevo aviso» sin tocar la fecha de entrega.</summary>
    [TestClass]
    public class RetencionPedidosTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 27, 10, 0, 0);
        private const string ALFREDO = "NUEVAVISION\\Alfredo";

        private static ServicioRetencionPedidos Servicio(RepositorioPedidosRetenidosEnMemoria repo, IRepositorioPromesaRiesgo promesas = null)
            => new ServicioRetencionPedidos(repo, promesas, () => AHORA, ex => { });

        // ---------- Constantes: el modo 5 ----------

        [TestMethod]
        public void ModosServicio_Retenido_MandaSobreServirJunto()
        {
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.RETENIDO, Constantes.Pedidos.ModosServicio.Efectivo(5, true));
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.RETENIDO, Constantes.Pedidos.ModosServicio.Efectivo(5, false));
            Assert.AreEqual("Retenido", Constantes.Pedidos.ModosServicio.Nombre(5));
            Assert.IsFalse(Constantes.Pedidos.ModosServicio.EsValido(5), "no se elige en el selector");
            Assert.IsTrue(Constantes.Pedidos.ModosServicio.EsRetenido(5));
            Assert.IsFalse(Constantes.Pedidos.ModosServicio.EsRetenido(null));
            // El parámetro de usuario no puede forzar el 5
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.POR_DEFECTO, Constantes.Pedidos.ModosServicio.ParsearPorDefecto("5"));
            Assert.IsNull(Constantes.Pedidos.ModosServicio.ParsearModoForzado("5"));
        }

        [TestMethod]
        public void Normalizar_PedidoRetenido_ElClienteDevuelveEl5YSeQuedaRetenido_SinTocarServirJunto()
        {
            var pedido = new PedidoVentaDTO { modoServicio = 5, servirJunto = true };
            Assert.IsNull(Constantes.Pedidos.ModosServicio.Normalizar(pedido, modoAlmacenado: 5));
            Assert.AreEqual((byte)5, pedido.modoServicio);
            Assert.IsTrue(pedido.servirJunto, "servirJunto es lo que recupera «Liberar»: no se deriva del 5");
        }

        [TestMethod]
        public void Normalizar_PedidoRetenido_SinModo_NestoApp_SeQuedaRetenido()
        {
            var pedido = new PedidoVentaDTO { modoServicio = null, servirJunto = true };
            Assert.IsNull(Constantes.Pedidos.ModosServicio.Normalizar(pedido, modoAlmacenado: 5));
            Assert.AreEqual((byte)5, pedido.modoServicio);
        }

        [TestMethod]
        public void Normalizar_PedidoRetenido_CambiarElModoDesdeElSelector_SeRechaza()
        {
            var pedido = new PedidoVentaDTO { modoServicio = 3 };
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.MENSAJE_RETENIDO_NO_SE_CAMBIA, Constantes.Pedidos.ModosServicio.Normalizar(pedido, modoAlmacenado: 5));
        }

        [TestMethod]
        public void Normalizar_PonerEl5AManoEnUnPedidoNoRetenido_SeRechaza()
        {
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.MENSAJE_RETENIDO_SOLO_CON_RETENER,
                Constantes.Pedidos.ModosServicio.Normalizar(new PedidoVentaDTO { modoServicio = 5 }, modoAlmacenado: 3));
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.MENSAJE_RETENIDO_SOLO_CON_RETENER,
                Constantes.Pedidos.ModosServicio.NormalizarAlCrear(new PedidoVentaDTO { modoServicio = 5 }, 3), "tampoco al crear");
        }

        // ---------- Servicio: Retener con picking en curso (Carlos, 28/10/26) ----------

        [TestMethod]
        public async Task Retener_ConPickingEnCurso_AvisaAlAlmacen_YSeLoDiceAlUsuario()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            CabeceraPedidoRetencion cabecera = repo.Cabecera(929057, modo: 1);
            cabecera.LineasEnPicking = 2;
            cabecera.PickingsEnCurso = "99813, 99812";
            var avisos = new List<(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera, List<int> pickings)>();
            var servicio = new ServicioRetencionPedidos(repo, null, () => AHORA, ex => { }, (r, c, p) => { avisos.Add((r, c, p)); return Task.CompletedTask; });

            ResultadoRetencion resultado = await servicio.Retener("1", 929057, new RetenerPedidoRequest { Motivo = "Cliente de viaje" }, ALFREDO);

            Assert.IsNull(resultado.Error);
            CollectionAssert.AreEqual(new List<int> { 99812, 99813 }, resultado.Retencion.PickingsEnCurso, "ordenados y sin repetir");
            StringAssert.Contains(resultado.Retencion.Aviso, "los pickings 99812, 99813");
            StringAssert.Contains(resultado.Retencion.Aviso, "Almacén");
            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual(929057, avisos[0].retencion.Pedido);
            Assert.AreSame(cabecera, avisos[0].cabecera);
            CollectionAssert.AreEqual(new List<int> { 99812, 99813 }, avisos[0].pickings);
            Assert.AreEqual((byte?)5, repo.Cabeceras[929057].ModoServicio, "retenido igual: lo que para los pickings futuros es el modo");
        }

        [TestMethod]
        public async Task Retener_SinPickingEnCurso_NiAvisoNiAlmacen()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929057, modo: 2);
            int avisos = 0;
            var servicio = new ServicioRetencionPedidos(repo, null, () => AHORA, ex => { }, (r, c, p) => { avisos++; return Task.CompletedTask; });

            ResultadoRetencion resultado = await servicio.Retener("1", 929057, new RetenerPedidoRequest { Motivo = "Cliente de viaje" }, ALFREDO);

            Assert.IsNull(resultado.Error);
            Assert.IsNull(resultado.Retencion.Aviso);
            Assert.AreEqual(0, resultado.Retencion.PickingsEnCurso.Count);
            Assert.AreEqual(0, avisos);
        }

        [TestMethod]
        public async Task Retener_SiFallaElAvisoAlAlmacen_RetieneIgual_LoRegistra_YAvisaAlUsuario()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            CabeceraPedidoRetencion cabecera = repo.Cabecera(929057, modo: 1);
            cabecera.LineasEnPicking = 1;
            cabecera.PickingsEnCurso = "99812";
            var errores = new List<Exception>();
            var servicio = new ServicioRetencionPedidos(repo, null, () => AHORA, errores.Add, (r, c, p) => throw new InvalidOperationException("sin red"));

            ResultadoRetencion resultado = await servicio.Retener("1", 929057, new RetenerPedidoRequest { Motivo = "Cliente de viaje" }, ALFREDO);

            Assert.IsNull(resultado.Error);
            Assert.AreEqual((byte?)5, repo.Cabeceras[929057].ModoServicio);
            StringAssert.Contains(resultado.Retencion.Aviso, "el picking 99812");
            StringAssert.Contains(errores.Single().Message, "929057");
            StringAssert.Contains(errores.Single().Message, "sin red");
        }

        [TestMethod]
        public async Task AvisadorRetencionAlmacen_CampanaDeNestoYBuzonDeAriadna_ATodoElGrupoAlmacen()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorRetencionAlmacen(push, () => new List<string> { "Andre", "NUEVAVISION\\Santiago", " ", "andre", "(ninguno)" });
            var retencion = new RetencionPedidoDTO { Empresa = "1", Pedido = 929057, Motivo = "Cliente de viaje", Usuario = ALFREDO, Fecha = AHORA };

            await avisador.Avisar(retencion, new CabeceraPedidoRetencion { Cliente = "15191", NombreCliente = "Isabel Hurtado" }, new List<int> { 99812 });

            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Andre", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.That.Matches(n =>
                n.Titulo.Contains("929057") && n.Titulo.Contains("99812") && n.Cuerpo.Contains("Alfredo") && n.Cuerpo.Contains("Isabel Hurtado") && n.Cuerpo.Contains("Cliente de viaje"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("Andre", Constantes.Aplicaciones.ARIADNA, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("Santiago", Constantes.Aplicaciones.ARIADNA, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustHaveHappened(4, Times.Exactly);
        }

        [TestMethod]
        public async Task AvisadorRetencionAlmacen_SinNadieEnElGrupo_Lanza()
        {
            var avisador = new AvisadorRetencionAlmacen(A.Fake<IServicioNotificacionesPush>(), () => new List<string>());
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => avisador.Avisar(new RetencionPedidoDTO { Pedido = 1 }, null, new List<int>()));
        }

        // ---------- Guardia: un retenido no sale (envío, etiqueta, albarán) ----------

        [TestMethod]
        public async Task Guardia_PedidoRetenido_DevuelveElMotivoConQuienYPorQue()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929057, modo: 5);
            repo.Filas.Add(new RetencionPedidoDTO { Id = 1, Empresa = "1", Pedido = 929057, Motivo = "Cliente de viaje", Usuario = ALFREDO, Fecha = AHORA });

            string motivo = await GuardiaPedidoRetenido.Motivo(repo, "1", 929057);

            StringAssert.Contains(motivo, "929057");
            StringAssert.Contains(motivo, "Alfredo");
            StringAssert.Contains(motivo, "Cliente de viaje");
            StringAssert.Contains(motivo, "Liberar");
        }

        [TestMethod]
        public async Task Guardia_PedidoNoRetenido_OInexistente_Null()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929058, modo: 1);
            Assert.IsNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 929058));
            Assert.IsNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 1), "no existe: de eso se ocupa cada camino");
            Assert.IsNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 0));
        }

        [TestMethod]
        public async Task Guardia_Modo5SinFilaViva_BloqueaIgual()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929057, modo: 5);
            string motivo = await GuardiaPedidoRetenido.Motivo(repo, "1", 929057);
            StringAssert.Contains(motivo, "retenido hasta nuevo aviso");
        }

        // ---------- Servicio: Retener ----------

        [TestMethod]
        public async Task Retener_PasaAlModo5_GuardaLaFila_YDejaConstanciaEnModificaciones()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929057, modo: 1, servirJunto: true);

            ResultadoRetencion resultado = await Servicio(repo).Retener("1", 929057,
                new RetenerPedidoRequest { Motivo = "  El cliente lo quiere para noviembre  ", HastaFecha = new DateTime(2026, 11, 15, 9, 30, 0) }, ALFREDO);

            Assert.IsNull(resultado.Error);
            Assert.IsFalse(resultado.NoEncontrado);
            RetencionPedidoDTO r = resultado.Retencion;
            Assert.AreEqual(1, r.Id);
            Assert.AreEqual("El cliente lo quiere para noviembre", r.Motivo, "recortado");
            Assert.AreEqual(new DateTime(2026, 11, 15), r.HastaFecha, "solo la fecha");
            Assert.AreEqual((byte?)1, r.ModoServicioAnterior);
            Assert.AreEqual(ALFREDO, r.Usuario);
            Assert.AreEqual(AHORA, r.Fecha);
            Assert.IsNull(r.Liberado);
            (int pedido, byte? modo, string usuario, string anterior, string nuevo) cambio = repo.CambiosModo.Single();
            Assert.AreEqual(929057, cambio.pedido);
            Assert.AreEqual((byte?)5, cambio.modo);
            Assert.AreEqual(ALFREDO, cambio.usuario);
            Assert.IsTrue(cambio.anterior.Contains("ModoServicio=1"));
            Assert.IsTrue(cambio.nuevo.Contains("hasta el 15/11/2026") && cambio.nuevo.Contains("El cliente lo quiere para noviembre"), cambio.nuevo);
            Assert.AreEqual((byte?)5, repo.Cabeceras[929057].ModoServicio);
        }

        [TestMethod]
        public async Task Retener_SinFecha_EsHastaNuevoAviso_YConservaModoNull()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(927991, modo: null, servirJunto: true);
            ResultadoRetencion resultado = await Servicio(repo).Retener("1", 927991, new RetenerPedidoRequest { Motivo = "Pendiente de pago" }, "Paloma");
            Assert.IsNull(resultado.Error);
            Assert.IsNull(resultado.Retencion.HastaFecha);
            Assert.IsNull(resultado.Retencion.ModoServicioAnterior, "al liberar vuelve a NULL y manda ServirJunto, como antes");
            Assert.IsTrue(repo.CambiosModo.Single().nuevo.Contains("hasta nuevo aviso"));
        }

        [TestMethod]
        public async Task Retener_SinMotivo_SeRechaza()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            Assert.AreEqual(ServicioRetencionPedidos.ERROR_SIN_MOTIVO, (await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "   " }, ALFREDO)).Error);
            Assert.AreEqual(ServicioRetencionPedidos.ERROR_SIN_MOTIVO, (await Servicio(repo).Retener("1", 1, null, ALFREDO)).Error);
            Assert.AreEqual(0, repo.Filas.Count);
        }

        [TestMethod]
        public async Task Retener_FechaDeHoyOPasada_SeRechaza()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            Assert.AreEqual(ServicioRetencionPedidos.ERROR_FECHA_PASADA,
                (await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x", HastaFecha = AHORA.Date }, ALFREDO)).Error);
            Assert.IsNull((await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x", HastaFecha = AHORA.Date.AddDays(1) }, ALFREDO)).Error, "mañana sí");
        }

        [TestMethod]
        public async Task Retener_PedidoInexistente_NoEncontrado()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            ResultadoRetencion resultado = await Servicio(repo).Retener("1", 999, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO);
            Assert.IsTrue(resultado.NoEncontrado);
        }

        [TestMethod]
        public async Task Retener_SinNadaPendiente_SeRechaza()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2, pendiente: false);
            Assert.AreEqual(ServicioRetencionPedidos.ERROR_SIN_PENDIENTE, (await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO)).Error);
        }

        [TestMethod]
        public async Task Retener_YaRetenido_SeRechazaDiciendoQuienYHastaCuando()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            ServicioRetencionPedidos servicio = Servicio(repo);
            _ = await servicio.Retener("1", 1, new RetenerPedidoRequest { Motivo = "Primero" }, ALFREDO);
            ResultadoRetencion segundo = await servicio.Retener("1", 1, new RetenerPedidoRequest { Motivo = "Segundo" }, "Paloma");
            Assert.IsNotNull(segundo.Error);
            Assert.IsTrue(segundo.Error.Contains("ya está retenido") && segundo.Error.Contains("Alfredo") && segundo.Error.Contains("Primero"), segundo.Error);
            Assert.AreEqual(1, repo.Filas.Count);
        }

        [TestMethod]
        public async Task Retener_SinTabla_AvisaDelScript()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria { Existe = false };
            repo.Cabecera(1, 2);
            Assert.AreEqual(ServicioRetencionPedidos.ERROR_SIN_TABLA, (await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO)).Error);
        }

        [TestMethod]
        public async Task Retener_ResuelveLaPromesaEnRiesgoAbierta_693()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            var promesas = A.Fake<IRepositorioPromesaRiesgo>();
            A.CallTo(() => promesas.TablaExiste()).Returns(true);
            A.CallTo(() => promesas.LeerSinResolver("1", A<IEnumerable<int>>.That.Matches(p => p.Single() == 1)))
                .Returns(Task.FromResult(new List<RiesgoPromesa> { new RiesgoPromesa { Id = 77, Pedido = 1 } }));

            ResultadoRetencion resultado = await Servicio(repo, promesas).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO);

            Assert.IsNull(resultado.Error);
            A.CallTo(() => promesas.Resolver(77, AHORA, ServicioRetencionPedidos.RESOLUCION_PROMESA)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Retener_SiFallaLaPromesa_RetieneIgual()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            var promesas = A.Fake<IRepositorioPromesaRiesgo>();
            A.CallTo(() => promesas.TablaExiste()).Throws(new InvalidOperationException("sin BD"));
            var errores = new List<Exception>();
            var servicio = new ServicioRetencionPedidos(repo, promesas, () => AHORA, errores.Add);
            ResultadoRetencion resultado = await servicio.Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO);
            Assert.IsNull(resultado.Error);
            Assert.AreEqual(1, errores.Count);
            Assert.AreEqual((byte?)5, repo.Cabeceras[1].ModoServicio);
        }

        // ---------- Servicio: Liberar ----------

        [TestMethod]
        public async Task Liberar_CierraLaFila_YDevuelveElModoAnterior()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, modo: 4);
            ServicioRetencionPedidos servicio = Servicio(repo);
            _ = await servicio.Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO);

            ResultadoRetencion resultado = await servicio.Liberar("1", 1, "NUEVAVISION\\Paloma");

            Assert.IsNull(resultado.Error);
            Assert.AreEqual(AHORA, resultado.Retencion.Liberado);
            Assert.AreEqual("NUEVAVISION\\Paloma", resultado.Retencion.UsuarioLiberacion);
            Assert.AreEqual(TextosRetencion.LIBERADO_A_MANO, resultado.Retencion.MotivoLiberacion);
            Assert.AreEqual((byte?)4, repo.Cabeceras[1].ModoServicio);
            Assert.AreEqual((byte?)4, repo.CambiosModo.Last().modo);
            Assert.IsNull(await servicio.Leer("1", 1), "ya no hay retención viva");
        }

        [TestMethod]
        public async Task Liberar_NoRetenido_SeRechaza()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            Assert.AreEqual(ServicioRetencionPedidos.ERROR_NO_RETENIDO, (await Servicio(repo).Liberar("1", 1, ALFREDO)).Error);
            Assert.IsTrue((await Servicio(repo).Liberar("1", 999, ALFREDO)).NoEncontrado);
        }

        [TestMethod]
        public async Task Liberar_Modo5SinFila_QuitaElModoIgual()
        {
            // Si alguien dejó el 5 sin fila (no debería), «Liberar» no puede quedarse sin hacer nada.
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, modo: 5);
            ResultadoRetencion resultado = await Servicio(repo).Liberar("1", 1, ALFREDO);
            Assert.IsNull(resultado.Error);
            Assert.IsNull(repo.Cabeceras[1].ModoServicio);
            Assert.AreEqual(AHORA, resultado.Retencion.Liberado);
        }

        // ---------- Textos ----------

        [TestMethod]
        public void Textos_Descripcion_ConYSinFecha_SinDominio()
        {
            var conFecha = new RetencionPedidoDTO { Usuario = ALFREDO, Fecha = new DateTime(2026, 10, 22), HastaFecha = new DateTime(2026, 11, 15), Motivo = "El cliente lo quiere para noviembre" };
            Assert.AreEqual("Retenido por Alfredo desde el 22/10/2026 hasta el 15/11/2026: El cliente lo quiere para noviembre", TextosRetencion.Descripcion(conFecha));
            var sinFecha = new RetencionPedidoDTO { Usuario = "Paloma", Fecha = new DateTime(2026, 10, 22), Motivo = "Pendiente de pago" };
            Assert.AreEqual("Retenido por Paloma desde el 22/10/2026 hasta nuevo aviso: Pendiente de pago", TextosRetencion.Descripcion(sinFecha));
            Assert.AreEqual("retenido por Paloma desde el 22/10/2026 hasta nuevo aviso: Pendiente de pago", TextosRetencion.Descripcion(sinFecha, false));
        }

        [TestMethod]
        public void Textos_ModoAlLiberar_SinAnterior_MandaServirJunto()
        {
            var r = new RetencionPedidoDTO { ModoServicioAnterior = null };
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, TextosRetencion.ModoAlLiberar(r, new CabeceraPedidoRetencion { ServirJunto = true }));
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, TextosRetencion.ModoAlLiberar(r, new CabeceraPedidoRetencion { ServirJunto = false }));
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, TextosRetencion.ModoAlLiberar(new RetencionPedidoDTO { ModoServicioAnterior = 3 }, null));
        }

        [TestMethod]
        public void Ajustes_DiasRecordatorio_7PorDefecto_YConfigurable()
        {
            Assert.AreEqual(7, AjustesRetencion.Interpretar(null));
            Assert.AreEqual(7, AjustesRetencion.Interpretar("siete"));
            Assert.AreEqual(30, AjustesRetencion.Interpretar(" 30 "));
            Assert.AreEqual(0, AjustesRetencion.Interpretar("0"));
        }

        // ---------- Picking ----------

        private static PedidoPicking PedidoPickingRetenido(bool servirJunto)
        {
            return new PedidoPicking(A.Fake<IRellenadorPrepagosService>())
            {
                Empresa = "1", Id = 929057, ServirJunto = servirJunto, ModoServicio = 5, PlazosPago = "CONTADO",
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking { Id = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "11111", Cantidad = 2, CantidadReservada = 2, BaseImponible = 50, Total = 60.5M }
                }
            };
        }

        [TestMethod]
        public void Picking_Retenido_NoSaleAunqueHayaStockDeTodo_ConServirJuntoOSinEl()
        {
            foreach (bool servirJunto in new[] { true, false })
            {
                PedidoPicking pedido = PedidoPickingRetenido(servirJunto);
                Assert.IsFalse(pedido.saleEnPicking(), $"servirJunto={servirJunto}");
                Assert.AreEqual(MotivoNoSalePicking.Retenido, pedido.MotivoNoSale);
                Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            }
        }

        [TestMethod]
        public void Explicador_Retenido_DiceQuienHastaCuandoYMotivo()
        {
            PedidoPicking pedido = PedidoPickingRetenido(false);
            pedido.Retencion = new RetencionPedidoDTO { Usuario = ALFREDO, Fecha = new DateTime(2026, 10, 22), HastaFecha = new DateTime(2026, 11, 15), Motivo = "El cliente lo quiere para noviembre" };
            _ = pedido.saleEnPicking();
            NestoAPI.Infraestructure.Exceptions.NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, null, null);
            Assert.IsNotNull(error);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_RETENIDO, error.Context.ErrorCode);
            Assert.AreEqual("El pedido 929057 no sale: está retenido por Alfredo desde el 22/10/2026 hasta el 15/11/2026: El cliente lo quiere para noviembre. " +
                "No saldrá en ningún picking hasta que se libere: botón «Liberar» en el pedido (lo puede hacer el vendedor o el almacén).", error.Message);
        }

        [TestMethod]
        public void Explicador_Retenido_SinFila_AlgoDice()
        {
            PedidoPicking pedido = PedidoPickingRetenido(false);
            _ = pedido.saleEnPicking();
            NestoAPI.Infraestructure.Exceptions.NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, null, null);
            Assert.IsTrue(error.Message.StartsWith("El pedido 929057 no sale: está retenido hasta nuevo aviso."), error.Message);
        }

        [TestMethod]
        public void PromesaRiesgo_LosRetenidosNoSonCandidatos()
        {
            Assert.IsTrue(RepositorioPromesaRiesgo.SQL_CANDIDATOS.Contains("ISNULL(c.ModoServicio, 0) <> 5"));
        }

        // ---------- Pasada nocturna ----------

        private static async Task<(ResultadoPasadaRetenidos resultado, List<(string usuario, NotificacionPushDTO notificacion)> avisos)> Pasada(
            RepositorioPedidosRetenidosEnMemoria repo, DateTime ahora, int dias = 7, Func<string, int, List<string>> vendedor = null)
        {
            var avisos = new List<(string, NotificacionPushDTO)>();
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .Invokes((string u, string app, NotificacionPushDTO n) => avisos.Add((u, n)))
                .Returns(Task.CompletedTask);
            ResultadoPasadaRetenidos resultado = await PedidosRetenidosJobsService.Procesar(repo,
                new ServicioRetencionPedidos(repo, null, () => ahora, ex => { }), notificaciones,
                vendedor ?? ((e, p) => new List<string> { "NUEVAVISION\\Paloma" }), ahora, dias, ex => { });
            return (resultado, avisos);
        }

        [TestMethod]
        public async Task Nocturna_LlegaLaFecha_LiberaSolo_YAvisaAQuienRetuvoYAlVendedor()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929057, modo: 3);
            _ = await Servicio(repo).Retener("1", 929057, new RetenerPedidoRequest { Motivo = "El cliente lo quiere para noviembre", HastaFecha = new DateTime(2026, 11, 15) }, ALFREDO);

            (ResultadoPasadaRetenidos antes, List<(string usuario, NotificacionPushDTO notificacion)> avisosAntes) = await Pasada(repo, new DateTime(2026, 11, 14, 5, 35, 0));
            Assert.AreEqual(0, antes.Liberados, "el día antes no");
            Assert.AreEqual(0, avisosAntes.Count);

            (ResultadoPasadaRetenidos resultado, List<(string usuario, NotificacionPushDTO notificacion)> avisos) = await Pasada(repo, new DateTime(2026, 11, 15, 5, 35, 0));

            Assert.AreEqual(1, resultado.Liberados);
            CollectionAssert.AreEqual(new List<int> { 929057 }, resultado.PedidosLiberados);
            RetencionPedidoDTO fila = repo.Filas.Single();
            Assert.IsNotNull(fila.Liberado);
            Assert.AreEqual(TextosRetencion.LIBERADO_POR_FECHA, fila.MotivoLiberacion);
            Assert.AreEqual(ServicioRetencionPedidos.USUARIO_SISTEMA, fila.UsuarioLiberacion);
            Assert.AreEqual((byte?)3, repo.Cabeceras[929057].ModoServicio, "vuelve a su modo");
            CollectionAssert.AreEquivalent(new List<string> { ALFREDO, "NUEVAVISION\\Paloma" }, avisos.Select(a => a.usuario).ToList());
            NotificacionPushDTO n = avisos.First().notificacion;
            Assert.AreEqual("El pedido 929057 ya no está retenido", n.Titulo);
            Assert.IsTrue(n.Cuerpo.Contains("15191") && n.Cuerpo.Contains("Alfredo") && n.Cuerpo.Contains("Tras reponer de tiendas") && n.Cuerpo.Contains("vuelva a retenerlo"), n.Cuerpo);
            Assert.AreEqual(TextosRetencion.TIPO_NOTIFICACION, n.Tipo);
            Assert.AreEqual("929057", n.Datos["pedido"]);
            Assert.AreEqual("liberado", n.Datos["accion"]);
        }

        [TestMethod]
        public async Task Nocturna_SinFecha_RecuerdaCadaNDias_YNoAntes()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(927991, modo: null, servirJunto: true);
            _ = await Servicio(repo).Retener("1", 927991, new RetenerPedidoRequest { Motivo = "Pendiente de pago" }, "Paloma");

            (ResultadoPasadaRetenidos dia6, List<(string usuario, NotificacionPushDTO notificacion)> avisos6) = await Pasada(repo, AHORA.AddDays(6));
            Assert.AreEqual(0, dia6.Recordados);
            Assert.AreEqual(0, avisos6.Count);

            (ResultadoPasadaRetenidos dia7, List<(string usuario, NotificacionPushDTO notificacion)> avisos7) = await Pasada(repo, AHORA.AddDays(7));
            Assert.AreEqual(1, dia7.Recordados);
            Assert.AreEqual(1, avisos7.Count, "Paloma retuvo y es la vendedora: un solo aviso");
            Assert.AreEqual("NUEVAVISION\\Paloma", avisos7[0].usuario);
            Assert.AreEqual("El pedido 927991 sigue retenido", avisos7[0].notificacion.Titulo);
            Assert.IsTrue(avisos7[0].notificacion.Cuerpo.Contains("7 días") && avisos7[0].notificacion.Cuerpo.Contains("libérelo"), avisos7[0].notificacion.Cuerpo);
            Assert.AreEqual(AHORA.AddDays(7), repo.Filas.Single().UltimoRecordatorio);
            Assert.IsNull(repo.Filas.Single().Liberado, "sigue retenido");

            (ResultadoPasadaRetenidos dia8, _) = await Pasada(repo, AHORA.AddDays(8));
            Assert.AreEqual(0, dia8.Recordados, "hasta dentro de otros 7 días, no");
            (ResultadoPasadaRetenidos dia14, _) = await Pasada(repo, AHORA.AddDays(14));
            Assert.AreEqual(1, dia14.Recordados);
        }

        [TestMethod]
        public async Task Nocturna_DiasRecordatorioCero_NoRecuerda()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            _ = await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO);
            (ResultadoPasadaRetenidos resultado, _) = await Pasada(repo, AHORA.AddDays(60), dias: 0);
            Assert.AreEqual(0, resultado.Recordados);
        }

        [TestMethod]
        public async Task Nocturna_SinFecha_YaServido_CierraLaFilaSinAvisar()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            CabeceraPedidoRetencion cabecera = repo.Cabecera(1, 2);
            _ = await Servicio(repo).Retener("1", 1, new RetenerPedidoRequest { Motivo = "x" }, ALFREDO);
            cabecera.QuedaPendiente = false;
            (ResultadoPasadaRetenidos resultado, List<(string usuario, NotificacionPushDTO notificacion)> avisos) = await Pasada(repo, AHORA.AddDays(10));
            Assert.AreEqual(0, resultado.Recordados);
            Assert.AreEqual(0, avisos.Count);
            Assert.IsNotNull(repo.Filas.Single().Liberado);
        }

        [TestMethod]
        public async Task Nocturna_SinTabla_NoHaceNada()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria { Existe = false };
            (ResultadoPasadaRetenidos resultado, _) = await Pasada(repo, AHORA);
            Assert.AreEqual(0, resultado.Liberados + resultado.Recordados + resultado.Avisos);
        }

        [TestMethod]
        public void Nocturna_Destinatarios_SinRepetir_ConDominio_YSinElSistema()
        {
            var retencion = new RetencionPedidoDTO { Empresa = "1", Pedido = 1, Usuario = "alfredo" };
            List<string> usuarios = PedidosRetenidosJobsService.Destinatarios(retencion, (e, p) => new List<string> { "NUEVAVISION\\Alfredo", "NUEVAVISION\\Paloma", null });
            CollectionAssert.AreEqual(new List<string> { "NUEVAVISION\\alfredo", "NUEVAVISION\\Paloma" }, usuarios);
            Assert.AreEqual(0, PedidosRetenidosJobsService.Destinatarios(new RetencionPedidoDTO { Usuario = ServicioRetencionPedidos.USUARIO_SISTEMA }, (e, p) => null).Count);
            Assert.AreEqual(1, PedidosRetenidosJobsService.Destinatarios(retencion, (e, p) => throw new InvalidOperationException()).Count, "si falla el vendedor, al menos quien retuvo");
        }

        // ---------- Controlador ----------

        private static PedidosVentaController Controlador(RepositorioPedidosRetenidosEnMemoria repo, string usuario = ALFREDO, bool cliente = false)
        {
            var controller = new PedidosVentaController(A.Fake<NVEntities>()) { Request = new System.Net.Http.HttpRequestMessage(), Configuration = new HttpConfiguration() };
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, usuario) };
            if (cliente)
            {
                claims.Add(new Claim("cliente", "15191"));
            }
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            controller.CrearServicioRetencion = () => new ServicioRetencionPedidos(repo, null, () => AHORA, ex => { });
            return controller;
        }

        [TestMethod]
        public async Task Controlador_Retener_Liberar_YLeer()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(929057, 1, servirJunto: true);
            PedidosVentaController controller = Controlador(repo);

            IHttpActionResult sinRetener = await controller.GetRetencion("1", 929057);
            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)sinRetener).StatusCode);

            IHttpActionResult retener = await controller.PostRetener("1", 929057, new RetenerPedidoRequest { Motivo = "El cliente lo quiere para noviembre" });
            var ok = retener as OkNegotiatedContentResult<RetencionPedidoDTO>;
            Assert.IsNotNull(ok, retener.GetType().Name);
            Assert.AreEqual(ALFREDO, ok.Content.Usuario, "el usuario del token, no el del cuerpo");

            IHttpActionResult leer = await controller.GetRetencion("1", 929057);
            Assert.AreEqual(1, ((OkNegotiatedContentResult<RetencionPedidoDTO>)leer).Content.Id);

            IHttpActionResult listado = await controller.GetRetenidos("1");
            Assert.AreEqual(1, ((OkNegotiatedContentResult<List<PedidoRetenidoListadoDTO>>)listado).Content.Count);

            IHttpActionResult otraVez = await controller.PostRetener("1", 929057, new RetenerPedidoRequest { Motivo = "Otra" });
            Assert.IsInstanceOfType(otraVez, typeof(BadRequestErrorMessageResult));

            IHttpActionResult liberar = await controller.PostLiberar("1", 929057, null);
            Assert.IsNotNull(((OkNegotiatedContentResult<RetencionPedidoDTO>)liberar).Content.Liberado);
            Assert.AreEqual((byte?)1, repo.Cabeceras[929057].ModoServicio);

            Assert.IsInstanceOfType(await controller.PostRetener("1", 999, new RetenerPedidoRequest { Motivo = "x" }), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Controlador_ClienteDeTienda_NoRetieneNiLibera()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            repo.Cabecera(1, 2);
            PedidosVentaController controller = Controlador(repo, "cliente@tienda.es", cliente: true);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)await controller.PostRetener("1", 1, new RetenerPedidoRequest { Motivo = "x" })).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)await controller.PostLiberar("1", 1, null)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)await controller.GetRetenidos("1")).StatusCode);
            Assert.AreEqual(0, repo.Filas.Count);
        }
    }
}
