using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#880 (corte 1, Carlos 19/11/26): entrega incompleta del reparto propio. «Solo entrego los leídos» deja en la furgoneta los no
    /// leídos; los que no se cargaron, volvieron al almacén o lleva otro también faltan. El envío queda Entregado con el DetalleEstado
    /// «4 de 5 bultos», una línea de historia y UN aviso (Almacén, quien hizo el pedido y su vendedor).
    /// </summary>
    public partial class FurgonetaBultosTests
    {
        private const int ENVIO_INCOMPLETO = 251099;

        private static EntregaRepartoDTO EntregaSoloLeidos(Guid? clave, params string[] bultos)
        {
            EntregaRepartoDTO entrega = Entrega(clave, bultos);
            entrega.SoloLeidos = true;
            return entrega;
        }

        private static string[] Rp(int envio, params int[] bultos) => bultos.Select(b => $"RP{envio}-{b}").ToArray();

        [TestMethod]
        public async Task Issue880_CasoCarlos_Cargados5Leidos4SoloLeidos_ElQuintoSeQuedaEnLaFurgonetaYSeAvisaUnaVez()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 5);
            await CargarTodos(ENVIO_INCOMPLETO, 5, MIGUEL);
            Guid clave = Guid.NewGuid();

            var r = await servicio.Entregar(ENVIO_INCOMPLETO, EntregaSoloLeidos(clave, Rp(ENVIO_INCOMPLETO, 1, 2, 3, 4)), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            CollectionAssert.AreEqual(new[] { "RP251099-5" }, r.Contenido.BultosSinEntregar);
            Assert.AreEqual("no se ha leído al entregar", r.Contenido.BultosQueFaltan.Single().Motivo);
            Assert.AreEqual("NoLeidoSeQueda", r.Contenido.BultosQueFaltan.Single().Situacion);
            Assert.AreEqual(4, r.Contenido.BultosEntregados);
            CollectionAssert.AreEqual(new[] { "RP251099-5" }, (await LoQueLleva(MIGUEL)).Select(b => b.Codigo).ToList(), "El quinto sigue en la furgoneta");
            EnviosAgencia envio = repositorio.Envios[ENVIO_INCOMPLETO];
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            Assert.AreEqual("Entregado (reparto propio): 4 de 5 bultos. Falta RP251099-5", envio.DetalleEstado);
            Assert.IsTrue(EntregaIncompletaReparto.EsIncompleta(envio.DetalleEstado));
            EnvioHistoria linea = repositorio.Registros.Last().Historia.Single(h => h.Campo == "Bultos");
            StringAssert.Contains(linea.Observaciones, "Falta RP251099-5: no se ha leído al entregar.");
            Assert.AreEqual(1, avisosIncompleta.Llamadas.Count);
            Assert.AreEqual(MIGUEL, avisosIncompleta.Llamadas[0].Repartidor);
            StringAssert.Contains(EntregaIncompletaReparto.Notificacion(envio, avisosIncompleta.Llamadas[0].Evaluacion, "Miguel").Cuerpo,
                "con 4 de 5 bultos. Falta RP251099-5: no se ha leído al entregar.");

            // Reintento con la misma clave: la misma respuesta, sin otro aviso ni otro movimiento
            int filas = furgoneta.Filas.Count;
            var otra = await servicio.Entregar(ENVIO_INCOMPLETO, EntregaSoloLeidos(clave, Rp(ENVIO_INCOMPLETO, 1, 2, 3, 4)), MIGUEL);
            Assert.AreEqual(TipoResultadoReparto.Ok, otra.Tipo, otra.Mensaje);
            CollectionAssert.AreEqual(new[] { "RP251099-5" }, otra.Contenido.BultosSinEntregar);
            Assert.AreEqual(1, avisosIncompleta.Llamadas.Count, "Una sola vez");
            Assert.AreEqual(filas, furgoneta.Filas.Count);
        }

        [TestMethod]
        public async Task Issue880_NoSeCargo_Lleva4De5_FaltaElQuintoConSuMotivo()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 5);
            await CargarTodos(ENVIO_INCOMPLETO, 4, MIGUEL);

            var r = await servicio.Entregar(ENVIO_INCOMPLETO, Entrega(null, Rp(ENVIO_INCOMPLETO, 1, 2, 3, 4)), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            CollectionAssert.AreEqual(new[] { "RP251099-5" }, r.Contenido.BultosSinEntregar);
            Assert.AreEqual("no se cargó", r.Contenido.BultosQueFaltan.Single().Motivo);
            Assert.AreEqual("Entregado con 4 de 5 bultos. Falta RP251099-5: no se cargó.", r.Contenido.ResumenEntregaIncompleta);
            Assert.AreEqual("Entregado (reparto propio): 4 de 5 bultos. Falta RP251099-5", repositorio.Envios[ENVIO_INCOMPLETO].DetalleEstado);
            Assert.AreEqual(1, avisosIncompleta.Llamadas.Count);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
        }

        [TestMethod]
        public async Task Issue880_SinSoloLeidos_ComoHoy_LosNoLeidosSeCierranYNoFaltaNada()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 5);
            await CargarTodos(ENVIO_INCOMPLETO, 5, MIGUEL);

            var r = await servicio.Entregar(ENVIO_INCOMPLETO, Entrega(null, Rp(ENVIO_INCOMPLETO, 1, 2, 3, 4)), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsNull(r.Contenido.BultosSinEntregar);
            Assert.AreEqual(5, r.Contenido.BultosEntregados);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            Assert.AreEqual(ServicioRepartoPropio.DETALLE_ENTREGADO, repositorio.Envios[ENVIO_INCOMPLETO].DetalleEstado);
            Assert.IsFalse(repositorio.Registros.Last().Historia.Any(h => h.Campo == "Bultos"));
            Assert.AreEqual(0, avisosIncompleta.Llamadas.Count);
        }

        [TestMethod]
        public async Task Issue880_SoloLeidosSinLeerNinguno_SeIgnora()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 2);
            await CargarTodos(ENVIO_INCOMPLETO, 2, MIGUEL);
            EntregaRepartoDTO entrega = Entrega();
            entrega.SoloLeidos = true;

            var r = await servicio.Entregar(ENVIO_INCOMPLETO, entrega, MIGUEL);

            Assert.IsNull(r.Contenido.BultosSinEntregar);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            Assert.AreEqual(0, avisosIncompleta.Llamadas.Count);
        }

        [TestMethod]
        public async Task Issue880_SalioSinLeerNadaYSeEntregaSinLeer_NoAvisaNiMarca()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 3);

            var r = await servicio.Entregar(ENVIO_INCOMPLETO, Entrega(), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsNull(r.Contenido.BultosSinEntregar);
            Assert.AreEqual(ServicioRepartoPropio.DETALLE_ENTREGADO, repositorio.Envios[ENVIO_INCOMPLETO].DetalleEstado);
            Assert.AreEqual(0, avisosIncompleta.Llamadas.Count);
        }

        [TestMethod]
        public async Task Issue880_LoLlevaOtro_FaltaConSuNombre()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 3);
            _ = await servicio.Cargar(Carga($"RP{ENVIO_INCOMPLETO}-1"), MIGUEL);
            _ = await servicio.Cargar(Carga($"RP{ENVIO_INCOMPLETO}-2"), MIGUEL);
            MovimientoBultoReparto deAndre = Mov(1000, $"RP{ENVIO_INCOMPLETO}-3", TiposMovimientoBulto.CARGADO, ANDRE, AHORA.AddMinutes(-5));
            deAndre.EnvioId = ENVIO_INCOMPLETO;
            furgoneta.Filas.Add(deAndre);

            var r = await servicio.Entregar(ENVIO_INCOMPLETO, Entrega(null, $"RP{ENVIO_INCOMPLETO}-1"), MIGUEL);

            Assert.AreEqual($"lo lleva {ServicioRepartoPropio.NombreRepartidor(ANDRE)}", r.Contenido.BultosQueFaltan.Single().Motivo);
            Assert.AreEqual("Entregado (reparto propio): 2 de 3 bultos. Falta RP251099-3", repositorio.Envios[ENVIO_INCOMPLETO].DetalleEstado);
        }

        [TestMethod]
        public void Issue880_Regla_DescargadoAntes_VolvioAlAlmacen()
        {
            var historia = new List<MovimientoBultoReparto>
            {
                MovRp(1, 1, TiposMovimientoBulto.CARGADO, MIGUEL, AHORA.AddHours(-2)),
                MovRp(2, 2, TiposMovimientoBulto.CARGADO, MIGUEL, AHORA.AddHours(-2)),
                MovRp(3, 2, TiposMovimientoBulto.DESCARGADO, MIGUEL, AHORA.AddHours(-1))
            };

            EvaluacionBultosEntrega e = EntregaIncompletaReparto.Evaluar(ENVIO_INCOMPLETO, 2, historia, null, new[] { "RP251099-1" }, MIGUEL, false);

            Assert.AreEqual(SituacionBultoEntrega.Leido, e.Bultos[0].Situacion);
            Assert.AreEqual(SituacionBultoEntrega.VolvioAlAlmacen, e.Bultos[1].Situacion);
            Assert.IsTrue(e.Incompleta);
        }

        [TestMethod]
        public void Issue880_Regla_CargaSinMovimiento_CuentaComoCargado()
        {
            var cargas = new List<CargaBultoReparto> { new CargaBultoReparto { Id = 1, EnvioId = ENVIO_INCOMPLETO, Bulto = 2, Usuario = MIGUEL } };

            EvaluacionBultosEntrega e = EntregaIncompletaReparto.Evaluar(ENVIO_INCOMPLETO, 2, null, cargas, new[] { "RP251099-1" }, MIGUEL, false);

            Assert.AreEqual(SituacionBultoEntrega.EntregadoSinLeer, e.Bultos[1].Situacion);
            Assert.IsFalse(e.Incompleta);
        }

        [TestMethod]
        public void Issue880_Detalle_NoPasaDeLaColumna()
        {
            EvaluacionBultosEntrega e = EntregaIncompletaReparto.Evaluar(ENVIO_INCOMPLETO, 40, null, null, new[] { "RP251099-1" }, MIGUEL, false);

            string detalle = EntregaIncompletaReparto.Detalle(e);

            Assert.IsTrue(detalle.Length <= EntregaIncompletaReparto.LONGITUD_DETALLE, detalle);
            StringAssert.StartsWith(detalle, "Entregado (reparto propio): 1 de 40 bultos. Faltan ");
            Assert.IsTrue(EntregaIncompletaReparto.EsIncompleta(detalle));
            Assert.IsFalse(EntregaIncompletaReparto.EsIncompleta(ServicioRepartoPropio.DETALLE_ENTREGADO));
            Assert.IsFalse(EntregaIncompletaReparto.EsIncompleta(null));
        }

        [TestMethod]
        public async Task Issue880_Job22_NoCierraElBultoDeUnaEntregaIncompleta()
        {
            // Lo que el job pregunta a la BD: solo devuelve los entregados COMPLETOS (EnviosEntregados filtra por EsIncompleta)
            Envio(ENVIO_INCOMPLETO, bultos: 5);
            await CargarTodos(ENVIO_INCOMPLETO, 5, MIGUEL);
            _ = await servicio.Entregar(ENVIO_INCOMPLETO, EntregaSoloLeidos(null, Rp(ENVIO_INCOMPLETO, 1, 2, 3, 4)), MIGUEL);
            EnviosAgencia envio = repositorio.Envios[ENVIO_INCOMPLETO];
            var job = new FurgonetaSinDescargarJobsService(furgoneta, avisosFurgoneta, () => AHORA.Date.AddHours(22),
                enviosEntregados: envios => Task.FromResult(new HashSet<int>(envios.Where(n => repositorio.Envios.TryGetValue(n, out EnviosAgencia e)
                    && e.Estado == Constantes.Agencias.ESTADO_ENTREGADO && !EntregaIncompletaReparto.EsIncompleta(e.DetalleEstado)))));

            int avisados = await job.AvisarSinDescargar();

            Assert.IsTrue(EntregaIncompletaReparto.EsIncompleta(envio.DetalleEstado));
            Assert.AreEqual(1, avisados, "Se avisa de la furgoneta con el bulto que falta");
            CollectionAssert.AreEqual(new[] { "RP251099-5" }, (await LoQueLleva(MIGUEL)).Select(b => b.Codigo).ToList(), "No lo da por entregado");
        }

        [TestMethod]
        public async Task Issue880_Avisos_CampanaAAlmacenYAlPedido_YSinFaltaNada()
        {
            var push = FakeItEasy.A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var alPedido = new List<NotificacionPushDTO>();
            var avisos = new AvisosEntregaIncompleta(() => push, g => g == Constantes.GruposSeguridad.ALMACEN ? new List<string> { "Andre", "Pedro" } : new List<string>(),
                (e, n) => { alPedido.Add(n); return Task.CompletedTask; });
            EnviosAgencia envio = Envio(ENVIO_INCOMPLETO, bultos: 2);
            EvaluacionBultosEntrega incompleta = EntregaIncompletaReparto.Evaluar(ENVIO_INCOMPLETO, 2, null, null, new[] { "RP251099-1" }, MIGUEL, false);
            EvaluacionBultosEntrega completa = EntregaIncompletaReparto.Evaluar(ENVIO_INCOMPLETO, 1, null, null, new[] { "RP251099-1" }, MIGUEL, false);

            await avisos.Avisar(envio, incompleta, MIGUEL);
            await avisos.Avisar(envio, completa, MIGUEL);

            FakeItEasy.A.CallTo(() => push.GuardarEnBuzonDeUsuario(FakeItEasy.A<string>._, Constantes.Aplicaciones.NESTO, FakeItEasy.A<NotificacionPushDTO>._))
                .MustHaveHappened(2, FakeItEasy.Times.Exactly);
            Assert.AreEqual(1, alPedido.Count);
            Assert.AreEqual(EntregaIncompletaReparto.TIPO_NOTIFICACION, alPedido[0].Tipo);
            StringAssert.Contains(alPedido[0].Cuerpo, "Falta RP251099-2: no se cargó.");
        }

        [TestMethod]
        public async Task Novedad1366_AlEntregar_SeMiraSiSobraElReenvio_UnaVez()
        {
            Envio(ENVIO_INCOMPLETO, bultos: 1);
            var mirados = new List<(int Envio, string Motivo)>();
            servicio.ResolverReenvioSobrante = (e, u, m) => { mirados.Add((e.Numero, m)); return Task.FromResult(NestoAPI.Infraestructure.Agencias.ResultadoReenvioEntregado.SinReenvio); };
            Guid clave = Guid.NewGuid();

            _ = await servicio.Entregar(ENVIO_INCOMPLETO, Entrega(clave), MIGUEL);
            _ = await servicio.Entregar(ENVIO_INCOMPLETO, Entrega(clave), MIGUEL);

            Assert.AreEqual(1, mirados.Count, "El reintento no vuelve a mirar");
            Assert.AreEqual((ENVIO_INCOMPLETO, "Ya está entregado"), mirados[0]);
        }

        private static MovimientoBultoReparto MovRp(int id, int bulto, string tipo, string usuario, DateTime fechaHora)
            => new MovimientoBultoReparto
            {
                Id = id, Codigo = $"RP{ENVIO_INCOMPLETO}-{bulto}", TipoCodigo = CodigoBultoFurgoneta.RP, EnvioId = ENVIO_INCOMPLETO, Bulto = bulto,
                Tipo = tipo, Usuario = usuario, FechaHora = fechaHora, FechaRegistro = fechaHora, ClaveIdempotencia = Guid.NewGuid()
            };

        internal class AvisosEntregaIncompletaFalsos : IAvisosEntregaIncompleta
        {
            public readonly List<(EnviosAgencia Envio, EvaluacionBultosEntrega Evaluacion, string Repartidor)> Llamadas
                = new List<(EnviosAgencia, EvaluacionBultosEntrega, string)>();

            public Task Avisar(EnviosAgencia envio, EvaluacionBultosEntrega evaluacion, string repartidor)
            {
                Llamadas.Add((envio, evaluacion, repartidor));
                return Task.CompletedTask;
            }
        }
    }
}
