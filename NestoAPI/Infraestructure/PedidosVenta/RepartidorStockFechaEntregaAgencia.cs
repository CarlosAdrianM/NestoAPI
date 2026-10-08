using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#606: una línea del pedido (o de la plantilla) tal como la necesita el repartidor.</summary>
    public class LineaPedidoFechaEntregaAgencia
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public bool YaEnPicking { get; set; }
    }

    /// <summary>
    /// NestoAPI#606: de dónde sale cada unidad de las líneas de Algete, con el stock leído para la sombra del modo de
    /// servicio (<see cref="DatosSombraModoServicio"/>, pendientes SIN el propio pedido) y el MISMO reparto por causas que
    /// usa el sugeridor (<see cref="ClasificadorCausasModoServicio.ClasificarLinea"/>, #563): libres en Algete → tiendas →
    /// pedido a proveedor enviado (con su fecha prevista, si no pasa de
    /// <see cref="ClasificadorCausasModoServicio.DIAS_MAXIMOS_EN_CAMINO"/> días) → sobre pedido / sin fecha.
    ///
    /// <para>Lo de las tiendas se reparte entre Reina y Alcobendas según las unidades libres de cada una (stock − sus
    /// pendientes), empezando por la tienda cuya reposición deja salir antes el pedido (la que se lo lleva primero); lo
    /// que las tiendas no tienen libre es lo que ya viene de camino (reposiciones generadas y sin recibir). Varias líneas
    /// del mismo producto se reparten en orden: primero las que ya tienen picking.</para>
    /// </summary>
    public static class RepartidorStockFechaEntregaAgencia
    {
        public static readonly IReadOnlyList<string> TIENDAS = new[] { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public static bool EsDeAlgete(string almacen) =>
            string.Equals(almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase);

        /// <param name="tiendasPorOrden">Las tiendas en el orden en que se cogen sus unidades.</param>
        public static List<LineaFechaEntregaAgencia> Repartir(IEnumerable<LineaPedidoFechaEntregaAgencia> lineas, DatosSombraModoServicio datos,
            IEnumerable<string> tiendasPorOrden, DateTime hoy)
        {
            datos = datos ?? new DatosSombraModoServicio();
            List<string> tiendas = (tiendasPorOrden ?? TIENDAS).ToList();
            var fuente = new FuenteDatosCausasSombra(datos);
            List<LineaPedidoFechaEntregaAgencia> deAlgete = (lineas ?? Enumerable.Empty<LineaPedidoFechaEntregaAgencia>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0 && EsDeAlgete(l.Almacen))
                .ToList();
            var repartidas = new Dictionary<LineaPedidoFechaEntregaAgencia, LineaFechaEntregaAgencia>();

            foreach (IGrouping<string, LineaPedidoFechaEntregaAgencia> grupo in deAlgete.GroupBy(l => ResumenStocksProductos.Clave(l.Producto)))
            {
                string producto = grupo.First().Producto.Trim();
                CausasLineaModoServicio causas = ClasificadorCausasModoServicio.ClasificarLinea(producto, Constantes.Almacenes.ALGETE,
                    grupo.Sum(l => l.Cantidad), fuente.Leer(producto, Constantes.Almacenes.ALGETE), hoy);

                int libres = causas.Libres;
                int paraTiendas = causas.Tiendas;
                var porTienda = new List<KeyValuePair<string, int>>();
                foreach (string tienda in tiendas)
                {
                    string clave = ResumenStocksProductos.Clave(producto, tienda);
                    int libresTienda = Math.Max(0, ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, clave)
                        - ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, clave));
                    int unidades = Math.Min(paraTiendas, libresTienda);
                    if (unidades > 0)
                    {
                        porTienda.Add(new KeyValuePair<string, int>(tienda, unidades));
                        paraTiendas -= unidades;
                    }
                }
                int enCamino = paraTiendas;
                int delProveedor = causas.EnCamino;
                string motivoSinFecha = causas.SobrePedido > 0
                    ? "es un producto sobre pedido y aún no hay pedido al proveedor que lo cubra"
                    : null;

                foreach (LineaPedidoFechaEntregaAgencia linea in grupo.OrderByDescending(l => l.YaEnPicking))
                {
                    int resto = linea.Cantidad;
                    int Tomar(ref int bolsa)
                    {
                        int tomadas = Math.Min(resto, bolsa);
                        bolsa -= tomadas;
                        resto -= tomadas;
                        return tomadas;
                    }

                    var repartida = new LineaFechaEntregaAgencia
                    {
                        Producto = producto,
                        Cantidad = linea.Cantidad,
                        BaseImponible = linea.BaseImponible,
                        FechaEntrega = linea.FechaEntrega,
                        YaEnPicking = linea.YaEnPicking,
                        EnAlgete = Tomar(ref libres),
                        FechaProveedor = causas.FechaPrevista,
                        MotivoSinFecha = motivoSinFecha
                    };
                    for (int i = 0; i < porTienda.Count && resto > 0; i++)
                    {
                        int bolsa = porTienda[i].Value;
                        int unidades = Tomar(ref bolsa);
                        porTienda[i] = new KeyValuePair<string, int>(porTienda[i].Key, bolsa);
                        if (unidades > 0)
                        {
                            repartida.EnTiendas[porTienda[i].Key] = unidades;
                        }
                    }
                    repartida.EnCaminoDeTiendas = Tomar(ref enCamino);
                    repartida.DelProveedor = Tomar(ref delProveedor);
                    repartidas[linea] = repartida;
                }
            }

            return deAlgete.Select(l => repartidas[l]).ToList();
        }
    }
}
