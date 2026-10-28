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
            StringAssert.Contains(r.Mensaje, Constantes.Correos.LOGISTICA);
            Assert.IsFalse(r.Mensaje.Contains("llame a la agencia"), "Carlos: no se le dice al vendedor que llame a la agencia");
        }

        [TestMethod]
        public void YaLoTieneLaAgencia_UnDevueltoNoCuenta()
        {
            Assert.IsFalse(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(new[] { new EnvioDelPedido { Numero = 1, Estado = Constantes.Agencias.ESTADO_DEVUELTO } }, out _));
            Assert.IsTrue(AplazamientoPedidoConPicking.YaLoTieneLaAgencia(new[] { new EnvioDelPedido { Numero = 2, Estado = Constantes.Agencias.ESTADO_ENTREGADO } }, out int envio));
            Assert.AreEqual(2, envio);
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
    }
}
