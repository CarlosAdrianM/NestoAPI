using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#517: resultados de cálculos sobre un pedido que se está montando (sugerencias de ofertas,
    /// modo de servicio sugerido) guardados <see cref="DURACION"/> por huella del pedido. La plantilla de
    /// Nesto (y NestoApp) los pide tras cada cambio de totales; si un cliente entra en bucle o repite el
    /// mismo pedido, las repeticiones salen de aquí sin tocar la BD. La huella recoge lo que decide el
    /// resultado: cabecera (empresa, cliente, contacto, IVA) y, por línea, producto, tipo, cantidad,
    /// precio, descuentos, aplicar descuento, almacén, forma de venta y oferta. Cualquier cambio real del
    /// pedido cambia la huella y se recalcula. Cada cálculo va en su propio espacio de claves.
    /// </summary>
    public static class CachePorHuellaPedido
    {
        public static readonly TimeSpan DURACION = TimeSpan.FromSeconds(30);

        public const string ESPACIO_SUGERENCIAS_OFERTAS = "SugerenciasOfertas";
        public const string ESPACIO_MODO_SERVICIO = "ModoServicioSugerido";

        private static readonly MemoryCache cache = MemoryCache.Default;

        /// <summary>El valor guardado para ese pedido en ese espacio, o el calculado (y guardado) si no hay.</summary>
        public static T ObtenerOCalcular<T>(string espacio, PedidoVentaDTO pedido, Func<T> calcular) where T : class
        {
            string clave = "HuellaPedido|" + espacio + "|" + Huella(pedido);
            if (cache.Get(clave) is T guardado)
            {
                return guardado;
            }
            T calculado = calcular();
            if (calculado != null)
            {
                cache.Set(clave, calculado, DateTimeOffset.UtcNow.Add(DURACION));
            }
            return calculado;
        }

        /// <summary>
        /// Huella estable del pedido: no depende del orden de las líneas ni del relleno de los char ni de
        /// mayúsculas en los códigos. Pura, para poder probarla.
        /// </summary>
        public static string Huella(PedidoVentaDTO pedido)
        {
            if (pedido == null)
            {
                return string.Empty;
            }
            var texto = new StringBuilder();
            texto.Append(Codigo(pedido.empresa)).Append('|')
                 .Append(Codigo(pedido.cliente)).Append('|')
                 .Append(Codigo(pedido.contacto)).Append('|')
                 .Append(Codigo(pedido.iva)).Append('#');

            IEnumerable<string> lineas = (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Where(l => l != null)
                .Select(l => string.Join("|",
                    Codigo(l.Producto),
                    l.tipoLinea?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    l.Cantidad.ToString(CultureInfo.InvariantCulture),
                    Numero(l.PrecioUnitario),
                    Numero(l.DescuentoLinea),
                    Numero(l.DescuentoProducto),
                    Numero(l.DescuentoEntidad),
                    Numero(l.DescuentoPP),
                    l.AplicarDescuento ? "1" : "0",
                    Codigo(l.almacen),
                    Codigo(l.formaVenta),
                    l.oferta?.ToString(CultureInfo.InvariantCulture) ?? string.Empty))
                .OrderBy(l => l, StringComparer.Ordinal);
            texto.Append(string.Join(";", lineas));

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(texto.ToString()));
                return Convert.ToBase64String(hash);
            }
        }

        private static string Codigo(string valor) => valor?.Trim().ToUpperInvariant() ?? string.Empty;

        // Normaliza la escala: 10 y 10.00 son el mismo precio.
        private static string Numero(decimal valor) => (valor / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
    }
}
