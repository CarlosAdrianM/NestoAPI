using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Tests.Infrastructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#663: prueba REAL contra el SANDBOX de Aplázame (no mueve dinero): crea un checkout con el payload que
    /// monta ConstructorCheckoutAplazame y comprueba que Aplázame lo acepta y devuelve su id. Ignorada por defecto (necesita
    /// red y las claves); para lanzarla, quitar el [Ignore] en local. La clave se lee de la variable de entorno
    /// APLAZAME_PRIVATE_KEY o, si no está, de NestoAPI/secretos.config. NUNCA contra producción.
    /// </summary>
    [TestClass]
    public class AplazameSandboxIntegracionTests
    {
        [TestMethod]
        [TestCategory("Integracion")]
        [Ignore]
        public async Task Sandbox_AceptaElCheckoutQueMontamos()
        {
            string clave = Environment.GetEnvironmentVariable("APLAZAME_PRIVATE_KEY") ?? LeerClaveDeSecretos();
            Assert.IsFalse(string.IsNullOrWhiteSpace(clave), "falta la clave privada de Aplázame");

            List<LineaPedidoVentaDTO> lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "17404", texto = "CHAMPU PRUEBA", Cantidad = 3, PrecioUnitario = 10.02M, DescuentoLinea = 0.15M, PorcentajeIva = 0.21M },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "31100", texto = "TINTE PRUEBA", Cantidad = 5, PrecioUnitario = 19.95M, PorcentajeIva = 0.21M },
                // Regalo de Ganavisiones (a cero) y cheque regalo (línea negativa, va dentro del descuento)
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "GANA1", texto = "REGALO PRUEBA", Cantidad = 1, PrecioUnitario = 12.40M, AplicarDescuento = false, DescuentoLinea = 1M, PorcentajeIva = 0.21M },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "CHEQUE", texto = "CHEQUE REGALO", Cantidad = -1, PrecioUnitario = 10M, AplicarDescuento = false, PorcentajeIva = 0.21M },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "29501", texto = "MASCARILLA PRUEBA", Cantidad = 7, PrecioUnitario = 3.33M, PorcentajeIva = 0.21M },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE, Producto = "62400003", texto = "PORTES", Cantidad = 1, PrecioUnitario = 4.95M, PorcentajeIva = 0.21M }
            };
            PedidoVentaDTO pedido = new PedidoVentaDTO();
            lineas.ForEach(l => pedido.Lineas.Add(l));

            CheckoutAplazame checkout = ConstructorCheckoutAplazame.Construir(
                "NV-TEST-" + DateTime.Now.ToString("yyyyMMddHHmmss"), "sandbox@nuevavision.es", "15191",
                new DireccionCheckout { Nombre = "PELUQUERIA PRUEBA", Direccion = "C/ Río Tiétar, 11", CodigoPostal = "28119", Poblacion = "Algete", Provincia = "Madrid", Pais = "ES", Telefono = "916281914" },
                new DireccionCheckout { Nombre = "PELUQUERIA PRUEBA", Direccion = "C/ Río Tiétar, 11", CodigoPostal = "28119", Poblacion = "Algete", Provincia = "Madrid", Pais = "ES", Telefono = "916281914" },
                "Envío", false, lineas, pedido.Total,
                "https://api.nuevavision.es/api/Aplazame/Notificacion", "nestotiendas://aplazame/");

            string id = await new ClienteApiAplazame(clave).CrearCheckout(checkout, sandbox: true);

            Assert.IsFalse(string.IsNullOrWhiteSpace(id));
        }

        private static string LeerClaveDeSecretos()
        {
            DirectoryInfo carpeta = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "NestoAPI", "secretos.config")))
            {
                carpeta = carpeta.Parent;
            }
            if (carpeta == null)
            {
                return null;
            }
            return XDocument.Load(Path.Combine(carpeta.FullName, "NestoAPI", "secretos.config"))
                .Descendants("add")
                .FirstOrDefault(a => (string)a.Attribute("key") == "AplazamePrivateKey")?
                .Attribute("value")?.Value;
        }
    }
}
