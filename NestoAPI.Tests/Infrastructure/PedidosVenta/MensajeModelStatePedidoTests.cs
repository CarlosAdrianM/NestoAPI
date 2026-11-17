using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.ModelBinding;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Novedades 1314: el 400 de ModelState del POST/PUT de pedidos dice qué falta en el Message, con el formato de siempre.
    /// </summary>
    [TestClass]
    public class MensajeModelStatePedidoTests
    {
        [TestMethod]
        public void Mensaje_SinPlazosDePago_DiceQueFaltan()
        {
            ModelStateDictionary modelState = new ModelStateDictionary();
            modelState.AddModelError("pedido.plazosPago", "The plazosPago field is required.");

            Assert.AreEqual("Faltan los plazos de pago del pedido", MensajeModelStatePedido.Mensaje(modelState));
        }

        [TestMethod]
        public void Mensaje_DatoQueNoSePuedeConvertir_DiceCualEs()
        {
            ModelStateDictionary modelState = new ModelStateDictionary();
            modelState.AddModelError("pedido.Lineas[0].cantidad", new FormatException("Could not convert string to integer"));

            Assert.AreEqual("El dato «Lineas[0].cantidad» del pedido no es válido", MensajeModelStatePedido.Mensaje(modelState));
        }

        [TestMethod]
        public void Mensaje_VariosErrores_LosJuntaSinRepetir()
        {
            ModelStateDictionary modelState = new ModelStateDictionary();
            modelState.AddModelError("pedido.plazosPago", "The plazosPago field is required.");
            modelState.AddModelError("pedido.plazosPago", "El campo plazosPago es obligatorio.");
            modelState.AddModelError("pedido.fecha", new FormatException("x"));

            Assert.AreEqual("Faltan los plazos de pago del pedido. El dato «fecha» del pedido no es válido", MensajeModelStatePedido.Mensaje(modelState));
        }

        [TestMethod]
        public void Mensaje_CuerpoIlegible_LoDice()
        {
            ModelStateDictionary modelState = new ModelStateDictionary();
            modelState.AddModelError("pedido", new Exception("Unexpected character"));

            Assert.AreEqual("No se ha podido leer el pedido enviado", MensajeModelStatePedido.Mensaje(modelState));
        }

        [TestMethod]
        public void Mensaje_SinErrores_Generico()
        {
            Assert.AreEqual(MensajeModelStatePedido.MENSAJE_GENERICO, MensajeModelStatePedido.Mensaje(new ModelStateDictionary()));
        }

        [TestMethod]
        public async Task PostPedidoVenta_SinPlazosDePago_400ConElMensajeYElModelStateDeSiempre()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            controller.ModelState.AddModelError("pedido.plazosPago", "The plazosPago field is required.");

            IHttpActionResult resultado = await controller.PostPedidoVenta(new PedidoVentaDTO { empresa = "1", cliente = "29064" });

            NegotiatedContentResult<HttpError> error = resultado as NegotiatedContentResult<HttpError>;
            Assert.IsNotNull(error, "Un 400 con HttpError, como BadRequest(ModelState)");
            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            Assert.AreEqual("Faltan los plazos de pago del pedido", error.Content.Message);
            Assert.IsTrue(error.Content.ContainsKey("ModelState"), "Sigue llevando el ModelState para quien lo lea");
        }
    }
}
