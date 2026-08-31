using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#436: el pedido que crea un cliente desde la app. La regla que ordena el diseño es
    /// que <b>el cliente dice qué y cuánto y todo lo demás lo decide el servidor</b>, así que estos
    /// tests comprueban sobre todo de dónde sale cada campo.
    /// </summary>
    [TestClass]
    public class ConstructorPedidoClienteTests
    {
        private const string CLIENTE = "15191";

        private static ClienteDTO FichaCliente()
        {
            return new ClienteDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                cliente = CLIENTE,
                contacto = "0",
                estado = 0,
                iva = Constantes.Empresas.IVA_POR_DEFECTO,
                ccc = "1",
                periodoFacturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                ruta = "FW",
                servirJunto = true,
                mantenerJunto = false,
                noComisiona = 0,
                vendedor = "NV"
            };
        }

        private static PedidoClienteRequest Peticion()
        {
            return new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest>
                {
                    new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 2 }
                },
                Comentarios = "Llamar antes de entregar",
                SuPedido = "REF-1"
            };
        }

        private static Dictionary<string, ProductoPlantillaDTO> Precios()
        {
            return new Dictionary<string, ProductoPlantillaDTO>
            {
                ["12345"] = new ProductoPlantillaDTO
                {
                    producto = "12345",
                    nombre = "CHAMPÚ DE PRUEBA",
                    precio = 10M,
                    descuento = 0.10M,
                    aplicarDescuento = true,
                    iva = Constantes.Empresas.IVA_POR_DEFECTO
                }
            };
        }

        private static PedidoVentaDTO Construir(PedidoClienteRequest peticion = null, ClienteDTO cliente = null)
        {
            return ConstructorPedidoCliente.Construir(
                peticion ?? Peticion(),
                cliente ?? FichaCliente(),
                Precios(),
                Constantes.FormasPago.TARJETA,
                Constantes.PlazosPago.PREPAGO,
                new DateTime(2026, 9, 1));
        }

        // --- Lo que decide el servidor ---

        [TestMethod]
        public void Construir_LaFormaDeVentaEsApp()
        {
            PedidoVentaDTO pedido = Construir();

            Assert.IsTrue(pedido.Lineas.All(l => l.formaVenta == Constantes.FormasVenta.APP));
        }

        [TestMethod]
        public void Construir_ElPedidoNoEsCanalExterno_ParaQueElServidorCalculeLosPortes()
        {
            // Es la condición que mira PostPedidoVenta para añadir los portes automáticos: si
            // alguna línea fuese de canal externo, se respetarían los portes que llegaran.
            PedidoVentaDTO pedido = Construir();

            Assert.IsFalse(pedido.Lineas.Any(l => Constantes.FormasVenta.EsCanalExterno(l.formaVenta)));
            Assert.IsTrue(pedido.AnadirPortes);
        }

        [TestMethod]
        public void Construir_AlmacenDelegacionYSerieSonLosFijosDelCanal()
        {
            PedidoVentaDTO pedido = Construir();

            Assert.AreEqual(Constantes.Series.SERIE_POR_DEFECTO, pedido.serie);
            Assert.IsTrue(pedido.Lineas.All(l => l.almacen == Constantes.Almacenes.ALGETE));
            Assert.IsTrue(pedido.Lineas.All(l => l.delegacion == Constantes.Empresas.DELEGACION_POR_DEFECTO));
        }

        [TestMethod]
        public void Construir_ElPrecioYElDescuentoSonLosCalculadosPorElServidor()
        {
            PedidoVentaDTO pedido = Construir();
            LineaPedidoVentaDTO linea = pedido.Lineas.Single();

            Assert.AreEqual(10M, linea.PrecioUnitario);
            Assert.AreEqual(0.10M, linea.DescuentoProducto);
            Assert.IsTrue(linea.AplicarDescuento);
            Assert.AreEqual(0M, linea.DescuentoLinea, "El cliente no pone descuentos de línea");
        }

        [TestMethod]
        public void Construir_LosDatosDeLaFichaNoLosEligeElCliente()
        {
            PedidoVentaDTO pedido = Construir();

            Assert.AreEqual(Constantes.Empresas.IVA_POR_DEFECTO, pedido.iva);
            Assert.AreEqual("1", pedido.ccc);
            Assert.AreEqual("FW", pedido.ruta);
            Assert.AreEqual("NV", pedido.vendedor);
            Assert.IsTrue(pedido.servirJunto);
        }

        [TestMethod]
        public void Construir_ClienteSinRuta_UsaLaRutaDeAgencia()
        {
            ClienteDTO cliente = FichaCliente();
            cliente.ruta = "   ";

            PedidoVentaDTO pedido = Construir(cliente: cliente);

            Assert.AreEqual(Constantes.Pedidos.RUTA_AGENCIA_00, pedido.ruta);
        }

        [TestMethod]
        public void Construir_ElClienteYElContactoSalenDeLaFichaDelJwt()
        {
            PedidoVentaDTO pedido = Construir();

            Assert.AreEqual(CLIENTE, pedido.cliente);
            Assert.AreEqual("0", pedido.contacto);
        }

        [TestMethod]
        public void Construir_ElUsuarioDiceCanalYCliente()
        {
            PedidoVentaDTO pedido = Construir();

            Assert.AreEqual("APP\\" + CLIENTE, pedido.Usuario);
            // El campo Usuario de CabPedidoVta es varchar(30)
            Assert.IsTrue(pedido.Usuario.Length <= 30);
            Assert.IsTrue(pedido.Lineas.All(l => l.usuario == pedido.Usuario));
        }

        [TestMethod]
        public void Construir_LasLineasSonDeProductoYEnCurso()
        {
            PedidoVentaDTO pedido = Construir();
            LineaPedidoVentaDTO linea = pedido.Lineas.Single();

            // NestoAPI#434 (punto 5): el endpoint de la app rellena SIEMPRE el tipo de línea
            Assert.AreEqual((byte)Constantes.TiposLineaVenta.PRODUCTO, linea.tipoLinea.Value);
            Assert.AreEqual((short)Constantes.EstadosLineaVenta.EN_CURSO, linea.estado);
            Assert.AreEqual("CHAMPÚ DE PRUEBA", linea.texto);
            Assert.AreEqual(2, linea.Cantidad);
        }

        [TestMethod]
        public void Construir_NoEsPresupuestoNiSaltaLaValidacion()
        {
            PedidoVentaDTO pedido = Construir();

            Assert.IsFalse(pedido.EsPresupuesto);
            Assert.IsFalse(pedido.CreadoSinPasarValidacion,
                "Un pedido de cliente pasa las validaciones como cualquier otro");
        }

        // --- Lo que sí dice el cliente, recortado ---

        [TestMethod]
        public void Construir_ComentarioMuyLargo_SeRecorta()
        {
            PedidoClienteRequest peticion = Peticion();
            peticion.Comentarios = new string('a', 1000);
            peticion.SuPedido = new string('b', 100);

            PedidoVentaDTO pedido = Construir(peticion);

            Assert.AreEqual(ConstructorPedidoCliente.LONGITUD_COMENTARIOS, pedido.comentarios.Length);
            Assert.AreEqual(50, pedido.suPedido.Length, "SuPedido es nvarchar(50) en CabPedidoVta");
        }

        [TestMethod]
        public void Construir_PedidoCompleto_PasaLasGuardasDeDatosObligatorios()
        {
            // El pedido que monta la app tiene que superar lo que exige PostPedidoVenta
            // (NestoAPI#434), o el cliente se llevaría un 400 en su primera compra.
            PedidoVentaDTO pedido = Construir();

            Assert.IsNull(NestoAPI.Controllers.PedidosVentaController.ValidarDatosObligatoriosPedido(pedido));
        }

        // --- Lo que el cliente puede pedir mal ---

        [TestMethod]
        public void ValidarPeticion_PeticionCorrecta_NoDaError()
        {
            Assert.IsNull(ConstructorPedidoCliente.ValidarPeticion(Peticion()));
        }

        [TestMethod]
        public void ValidarPeticion_SinLineas_DaError()
        {
            PedidoClienteRequest peticion = Peticion();
            peticion.Lineas.Clear();

            Assert.IsNotNull(ConstructorPedidoCliente.ValidarPeticion(peticion));
        }

        [TestMethod]
        public void ValidarPeticion_CantidadCero_DaErrorConElProducto()
        {
            PedidoClienteRequest peticion = Peticion();
            peticion.Lineas.Single().Cantidad = 0;

            string error = ConstructorPedidoCliente.ValidarPeticion(peticion);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "12345");
        }

        [TestMethod]
        public void ValidarPeticion_CantidadNegativa_DaError()
        {
            PedidoClienteRequest peticion = Peticion();
            peticion.Lineas.Single().Cantidad = -3;

            Assert.IsNotNull(ConstructorPedidoCliente.ValidarPeticion(peticion));
        }

        [TestMethod]
        public void ValidarPeticion_ProductoRepetido_DaError()
        {
            PedidoClienteRequest peticion = Peticion();
            peticion.Lineas.Add(new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 });

            Assert.IsNotNull(ConstructorPedidoCliente.ValidarPeticion(peticion));
        }

        [TestMethod]
        public void ValidarPeticion_DemasiadasLineas_DaError()
        {
            PedidoClienteRequest peticion = new PedidoClienteRequest();
            for (int i = 0; i <= ConstructorPedidoCliente.MAXIMO_LINEAS; i++)
            {
                peticion.Lineas.Add(new LineaPedidoClienteRequest { Producto = "P" + i, Cantidad = 1 });
            }

            Assert.IsNotNull(ConstructorPedidoCliente.ValidarPeticion(peticion));
        }
    }
}
