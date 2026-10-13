using System;
using System.Collections.Generic;
using System.Linq;
using System.Transactions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#506: el modo de servicio con el que debe nacer un pedido según el stock REAL de sus
    /// líneas, con los mismos colores que el correo de pedido (<see cref="GestorStocks.ColorStock"/>):
    /// <list type="bullet">
    /// <item><b>verde</b>: stock suficiente en el almacén del pedido.</item>
    /// <item><b>rosa</b>: falta en el almacén pero lo hay en el conjunto (hay que traerlo de las tiendas).</item>
    /// <item><b>rojo</b>: no hay stock en ningún sitio.</item>
    /// </list>
    /// Regla (Carlos, 22/09/26): todo verde → «Todo junto» (1); solo verde y rojo → «Ahora lo que hay, el
    /// resto de una vez» (4); alguna rosa (haya o no verde y rojo) → «Tras reponer de tiendas» (3).
    /// Nunca se sugiere «Según vaya entrando» (2). Decisiones de los casos abiertos (22/09/26):
    /// <list type="bullet">
    /// <item>Pedido sin líneas de producto (solo texto o cuenta contable): el defecto de siempre (3), no hay nada que mirar.</item>
    /// <item>Rosa + rojo → 3, aceptando que la parte roja pueda llegar después en más de una entrega.</item>
    /// <item>Una línea parcialmente cubrible desde tiendas sale roja (como en el correo) y cuenta como roja.</item>
    /// <item>Solo se sugiere al crear; al modificar no se recalcula el modo.</item>
    /// <item>NestoAPI#515: el color se pide con la cantidad de la línea y agrupando por producto y
    /// almacén, porque el pedido aún no está grabado. Los recuentos (LineasVerdes/Rosas/Rojas) son, por
    /// tanto, de grupos producto+almacén, no de líneas sueltas.</item>
    /// </list>
    /// Es la única fuente: la usan <c>PostPedidoVenta</c> cuando el cliente no manda modo, la estimación de
    /// portes del carrito de la app y el endpoint <c>POST api/PedidosVenta/ModoServicioSugerido</c> con el
    /// que las plantillas de Nesto y NestoApp preseleccionan el modo antes de confirmar.
    /// </summary>
    public static class SugeridorModoServicio
    {
        public const string VERDE = "green";
        public const string ROSA = "DeepPink";
        public const string ROJO = "red";

        public class Sugerencia
        {
            public byte Modo { get; set; }
            public string Nombre { get; set; }
            public int LineasVerdes { get; set; }
            public int LineasRosas { get; set; }
            public int LineasRojas { get; set; }
            /// <summary>NestoAPI#561: alguna línea tiene al menos una unidad libre ya en su almacén (verde, o roja con
            /// parte del stock). Sin ninguna, el pedido va todo junto.</summary>
            public bool HayAlgoQueServirAhora { get; set; } = true;
            public string Motivo { get; set; }
            /// <summary>NestoAPI#518: los modos que se pueden elegir para este pedido (el sugerido siempre está).</summary>
            public List<byte> ModosPermitidos { get; set; } = new List<byte>();
            /// <summary>NestoAPI#518: los cuatro modos con su permiso y, si no se puede, el motivo para enseñarlo.</summary>
            public List<ModoServicioPermitidoDTO> Modos { get; set; } = new List<ModoServicioPermitidoDTO>();
        }

        public static Sugerencia Sugerir(PedidoVentaDTO pedido, IGestorStocks stocks) => Sugerir(pedido, stocks, null);

        /// <summary>
        /// NestoAPI#606 (kits, Carlos 13/10/26): con <paramref name="kits"/>, lo que falta en Algete y sale desmontando un kit
        /// libre en Algete cuenta como stock, con las MISMAS cuentas que la fecha de entrega a la agencia
        /// (<see cref="RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete"/> sobre
        /// <see cref="RepositorioSombraModoServicioSql.LeerDatosConKits"/>): si no, la plantilla proponía «Tras reponer de
        /// tiendas» con un pedido que sale entero hoy desmontando un kit (928288). Solo se leen los kits si alguna línea de
        /// Algete no es verde; si la lectura falla, la sugerencia es la de siempre (sin kits).
        /// </summary>
        public static Sugerencia Sugerir(PedidoVentaDTO pedido, IGestorStocks stocks, IFuenteKitsModoServicio kits)
        {
            if (stocks == null)
            {
                throw new ArgumentNullException(nameof(stocks));
            }
            List<LineaPedidoVentaDTO> productos = (pedido?.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                            && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .ToList();
            if (!productos.Any())
            {
                return ConModos(new Sugerencia
                {
                    Modo = Constantes.Pedidos.ModosServicio.POR_DEFECTO,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.POR_DEFECTO),
                    Motivo = "El pedido no tiene líneas de producto: se aplica el modo por defecto."
                }, ModosServicioPermitidos.TipoAlmacen.Otro, hayLineasProducto: false);
            }

            // NestoAPI#518: en tienda el cliente se lleva lo que hay; no hace falta mirar el stock.
            ModosServicioPermitidos.TipoAlmacen tipo = ModosServicioPermitidos.Clasificar(productos);
            if (tipo == ModosServicioPermitidos.TipoAlmacen.Tienda)
            {
                return ConModos(new Sugerencia
                {
                    Modo = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO),
                    Motivo = "Pedido de tienda: el cliente se lleva lo que hay y el resto se sirve según vaya entrando."
                }, tipo, hayLineasProducto: true);
            }

            // NestoAPI#518 (Carlos, 23/09/26): un almacén que no es Algete ni tienda (Amazon, AMZ) no tiene
            // restricción de modos, pero por defecto sale todo junto: no se mira el stock de Algete.
            if (tipo == ModosServicioPermitidos.TipoAlmacen.Otro)
            {
                return ConModos(new Sugerencia
                {
                    Modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.TODO_JUNTO),
                    Motivo = "Pedido de un almacén sin reglas de stock (por ejemplo Amazon): por defecto sale todo junto."
                }, tipo, hayLineasProducto: true);
            }

            // NestoAPI#515: un color por producto y almacén, con la CANTIDAD que se pide sumada. Dos cosas:
            //   - el pedido todavía no existe, así que sus unidades no están en pendientes de entregar y
            //     hay que descontarlas a mano (si no, 7 en el almacén salen verdes aunque se pidan 8);
            //   - dos líneas del mismo producto y almacén (la normal y la de regalo de una oferta) consumen
            //     el mismo stock: se suman y se pide un solo color, o se contaría dos veces.
            List<GrupoColor> grupos = productos
                .GroupBy(l => new { Producto = l.Producto.Trim(), Almacen = l.almacen?.Trim() })
                .Select(g => new GrupoColor
                {
                    Producto = g.Key.Producto,
                    Almacen = g.Key.Almacen,
                    Cantidad = g.Sum(l => l.Cantidad),
                    Color = stocks.ColorStock(g.Key.Producto, g.Key.Almacen, g.Sum(l => l.Cantidad))
                })
                .ToList();
            // NestoAPI#606 (kits): lo que sale desmontando un kit en Algete cuenta como stock (el color se recalcula con
            // las unidades que quedan después de los kits).
            bool conKits = kits != null && CubrirConKits(pedido, grupos, kits, stocks);
            int verdes = grupos.Count(g => g.Color == VERDE);
            int rosas = grupos.Count(g => g.Color == ROSA);
            int rojas = grupos.Count(g => g.Color != VERDE && g.Color != ROSA);

            // NestoAPI#561: una roja puede tener parte del stock (1 de 2). Solo cuando no hay NINGUNA unidad libre
            // ya en el almacén de la línea el pedido tiene que ir todo junto. NestoAPI#606: lo que sale de un kit, también.
            bool hayAlgoQueServirAhora = verdes > 0 || conKits || grupos
                .Any(g => stocks.Stock(g.Producto, g.Almacen) - stocks.UnidadesPendientesEntregarAlmacen(g.Producto, g.Almacen) > 0);

            Sugerencia decidida = Decidir(verdes, rosas, rojas, hayAlgoQueServirAhora);
            if (conKits)
            {
                decidida.Motivo = decidida.Motivo.TrimEnd('.') + " " + MOTIVO_CON_KITS;
            }
            return ConModos(decidida, tipo, hayLineasProducto: true);
        }

        /// <summary>NestoAPI#606 (kits): coletilla del motivo cuando algún kit cambia el color de una línea.</summary>
        internal const string MOTIVO_CON_KITS = "(contando los kits que se pueden desmontar en Algete).";

        private class GrupoColor
        {
            public string Producto { get; set; }
            public string Almacen { get; set; }
            public int Cantidad { get; set; }
            public string Color { get; set; }
        }

        /// <summary>
        /// NestoAPI#606 (kits): a cada grupo de Algete que no es verde le quita lo que sale desmontando kits libres en Algete
        /// (<see cref="RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete"/>: kits que deben otros pedidos y los que pide
        /// este, deuda previa del componente, sobras de un kit ya desmontado, primero el kit que más lleva) y recalcula su
        /// color con lo que queda. Las libres y los kits salen de la misma lectura que la fecha de entrega (pendientes sin el
        /// propio pedido). Devuelve si algún grupo ha sacado algo de un kit.
        /// </summary>
        private static bool CubrirConKits(PedidoVentaDTO pedido, List<GrupoColor> grupos, IFuenteKitsModoServicio fuente, IGestorStocks stocks)
        {
            List<GrupoColor> sinStock = grupos
                .Where(g => g.Color != VERDE && RepartidorStockFechaEntregaAgencia.EsDeAlgete(g.Almacen))
                .ToList();
            if (!sinStock.Any())
            {
                return false;
            }
            DatosSombraModoServicio datos;
            try
            {
                string empresa = string.IsNullOrWhiteSpace(pedido?.empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : pedido.empresa.Trim();
                int? pedidoExcluir = pedido != null && pedido.numero > 0 ? pedido.numero : (int?)null;
                datos = fuente.LeerConKits(empresa, pedidoExcluir, sinStock.Select(g => g.Producto).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"NestoAPI#606: no se pudieron leer los kits para el modo de servicio; se sugiere sin ellos ({ex.Message})", ex));
                return false;
            }
            if (datos == null || !datos.Kits.Any())
            {
                return false;
            }

            List<LineaPedidoFechaEntregaAgencia> lineasAlgete = grupos
                .Where(g => RepartidorStockFechaEntregaAgencia.EsDeAlgete(g.Almacen))
                .Select(g => new LineaPedidoFechaEntregaAgencia { Producto = g.Producto, Almacen = g.Almacen, Cantidad = g.Cantidad })
                .ToList();
            var bolsa = new RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete(datos, lineasAlgete);
            bool alguno = false;
            foreach (GrupoColor grupo in sinStock)
            {
                string clave = ResumenStocksProductos.Clave(grupo.Producto, Constantes.Almacenes.ALGETE);
                int libres = Math.Max(0, ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, clave)
                    - ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, clave));
                int faltan = grupo.Cantidad - libres;
                if (faltan <= 0)
                {
                    continue;
                }
                int deKits = bolsa.Tomar(grupo.Producto, faltan).Values.Sum();
                if (deKits <= 0)
                {
                    continue;
                }
                alguno = true;
                grupo.Color = stocks.ColorStock(grupo.Producto, grupo.Almacen, grupo.Cantidad - deKits);
            }
            return alguno;
        }

        /// <summary>NestoAPI#518: rellena los modos permitidos con los recuentos ya calculados.</summary>
        private static Sugerencia ConModos(Sugerencia sugerencia, ModosServicioPermitidos.TipoAlmacen tipo, bool hayLineasProducto)
        {
            sugerencia.Modos = ModosServicioPermitidos.Calcular(tipo, sugerencia.LineasVerdes, sugerencia.LineasRosas, sugerencia.LineasRojas, hayLineasProducto,
                sugerencia.HayAlgoQueServirAhora);
            sugerencia.ModosPermitidos = ModosServicioPermitidos.Permitidos(sugerencia.Modos);
            return sugerencia;
        }

        /// <summary>
        /// NestoAPI#518: el modo que fuerza el parámetro ModoServicioPorDefecto se respeta SOLO si tiene sentido
        /// para el pedido; si no, se propone el del stock y se explica. Devuelve una COPIA (la sugerencia de
        /// entrada puede venir de la caché por huella de #517, compartida entre usuarios).
        /// </summary>
        public static Sugerencia AplicarForzado(Sugerencia sugerencia, byte forzado)
        {
            var copia = new Sugerencia
            {
                Modo = sugerencia.Modo,
                Nombre = sugerencia.Nombre,
                LineasVerdes = sugerencia.LineasVerdes,
                LineasRosas = sugerencia.LineasRosas,
                LineasRojas = sugerencia.LineasRojas,
                Motivo = sugerencia.Motivo,
                ModosPermitidos = new List<byte>(sugerencia.ModosPermitidos ?? new List<byte>()),
                Modos = sugerencia.Modos
            };
            if (copia.ModosPermitidos.Contains(forzado))
            {
                copia.Modo = forzado;
                copia.Nombre = Constantes.Pedidos.ModosServicio.Nombre(forzado);
                copia.Motivo = "Modo fijado por el parámetro ModoServicioPorDefecto del usuario.";
                return copia;
            }
            string porQue = copia.Modos?.FirstOrDefault(m => m.Modo == forzado)?.Motivo;
            copia.Motivo = $"Tu parámetro ModoServicioPorDefecto fija «{Constantes.Pedidos.ModosServicio.Nombre(forzado)}», " +
                           $"pero no tiene sentido para este pedido{(string.IsNullOrWhiteSpace(porQue) ? string.Empty : $" ({porQue.TrimEnd('.')})")}. " +
                           sugerencia.Motivo;
            return copia;
        }

        /// <summary>El núcleo puro de la regla, a partir de los recuentos por color.</summary>
        internal static Sugerencia Decidir(int verdes, int rosas, int rojas, bool hayAlgoQueServirAhora = true)
        {
            byte modo;
            string motivo;
            if (rosas > 0)
            {
                modo = Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS;
                motivo = $"{rosas} línea{(rosas == 1 ? string.Empty : "s")} hay que traerla{(rosas == 1 ? string.Empty : "s")} de las tiendas: se espera a la reposición.";
            }
            else if (rojas > 0 && verdes == 0 && !hayAlgoQueServirAhora)
            {
                // NestoAPI#561 (pedido 927293): sin ninguna unidad que servir, «ahora lo que hay» no servía nada
                // (salían solo los portes). Sale todo junto cuando llegue.
                modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                motivo = "No hay ninguna unidad del pedido en stock: sale todo junto cuando llegue.";
            }
            else if (rojas > 0)
            {
                modo = Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ;
                motivo = $"{rojas} línea{(rojas == 1 ? string.Empty : "s")} sin stock en ningún almacén: sale lo que hay y el resto en una sola entrega más.";
            }
            else
            {
                modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                motivo = "Hay stock de todo en el almacén del pedido: sale todo junto.";
            }
            return new Sugerencia
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                LineasVerdes = verdes,
                LineasRosas = rosas,
                LineasRojas = rojas,
                HayAlgoQueServirAhora = hayAlgoQueServirAhora,
                Motivo = motivo
            };
        }
    }

    /// <summary>
    /// NestoAPI#606 (kits, 13/10/26): de dónde lee <see cref="SugeridorModoServicio"/> los kits que llevan los productos y
    /// su stock (los mismos datos que la fecha de entrega a la agencia).
    /// </summary>
    public interface IFuenteKitsModoServicio
    {
        DatosSombraModoServicio LeerConKits(string empresa, int? pedidoExcluir, IEnumerable<string> productos);
    }

    /// <summary>
    /// NestoAPI#606 (kits): <see cref="RepositorioSombraModoServicioSql.LeerDatosConKits"/> fuera de cualquier transacción
    /// ambiente (como <c>ServicioFechaEntregaAgencia</c>, sugerencia 579: la conexión propia dentro de una transacción la
    /// escalaría a distribuida). Lecturas con NOLOCK.
    /// </summary>
    public class FuenteKitsModoServicioSql : IFuenteKitsModoServicio
    {
        public DatosSombraModoServicio LeerConKits(string empresa, int? pedidoExcluir, IEnumerable<string> productos)
        {
            using (var sinTransaccion = new TransactionScope(TransactionScopeOption.Suppress))
            {
                DatosSombraModoServicio datos = RepositorioSombraModoServicioSql.DesdeConfiguracion().LeerDatosConKits(empresa, pedidoExcluir, productos);
                sinTransaccion.Complete();
                return datos;
            }
        }
    }
}
