using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Notificaciones;
using NestoAPI.Models.PedidosVenta;
using System;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// TNV#66: de qué se avisa al cliente por push y de qué no. La regla: solo lo que no puede
    /// saber por su cuenta o puede resolver. Avisar de todo es la forma más rápida de que apague
    /// las notificaciones, y entonces se pierden también las que sí importan.
    /// </summary>
    [TestClass]
    public class AvisosEstadoPedidoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 9, 4, 17, 0, 0);

        [TestMethod]
        public void HayQueAvisar_LaPrimeraVezQueSeVeElPedido_NoSeAvisa()
        {
            // Al desplegar esto hay meses de pedidos: sin esta línea base, el cliente estrenaría
            // la función con una avalancha de avisos de envíos que llegaron hace semanas.
            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Enviado, null, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_ElPedidoHaSalido_SeAvisa()
        {
            EstadoNotificadoPedido registro = Registro(EstadoPedidoCliente.EnPreparacion);

            Assert.IsTrue(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Enviado, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_YaSeAvisoDeEsteEstado_NoSeRepite()
        {
            // El job pasa cada media hora y el estado dura días: sin esto sería el mismo aviso
            // una y otra vez toda la semana.
            EstadoNotificadoPedido registro = Registro(EstadoPedidoCliente.Enviado, notificado: EstadoPedidoCliente.Enviado);

            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Enviado, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_HaSalidoUnaParte_SeAvisa()
        {
            // Es el aviso que evita la llamada de "me falta media caja".
            EstadoNotificadoPedido registro = Registro(EstadoPedidoCliente.EnPreparacion);

            Assert.IsTrue(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.EnviadoEnParte, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_IncidenciaDeLaAgencia_SeAvisa()
        {
            // Es lo único malo que el cliente puede resolver: llamar o dar otra dirección.
            EstadoNotificadoPedido registro = Registro(EstadoPedidoCliente.Enviado, notificado: EstadoPedidoCliente.Enviado);

            Assert.IsTrue(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Incidencia, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_LoQueElClienteYaSabe_NoSeAvisa()
        {
            // "Lo hemos recibido" acaba de verlo en pantalla, "preparándolo" no le aporta nada y
            // "entregado" se lo acaba de dar el repartidor en la mano.
            EstadoNotificadoPedido registro = Registro(EstadoPedidoCliente.Recibido);

            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Recibido, registro, AHORA));
            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.EnPreparacion, registro, AHORA));
            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Entregado, registro, AHORA));
            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.Servido, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_PagoRecienPendiente_TodaviaNoSeLePersigue()
        {
            // El cliente acaba de ver la pantalla del pago y puede estar pagando ahora mismo.
            EstadoNotificadoPedido registro = Registro(
                EstadoPedidoCliente.PendienteDePago, desde: AHORA.AddMinutes(-30));

            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.PendienteDePago, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_PagoPendienteDesdeHaceHoras_SeLeRecuerda()
        {
            // El caso real: se le cayó la pasarela, cerró la app y su pedido está parado.
            EstadoNotificadoPedido registro = Registro(
                EstadoPedidoCliente.PendienteDePago,
                desde: AHORA - AvisosEstadoPedido.EsperaAntesDeRecordarElPago);

            Assert.IsTrue(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.PendienteDePago, registro, AHORA));
        }

        [TestMethod]
        public void HayQueAvisar_PagoPendienteYaRecordado_NoSeInsiste()
        {
            EstadoNotificadoPedido registro = Registro(
                EstadoPedidoCliente.PendienteDePago,
                desde: AHORA.AddDays(-2),
                notificado: EstadoPedidoCliente.PendienteDePago);

            Assert.IsFalse(AvisosEstadoPedido.HayQueAvisar(EstadoPedidoCliente.PendienteDePago, registro, AHORA));
        }

        // --- El texto que le llega ---

        [TestMethod]
        public void Construir_DiceElNumeroDePedidoYLaAgencia()
        {
            // El número es lo que el cliente usa para hablar con nosotros por teléfono.
            PedidoClienteResumenDTO pedido = Pedido(EstadoPedidoCliente.Enviado);
            pedido.Envio = new UltimoEnvioClienteDTO { AgenciaNombre = "CTT Express" };

            NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(pedido);

            StringAssert.Contains(aviso.Cuerpo, "925368");
            StringAssert.Contains(aviso.Cuerpo, "CTT Express");
            Assert.AreEqual(AvisosEstadoPedido.TIPO, aviso.Tipo);
            Assert.AreEqual("925368", aviso.Datos["pedido"]);
            Assert.AreEqual("Enviado", aviso.Datos["estado"]);
        }

        [TestMethod]
        public void Construir_SinAgencia_NoDejaElHuecoRaro()
        {
            PedidoClienteResumenDTO pedido = Pedido(EstadoPedidoCliente.Enviado);

            NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(pedido);

            StringAssert.Contains(aviso.Cuerpo, "ha salido.");
        }

        [TestMethod]
        public void Construir_DelEnvioParcial_DiceQueFaltaElResto()
        {
            PedidoClienteResumenDTO pedido = Pedido(EstadoPedidoCliente.EnviadoEnParte);

            NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(pedido);

            StringAssert.Contains(aviso.Titulo, "parte");
            StringAssert.Contains(aviso.Cuerpo, "el resto");
        }

        [TestMethod]
        public void Construir_DelPagoPendiente_DiceElImporte()
        {
            PedidoClienteResumenDTO pedido = Pedido(EstadoPedidoCliente.PendienteDePago);
            pedido.ImportePendiente = 24.20M;

            NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(pedido);

            StringAssert.Contains(aviso.Cuerpo, "24,20");
        }

        [TestMethod]
        public void Construir_SinImportesVisibles_AvisaIgualPeroSinCifras()
        {
            // A quien hace pedidos sin ver precios (NestoAPI#446) le llega 0 como importe: el
            // aviso vale igual, pero no puede enseñarle una cifra que en su pantalla está oculta.
            PedidoClienteResumenDTO pedido = Pedido(EstadoPedidoCliente.PendienteDePago);
            pedido.ImportePendiente = 0M;

            NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(pedido);

            Assert.IsFalse(aviso.Cuerpo.Contains("("), "no puede colarse un importe vacío entre paréntesis");
        }

        [TestMethod]
        public void Construir_DeUnEstadoQueNoSeAvisa_NoDevuelveNada()
        {
            Assert.IsNull(AvisosEstadoPedido.Construir(Pedido(EstadoPedidoCliente.EnPreparacion)));
        }

        [TestMethod]
        public void Construir_TodoLoQueSeAvisa_TieneTituloYCuerpo()
        {
            foreach (EstadoPedidoCliente estado in Enum.GetValues(typeof(EstadoPedidoCliente)))
            {
                EstadoNotificadoPedido registro = Registro(EstadoPedidoCliente.Recibido, desde: AHORA.AddDays(-3));
                if (!AvisosEstadoPedido.HayQueAvisar(estado, registro, AHORA))
                {
                    continue;
                }

                NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(Pedido(estado));

                Assert.IsNotNull(aviso, $"el estado {estado} se avisa pero no tiene texto");
                Assert.IsFalse(string.IsNullOrWhiteSpace(aviso.Titulo), $"{estado} sin título");
                Assert.IsFalse(string.IsNullOrWhiteSpace(aviso.Cuerpo), $"{estado} sin cuerpo");
            }
        }

        private static EstadoNotificadoPedido Registro(
            EstadoPedidoCliente estado,
            DateTime? desde = null,
            EstadoPedidoCliente? notificado = null)
        {
            return new EstadoNotificadoPedido
            {
                Empresa = "1",
                Pedido = 925368,
                Estado = estado.ToString(),
                FechaEstado = desde ?? AHORA.AddHours(-1),
                EstadoNotificado = notificado?.ToString()
            };
        }

        private static PedidoClienteResumenDTO Pedido(EstadoPedidoCliente estado)
        {
            return new PedidoClienteResumenDTO
            {
                Numero = 925368,
                Fecha = AHORA.Date,
                Total = 24.20M,
                Estado = estado,
                EstadoTexto = "da igual"
            };
        }
    }
}
