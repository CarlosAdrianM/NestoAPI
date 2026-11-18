using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>NestoAPI#722: aplazar la fecha de entrega de un pedido que ya tiene picking.</summary>
    [TestClass]
    public class AplazamientoPedidoConPickingTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 28);
        private static readonly DateTime VIERNES = new DateTime(2026, 10, 30);

        private static LineaAplazable Linea(int picking, DateTime nueva, int orden = 1)
            => new LineaAplazable { NumeroOrden = orden, Producto = "33896", Picking = picking, FechaAnterior = HOY, FechaNueva = nueva };

        [TestMethod]
        public void Evaluar_SinLineasConPickingQueCambien_Nada()
        {
            Assert.AreEqual(DecisionAplazamiento.Nada, AplazamientoPedidoConPicking.Evaluar(928083, new List<LineaAplazable>(), null, HOY).Decision);
            Assert.AreEqual(DecisionAplazamiento.Nada, AplazamientoPedidoConPicking.Evaluar(928083, new[] { Linea(0, VIERNES) }, null, HOY).Decision, "sin picking");
            Assert.AreEqual(DecisionAplazamiento.Nada, AplazamientoPedidoConPicking.Evaluar(928083, new[] { Linea(99812, HOY) }, null, HOY).Decision, "misma fecha");
        }

        [TestMethod]
        public void Evaluar_ConPickingYSinEnvioTramitado_Aplaza_YAvisa()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(928083,
                new[] { Linea(99813, VIERNES), Linea(99812, VIERNES, 2), Linea(99812, VIERNES.AddDays(-1), 3) },
                new[] { new EnvioDelPedido { Numero = 250300, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO } }, HOY);

            Assert.AreEqual(DecisionAplazamiento.Aplazar, r.Decision);
            CollectionAssert.AreEqual(new[] { 99812, 99813 }, r.Pickings);
            CollectionAssert.AreEqual(new[] { 250300 }, r.Etiquetas, "la etiqueta en curso hay que anularla");
            Assert.AreEqual(VIERNES, r.FechaNueva);
            StringAssert.Contains(r.Mensaje, "los pickings 99812, 99813");
            StringAssert.Contains(r.Mensaje, "30/10/2026");
            StringAssert.Contains(r.Mensaje, "etiqueta del envío 250300");
            StringAssert.Contains(r.Mensaje, "no dejará hacer el envío ni el albarán");
        }

        [TestMethod]
        public void Evaluar_ConEnvioTramitado_Rechaza_ConElTextoDeCarlos()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(928083, new[] { Linea(99812, VIERNES) },
                new[]
                {
                    new EnvioDelPedido { Numero = 250290, Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE },
                    new EnvioDelPedido { Numero = 250300, Estado = Constantes.Agencias.ESTADO_TRAMITADO }
                }, HOY);

            Assert.AreEqual(DecisionAplazamiento.Rechazar, r.Decision);
            Assert.AreEqual(250300, r.EnvioEnAgencia);
            StringAssert.Contains(r.Mensaje, "ya lo tiene la agencia (envío 250300)");
            StringAssert.Contains(r.Mensaje, "no llegamos a tiempo");
            StringAssert.Contains(r.Mensaje, "que el cliente lo rechace");
            StringAssert.Contains(r.Mensaje, "mandamos un correo a logística");
            Assert.IsFalse(r.Mensaje.Contains("llame a la agencia"), "Carlos: no se le dice al vendedor que llame a la agencia");
        }

        [TestMethod]
        public void YaLoTieneLaAgencia_UnDevueltoNoCuenta_NiUnEntregadoSinDato_UnIncidentadoSi()
        {
            Assert.IsFalse(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(new[] { new EnvioDelPedido { Numero = 1, Estado = Constantes.Agencias.ESTADO_DEVUELTO } }, out _));
            // Incidencia 1328: lo entregado no puede contener lo que sigue en el almacén (antes contaba: el fallo del 930358)
            Assert.IsFalse(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(new[] { new EnvioDelPedido { Numero = 2, Estado = Constantes.Agencias.ESTADO_ENTREGADO } }, out _));
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(new[] { new EnvioDelPedido { Numero = 3, Estado = Constantes.Agencias.ESTADO_INCIDENTADO } }, out int envio));
            Assert.AreEqual(3, envio);
        }

        [TestMethod]
        public void Notificacion_AlAlmacen_DiceQuienFechaPickingsYEtiquetas()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(928083, new[] { Linea(99812, VIERNES) },
                new[] { new EnvioDelPedido { Numero = 250300, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO } }, HOY);
            NotificacionPushDTO n = AplazamientoPedidoConPicking.Notificacion(928083, "15191", "Isabel Hurtado", "NUEVAVISION\\Alfredo", r);

            StringAssert.Contains(n.Titulo, "NO ENTREGAR HASTA EL 30/10/2026");
            StringAssert.Contains(n.Titulo, "928083");
            StringAssert.Contains(n.Cuerpo, "Alfredo ha aplazado al 30/10/2026");
            StringAssert.Contains(n.Cuerpo, "Isabel Hurtado");
            StringAssert.Contains(n.Cuerpo, "el picking 99812");
            StringAssert.Contains(n.Cuerpo, "Anulen la etiqueta del envío 250300");
            Assert.AreEqual("2026-10-30", n.Datos["fecha"]);
        }

        [TestMethod]
        public async Task Guardia_PedidoAplazado_BloqueaHastaLaFecha_YNoAntes()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            CabeceraPedidoRetencion cabecera = repo.Cabecera(928083, modo: 2);
            cabecera.LineasEnPicking = 2;
            cabecera.PickingsEnCurso = "99812";
            cabecera.FechaEntregaPicking = VIERNES;
            Func<DateTime> antes = GuardiaPedidoRetenido.hoy;
            try
            {
                GuardiaPedidoRetenido.hoy = () => HOY;
                string motivo = await GuardiaPedidoRetenido.Motivo(repo, "1", 928083);
                StringAssert.Contains(motivo, "aplazado hasta el 30/10/2026");
                StringAssert.Contains(motivo, "el picking 99812");

                GuardiaPedidoRetenido.hoy = () => VIERNES;
                Assert.IsNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 928083), "llegado el día, sale");

                cabecera.FechaEntregaPicking = null;
                GuardiaPedidoRetenido.hoy = () => HOY;
                Assert.IsNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 928083), "sin picking con fecha no hay aplazamiento");
            }
            finally
            {
                GuardiaPedidoRetenido.hoy = antes;
            }
        }

        [TestMethod]
        public async Task AvisadorRetencionAlmacen_MandaCualquierNotificacionAlGrupo()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorRetencionAlmacen(push, () => new List<string> { "Andre" });
            await avisador.Avisar(new NotificacionPushDTO { Titulo = "t", Cuerpo = "c" });
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Andre", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "t"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("Andre", Constantes.Aplicaciones.ARIADNA, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        // ---- NestoAPI#831: la etiqueta del reparto propio sale aunque el reparto sea otro día ----

        private const int REPARTO = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;

        [TestMethod]
        public void Evaluar831_RepartoPropioYaTramitado_NoEsLaAgenciaYaLoTiene_SeAplazaYNoSeAnula()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(928083, new[] { Linea(99812, VIERNES) },
                new[] { new EnvioDelPedido { Numero = 250300, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = REPARTO } }, HOY);

            Assert.AreEqual(DecisionAplazamiento.Aplazar, r.Decision);
            CollectionAssert.AreEqual(new int[0], r.Etiquetas, "la etiqueta RP no se anula");
            CollectionAssert.AreEqual(new[] { 250300 }, r.EnviosRepartoPropio);
            StringAssert.Contains(r.Mensaje, "La etiqueta del reparto propio (envío 250300) sigue valiendo: pasa al reparto del 30/10/2026");
        }

        [TestMethod]
        public void Evaluar831_RepartoPropioEnCurso_NoSeAnula_LaDeOtraAgenciaSi()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(928083, new[] { Linea(99812, VIERNES) },
                new[]
                {
                    new EnvioDelPedido { Numero = 250300, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, Agencia = REPARTO },
                    new EnvioDelPedido { Numero = 250301, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, Agencia = Constantes.Agencias.AGENCIA_CTT }
                }, HOY);

            CollectionAssert.AreEqual(new[] { 250301 }, r.Etiquetas);
            CollectionAssert.AreEqual(new[] { 250300 }, r.EnviosRepartoPropio);
        }

        [TestMethod]
        public void YaLoTieneLaAgencia831_RepartoPropioEntregadoConSuPicking_SiCuenta_OtraAgenciaTramitadaTambien()
        {
            Assert.IsFalse(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 1, Estado = Constantes.Agencias.ESTADO_INCIDENTADO, Agencia = REPARTO } }, out _));
            // Incidencia 1328: un entregado cuenta solo si se sabe que lleva esas líneas (sin dato, ya no)
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 2, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Agencia = REPARTO, Pickings = new List<int> { 99812 } } },
                new[] { 99812 }, out _));
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 3, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = Constantes.Agencias.AGENCIA_CTT } }, out _));
        }

        [TestMethod]
        public void MoverFechaReparto831_SoloLosDelRepartoPropioPorEntregar_NuncaAntesDeHoy()
        {
            var rp = new EnviosAgencia { Numero = 250300, Agencia = REPARTO, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Fecha = HOY, FechaEntrega = HOY };
            var entregado = new EnviosAgencia { Numero = 250301, Agencia = REPARTO, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Fecha = HOY, FechaEntrega = HOY };
            var ctt = new EnviosAgencia { Numero = 250302, Agencia = Constantes.Agencias.AGENCIA_CTT, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, Fecha = HOY, FechaEntrega = HOY.AddDays(1) };

            List<int> movidos = AplazamientoPedidoConPicking.MoverFechaReparto(new[] { rp, entregado, ctt }, VIERNES, HOY);

            CollectionAssert.AreEqual(new[] { 250300 }, movidos);
            Assert.AreEqual(VIERNES, rp.FechaEntrega);
            Assert.AreEqual(HOY, entregado.FechaEntrega);
            Assert.AreEqual(HOY.AddDays(1), ctt.FechaEntrega);

            AplazamientoPedidoConPicking.MoverFechaReparto(new[] { rp }, HOY.AddDays(-3), HOY);
            Assert.AreEqual(HOY, rp.FechaEntrega, "adelantar a un día pasado = hoy");
        }

        [TestMethod]
        public async Task Guardia831_AplazadoDejaPasarLaEtiquetaDelRepartoPropio_ElRetenidoNo()
        {
            var repo = new RepositorioPedidosRetenidosEnMemoria();
            CabeceraPedidoRetencion cabecera = repo.Cabecera(928083, modo: 2);
            cabecera.LineasEnPicking = 2;
            cabecera.PickingsEnCurso = "99812";
            cabecera.FechaEntregaPicking = VIERNES;
            Func<DateTime> antes = GuardiaPedidoRetenido.hoy;
            try
            {
                GuardiaPedidoRetenido.hoy = () => HOY;
                Assert.IsNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 928083, etiquetaRepartoPropio: true), "la etiqueta RP sale");
                StringAssert.Contains(await GuardiaPedidoRetenido.Motivo(repo, "1", 928083, etiquetaRepartoPropio: false), "aplazado hasta el 30/10/2026");
                StringAssert.Contains(await GuardiaPedidoRetenido.Motivo(repo, "1", 928083), "aplazado", "sin decir nada (albarán, otra agencia), bloquea");

                cabecera.ModoServicio = Constantes.Pedidos.ModosServicio.RETENIDO;
                Assert.IsNotNull(await GuardiaPedidoRetenido.Motivo(repo, "1", 928083, etiquetaRepartoPropio: true), "un retenido (modo 5) no sale ni por el reparto propio");
            }
            finally
            {
                GuardiaPedidoRetenido.hoy = antes;
            }
        }

        // ---- Incidencia 1328 (18/11/26): el envío sabe qué picking lleva ----
        // Pedido 930358: 2 líneas en albarán 733248 (picking 100095) salieron en el envío 250999, ENTREGADO el 17/11; las 6 que
        // quedan (picking 100114, estado 1, con fecha 11/11) siguen en el almacén y se les cambia la fecha.

        private static readonly DateTime HOY_1328 = new DateTime(2026, 11, 18);
        private static readonly DateTime ANTERIOR_1328 = new DateTime(2026, 11, 11);
        private static readonly DateTime NUEVA_1328 = new DateTime(2026, 11, 20);

        private static List<LineaAplazable> SeisLineasDel100114()
            => Enumerable.Range(3, 6).Select(orden => new LineaAplazable
            {
                NumeroOrden = orden,
                Producto = "P" + orden,
                Picking = 100114,
                FechaAnterior = ANTERIOR_1328,
                FechaNueva = NUEVA_1328
            }).ToList();

        [TestMethod]
        public void Evaluar1328_CasoReal_EnvioEntregadoDelPicking100095_ConDato_Aplaza()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(930358, SeisLineasDel100114(),
                new[] { new EnvioDelPedido { Numero = 250999, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Agencia = Constantes.Agencias.AGENCIA_GLS, Pickings = new List<int> { 100095 } } },
                HOY_1328);

            Assert.AreEqual(DecisionAplazamiento.Aplazar, r.Decision);
            CollectionAssert.AreEqual(new[] { 100114 }, r.Pickings);
            CollectionAssert.AreEqual(new int[0], r.Etiquetas);
            Assert.AreEqual(NUEVA_1328, r.FechaNueva);
        }

        [TestMethod]
        public void Evaluar1328_CasoReal_EnvioEntregadoSinDato_Aplaza()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(930358, SeisLineasDel100114(),
                new[] { new EnvioDelPedido { Numero = 250999, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Agencia = Constantes.Agencias.AGENCIA_GLS } },
                HOY_1328);

            Assert.AreEqual(DecisionAplazamiento.Aplazar, r.Decision, "lo entregado no puede llevar lo que sigue en el almacén");
        }

        [TestMethod]
        public void Evaluar1328_EnvioTramitadoQueLlevaElPicking100114_Rechaza()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(930358, SeisLineasDel100114(),
                new[]
                {
                    new EnvioDelPedido { Numero = 250999, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Pickings = new List<int> { 100095 } },
                    new EnvioDelPedido { Numero = 251100, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Pickings = new List<int> { 100114 } }
                }, HOY_1328);

            Assert.AreEqual(DecisionAplazamiento.Rechazar, r.Decision);
            Assert.AreEqual(251100, r.EnvioEnAgencia);
            StringAssert.Contains(r.Mensaje, "envío 251100");
        }

        [TestMethod]
        public void Evaluar1328_EnvioTramitadoSinDatoYSinEntregar_Rechaza_ComoAntes()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(930358, SeisLineasDel100114(),
                new[]
                {
                    new EnvioDelPedido { Numero = 250999, Estado = Constantes.Agencias.ESTADO_ENTREGADO },
                    new EnvioDelPedido { Numero = 251100, Estado = Constantes.Agencias.ESTADO_TRAMITADO }
                }, HOY_1328);

            Assert.AreEqual(DecisionAplazamiento.Rechazar, r.Decision, "sin dato, prudente");
            Assert.AreEqual(251100, r.EnvioEnAgencia);
        }

        [TestMethod]
        public void Evaluar1328_EnvioTramitadoDeOtroPicking_ConDato_Aplaza()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(930358, SeisLineasDel100114(),
                new[] { new EnvioDelPedido { Numero = 251100, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Pickings = new List<int> { 100095 } } },
                HOY_1328);

            Assert.AreEqual(DecisionAplazamiento.Aplazar, r.Decision);
        }

        [TestMethod]
        public void YaLoTieneLaAgencia1328_CerradoAMano_SinDatoNoCuenta_IncidentadoSinDatoSi_RetornoSinMercanciaNo()
        {
            int[] lineas = { 100114 };
            Assert.IsFalse(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 1, Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO } }, lineas, out _));
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 2, Estado = Constantes.Agencias.ESTADO_INCIDENTADO } }, lineas, out _));
            Assert.IsFalse(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 3, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Pickings = new List<int> { NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.SIN_MERCANCIA } } },
                lineas, out _), "el retorno no lleva nada del almacén");
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 4, Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO, Pickings = new List<int> { 100114 } } }, lineas, out _),
                "con dato manda el dato");
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(
                new[] { new EnvioDelPedido { Numero = 5, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Pickings = new List<int> { 100095 } } }, out _),
                "sin saber los pickings de las líneas, con dato cuenta (prudente)");
        }

        [TestMethod]
        public void Evaluar1328_EtiquetasYRepartoPropio_SoloLosQuePuedenLlevarLasLineas()
        {
            ResultadoAplazamiento r = AplazamientoPedidoConPicking.Evaluar(930358, SeisLineasDel100114(),
                new[]
                {
                    new EnvioDelPedido { Numero = 251001, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, Agencia = Constantes.Agencias.AGENCIA_CTT, Pickings = new List<int> { 100095 } },
                    new EnvioDelPedido { Numero = 251002, Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, Agencia = Constantes.Agencias.AGENCIA_CTT, Pickings = new List<int> { 100114 } },
                    new EnvioDelPedido { Numero = 251003, Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE, Agencia = Constantes.Agencias.AGENCIA_CTT },
                    new EnvioDelPedido { Numero = 251004, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = REPARTO, Pickings = new List<int> { 100095 } },
                    new EnvioDelPedido { Numero = 251005, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = REPARTO }
                }, HOY_1328);

            Assert.AreEqual(DecisionAplazamiento.Aplazar, r.Decision);
            CollectionAssert.AreEqual(new[] { 251002, 251003 }, r.Etiquetas, "la del otro picking no se anula; la sin dato, como antes");
            CollectionAssert.AreEqual(new[] { 251005 }, r.EnviosRepartoPropio);
            CollectionAssert.AreEqual(new[] { 251005 }, AplazamientoPedidoConPicking.EnviosRepartoPropioAfectados(
                new[]
                {
                    new EnvioDelPedido { Numero = 251004, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = REPARTO, Pickings = new List<int> { 100095 } },
                    new EnvioDelPedido { Numero = 251005, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = REPARTO }
                }, new[] { 100114 }));
        }

        [TestMethod]
        public void ReglasPickingsEnvio1328_PickingsDeLasLineas_EnCursoYAlbaran_SinPendientesNiFacturadas()
        {
            List<int> pickings = NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.PickingsDeLasLineas(new[]
            {
                new NestoAPI.Infraestructure.Agencias.LineaConPicking { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 100114 },
                new NestoAPI.Infraestructure.Agencias.LineaConPicking { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 100114 },
                new NestoAPI.Infraestructure.Agencias.LineaConPicking { Estado = Constantes.EstadosLineaVenta.ALBARAN, Picking = 100095 },
                new NestoAPI.Infraestructure.Agencias.LineaConPicking { Estado = Constantes.EstadosLineaVenta.FACTURA, Picking = 99000 },
                new NestoAPI.Infraestructure.Agencias.LineaConPicking { Estado = Constantes.EstadosLineaVenta.PENDIENTE, Picking = 0 },
                new NestoAPI.Infraestructure.Agencias.LineaConPicking { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = null },
                null
            });

            CollectionAssert.AreEqual(new[] { 100095, 100114 }, pickings);
            Assert.AreEqual(0, NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.PickingsDeLasLineas(null).Count);
        }

        [TestMethod]
        public void ReglasPickingsEnvio1328_ApuntarAlModificar_SoloSiEstabaEnElAlmacenYSeRegistra()
        {
            short pendiente = (short)Constantes.Agencias.ESTADO_PENDIENTE;
            short enCurso = (short)Constantes.Agencias.ESTADO_EN_CURSO;
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.ApuntarAlModificar(pendiente, enCurso), "registrar un pendiente");
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.ApuntarAlModificar(enCurso, Constantes.Agencias.ESTADO_TRAMITADO));
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.ApuntarAlModificar(enCurso, enCurso));
            Assert.IsFalse(NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.ApuntarAlModificar(pendiente, pendiente), "sigue pendiente: aún no lleva nada");
            Assert.IsFalse(NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.ApuntarAlModificar(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_TRAMITADO),
                "ya está en la agencia: no se le apunta un picking nuevo");
            Assert.IsFalse(NestoAPI.Infraestructure.Agencias.ReglasPickingsEnvio.ApuntarAlModificar(null, enCurso), "no existía");
        }
    }
}
