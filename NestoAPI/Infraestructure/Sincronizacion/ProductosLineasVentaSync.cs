using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#705 (incidencia 784, 39598): un pedido pendiente compromete stock pero no genera
    /// movimiento en ExtractoProducto, así que el producto no se volvía a publicar y la tienda seguía
    /// vendiendo lo ya comprometido. Aquí se decide, a partir de las líneas de venta que se van a
    /// grabar (antes y después), qué productos hay que encolar en Nesto_sync para que viaje el
    /// CantidadDisponibleTotal nuevo.
    ///
    /// Puro (sin EF ni BD) para poder probarlo; quien lo usa es <c>NVEntities.SaveChanges</c>, que
    /// es el único punto por el que pasan TODAS las grabaciones de líneas de la API (POST, PUT,
    /// presupuesto → pedido, canales externos, tienda, unir pedidos...), y el DELETE de pedidos, que
    /// borra la cabecera y deja las líneas al SQL (no pasan por el ChangeTracker).
    ///
    /// Encolar de más es gratis (la guarda de Nesto_sync no reencola lo pendiente y la publicación
    /// manda siempre el estado ACTUAL); callar de más deja la tienda vendiendo stock que no hay.
    /// </summary>
    public static class ProductosLineasVentaSync
    {
        internal const string USUARIO_ENCOLADO = "Líneas de pedido de venta";

        private static readonly HashSet<string> ALMACENES_STOCK = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Constantes.Almacenes.ALGETE,
            Constantes.Almacenes.REINA,
            Constantes.Almacenes.ALCOBENDAS
        };

        private static readonly HashSet<string> EMPRESAS_STOCK = new HashSet<string>
        {
            Constantes.Empresas.EMPRESA_POR_DEFECTO,
            Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO
        };

        /// <summary>
        /// Una línea "cuenta" para el disponible de la tienda si es de producto, está pendiente de
        /// entregar (-1 pendiente, 1 en curso) y es de los almacenes y empresas con los que
        /// ProductoService calcula los Stocks que viajan en el mensaje.
        /// </summary>
        public static bool ComprometeStock(LineaVentaStock linea)
        {
            return linea != null
                && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && (linea.Estado == Constantes.EstadosLineaVenta.PENDIENTE || linea.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                && !string.IsNullOrWhiteSpace(linea.Producto)
                && linea.Almacen != null && ALMACENES_STOCK.Contains(linea.Almacen.Trim())
                && linea.Empresa != null && EMPRESAS_STOCK.Contains(linea.Empresa.Trim());
        }

        /// <summary>
        /// Productos (sin repetir, recortados) que hay que encolar. Una línea nueva o borrada cuenta si
        /// compromete stock; una modificada, si compromete stock antes O después (pasar de pendiente a
        /// presupuesto LIBERA stock) y ha cambiado algo que afecte al disponible. Si se cambia el
        /// producto de una línea se encolan los dos: el que deja de estar comprometido y el nuevo.
        /// </summary>
        public static List<string> ProductosAEncolar(IEnumerable<CambioLineaVenta> cambios)
        {
            var productos = new List<string>();
            if (cambios == null)
            {
                return productos;
            }
            foreach (CambioLineaVenta cambio in cambios)
            {
                if (cambio == null)
                {
                    continue;
                }
                bool esModificacion = cambio.Antes != null && cambio.Despues != null;
                if (esModificacion && !(cambio.CamposDeStockModificados ?? CambianCamposDeStock(cambio.Antes, cambio.Despues)))
                {
                    continue;
                }
                if (ComprometeStock(cambio.Antes))
                {
                    productos.Add(cambio.Antes.Producto.Trim());
                }
                if (ComprometeStock(cambio.Despues))
                {
                    productos.Add(cambio.Despues.Producto.Trim());
                }
            }
            return productos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool CambianCamposDeStock(LineaVentaStock antes, LineaVentaStock despues)
        {
            return !string.Equals(antes.Producto?.Trim(), despues.Producto?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(antes.Almacen?.Trim(), despues.Almacen?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(antes.Empresa?.Trim(), despues.Empresa?.Trim(), StringComparison.Ordinal)
                || antes.Estado != despues.Estado
                || antes.TipoLinea != despues.TipoLinea
                || antes.Cantidad != despues.Cantidad;
        }

        public static LineaVentaStock Foto(LinPedidoVta linea)
        {
            return linea == null ? null : new LineaVentaStock
            {
                Empresa = linea.Empresa,
                Producto = linea.Producto,
                Almacen = linea.Almacén,
                Estado = linea.Estado,
                TipoLinea = linea.TipoLinea,
                Cantidad = linea.Cantidad
            };
        }
    }

    /// <summary>NestoAPI#705: lo de una línea de venta que influye en el disponible del producto.</summary>
    public class LineaVentaStock
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public short Estado { get; set; }
        public byte? TipoLinea { get; set; }
        public short? Cantidad { get; set; }
    }

    /// <summary>
    /// NestoAPI#705: una línea que se graba. Antes null = alta; Despues null = borrado; los dos = modificación.
    /// </summary>
    public class CambioLineaVenta
    {
        public LineaVentaStock Antes { get; set; }
        public LineaVentaStock Despues { get; set; }

        /// <summary>
        /// Lo dice EF (Property.IsModified) en las modificaciones: si la entidad se adjuntó y se marcó
        /// entera como Modified, los valores originales son los actuales y compararlos no vería nada,
        /// pero IsModified viene a true. Null = decidir comparando Antes y Despues.
        /// </summary>
        public bool? CamposDeStockModificados { get; set; }

        public static CambioLineaVenta Alta(LineaVentaStock linea) => new CambioLineaVenta { Despues = linea };
        public static CambioLineaVenta Borrado(LineaVentaStock linea) => new CambioLineaVenta { Antes = linea };
    }
}
