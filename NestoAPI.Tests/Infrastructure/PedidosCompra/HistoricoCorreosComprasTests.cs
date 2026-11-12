using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857: lectura hacia atrás del buzón de Compras y adjuntos (lector, repositorio y Hangfire falsos).</summary>
    [TestClass]
    public class HistoricoCorreosComprasTests
    {
        // Jueves 12/11/26 a las 22:00: fuera de horario
        private static readonly DateTime NOCHE = new DateTime(2026, 11, 12, 22, 0, 0);
        private static readonly DateTime MANANA_LABORABLE = new DateTime(2026, 11, 12, 10, 0, 0);
        private const string BUZON = "buzon-prueba@ejemplo.test";
        private static readonly string CLAVE = ServicioHistoricoCorreosCompras.Clave(BUZON);

        private ILectorBuzonCompras lector;
        private IRepositorioCorreosSeguimiento repositorio;
        private IProgramadorHistoricoCorreos programador;
        private Dictionary<string, string> estados;
        private HashSet<string> enTabla;
        private List<CorreoSeguimientoFila> insertadas;
        private List<Exception> errores;
        private List<TimeSpan> esperas;
        private DateTime reloj;

        [TestInitialize]
        public void Preparar()
        {
            estados = new Dictionary<string, string>();
            enTabla = new HashSet<string>();
            insertadas = new List<CorreoSeguimientoFila>();
            errores = new List<Exception>();
            esperas = new List<TimeSpan>();
            reloj = NOCHE;

            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns(BUZON);

            repositorio = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repositorio.TablasExisten()).Returns(true);
            A.CallTo(() => repositorio.AdjuntosDisponibles()).Returns(false);
            A.CallTo(() => repositorio.LeerEstado(A<string>._)).ReturnsLazily((string clave) =>
                Task.FromResult(estados.TryGetValue(clave, out string v) ? v : null));
            A.CallTo(() => repositorio.GuardarEstado(A<string>._, A<string>._, A<string>._)).ReturnsLazily((string clave, string valor, string _) =>
            {
                estados[clave] = valor;
                return Task.CompletedTask;
            });
            A.CallTo(() => repositorio.LeerRemitentes()).Returns(new List<RemitenteMarcado>());
            A.CallTo(() => repositorio.LeerPedidosDesde(A<string>._, A<DateTime>._)).Returns(new List<PedidoCompraEnAire>());
            A.CallTo(() => repositorio.LeerPendientesDeMedirPorId(A<DateTime>._, A<int>._, A<int>._)).Returns(new List<CorreoSeguimientoFila>());
            A.CallTo(() => repositorio.Insertar(A<CorreoSeguimientoFila>._)).ReturnsLazily((CorreoSeguimientoFila f) =>
            {
                if (!enTabla.Add(f.MensajeId))
                {
                    return Task.FromResult(false);
                }
                insertadas.Add(f);
                return Task.FromResult(true);
            });

            programador = A.Fake<IProgramadorHistoricoCorreos>();
        }

        private ServicioHistoricoCorreosCompras Servicio(bool sinLector = false) => new ServicioHistoricoCorreosCompras(
            sinLector ? null : lector, repositorio, programador, () => reloj, () => reloj,
            espera => { esperas.Add(espera); return Task.CompletedTask; }, errores.Add);

        private EstadoHistoricoCorreosCompras Estado() => ServicioHistoricoCorreosCompras.Deserializar(estados.TryGetValue(CLAVE, out string v) ? v : null);

        private static CorreoCompras Seguimiento(string id, DateTime recibido, bool adjuntos = false) => new CorreoCompras
        {
            Id = "graph-" + id,
            MensajeId = "<" + id + "@ejemplo.test>",
            Recibido = recibido,
            RemitenteDireccion = "avisos@gls-spain.es",
            Asunto = "Su envío está en camino",
            Cuerpo = "<a href=\"https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=61770001" + id.Length + "11\">Seguir</a>",
            CuerpoEsHtml = true,
            TieneAdjuntos = adjuntos
        };

        private static CorreoCompras SinSeguimiento(string id, DateTime recibido) => new CorreoCompras
        {
            Id = "graph-" + id, MensajeId = "<" + id + "@ejemplo.test>", Recibido = recibido, RemitenteDireccion = "facturas@proveedor-inventado.es",
            Asunto = "Factura", Cuerpo = "Adjunto la factura. Un saludo."
        };

        private static LecturaBuzonCompras Pagina(string siguiente, params CorreoCompras[] correos) =>
            new LecturaBuzonCompras { Correos = correos.ToList(), SiguienteEnlace = siguiente, Completa = siguiente == null };

        private async Task<EstadoHistoricoCorreosCompras> LanzarYEjecutar(int meses = 12)
        {
            ResultadoLanzamientoHistorico lanzamiento = await Servicio().Lanzar(meses, false, "prueba").ConfigureAwait(false);
            Assert.AreEqual(CodigoLanzamientoHistorico.Aceptado, lanzamiento.Codigo, lanzamiento.Mensaje);
            await Servicio().Ejecutar(false).ConfigureAwait(false);
            return Estado();
        }

        [TestMethod]
        public async Task Historico_RecorreLasPaginasConElMismoNucleoYNoTocaElDelta()
        {
            DateTime marzo = new DateTime(2026, 3, 2, 9, 0, 0);
            A.CallTo(() => lector.LeerPagina(null, A<DateTime>._, A<DateTime>._))
                .Returns(Pagina("pagina-2", Seguimiento("h1", marzo), SinSeguimiento("h2", marzo.AddHours(1))));
            A.CallTo(() => lector.LeerPagina("pagina-2", A<DateTime>._, A<DateTime>._))
                .Returns(Pagina(null, Seguimiento("h33", marzo.AddDays(1))));

            EstadoHistoricoCorreosCompras estado = await LanzarYEjecutar();

            Assert.AreEqual(EstadoHistoricoCorreosCompras.TERMINADO, estado.Estado);
            Assert.AreEqual(3, estado.Leidos);
            Assert.AreEqual(2, estado.Guardados);
            Assert.AreEqual(2, estado.Paginas);
            Assert.AreEqual(marzo.AddDays(1), estado.Cursor);
            Assert.AreEqual(NOCHE.AddMonths(-12), estado.Desde);
            Assert.AreEqual(NOCHE, estado.Hasta);
            Assert.AreEqual("GLS", insertadas[0].Agencia, "El mismo análisis que el job");
            A.CallTo(() => lector.LeerPagina(null, NOCHE.AddMonths(-12), NOCHE)).MustHaveHappenedOnceExactly();
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarEstado(BUZON, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerEstado(BUZON)).MustNotHaveHappened();
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Historico_EsIdempotente_LanzadoDosVecesNoDuplica()
        {
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(Pagina(null, Seguimiento("h1", new DateTime(2026, 5, 1))));

            EstadoHistoricoCorreosCompras primera = await LanzarYEjecutar();
            EstadoHistoricoCorreosCompras segunda = await LanzarYEjecutar();

            Assert.AreEqual(1, primera.Guardados);
            Assert.AreEqual(0, segunda.Guardados, "Terminada, la segunda empieza de cero y no duplica");
            Assert.AreEqual(1, segunda.YaGuardados);
            Assert.AreEqual(1, insertadas.Count);
        }

        [TestMethod]
        public async Task Historico_Cortado_AlVolverALanzarloSigueDesdeElCursor()
        {
            DateTime cursor = new DateTime(2026, 6, 15, 12, 30, 0);
            estados[CLAVE] = JsonConvert.SerializeObject(new EstadoHistoricoCorreosCompras
            {
                Estado = EstadoHistoricoCorreosCompras.EN_MARCHA,
                Meses = 12,
                Desde = NOCHE.AddMonths(-13),
                Hasta = NOCHE.AddDays(-2),
                Cursor = cursor,
                Leidos = 900,
                Guardados = 40,
                Actualizado = NOCHE.AddHours(-3) // sin latido: se cortó (reciclado del pool)
            });
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(Pagina(null, Seguimiento("h9", cursor.AddMinutes(5))));

            EstadoHistoricoCorreosCompras estado = await LanzarYEjecutar();

            A.CallTo(() => lector.LeerPagina(null, cursor, NOCHE.AddDays(-2))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(EstadoHistoricoCorreosCompras.TERMINADO, estado.Estado);
            Assert.AreEqual(901, estado.Leidos);
            Assert.AreEqual(41, estado.Guardados);
            Assert.AreEqual(NOCHE.AddMonths(-13), estado.Desde, "Conserva el tramo de la primera vez");
        }

        [TestMethod]
        public async Task Historico_429_EsperaElRetryAfterConTopeYReintentaLaMismaPagina()
        {
            int llamadas = 0;
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(++llamadas == 1
                ? new LecturaBuzonCompras { Error = "Graph ha contestado 429", Estado = (HttpStatusCode)429, EsperarAntesDeReintentar = TimeSpan.FromMinutes(20) }
                : Pagina(null, Seguimiento("h1", new DateTime(2026, 5, 1)))));

            EstadoHistoricoCorreosCompras estado = await LanzarYEjecutar();

            CollectionAssert.AreEqual(new[] { ServicioHistoricoCorreosCompras.ESPERA_MAXIMA }, esperas);
            Assert.AreEqual(2, llamadas);
            A.CallTo(() => lector.LeerPagina(null, A<DateTime>._, A<DateTime>._)).MustHaveHappenedTwiceExactly();
            Assert.AreEqual(EstadoHistoricoCorreosCompras.TERMINADO, estado.Estado);
            Assert.AreEqual(0, estado.FallosSeguidos);
            Assert.AreEqual(0, errores.Count, "Un 429 que se recupera no va a ELMAH");
        }

        [TestMethod]
        public async Task Historico_FallosSeguidos_SeParaConFalloYUnaSolaLineaEnElmah()
        {
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(new LecturaBuzonCompras { Error = "Graph ha contestado 401 (Unauthorized)", Estado = HttpStatusCode.Unauthorized });

            EstadoHistoricoCorreosCompras estado = await LanzarYEjecutar();
            await Servicio().Ejecutar(false); // un trabajo repetido no vuelve a empezar solo

            Assert.AreEqual(EstadoHistoricoCorreosCompras.FALLO, estado.Estado);
            StringAssert.Contains(estado.UltimoError, "401");
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .MustHaveHappened(ServicioHistoricoCorreosCompras.MAXIMO_FALLOS_SEGUIDOS, Times.Exactly);
            Assert.AreEqual(1, errores.Count);
            Assert.AreEqual(ServicioHistoricoCorreosCompras.MAXIMO_FALLOS_SEGUIDOS - 1, esperas.Count);
        }

        [TestMethod]
        public async Task Historico_SiFallaUnaInsercion_NoAvanzaElCursorYReintenta()
        {
            int intentos = 0;
            A.CallTo(() => repositorio.Insertar(A<CorreoSeguimientoFila>._)).ReturnsLazily((CorreoSeguimientoFila f) =>
            {
                if (++intentos == 1)
                {
                    throw new Exception("bloqueo");
                }
                insertadas.Add(f);
                return Task.FromResult(true);
            });
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(Pagina(null, Seguimiento("h1", new DateTime(2026, 5, 1))));

            EstadoHistoricoCorreosCompras estado = await LanzarYEjecutar();

            Assert.AreEqual(1, estado.Paginas);
            Assert.AreEqual(1, estado.Leidos, "La página fallida no cuenta");
            Assert.AreEqual(1, insertadas.Count);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Lanzar_EnHorario_SeProgramaParaLas21SinLeerNada()
        {
            reloj = MANANA_LABORABLE;

            ResultadoLanzamientoHistorico resultado = await Servicio().Lanzar(12, false, "prueba");

            Assert.AreEqual(CodigoLanzamientoHistorico.Aceptado, resultado.Codigo);
            StringAssert.Contains(resultado.Mensaje, "21:00");
            A.CallTo(() => programador.Programar(new DateTime(2026, 11, 12, 21, 0, 0), false)).MustHaveHappenedOnceExactly();
            A.CallTo(() => programador.Encolar(A<bool>._)).MustNotHaveHappened();
            Assert.AreEqual(EstadoHistoricoCorreosCompras.PROGRAMADO, Estado().Estado);
            Assert.AreEqual(new DateTime(2026, 11, 12, 21, 0, 0), Estado().ProgramadoPara);
        }

        [TestMethod]
        public async Task Ejecutar_EnHorario_SeReprogramaYNoLee()
        {
            await Servicio().Lanzar(12, false, "prueba"); // de noche: encolado
            reloj = MANANA_LABORABLE; // Hangfire lo coge tarde, ya en horario

            await Servicio().Ejecutar(false);

            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => programador.Programar(new DateTime(2026, 11, 12, 21, 0, 0), false)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(EstadoHistoricoCorreosCompras.PROGRAMADO, Estado().Estado);
        }

        [TestMethod]
        public async Task Lanzar_ConAhora_EnHorarioSeEncolaForzado()
        {
            reloj = MANANA_LABORABLE;
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Pagina(null));

            ResultadoLanzamientoHistorico resultado = await Servicio().Lanzar(3, true, "prueba");
            await Servicio().Ejecutar(true);

            Assert.AreEqual(CodigoLanzamientoHistorico.Aceptado, resultado.Codigo);
            A.CallTo(() => programador.Encolar(true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => programador.Programar(A<DateTime>._, A<bool>._)).MustNotHaveHappened();
            Assert.AreEqual(EstadoHistoricoCorreosCompras.TERMINADO, Estado().Estado);
        }

        [TestMethod]
        public void Horario_LaborablesDe8A20()
        {
            Assert.IsTrue(HorarioHistoricoCorreos.EnHorario(new DateTime(2026, 11, 12, 8, 0, 0)));
            Assert.IsTrue(HorarioHistoricoCorreos.EnHorario(new DateTime(2026, 11, 13, 19, 59, 0)));
            Assert.IsFalse(HorarioHistoricoCorreos.EnHorario(new DateTime(2026, 11, 12, 20, 0, 0)));
            Assert.IsFalse(HorarioHistoricoCorreos.EnHorario(new DateTime(2026, 11, 12, 7, 59, 0)));
            Assert.IsFalse(HorarioHistoricoCorreos.EnHorario(new DateTime(2026, 11, 14, 11, 0, 0)), "Sábado");
        }

        [TestMethod]
        public async Task Lanzar_ConUnaEnMarcha_YaEnMarcha()
        {
            estados[CLAVE] = JsonConvert.SerializeObject(new EstadoHistoricoCorreosCompras
            {
                Estado = EstadoHistoricoCorreosCompras.EN_MARCHA, Meses = 12, Desde = NOCHE.AddMonths(-12), Hasta = NOCHE, Actualizado = NOCHE.AddMinutes(-2)
            });

            ResultadoLanzamientoHistorico resultado = await Servicio().Lanzar(12, true, "prueba");

            Assert.AreEqual(CodigoLanzamientoHistorico.YaEnMarcha, resultado.Codigo);
            A.CallTo(programador).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Lanzar_SinClavesOSinTabla_NoProgramaNada()
        {
            Assert.AreEqual(CodigoLanzamientoHistorico.SinConfiguracion, (await Servicio(sinLector: true).Lanzar(12, false, "x")).Codigo);
            A.CallTo(() => repositorio.TablasExisten()).Returns(false);
            Assert.AreEqual(CodigoLanzamientoHistorico.SinTabla, (await Servicio().Lanzar(12, false, "x")).Codigo);
            await Servicio().Ejecutar(true);
            A.CallTo(programador).MustNotHaveHappened();
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Historico_AsignaContraLosPedidosEnElAireElDiaDelCorreoYMidePorLotes()
        {
            DateTime dia = new DateTime(2026, 4, 10, 9, 0, 0);
            var correo = Seguimiento("h1", dia);
            correo.Asunto = "Su pedido 912001 está en camino";
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Pagina(null, correo));
            A.CallTo(() => repositorio.LeerPedidosDesde("1", NOCHE.AddMonths(-12).Date)).Returns(new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, Proveedor = "9001", FechaPedido = dia.AddDays(-5), FechaRecibidoEntero = dia.AddDays(2) },
                // Aún no existía ese día: no puede ser
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912002, Proveedor = "9001", FechaPedido = dia.AddDays(30) }
            });
            var lote1 = new List<CorreoSeguimientoFila>
            {
                new CorreoSeguimientoFila { Id = 5, Recibido = dia, PedidosCandidatos = "[{\"Empresa\":\"1\",\"Pedido\":912001}]" }
            };
            A.CallTo(() => repositorio.LeerPendientesDeMedirPorId(NOCHE.AddMonths(-12), 0, ServicioHistoricoCorreosCompras.LOTE_MEDICION)).Returns(lote1);
            var albaran = new AlbaranPedidoCompra { Empresa = "1", Pedido = 912001, FechaAlbaran = dia.Date.AddDays(2) };
            A.CallTo(() => repositorio.LeerAlbaranes(A<IEnumerable<CandidatoPedidoCompra>>._)).Returns(new List<AlbaranPedidoCompra> { albaran });

            EstadoHistoricoCorreosCompras estado = await LanzarYEjecutar();

            CollectionAssert.AreEqual(new[] { 912001 }, AsignadorPedidosCompra.Deserializar(insertadas.Single().PedidosCandidatos).Select(c => c.Pedido).ToArray());
            A.CallTo(() => repositorio.GuardarReal(5, albaran)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerPendientesDeMedirPorId(A<DateTime>._, 5, A<int>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, estado.Medidos);
            A.CallTo(() => repositorio.LeerPedidosEnAire(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EnAireEl_HechoAntesYSinRecibirEnteroAntes()
        {
            DateTime dia = new DateTime(2026, 4, 10, 16, 0, 0);
            var pedidos = new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Pedido = 1, FechaPedido = dia.Date },
                new PedidoCompraEnAire { Pedido = 2, FechaPedido = dia.AddDays(1) },
                new PedidoCompraEnAire { Pedido = 3, FechaPedido = dia.AddDays(-9), FechaRecibidoEntero = dia.AddDays(-1) },
                new PedidoCompraEnAire { Pedido = 4, FechaPedido = dia.AddDays(-9), FechaRecibidoEntero = dia.Date },
                new PedidoCompraEnAire { Pedido = 5, FechaEnvio = dia.AddDays(-1) },
                new PedidoCompraEnAire { Pedido = 6 }
            };
            CollectionAssert.AreEqual(new[] { 1, 4, 5, 6 }, AsignadorPedidosCompra.EnAireEl(pedidos, dia).Select(p => p.Pedido).ToArray());
        }

        // ---------- Adjuntos ----------

        [TestMethod]
        public void ResumenAdjuntos_SinLosDeLaFirmaYConTopeDeDiez()
        {
            var adjuntos = Enumerable.Range(1, 12).Select(i => new AdjuntoCorreo { Nombre = $"albaran{i}.pdf", Tipo = "application/pdf", Tamano = 1000 + i })
                .Concat(new[] { new AdjuntoCorreo { Nombre = "image001.png", Tipo = "image/png", Tamano = 5, EnLinea = true } }).ToList();

            ResumenAdjuntos resumen = ResumenAdjuntos.Calcular(adjuntos);

            Assert.AreEqual(12, resumen.Numero, "Cuenta todos los de verdad");
            var lista = JsonConvert.DeserializeObject<List<AdjuntoCorreoCompraDTO>>(resumen.Json);
            Assert.AreEqual(ResumenAdjuntos.MAXIMO, lista.Count);
            Assert.AreEqual("albaran1.pdf", lista[0].Nombre);
            Assert.AreEqual("application/pdf", lista[0].Tipo);
            Assert.AreEqual(1001, lista[0].Tamano);
            Assert.IsFalse(resumen.Json.Contains("image001"));
            Assert.IsTrue(resumen.Json.Length <= ResumenAdjuntos.LONGITUD_JSON);
        }

        [TestMethod]
        public void ResumenAdjuntos_SoloFirma_CeroYSinJson()
        {
            ResumenAdjuntos resumen = ResumenAdjuntos.Calcular(new[] { new AdjuntoCorreo { Nombre = "logo.png", EnLinea = true } });
            Assert.AreEqual(0, resumen.Numero);
            Assert.IsNull(resumen.Json);
        }

        [TestMethod]
        public void ResumenAdjuntos_NombresLarguisimos_ElJsonCabeYEsValido()
        {
            var adjuntos = Enumerable.Range(1, 10).Select(i => new AdjuntoCorreo { Nombre = new string('x', 400) + i, Tipo = "application/pdf", Tamano = i }).ToList();
            ResumenAdjuntos resumen = ResumenAdjuntos.Calcular(adjuntos);
            Assert.AreEqual(10, resumen.Numero);
            Assert.IsTrue(resumen.Json.Length <= ResumenAdjuntos.LONGITUD_JSON);
            Assert.IsTrue(JsonConvert.DeserializeObject<List<AdjuntoCorreoCompraDTO>>(resumen.Json).Count >= 1);
        }

        [TestMethod]
        public async Task Job_ConColumnas_ApuntaLosAdjuntosDeLosQueSeGuardan()
        {
            A.CallTo(() => repositorio.AdjuntosDisponibles()).Returns(true);
            A.CallTo(() => repositorio.LeerEstado(BUZON)).Returns("delta");
            A.CallTo(() => repositorio.LeerPedidosEnAire(A<string>._)).Returns(new List<PedidoCompraEnAire>());
            A.CallTo(() => repositorio.LeerPendientesDeMedir(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>());
            var conAdjunto = Seguimiento("a1", NOCHE, adjuntos: true);
            var sinGuardar = SinSeguimiento("a2", NOCHE);
            sinGuardar.TieneAdjuntos = true;
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(new LecturaBuzonCompras
            {
                Correos = new List<CorreoCompras> { conAdjunto, sinGuardar, Seguimiento("a333", NOCHE) },
                SiguienteEnlace = "delta-2"
            });
            A.CallTo(() => lector.LeerAdjuntos("graph-a1")).Returns(new List<AdjuntoCorreo>
            {
                new AdjuntoCorreo { Nombre = "albaran.pdf", Tipo = "application/pdf", Tamano = 2048 },
                new AdjuntoCorreo { Nombre = "image001.png", Tipo = "image/png", Tamano = 10, EnLinea = true }
            });

            _ = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add).Pasada(NOCHE);

            CorreoSeguimientoFila fila = insertadas.Single(f => f.MensajeId == "<a1@ejemplo.test>");
            Assert.AreEqual(1, fila.NumAdjuntos);
            StringAssert.Contains(fila.Adjuntos, "albaran.pdf");
            Assert.IsNull(insertadas.Single(f => f.MensajeId == "<a333@ejemplo.test>").NumAdjuntos, "Sin hasAttachments no se pregunta");
            A.CallTo(() => lector.LeerAdjuntos(A<string>._)).MustHaveHappenedOnceExactly(); // ni el que no se guarda ni el que no tiene
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Job_SinColumnas_GuardaComoSiempreSinPreguntarAdjuntos()
        {
            A.CallTo(() => repositorio.LeerPendientesDeMedir(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>());
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(new LecturaBuzonCompras
            {
                Correos = new List<CorreoCompras> { Seguimiento("a1", NOCHE, adjuntos: true) },
                SiguienteEnlace = "delta-2"
            });

            ResultadoPasadaCorreosSeguimiento resultado = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add).Pasada(NOCHE);

            Assert.AreEqual(1, resultado.Guardados);
            Assert.IsNull(insertadas.Single().NumAdjuntos);
            A.CallTo(() => lector.LeerAdjuntos(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_AdjuntosIlegibles_SeGuardaIgualSinEllos()
        {
            A.CallTo(() => repositorio.AdjuntosDisponibles()).Returns(true);
            A.CallTo(() => repositorio.LeerPendientesDeMedir(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>());
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(new LecturaBuzonCompras
            {
                Correos = new List<CorreoCompras> { Seguimiento("a1", NOCHE, adjuntos: true) },
                SiguienteEnlace = "delta-2"
            });
            A.CallTo(() => lector.LeerAdjuntos(A<string>._)).Returns(Task.FromResult<List<AdjuntoCorreo>>(null));

            _ = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add).Pasada(NOCHE);

            Assert.IsNull(insertadas.Single().NumAdjuntos);
            A.CallTo(() => repositorio.GuardarEstado(BUZON, "delta-2", A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, errores.Count, "Una línea informativa por tanda");
        }

        // ---------- Lector (Graph falso) ----------

        private class HandlerFalso : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Peticiones = new List<HttpRequestMessage>();
            public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Peticiones.Add(request);
                return Task.FromResult(Responder(request));
            }
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode estado = HttpStatusCode.OK)
            => new HttpResponseMessage(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static (LectorBuzonComprasGraph, HandlerFalso) Lector(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var handler = new HandlerFalso
            {
                Responder = r => r.RequestUri.Host == "login.microsoftonline.com"
                    ? Json("{\"expires_in\":3599,\"access_token\":\"tok\"}")
                    : responder(r)
            };
            var lectorGraph = new LectorBuzonComprasGraph(new ConfiguracionBuzonCompras { Tenant = "t", ClientId = "c", Secreto = "s", Buzon = BUZON },
                new HttpClient(handler), () => new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc));
            return (lectorGraph, handler);
        }

        [TestMethod]
        public async Task LeerPagina_PorFechasOrdenadoSinDeltaYConHasAttachments()
        {
            var (lectorGraph, handler) = Lector(r => Json("{\"value\":[{\"id\":\"m1\",\"internetMessageId\":\"<m1@x>\",\"receivedDateTime\":\"2026-03-02T08:00:00Z\"," +
                "\"hasAttachments\":true,\"subject\":\"Uno\"}],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/siguiente?$skip=50\"}"));

            LecturaBuzonCompras lectura = await lectorGraph.LeerPagina(null, new DateTime(2025, 11, 12, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc));

            HttpRequestMessage peticion = handler.Peticiones.Last();
            Assert.AreEqual(HttpMethod.Get, peticion.Method);
            string url = Uri.UnescapeDataString(peticion.RequestUri.AbsoluteUri);
            StringAssert.Contains(url, "/mailFolders/inbox/messages?");
            Assert.IsFalse(url.Contains("/delta"), "El histórico no usa el delta");
            StringAssert.Contains(url, "receivedDateTime ge 2025-11-12T00:00:00Z and receivedDateTime lt 2026-11-12T00:00:00Z");
            StringAssert.Contains(url, "$orderby=receivedDateTime asc");
            StringAssert.Contains(url, "hasAttachments");
            Assert.IsTrue(lectura.Correos.Single().TieneAdjuntos);
            Assert.AreEqual("https://graph.microsoft.com/v1.0/siguiente?$skip=50", lectura.SiguienteEnlace);
            Assert.IsFalse(lectura.Completa);
        }

        [TestMethod]
        public async Task LeerPagina_429ConRetryAfter_LoDevuelve()
        {
            var (lectorGraph, _) = Lector(r =>
            {
                HttpResponseMessage respuesta = Json("{\"error\":{\"code\":\"TooManyRequests\"}}", (HttpStatusCode)429);
                respuesta.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
                return respuesta;
            });

            LecturaBuzonCompras lectura = await lectorGraph.LeerPagina("https://graph.microsoft.com/v1.0/siguiente", DateTime.UtcNow, DateTime.UtcNow);

            Assert.AreEqual((HttpStatusCode)429, lectura.Estado);
            Assert.AreEqual(TimeSpan.FromSeconds(42), lectura.EsperarAntesDeReintentar);
        }

        [TestMethod]
        public async Task LeerAdjuntos_SoloMetadatosSinContenido()
        {
            var (lectorGraph, handler) = Lector(r => Json("{\"value\":[{\"name\":\"albaran.pdf\",\"contentType\":\"application/pdf\",\"size\":2048,\"isInline\":false}," +
                "{\"name\":\"image001.png\",\"contentType\":\"image/png\",\"size\":10,\"isInline\":true}]}"));

            List<AdjuntoCorreo> adjuntos = await lectorGraph.LeerAdjuntos("AAMk=");

            string url = Uri.UnescapeDataString(handler.Peticiones.Last().RequestUri.AbsoluteUri);
            StringAssert.Contains(url, "/messages/AAMk=/attachments?$select=name,contentType,size,isInline");
            Assert.AreEqual(HttpMethod.Get, handler.Peticiones.Last().Method);
            Assert.AreEqual(2, adjuntos.Count);
            Assert.AreEqual(2048, adjuntos[0].Tamano);
            Assert.IsTrue(adjuntos[1].EnLinea);
        }

        [TestMethod]
        public async Task LeerAdjuntos_Error_Null()
        {
            var (lectorGraph, _) = Lector(r => Json("{}", HttpStatusCode.InternalServerError));
            Assert.IsNull(await lectorGraph.LeerAdjuntos("x"));
        }

        // ---------- Endpoints ----------

        private ComprasAgenciasEntradaController Controller(params string[] roles) =>
            new ComprasAgenciasEntradaController(repositorio, () => Servicio())
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [TestMethod]
        public async Task PostHistorico_DeCompras_403()
        {
            var resultado = await Controller("Compras").PostHistorico() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(programador).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(25)]
        public async Task PostHistorico_MesesFueraDeRango_400(int meses)
        {
            Assert.IsInstanceOfType(await Controller("Dirección").PostHistorico(meses), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostHistorico_202YDespues409()
        {
            var primero = await Controller("Informática").PostHistorico(6) as NegotiatedContentResult<ResultadoLanzamientoHistorico>;
            var segundo = await Controller("Dirección").PostHistorico(6) as NegotiatedContentResult<ResultadoLanzamientoHistorico>;

            Assert.AreEqual(HttpStatusCode.Accepted, primero.StatusCode);
            Assert.AreEqual(6, primero.Content.Estado.Meses);
            Assert.AreEqual(HttpStatusCode.Conflict, segundo.StatusCode);
            A.CallTo(() => programador.Encolar(false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostHistorico_SinTabla_503()
        {
            A.CallTo(() => repositorio.TablasExisten()).Returns(false);
            var resultado = await Controller("Dirección").PostHistorico() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
        }

        [TestMethod]
        public async Task PostHistorico_SinClaves_503()
        {
            var controller = new ComprasAgenciasEntradaController(repositorio, () => Servicio(sinLector: true))
            {
                User = new GenericPrincipal(new GenericIdentity("x", "Bearer"), new[] { "Dirección" }),
                Request = new HttpRequestMessage()
            };
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await controller.PostHistorico()).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await controller.GetHistorico()).StatusCode);
        }

        [TestMethod]
        public async Task GetHistorico_DeCompras_ElProgreso()
        {
            await Controller("Dirección").PostHistorico(12);

            var resultado = await Controller("Compras").GetHistorico() as OkNegotiatedContentResult<EstadoHistoricoCorreosCompras>;

            Assert.AreEqual(EstadoHistoricoCorreosCompras.PROGRAMADO, resultado.Content.Estado);
            Assert.AreEqual(12, resultado.Content.Meses);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await Controller("Almacén").GetHistorico()).StatusCode);
        }

        [TestMethod]
        public async Task GetCorreos_DevuelveLosAdjuntos()
        {
            A.CallTo(() => repositorio.LeerCorreos(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>
            {
                new CorreoSeguimientoFila { Id = 1, NumAdjuntos = 1, Adjuntos = "[{\"Nombre\":\"albaran.pdf\",\"Tipo\":\"application/pdf\",\"Tamano\":2048}]" },
                new CorreoSeguimientoFila { Id = 2 }
            });

            var resultado = await Controller("Compras").GetCorreos(7) as OkNegotiatedContentResult<List<CorreoSeguimientoCompraDTO>>;

            Assert.AreEqual("albaran.pdf", resultado.Content[0].Adjuntos.Single().Nombre);
            Assert.AreEqual(1, resultado.Content[0].NumAdjuntos);
            Assert.IsNull(resultado.Content[1].NumAdjuntos);
            Assert.IsNull(resultado.Content[1].Adjuntos);
        }
    }
}
