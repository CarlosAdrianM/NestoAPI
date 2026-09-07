using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// TNV#68: cobrar ANTES de crear el pedido.
    ///
    /// <para>El caso real: el 07/09/26 el cliente 25299 montó un pedido de 203,86 €, la app le
    /// llevó a Redsys y canceló el pago. El PagoTPV 689 quedó Denegado con código 9915
    /// (cancelación a petición del usuario) y el pedido 925607 se creó igualmente, porque el orden
    /// era crear-y-luego-cobrar. Hubo que borrarlo a mano y el cliente lo veía en «Mis pedidos».
    /// </para>
    ///
    /// <para>Aquí se prueba la regla que lo impide: con el cobro denegado, el cobro no vale para
    /// crear ningún pedido.</para>
    /// </summary>
    [TestClass]
    public class CobroCarritoTests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "25299";

        [TestMethod]
        public void ComprobarCobroCarrito_ElClienteCancelaEnRedsys_NoSirveParaCrearElPedido()
        {
            // El pago 689 tal cual quedó: Denegado, código 9915.
            PagoTPV cancelado = CobroDeCarrito();
            cancelado.Estado = Constantes.EstadosPagoTPV.DENEGADO;
            cancelado.CodigoRespuesta = "9915";

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                cancelado, EMPRESA, CLIENTE, DateTime.Now);

            Assert.IsFalse(reserva.Valido, "con el pago cancelado no se puede crear el pedido");
            Assert.IsFalse(reserva.HayQueDevolver, "no hay nada que devolver: no se llegó a cobrar");
            StringAssert.Contains(reserva.Motivo, "no ha confirmado el pago");
        }

        [TestMethod]
        public void ComprobarCobroCarrito_PagoPendienteDeConfirmar_TampocoSirve()
        {
            // La notificación de Redsys puede no haber llegado: mientras no diga que sí, no hay
            // pedido. Un "pendiente" tratado como bueno es exactamente el pedido sin cobrar.
            PagoTPV pendiente = CobroDeCarrito();
            pendiente.Estado = Constantes.EstadosPagoTPV.PENDIENTE;

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                pendiente, EMPRESA, CLIENTE, DateTime.Now);

            Assert.IsFalse(reserva.Valido);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_PagoAutorizadoDeEsteCliente_SirveYTraeElImporteCobrado()
        {
            // El caso normal: el banco acaba de autorizar y la app pide crear el pedido.
            PagoTPV cobro = CobroDeCarrito();

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                cobro, EMPRESA, CLIENTE, cobro.FechaCreacion.AddSeconds(2));

            Assert.IsTrue(reserva.Valido);
            Assert.AreEqual(203.86M, reserva.Importe, "es el importe contra el que se compara el pedido");
            Assert.AreEqual("0336B1C25299", reserva.NumeroOrden);
            Assert.IsNull(reserva.Motivo);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_ElCobroEsDeOtroCliente_NoSirve()
        {
            // Mandar el id de un cobro ajeno no puede servir para pagarse un pedido.
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                CobroDeCarrito(), EMPRESA, "15191", DateTime.Now);

            Assert.IsFalse(reserva.Valido);
            StringAssert.Contains(reserva.Motivo, "No encontramos el pago");
        }

        [TestMethod]
        public void ComprobarCobroCarrito_NoEsUnCobroDeCarrito_NoSirve()
        {
            // Un enlace de pago del cliente, o el alta de su tarjeta, no son el cobro de este
            // carrito: si valieran, se crearía un pedido con dinero que paga otra cosa.
            foreach (string tipo in new[]
            {
                Constantes.TiposPagoTPV.TPV_VIRTUAL,
                Constantes.TiposPagoTPV.ALTA_TARJETA,
                Constantes.TiposPagoTPV.PEDIDO_APP
            })
            {
                PagoTPV otro = CobroDeCarrito();
                otro.Tipo = tipo;

                ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                    otro, EMPRESA, CLIENTE, DateTime.Now);

                Assert.IsFalse(reserva.Valido, tipo);
            }
        }

        [TestMethod]
        public void ComprobarCobroCarrito_ElCobroYaTienePedido_NoSeUsaDosVeces()
        {
            // Reintento de la app sobre una llamada que sí había llegado: el pedido ya existe y
            // volver a crearlo sería cobrarle una vez y servirle dos.
            PagoTPV usado = CobroDeCarrito();
            usado.Documento = "925607";

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                usado, EMPRESA, CLIENTE, DateTime.Now);

            Assert.IsFalse(reserva.Valido);
            StringAssert.Contains(reserva.Motivo, "ya corresponde a un pedido");
        }

        [TestMethod]
        public void ComprobarCobroCarrito_ElCobroEstaReservado_NoSeUsaDosVeces()
        {
            // La marca de reservado ocupa el mismo hueco: otra petición simultánea no lo coge.
            PagoTPV reservado = CobroDeCarrito();
            reservado.Documento = ServicioPagos.MARCA_COBRO_RESERVADO;

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                reservado, EMPRESA, CLIENTE, DateTime.Now);

            Assert.IsFalse(reserva.Valido);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_CobroDeHaceHoras_NiPedidoNiDineroRetenido()
        {
            // El cliente pagó y cerró la app. Los precios de entonces ya no tienen por qué ser los
            // de ahora, así que no se crea el pedido; y el dinero no se queda aquí.
            PagoTPV viejo = CobroDeCarrito();
            DateTime ahora = viejo.FechaCreacion.AddMinutes(ServicioPagos.MINUTOS_VALIDEZ_COBRO_CARRITO + 1);

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(viejo, EMPRESA, CLIENTE, ahora);

            Assert.IsFalse(reserva.Valido);
            Assert.IsTrue(reserva.HayQueDevolver, "cobrado y sin pedido: el dinero vuelve");
        }

        [TestMethod]
        public void ComprobarCobroCarrito_JustoEnElLimite_TodaviaSirve()
        {
            PagoTPV justo = CobroDeCarrito();
            DateTime ahora = justo.FechaCreacion.AddMinutes(ServicioPagos.MINUTOS_VALIDEZ_COBRO_CARRITO);

            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(justo, EMPRESA, CLIENTE, ahora);

            Assert.IsTrue(reserva.Valido);
        }

        [TestMethod]
        public void MarcaCobroReservado_CabeEnLaColumna()
        {
            // PagosTPV.Documento es nvarchar(10): si la marca no cupiera, la reserva reventaría en
            // producción con el dinero ya cobrado.
            Assert.IsTrue(ServicioPagos.MARCA_COBRO_RESERVADO.Length <= 10);
        }

        #region El reparto de la notificación de Redsys

        [TestMethod]
        public void EsPagoDePedido_CobroDeCarritoConSuPedido_EsCobroDePedido()
        {
            // Cuando el pedido ya existe, el cobro del carrito es un cobro de pedido a todos los
            // efectos: lo que toca es apuntarle el prepago.
            PagoTPV conPedido = CobroDeCarrito();
            conPedido.Documento = "925607";

            Assert.IsTrue(ServicioPagos.EsPagoDePedido(conPedido));
        }

        [TestMethod]
        public void EsPagoDePedido_CobroDeCarritoSinPedidoTodavia_No()
        {
            // Sin pedido no hay a qué apuntar el prepago. Y con la marca de reservado tampoco: no
            // es un número de pedido.
            Assert.IsFalse(ServicioPagos.EsPagoDePedido(CobroDeCarrito()));

            PagoTPV reservado = CobroDeCarrito();
            reservado.Documento = ServicioPagos.MARCA_COBRO_RESERVADO;
            Assert.IsFalse(ServicioPagos.EsPagoDePedido(reservado));
        }

        [TestMethod]
        public void EsPagoDePedido_AltaDeTarjetaConDocumento_No()
        {
            // Una autorización de 0 € no mueve dinero: no puede acabar de prepago de nada.
            PagoTPV alta = CobroDeCarrito();
            alta.Tipo = Constantes.TiposPagoTPV.ALTA_TARJETA;
            alta.Documento = "925607";

            Assert.IsFalse(ServicioPagos.EsPagoDePedido(alta));
        }

        [TestMethod]
        public void EsCobroDeLaTienda_NingunCobroDeLaAppEsUnEnlaceDePago()
        {
            // De esto depende que no se contabilice contra el extracto (se contaría dos veces con
            // el prepago), que no se manden los correos del circuito de enlaces y que no se
            // generen enlaces de reintento.
            foreach (string tipo in new[]
            {
                Constantes.TiposPagoTPV.PEDIDO_APP,
                Constantes.TiposPagoTPV.CARRITO_APP,
                Constantes.TiposPagoTPV.ALTA_TARJETA
            })
            {
                PagoTPV pago = CobroDeCarrito();
                pago.Tipo = tipo;
                Assert.IsTrue(ServicioPagos.EsCobroDeLaTienda(pago), tipo);
            }

            PagoTPV enlace = CobroDeCarrito();
            enlace.Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL;
            Assert.IsFalse(ServicioPagos.EsCobroDeLaTienda(enlace), "el enlace de pago sí contabiliza");
        }

        #endregion

        /// <summary>El pago 689 tal como lo dejó el pedido 925607, pero autorizado.</summary>
        private static PagoTPV CobroDeCarrito()
        {
            return new PagoTPV
            {
                Id = 689,
                NumeroOrden = "0336B1C25299",
                Tipo = Constantes.TiposPagoTPV.CARRITO_APP,
                Empresa = EMPRESA,
                Cliente = CLIENTE,
                Importe = 203.86M,
                Estado = Constantes.EstadosPagoTPV.AUTORIZADO,
                FechaCreacion = new DateTime(2026, 9, 7, 16, 31, 0)
            };
        }
    }
}
