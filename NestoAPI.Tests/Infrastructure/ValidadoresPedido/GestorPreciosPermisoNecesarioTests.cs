using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#501: hay denegaciones que no puede levantar cualquiera que tenga el permiso general
    /// de forzar pedidos. El validador marca qué permiso hace falta y el pipeline tiene que
    /// llevarlo hasta la respuesta consolidada, que es lo que mira el controlador; si se pierde por
    /// el camino, Sancho podría vender Kinetics con su permiso de forzar precios.
    /// </summary>
    [TestClass]
    public class GestorPreciosPermisoNecesarioTests
    {
        private const string PERMISO = "PermitirVenderFamiliasRestringidas";

        private class ValidadorQuePideUnPermiso : IValidadorDenegacion
        {
            public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "No se puede vender Kinetics a este cliente",
                    ProductoId = "12345",
                    PermisoNecesario = PERMISO,
                    Errores = new List<ErrorValidacion>
                    {
                        new ErrorValidacion
                        {
                            Motivo = "No se puede vender Kinetics a este cliente",
                            ProductoId = "12345",
                            // Sin "denegada expresamente": el pipeline pasa por los validadores de
                            // aceptación y hay que comprobar que el permiso sobrevive a ese camino.
                            AutorizadaDenegadaExpresamente = false,
                            PermisoNecesario = PERMISO
                        }
                    }
                };
            }
        }

        private class ValidadorQueDeniegaSinPermiso : IValidadorDenegacion
        {
            public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "El descuento no está autorizado",
                    ProductoId = "67890",
                    AutorizadaDenegadaExpresamente = true
                };
            }
        }

        private static PedidoVentaDTO Pedido()
        {
            return new PedidoVentaDTO
            {
                cliente = "15296",
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { Producto = "12345", Cantidad = 1, tipoLinea = 1 }
                }
            };
        }

        /// <summary>
        /// Uno que no acepta nada: la lista de aceptacion no puede quedarse vacia o el gestor
        /// recarga los de verdad, que van a la base de datos.
        /// </summary>
        private class ValidadorAceptacionQueNoAcepta : IValidadorAceptacion
        {
            public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
            {
                return new RespuestaValidacion { ValidacionSuperada = false };
            }
        }

        private static RespuestaValidacion ValidarCon(params IValidadorDenegacion[] validadores)
        {
            List<IValidadorDenegacion> denegacionOriginal = GestorPrecios.listaValidadoresDenegacion;
            List<IValidadorAceptacion> aceptacionOriginal = GestorPrecios.listaValidadoresAceptacion;
            IServicioPrecios servicioOriginal = GestorPrecios.servicio;
            try
            {
                GestorPrecios.listaValidadoresDenegacion = new List<IValidadorDenegacion>(validadores);
                GestorPrecios.listaValidadoresAceptacion = new List<IValidadorAceptacion> { new ValidadorAceptacionQueNoAcepta() };
                GestorPrecios.servicio = A.Fake<IServicioPrecios>();
                return GestorPrecios.EsPedidoValido(Pedido());
            }
            finally
            {
                GestorPrecios.listaValidadoresDenegacion = denegacionOriginal;
                GestorPrecios.listaValidadoresAceptacion = aceptacionOriginal;
                GestorPrecios.servicio = servicioOriginal;
            }
        }

        [TestMethod]
        public void ElPermisoNecesarioLlegaALaRespuestaConsolidada()
        {
            RespuestaValidacion respuesta = ValidarCon(new ValidadorQuePideUnPermiso());

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(PERMISO, respuesta.PermisoNecesario);
        }

        [TestMethod]
        public void UnaDenegacionCorriente_NoPideNingunPermisoEspecial()
        {
            RespuestaValidacion respuesta = ValidarCon(new ValidadorQueDeniegaSinPermiso());

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.IsNull(respuesta.PermisoNecesario);
        }

        [TestMethod]
        public void SiSeMezclan_ElPermisoDelQueLoPideMandaSobreElQueNoLoPide()
        {
            RespuestaValidacion respuesta = ValidarCon(
                new ValidadorQueDeniegaSinPermiso(), new ValidadorQuePideUnPermiso());

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(PERMISO, respuesta.PermisoNecesario,
                "Basta con que UNA de las denegaciones exija permiso propio para que haga falta");
        }
    }
}
