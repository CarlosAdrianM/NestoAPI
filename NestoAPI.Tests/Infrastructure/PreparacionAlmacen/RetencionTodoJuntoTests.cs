using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#858 punto 4: respetar «todo junto» cuando falta un producto de pago (retener, decidir, plazo, aviso, packing).</summary>
    [TestClass]
    public class RetencionTodoJuntoTests
    {
        // Miércoles
        private static readonly DateTime AHORA = new DateTime(2026, 11, 11, 10, 0, 0);

        private class TransaccionConRetencion : TransaccionSalidaFalsa, IRetencionTodoJuntoSalida
        {
            public List<LineaTodoJuntoSalida> Lineas { get; set; } = new List<LineaTodoJuntoSalida>();
            public bool HayTabla { get; set; } = true;
            public List<RetencionPorFaltaNueva> Retenciones { get; } = new List<RetencionPorFaltaNueva>();

            public Task<List<LineaTodoJuntoSalida>> LeerLineasTodoJunto(string empresa, IReadOnlyCollection<int> lineas) =>
                Task.FromResult(Lineas.Where(l => lineas.Contains(l.Linea)).ToList());

            public Task<DateTime?> RetenerPorFalta(RetencionPorFaltaNueva retencion)
            {
                if (!HayTabla)
                {
                    return Task.FromResult<DateTime?>(null);
                }
                Retenciones.Add(retencion);
                return Task.FromResult<DateTime?>(retencion.Plazo);
            }
        }

        private Func<bool> encendidoAntes;
        private Func<int> plazoAntes;
        private Func<DateTime> ahoraAntes;
        private Func<DateTime, bool> festivoAntes;
        private Func<string, IReadOnlyCollection<int>, Task<Dictionary<int, DateTime>>> pendientesAntes;
        private Func<string, int, Task<string>> leerAntes;

        [TestInitialize]
        public void Preparar()
        {
            encendidoAntes = ReglasRetencionTodoJunto.Encendido;
            plazoAntes = ReglasRetencionTodoJunto.PlazoHoras;
            ahoraAntes = ReglasRetencionTodoJunto.Ahora;
            festivoAntes = ReglasRetencionTodoJunto.EsFestivo;
            pendientesAntes = GuardiaRetencionPorFalta.PendientesDe;
            leerAntes = GuardiaRetencionPorFalta.Leer;
            ReglasRetencionTodoJunto.Encendido = () => true;
            ReglasRetencionTodoJunto.PlazoHoras = () => 2;
            ReglasRetencionTodoJunto.Ahora = () => AHORA;
            ReglasRetencionTodoJunto.EsFestivo = d => false;
        }

        [TestCleanup]
        public void Limpiar()
        {
            ReglasRetencionTodoJunto.Encendido = encendidoAntes;
            ReglasRetencionTodoJunto.PlazoHoras = plazoAntes;
            ReglasRetencionTodoJunto.Ahora = ahoraAntes;
            ReglasRetencionTodoJunto.EsFestivo = festivoAntes;
            GuardiaRetencionPorFalta.PendientesDe = pendientesAntes;
            GuardiaRetencionPorFalta.Leer = leerAntes;
        }

        // Pedido 930500: falta 1 de 17404 (línea 10) y sigue saliendo 30111 (línea 11)
        private static TransaccionConRetencion Transaccion(bool servirJunto = true, byte? modo = 1, decimal baseImponible = 25m, bool ficticio = false)
        {
            var tx = new TransaccionConRetencion();
            tx.LineasQueSiguen.Add(new LineaEnPickingSalida { Linea = 11, Pedido = 930500, Producto = "30111", TipoLinea = 1, Cantidad = 2 });
            tx.Lineas.Add(new LineaTodoJuntoSalida { Linea = 10, Pedido = 930500, BaseImponible = baseImponible, Ficticio = ficticio, ServirJunto = servirJunto, ModoServicio = modo });
            return tx;
        }

        private static List<RecorteSalida> Falta(bool esMuestra = false) => new List<RecorteSalida>
        {
            new RecorteSalida { Pieza = new PiezaSalida { Linea = 10, Pedido = 930500, Producto = "17404", Cantidad = 1, EsMuestra = esMuestra }, Cantidad = 1 }
        };

        private static async Task<List<FaltaPickingPedido>> Terminar(TransaccionConRetencion tx, bool esMuestra = false)
        {
            var avisar = new List<FaltaPickingPedido>();
            _ = await OrigenSalidaPicking.QuitarFaltasDeLosPedidos("1", 100070, Falta(esMuestra), tx, avisar);
            return avisar;
        }

        // ---------------- Retener ----------------

        [TestMethod]
        public async Task TodoJuntoConFaltaDePago_QuedaRetenidoConPlazo()
        {
            TransaccionConRetencion tx = Transaccion();

            List<FaltaPickingPedido> avisar = await Terminar(tx);

            RetencionPorFaltaNueva r = tx.Retenciones.Single();
            Assert.AreEqual(930500, r.Pedido);
            Assert.AreEqual(100070, r.Picking);
            Assert.AreEqual("17404 x1", r.Productos);
            Assert.AreEqual(AHORA.AddHours(2), r.Plazo);
            Assert.IsTrue(avisar.Single().Retenido);
            Assert.AreEqual(AHORA.AddHours(2), avisar.Single().PlazoRetencion);
            Assert.AreEqual(10, tx.SacadoDePedidos.Single().Linea, "La línea que falta queda pendiente como siempre");
        }

        [TestMethod]
        public async Task NoTodoJunto_NoSeRetiene()
        {
            TransaccionConRetencion tx = Transaccion(servirJunto: false, modo: 2);
            List<FaltaPickingPedido> avisar = await Terminar(tx);
            Assert.AreEqual(0, tx.Retenciones.Count);
            Assert.IsFalse(avisar.Single().Retenido);
        }

        [TestMethod]
        public async Task Muestra_NoRetiene()
        {
            TransaccionConRetencion tx = Transaccion();
            _ = await Terminar(tx, esMuestra: true);
            Assert.AreEqual(0, tx.Retenciones.Count);
        }

        [TestMethod]
        public async Task SinImporteOFicticio_NoRetiene()
        {
            TransaccionConRetencion gratis = Transaccion(baseImponible: 0);
            _ = await Terminar(gratis);
            TransaccionConRetencion ficticio = Transaccion(ficticio: true);
            _ = await Terminar(ficticio);
            Assert.AreEqual(0, gratis.Retenciones.Count + ficticio.Retenciones.Count);
        }

        [TestMethod]
        public async Task SiNoSaleNadaMas_NoRetiene()
        {
            TransaccionConRetencion tx = Transaccion();
            tx.LineasQueSiguen.Clear();
            List<FaltaPickingPedido> avisar = await Terminar(tx);
            Assert.AreEqual(0, tx.Retenciones.Count, "Ya espera entero");
            Assert.IsTrue(avisar.Single().NoSaleNadaDelPedido);
        }

        [TestMethod]
        public async Task SinTablaOApagado_ComoAntes()
        {
            TransaccionConRetencion sinTabla = Transaccion();
            sinTabla.HayTabla = false;
            List<FaltaPickingPedido> avisar = await Terminar(sinTabla);
            Assert.IsFalse(avisar.Single().Retenido);

            ReglasRetencionTodoJunto.Encendido = () => false;
            TransaccionConRetencion apagado = Transaccion();
            _ = await Terminar(apagado);
            Assert.AreEqual(0, apagado.Retenciones.Count);
        }

        [TestMethod]
        public async Task TransaccionSinLaCapacidad_NoRetiene()
        {
            var tx = new TransaccionSalidaFalsa();
            tx.LineasQueSiguen.Add(new LineaEnPickingSalida { Linea = 11, Pedido = 930500, Producto = "30111", TipoLinea = 1, Cantidad = 2 });
            var avisar = new List<FaltaPickingPedido>();
            _ = await OrigenSalidaPicking.QuitarFaltasDeLosPedidos("1", 100070, Falta(), tx, avisar);
            Assert.IsFalse(avisar.Single().Retenido);
        }

        [TestMethod]
        public void Interruptor_EncendidoSalvoCero()
        {
            Assert.IsTrue(ReglasRetencionTodoJunto.Interpretar(null));
            Assert.IsTrue(ReglasRetencionTodoJunto.Interpretar("1"));
            Assert.IsFalse(ReglasRetencionTodoJunto.Interpretar("0"));
            Assert.AreEqual(2, ReglasRetencionTodoJunto.InterpretarPlazo(null));
            Assert.AreEqual(4, ReglasRetencionTodoJunto.InterpretarPlazo("4"));
            Assert.AreEqual(2, ReglasRetencionTodoJunto.InterpretarPlazo("abc"));
        }

        // ---------------- Plazo en horas laborables ----------------

        [TestMethod]
        public void Plazo_HorasLaborables()
        {
            Assert.AreEqual(new DateTime(2026, 11, 11, 12, 0, 0), ReglasRetencionTodoJunto.SumarHorasLaborables(new DateTime(2026, 11, 11, 10, 0, 0), 2, null));
            // Viernes 17:00 + 2 h → lunes 9:00 (el fin de semana no cuenta)
            Assert.AreEqual(new DateTime(2026, 11, 16, 9, 0, 0), ReglasRetencionTodoJunto.SumarHorasLaborables(new DateTime(2026, 11, 13, 17, 0, 0), 2, null));
            // … y con el lunes festivo, martes 9:00
            Assert.AreEqual(new DateTime(2026, 11, 17, 9, 0, 0), ReglasRetencionTodoJunto.SumarHorasLaborables(new DateTime(2026, 11, 13, 17, 0, 0), 2,
                d => d == new DateTime(2026, 11, 16)));
            // Sábado o antes de abrir: se cuenta desde la apertura
            Assert.AreEqual(new DateTime(2026, 11, 16, 10, 0, 0), ReglasRetencionTodoJunto.SumarHorasLaborables(new DateTime(2026, 11, 14, 11, 0, 0), 2, null));
            Assert.AreEqual(new DateTime(2026, 11, 11, 10, 0, 0), ReglasRetencionTodoJunto.SumarHorasLaborables(new DateTime(2026, 11, 11, 6, 30, 0), 2, null));
        }

        // ---------------- Aviso ----------------

        [TestMethod]
        public void Aviso_RetenidoLlevaPlazoYDatos()
        {
            var pedido = new PedidoConFaltasPicking
            {
                Empresa = "1", Pedido = 930500, Picking = 100070, Cliente = "15191/0 - CENTRO", Almacen = "ALG",
                Retenido = true, PlazoRetencion = new DateTime(2026, 11, 11, 12, 0, 0),
                Faltas = new List<FaltaAvisoPicking> { new FaltaAvisoPicking { Producto = "17404", Unidades = 1 } }
            };

            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(pedido);

            Assert.AreEqual("PickingConFaltas", aviso.Tipo);
            Assert.AreEqual("true", aviso.Datos["retenido"]);
            Assert.AreEqual("2026-11-11T12:00:00", aviso.Datos["plazo"]);
            StringAssert.Contains(aviso.Cuerpo, "queda RETENIDO");
            StringAssert.Contains(aviso.Cuerpo, "si no contesta, se esperará a tenerlo completo");
            StringAssert.Contains(aviso.Titulo, "retenido");
        }

        [TestMethod]
        public void Aviso_NoRetenidoComoAntes()
        {
            var pedido = new PedidoConFaltasPicking
            {
                Empresa = "1", Pedido = 930500, Picking = 100070,
                Faltas = new List<FaltaAvisoPicking> { new FaltaAvisoPicking { Producto = "17404", Unidades = 1 } }
            };
            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(pedido);
            Assert.IsFalse(aviso.Datos.ContainsKey("retenido"));
            StringAssert.Contains(aviso.Cuerpo, "El resto del pedido sigue su curso.");
        }

        // ---------------- Packing y bloqueos ----------------

        [TestMethod]
        public async Task Packing_MarcaLosRetenidos()
        {
            GuardiaRetencionPorFalta.PendientesDe = (e, p) => Task.FromResult(new Dictionary<int, DateTime> { [930500] = AHORA.AddHours(2) });
            var packing = new PackingAlmacenDTO { Empresa = "1", Picking = 100070 };
            packing.Entregas.Add(new EntregaPackingAlmacenDTO
            {
                Pedidos = new List<PedidoPackingAlmacenDTO> { new PedidoPackingAlmacenDTO { Pedido = 930500 }, new PedidoPackingAlmacenDTO { Pedido = 930501 } }
            });

            await GuardiaRetencionPorFalta.MarcarPacking(packing);

            PedidoPackingAlmacenDTO retenido = packing.Entregas[0].Pedidos[0];
            Assert.IsTrue(retenido.RetenidoPorFalta);
            Assert.AreEqual("Esperando al vendedor: no empaquetar", retenido.AvisoRetencion);
            Assert.AreEqual(AHORA.AddHours(2), retenido.PlazoRetencion);
            Assert.IsFalse(packing.Entregas[0].Pedidos[1].RetenidoPorFalta);
        }

        [TestMethod]
        public async Task Guardia_DaElMotivoDeUsted()
        {
            GuardiaRetencionPorFalta.Leer = (e, p) => Task.FromResult(ReglasRetencionTodoJunto.MotivoBloqueo(p, AHORA.AddHours(2)));
            string motivo = await GuardiaRetencionPorFalta.Motivo("1", 930500);
            StringAssert.StartsWith(motivo, "Esperando al vendedor: no empaquetar.");
            StringAssert.Contains(motivo, "si no contesta, se esperará");
        }

        // ---------------- Decidir ----------------

        private class RepositorioFalso : IRepositorioRetencionesPorFalta
        {
            public bool Tabla { get; set; } = true;
            public RetencionPorFaltaDTO Pendiente { get; set; } = new RetencionPorFaltaDTO
            {
                Id = 7, Empresa = "1", Pedido = 930500, Picking = 100070, Cliente = "15191", Productos = "17404 x1",
                Plazo = AHORA.AddHours(2), Estado = EstadosRetencionPorFalta.PENDIENTE
            };
            public ResultadoEsperar AlEsperar { get; set; } = new ResultadoEsperar { Hecho = true, LineasSacadas = 2, UnidadesAPendienteDeUbicar = 3 };
            public List<(int Id, bool PorPlazo)> Esperas { get; } = new List<(int, bool)>();
            public List<int> Servidas { get; } = new List<int>();

            public Task<bool> TablaDisponible() => Task.FromResult(Tabla);
            public Task<RetencionPorFaltaDTO> LeerPendiente(string empresa, int pedido) => Task.FromResult(Pendiente?.Pedido == pedido ? Pendiente : null);
            public Task<List<RetencionPorFaltaDTO>> LeerPendientes() => Task.FromResult(Pendiente == null ? new List<RetencionPorFaltaDTO>() : new List<RetencionPorFaltaDTO> { Pendiente });
            public Task<List<RetencionPorFaltaDTO>> LeerVencidas(DateTime ahora) =>
                Task.FromResult(Pendiente != null && Pendiente.Plazo <= ahora ? new List<RetencionPorFaltaDTO> { Pendiente } : new List<RetencionPorFaltaDTO>());
            public Task<bool> ServirLoQueHay(int id, string usuario)
            {
                Servidas.Add(id);
                return Task.FromResult(true);
            }
            public Task<ResultadoEsperar> Esperar(RetencionPorFaltaDTO retencion, string usuario, bool porPlazo)
            {
                Esperas.Add((retencion.Id, porPlazo));
                return Task.FromResult(AlEsperar);
            }
        }

        private static ComercialesPedido Comerciales() => new ComercialesPedido
        {
            Empresa = "1", Pedido = 930500, UsuarioPedido = "Ana",
            Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "JE", Usuarios = new List<string> { "Jesus" } } }
        };

        private static ServicioRetencionesPorFalta Servicio(RepositorioFalso repositorio, DateTime? ahora = null) =>
            new ServicioRetencionesPorFalta(repositorio, (e, p) => Comerciales(), () => ahora ?? AHORA);

        private static IPrincipal Usuario(string nombre, string vendedor = null, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            if (vendedor != null)
            {
                claims.Add(new Claim("Vendedor", vendedor));
            }
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static DecidirRetencionPorFaltaDTO Decision(string d) => new DecidirRetencionPorFaltaDTO { Decision = d };

        [TestMethod]
        public void Permisos_QuienHizoElPedidoSuVendedorOLosGrupos()
        {
            Assert.IsTrue(ServicioRetencionesPorFalta.PuedeDecidir(Usuario("NUEVAVISION\\Ana"), Comerciales()));
            Assert.IsTrue(ServicioRetencionesPorFalta.PuedeDecidir(Usuario("app@example.com", "JE"), Comerciales()));
            Assert.IsTrue(ServicioRetencionesPorFalta.PuedeDecidir(Usuario("NUEVAVISION\\Jesus"), Comerciales()));
            Assert.IsTrue(ServicioRetencionesPorFalta.PuedeDecidir(Usuario("NUEVAVISION\\Pedro", null, "Almacén"), Comerciales()));
            Assert.IsFalse(ServicioRetencionesPorFalta.PuedeDecidir(Usuario("NUEVAVISION\\Otro", "NV"), Comerciales()));
        }

        [TestMethod]
        public async Task Decidir_ServirLoQueHay()
        {
            var repo = new RepositorioFalso();
            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> r = await Servicio(repo).Decidir("1", 930500, Decision("servirloquehay"), Usuario("NUEVAVISION\\Ana"));
            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            Assert.AreEqual(EstadosRetencionPorFalta.SERVIR_LO_QUE_HAY, r.Contenido.Estado);
            CollectionAssert.AreEqual(new[] { 7 }, repo.Servidas);
            Assert.AreEqual(0, repo.Esperas.Count);
        }

        [TestMethod]
        public async Task Decidir_Esperar_SaleDelPickingYDiceQueHayQueUbicar()
        {
            var repo = new RepositorioFalso();
            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> r = await Servicio(repo).Decidir("1", 930500, Decision("Esperar"), Usuario("app@example.com", "JE"));
            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            Assert.AreEqual((7, false), repo.Esperas.Single());
            StringAssert.Contains(r.Contenido.Mensaje, "«pendiente de ubicar»");
        }

        [TestMethod]
        public async Task Decidir_Errores()
        {
            var repo = new RepositorioFalso();
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio(repo).Decidir("1", 930500, Decision("Quizá"), Usuario("NUEVAVISION\\Ana"))).Codigo);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Servicio(repo).Decidir("1", 930500, Decision("Esperar"), Usuario("NUEVAVISION\\Otro"))).Codigo);
            Assert.AreEqual(0, repo.Esperas.Count);

            repo.AlEsperar = new ResultadoEsperar { PickingSinTerminar = true };
            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> sinTerminar = await Servicio(repo).Decidir("1", 930500, Decision("Esperar"), Usuario("NUEVAVISION\\Ana"));
            Assert.AreEqual(HttpStatusCode.Conflict, sinTerminar.Codigo);
            StringAssert.Contains(sinTerminar.Mensaje, "todavía se está recogiendo");

            repo.Pendiente = null;
            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> yaDecidida = await Servicio(repo).Decidir("1", 930500, Decision("ServirLoQueHay"), Usuario("NUEVAVISION\\Ana"));
            Assert.AreEqual(HttpStatusCode.Conflict, yaDecidida.Codigo);

            repo.Tabla = false;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Servicio(repo).Decidir("1", 930500, Decision("Esperar"), Usuario("NUEVAVISION\\Ana"))).Codigo);
        }

        [TestMethod]
        public async Task Leer_LosMiosOTodosSegunPermisos()
        {
            var repo = new RepositorioFalso();
            Assert.AreEqual(1, (await Servicio(repo).Leer(Usuario("NUEVAVISION\\Ana"))).Contenido.Count);
            Assert.AreEqual(0, (await Servicio(repo).Leer(Usuario("NUEVAVISION\\Otro", "NV"))).Contenido.Count);
            Assert.IsTrue((await Servicio(repo).Leer(Usuario("NUEVAVISION\\Carlos", null, "Dirección"))).Contenido.Single().PuedeDecidir);
        }

        // ---------------- Plazo vencido: «Esperar» por defecto ----------------

        [TestMethod]
        public async Task Plazo_VencidoAplicaEsperar_YNoAntes()
        {
            var repo = new RepositorioFalso();
            Assert.AreEqual(0, (await Servicio(repo, AHORA.AddHours(1)).AplicarVencidas()).Count);

            List<RetencionPorFaltaDTO> aplicadas = await Servicio(repo, AHORA.AddHours(3)).AplicarVencidas();

            Assert.AreEqual(930500, aplicadas.Single().Pedido);
            Assert.IsTrue(aplicadas.Single().PorPlazo);
            Assert.AreEqual((7, true), repo.Esperas.Single());
            NotificacionPushDTO aviso = ServicioRetencionesPorFalta.AvisoVencida(aplicadas.Single());
            Assert.AreEqual("930500", aviso.Datos["pedido"]);
            StringAssert.Contains(aviso.Cuerpo, "se espera a tenerlo completo");
        }

        // ---------------- Refrescar el packing de las PDA (haCambiado al grupo Packing:{picking}) ----------------

        private static ServicioRetencionesPorFalta ServicioConAvisos(RepositorioFalso repositorio, NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen avisos,
            DateTime? ahora = null) =>
            new ServicioRetencionesPorFalta(repositorio, (e, p) => Comerciales(), () => ahora ?? AHORA, avisos);

        [TestMethod]
        public async Task AvisoPacking_ServirLoQueHay_AvisaAlPackingDeSuPicking()
        {
            var avisos = A.Fake<NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen>();
            var repo = new RepositorioFalso();

            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> r = await ServicioConAvisos(repo, avisos).Decidir("1", 930500, Decision("ServirLoQueHay"), Usuario("NUEVAVISION\\Ana"));

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            A.CallTo(() => avisos.HaCambiado("Packing:100070", null, null, AvisosRetencionPorFalta.DISPOSITIVO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AvisoPacking_Esperar_AvisaAlPackingDeSuPicking_YSinTerminarNo()
        {
            var avisos = A.Fake<NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen>();
            var repo = new RepositorioFalso();

            Assert.AreEqual(HttpStatusCode.OK, (await ServicioConAvisos(repo, avisos).Decidir("1", 930500, Decision("Esperar"), Usuario("NUEVAVISION\\Ana"))).Codigo);
            A.CallTo(() => avisos.HaCambiado("Packing:100070", null, null, AvisosRetencionPorFalta.DISPOSITIVO)).MustHaveHappenedOnceExactly();

            Fake.ClearRecordedCalls(avisos);
            repo.AlEsperar = new ResultadoEsperar { PickingSinTerminar = true };
            Assert.AreEqual(HttpStatusCode.Conflict, (await ServicioConAvisos(repo, avisos).Decidir("1", 930500, Decision("Esperar"), Usuario("NUEVAVISION\\Ana"))).Codigo);
            A.CallTo(avisos).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AvisoPacking_PlazoVencido_AvisaAlPackingDeSuPicking()
        {
            var avisos = A.Fake<NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen>();
            var repo = new RepositorioFalso();

            Assert.AreEqual(1, (await ServicioConAvisos(repo, avisos, AHORA.AddHours(3)).AplicarVencidas()).Count);

            A.CallTo(() => avisos.HaCambiado("Packing:100070", null, null, AvisosRetencionPorFalta.DISPOSITIVO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AvisoPacking_SiFalla_NoRompeLaDecisionNiElJob()
        {
            var avisos = A.Fake<NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen>();
            A.CallTo(() => avisos.HaCambiado(A<string>._, A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("SignalR caído"));

            var repo = new RepositorioFalso();
            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> servir = await ServicioConAvisos(repo, avisos).Decidir("1", 930500, Decision("ServirLoQueHay"), Usuario("NUEVAVISION\\Ana"));
            Assert.AreEqual(HttpStatusCode.OK, servir.Codigo);
            Assert.AreEqual(EstadosRetencionPorFalta.SERVIR_LO_QUE_HAY, servir.Contenido.Estado);

            ResultadoRetencionPorFalta<RetencionPorFaltaDTO> esperar = await ServicioConAvisos(repo, avisos).Decidir("1", 930500, Decision("Esperar"), Usuario("NUEVAVISION\\Ana"));
            Assert.AreEqual(HttpStatusCode.OK, esperar.Codigo);

            List<RetencionPorFaltaDTO> aplicadas = await ServicioConAvisos(new RepositorioFalso(), avisos, AHORA.AddHours(3)).AplicarVencidas();
            Assert.AreEqual(930500, aplicadas.Single().Pedido);
        }

        [TestMethod]
        public void AvisoPacking_HayRetenidos()
        {
            Assert.IsFalse(AvisosRetencionPorFalta.HayRetenidos(null));
            Assert.IsFalse(AvisosRetencionPorFalta.HayRetenidos(new[] { new FaltaPickingPedido { Pedido = 1 } }));
            Assert.IsTrue(AvisosRetencionPorFalta.HayRetenidos(new[] { new FaltaPickingPedido { Pedido = 1 }, new FaltaPickingPedido { Pedido = 2, Retenido = true } }));
        }

        [TestMethod]
        public async Task Plazo_PickingSinTerminar_SeDejaParaLaSiguientePasada()
        {
            var repo = new RepositorioFalso { AlEsperar = new ResultadoEsperar { PickingSinTerminar = true } };
            Assert.AreEqual(0, (await Servicio(repo, AHORA.AddHours(3)).AplicarVencidas()).Count);
        }
    }
}
