using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Filters;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.Filters
{
    /// <summary>
    /// NestoAPI#215: el JSON del pedido que falla la validación debe adjuntarse a la
    /// PedidoValidacionException y volcarse a las ServerVariables de ELMAH para poder reproducir el caso.
    /// (Nota: GlobalExceptionFilterTests.cs es un fichero huérfano —no está en el .csproj— con tests
    /// pre-existentes que no compilan/pasan; se deja como deuda aparte, hermana del issue #198.)
    /// </summary>
    [TestClass]
    public class GlobalExceptionFilterContextTests
    {
        [TestMethod]
        public void PedidoValidacionException_ConPedidoDTO_AdjuntaElJsonYLosDatosDelPedido()
        {
            // El constructor que recibe el DTO debe sacar empresa/numero/cliente/usuario del pedido
            // y adjuntar el DTO serializado al contexto para poder reproducir el caso.
            PedidoVentaDTO pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 12345,
                cliente = "29382",
                Usuario = "NUEVAVISION\\MariaJose"
            };
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "45449", Cantidad = 2 });
            RespuestaValidacion respuesta = new RespuestaValidacion { ValidacionSuperada = false };

            PedidoValidacionException excepcion = new PedidoValidacionException("falló la validación", respuesta, pedido);

            Assert.AreEqual("1", excepcion.Context.Empresa);
            Assert.AreEqual(12345, excepcion.Context.Pedido);
            Assert.AreEqual("29382", excepcion.Context.Cliente);
            Assert.AreEqual("NUEVAVISION\\MariaJose", excepcion.Context.Usuario);
            Assert.IsTrue(excepcion.Context.AdditionalData.ContainsKey("pedidoBorradorJson"));
            string json = (string)excepcion.Context.AdditionalData["pedidoBorradorJson"];
            Assert.IsTrue(json.Contains("45449"), "El JSON adjunto debe contener las líneas del pedido. JSON: " + json);
        }

        [TestMethod]
        public void VolcarContextoAServerVariables_ConJsonDelPedido_LoCopiaConPrefijoXContext()
        {
            // El JSON del pedido (y el resto de AdditionalData) debe acabar en las ServerVariables del
            // Error de ELMAH con prefijo X-Context-, para verlo en la misma ficha del error.
            PedidoVentaDTO pedido = new PedidoVentaDTO { empresa = "1", numero = 7, cliente = "29382" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "45449", Cantidad = 2 });
            PedidoValidacionException excepcion = new PedidoValidacionException(
                "falló", new RespuestaValidacion { ValidacionSuperada = false }, pedido);
            System.Collections.Specialized.NameValueCollection serverVariables =
                new System.Collections.Specialized.NameValueCollection();

            GlobalExceptionFilter.VolcarContextoAServerVariables(serverVariables, excepcion.Context);

            Assert.IsNotNull(serverVariables["X-Context-pedidoBorradorJson"]);
            Assert.IsTrue(serverVariables["X-Context-pedidoBorradorJson"].Contains("45449"));
        }
    }
}
