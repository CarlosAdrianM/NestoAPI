using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#434: PostPedidoVenta estaba escrito para un usuario empleado con ficha en
    /// ParametrosUsuario y daba por hechos varios datos. Cuando alguno faltaba —un cliente final
    /// creando su pedido desde la app, o un empleado nuevo sin el parámetro UltNumPedidoVta—
    /// reventaba con una NullReferenceException (500) a mitad de la creación, sin decir qué
    /// faltaba. Estos tests fijan que cada hueco da un mensaje entendible en vez de una NRE.
    /// </summary>
    [TestClass]
    public class PedidosVentaDatosObligatoriosTests
    {
        private static PedidoVentaDTO PedidoCompleto()
        {
            return new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                cliente = "15191",
                contacto = "0",
                Usuario = "NUEVAVISION\\carlos",
                fecha = DateTime.Today,
                formaPago = Constantes.FormasPago.TARJETA,
                plazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = "12345",
                        Cantidad = 1
                    }
                }
            };
        }

        // --- El pedido completo pasa: la guarda no puede bloquear lo que hoy funciona ---

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_PedidoCompleto_NoDevuelveError()
        {
            Assert.IsNull(PedidosVentaController.ValidarDatosObligatoriosPedido(PedidoCompleto()));
        }

        // --- Punto 1 de la issue: pedido.Usuario null (NRE en las líneas 1444 y 1545) ---

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_SinUsuario_AvisaDeQueFaltaElUsuario()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.Usuario = null;

            string error = PedidosVentaController.ValidarDatosObligatoriosPedido(pedido);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "usuario");
        }

        // --- Punto 3: pedido.fecha es DateTime? y se le hacía .Value sin comprobar ---

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_SinFecha_AvisaDeQueFaltaLaFecha()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.fecha = null;

            string error = PedidosVentaController.ValidarDatosObligatoriosPedido(pedido);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "fecha");
        }

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_SinPlazosDePago_AvisaDeQueFaltan()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.plazosPago = "";

            string error = PedidosVentaController.ValidarDatosObligatoriosPedido(pedido);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "plazos de pago");
        }

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_SinCliente_AvisaDeQueFaltaElCliente()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.cliente = null;

            string error = PedidosVentaController.ValidarDatosObligatoriosPedido(pedido);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "cliente");
        }

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_SinLineas_AvisaDeQueFaltanLineas()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.Lineas.Clear();

            string error = PedidosVentaController.ValidarDatosObligatoriosPedido(pedido);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "línea");
        }

        // --- Punto 5: TipoLinea null. Se detectaba, pero ya con las líneas construidas ---

        [TestMethod]
        public void ValidarDatosObligatoriosPedido_LineaSinTipo_AvisaConElProducto()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                linea.tipoLinea = null;
            }

            string error = PedidosVentaController.ValidarDatosObligatoriosPedido(pedido);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "12345");
        }

        // --- Puntos 3 y 4: los maestros que se leen de la BD y se usan sin comprobar ---

        [TestMethod]
        public void ValidarMaestrosPedido_PlazoPagoInexistente_AvisaConElCodigo()
        {
            PedidoVentaDTO pedido = PedidoCompleto();

            string error = PedidosVentaController.ValidarMaestrosPedido(pedido, null, new Empresa());

            Assert.IsNotNull(error);
            StringAssert.Contains(error, Constantes.PlazosPago.PREPAGO);
        }

        [TestMethod]
        public void ValidarMaestrosPedido_EmpresaInexistente_AvisaDeLaEmpresa()
        {
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.empresa = "9";

            string error = PedidosVentaController.ValidarMaestrosPedido(pedido, new PlazoPago(), null);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "empresa 9");
        }

        [TestMethod]
        public void ValidarMaestrosPedido_ConAmbosMaestros_NoDevuelveError()
        {
            Assert.IsNull(PedidosVentaController.ValidarMaestrosPedido(PedidoCompleto(), new PlazoPago(), new Empresa()));
        }

        // --- Punto 2, el más grave: SingleOrDefault sobre ParametrosUsuario sin null-check ---

        [TestMethod]
        public void GuardarUltimoNumeroPedido_UsuarioSinEseParametro_NoRompeLaCreacionDelPedido()
        {
            // Un cliente final nunca tendrá fila en ParametrosUsuario, y un empleado nuevo tampoco.
            // Antes esto era 'parametroUsuario.Valor = ...' y petaba con NullReferenceException.
            PedidosVentaController.GuardarUltimoNumeroPedido(null, 920001);
        }

        [TestMethod]
        public void GuardarUltimoNumeroPedido_UsuarioConParametro_GuardaElNumero()
        {
            ParametroUsuario parametro = new ParametroUsuario { Valor = "919999" };

            PedidosVentaController.GuardarUltimoNumeroPedido(parametro, 920001);

            Assert.AreEqual("920001", parametro.Valor);
        }

        // --- El nombre de usuario sin dominio ---

        [TestMethod]
        public void UsuarioSinDominio_UsuarioConDominio_QuitaElDominio()
        {
            Assert.AreEqual("carlos", PedidosVentaController.UsuarioSinDominio("NUEVAVISION\\carlos"));
        }

        [TestMethod]
        public void UsuarioSinDominio_UsuarioSinDominio_DevuelveElNombreEntero()
        {
            Assert.AreEqual("carlos", PedidosVentaController.UsuarioSinDominio("carlos"));
        }

        [TestMethod]
        public void UsuarioSinDominio_UsuarioNulo_DevuelveNuloSinRomper()
        {
            Assert.IsNull(PedidosVentaController.UsuarioSinDominio(null));
        }

        // --- El endpoint: datos incompletos son un 400 con mensaje, no un 500 con NRE ---

        [TestMethod]
        public async Task PostPedidoVenta_PedidoSinUsuario_DevuelveBadRequestSinTocarLaBaseDeDatos()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.Usuario = null;

            var resultado = await controller.PostPedidoVenta(pedido);

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest, "Un pedido sin usuario tiene que dar 400, no reventar con una NRE");
            StringAssert.Contains(badRequest.Message, "usuario");
        }

        [TestMethod]
        public async Task PostPedidoVenta_PedidoSinFecha_DevuelveBadRequest()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO pedido = PedidoCompleto();
            pedido.fecha = null;

            var resultado = await controller.PostPedidoVenta(pedido);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
