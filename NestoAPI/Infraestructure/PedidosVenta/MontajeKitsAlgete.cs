using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606 (packs montables, Carlos 18/11/26, caso 44376 PACK REPOSICION LIFTING): un producto que es el TITULAR de un kit (un pack que
    /// nunca se compra) y no tiene stock propio en Algete se MONTA con sus componentes. Quién lo monta y cuándo: almacén, a mano
    /// (<c>POST api/Productos/MontarKit</c>, Diario _MontarKit), con el informe «Kits que se pueden montar o desmontar»
    /// (<c>prdInformeKitsQueSePuedenMontar</c>, parte «m») que saca desde el popup del picking de Nesto. El picking NO monta: sin stock del
    /// pack no lo reserva. Es el mismo supuesto que ya hace el motor con los kits que se DESMONTAN (<see cref="RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete"/>).
    /// <list type="bullet">
    /// <item>Componente libre en Algete = stock en Algete − lo que deben los demás pedidos en Algete − lo que pide este mismo pedido suelto −
    /// lo que necesitan los packs que deben los demás pedidos por encima del stock de ese pack (cualquier pack que lo lleve, también el
    /// mismo), como la «deuda previa» de <see cref="RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete"/>: los que esperaban antes van primero.</item>
    /// <item>Se montan tantos packs como permita el componente más escaso; lo que se monta deja de estar libre para el siguiente pack del pedido.</item>
    /// <item>Un solo nivel: un componente que a su vez es kit cuenta como un producto normal (ni se monta ni se desmonta para esto), igual
    /// que en <see cref="RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete"/>, en el informe de kits y en <c>GestorKits</c>.</item>
    /// <item>Fuera los kits con materias primas (MTP): son listas de fabricación, no packs que se montan en el almacén.</item>
    /// </list>
    /// Pura.
    /// </summary>
    internal class BolsaMontajeAlgete
    {
        private readonly Dictionary<string, List<ComponenteKitFechaEntregaAgencia>> componentesPorTitular;
        private readonly Dictionary<string, int> libres = new Dictionary<string, int>();

        public BolsaMontajeAlgete(DatosSombraModoServicio datos, IEnumerable<LineaPedidoFechaEntregaAgencia> lineasAlgete)
        {
            datos = datos ?? new DatosSombraModoServicio();
            List<ComponenteKitFechaEntregaAgencia> filas = (datos.KitsMontaje ?? new List<ComponenteKitFechaEntregaAgencia>())
                .Where(k => k != null && !string.IsNullOrWhiteSpace(k.Kit) && !string.IsNullOrWhiteSpace(k.Componente) && k.Cantidad > 0
                    && ResumenStocksProductos.Clave(k.Kit) != ResumenStocksProductos.Clave(k.Componente))
                .ToList();
            Dictionary<string, List<ComponenteKitFechaEntregaAgencia>> porKit = filas
                .GroupBy(k => ResumenStocksProductos.Clave(k.Kit))
                .ToDictionary(g => g.Key, g => g.ToList());
            List<LineaPedidoFechaEntregaAgencia> lineas = (lineasAlgete ?? Enumerable.Empty<LineaPedidoFechaEntregaAgencia>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto)).ToList();
            HashSet<string> delPedido = new HashSet<string>(lineas.Select(l => ResumenStocksProductos.Clave(l.Producto)));
            componentesPorTitular = porKit.Where(k => delPedido.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value);

            foreach (string componente in componentesPorTitular.Values.SelectMany(c => c).Select(c => ResumenStocksProductos.Clave(c.Componente)).Distinct())
            {
                string clave = ResumenStocksProductos.Clave(componente, Constantes.Almacenes.ALGETE);
                int stock = ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, clave);
                int pendientes = ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, clave);
                int propio = lineas.Where(l => ResumenStocksProductos.Clave(l.Producto) == componente).Sum(l => l.Cantidad);
                int otrosPacks = 0;
                foreach (KeyValuePair<string, List<ComponenteKitFechaEntregaAgencia>> kit in porKit)
                {
                    int porPack = kit.Value.Where(c => ResumenStocksProductos.Clave(c.Componente) == componente).Sum(c => c.Cantidad);
                    if (porPack <= 0)
                    {
                        continue;
                    }
                    string claveKit = ResumenStocksProductos.Clave(kit.Key, Constantes.Almacenes.ALGETE);
                    int deuda = ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, claveKit)
                        - ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, claveKit);
                    otrosPacks += Math.Max(0, deuda) * porPack;
                }
                libres[componente] = stock - pendientes - propio - otrosPacks;
            }
        }

        /// <summary>¿Es el producto titular de un kit que se puede montar?</summary>
        public bool EsMontable(string producto) => componentesPorTitular.ContainsKey(ResumenStocksProductos.Clave(producto));

        /// <summary>Los componentes del pack (una fila por componente, cantidades sumadas).</summary>
        public List<ComponenteKitFechaEntregaAgencia> Componentes(string producto)
        {
            return componentesPorTitular.TryGetValue(ResumenStocksProductos.Clave(producto), out List<ComponenteKitFechaEntregaAgencia> filas)
                ? filas.GroupBy(c => ResumenStocksProductos.Clave(c.Componente))
                    .Select(g => new ComponenteKitFechaEntregaAgencia { Kit = g.First().Kit.Trim(), Componente = g.First().Componente.Trim(), Cantidad = g.Sum(c => c.Cantidad) })
                    .OrderBy(c => c.Componente, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<ComponenteKitFechaEntregaAgencia>();
        }

        /// <summary>Libres que quedan del componente en Algete (puede ser negativo: ya lo deben otros).</summary>
        public int Libres(string componente) => libres.TryGetValue(ResumenStocksProductos.Clave(componente), out int n) ? n : 0;

        /// <summary>Cuántos packs (hasta <paramref name="unidades"/>) se montan YA con lo libre en Algete; esos componentes dejan de estar libres.</summary>
        public int Montar(string producto, int unidades)
        {
            List<ComponenteKitFechaEntregaAgencia> componentes = Componentes(producto);
            if (unidades <= 0 || !componentes.Any())
            {
                return 0;
            }
            int montables = componentes.Min(c => Math.Max(0, Libres(c.Componente)) / c.Cantidad);
            int montados = Math.Min(unidades, montables);
            foreach (ComponenteKitFechaEntregaAgencia c in componentes)
            {
                libres[ResumenStocksProductos.Clave(c.Componente)] = Libres(c.Componente) - montados * c.Cantidad;
            }
            return montados;
        }

        /// <summary>
        /// Los packs que NO se pueden montar ya: por cada componente, cuántas unidades faltan en Algete para montarlos (solo los que faltan).
        /// Lo que sí hay queda apartado para ellos (no se promete a otro pack del pedido).
        /// </summary>
        public List<ComponenteKitFechaEntregaAgencia> Faltan(string producto, int unidades)
        {
            var faltan = new List<ComponenteKitFechaEntregaAgencia>();
            if (unidades <= 0)
            {
                return faltan;
            }
            foreach (ComponenteKitFechaEntregaAgencia c in Componentes(producto))
            {
                int necesarias = unidades * c.Cantidad;
                int hay = Math.Max(0, Libres(c.Componente));
                if (necesarias > hay)
                {
                    faltan.Add(new ComponenteKitFechaEntregaAgencia { Kit = c.Kit, Componente = c.Componente, Cantidad = necesarias - hay });
                }
                libres[ResumenStocksProductos.Clave(c.Componente)] = Libres(c.Componente) - necesarias;
            }
            return faltan;
        }
    }

    /// <summary>
    /// NestoAPI#606 (packs montables): la partida de un pack que espera un componente, vista como la del COMPONENTE (su origen y sus fechas), para
    /// reutilizar tal cual las reglas de certeza y los textos de cada origen. Pura.
    /// </summary>
    public static class MontajeKits
    {
        /// <summary>¿Es la partida un pack montado con todo en Algete (sale como lo libre)?</summary>
        public static bool SeMontaYa(PartidaFechaEntregaAgenciaDTO p) =>
            p != null && p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE && string.IsNullOrWhiteSpace(p.ComponenteQueFalta);

        /// <summary>La partida del componente que falta (mismo producto y unidades, con el origen del componente). Null si no falta ninguno.</summary>
        public static PartidaFechaEntregaAgenciaDTO ComoComponente(PartidaFechaEntregaAgenciaDTO p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.ComponenteQueFalta) || string.IsNullOrWhiteSpace(p.OrigenComponente))
            {
                return null;
            }
            return new PartidaFechaEntregaAgenciaDTO
            {
                Origen = p.OrigenComponente,
                Producto = p.Producto,
                Unidades = p.Unidades,
                Tienda = p.Tienda,
                LlegaAAlgete = p.LlegaAAlgete,
                PuedeSalirEl = p.PuedeSalirEl,
                FechaPrevistaProveedor = p.FechaPrevistaProveedor,
                LlegadaSupuesta = p.LlegadaSupuesta,
                PedidoProveedor = p.PedidoProveedor,
                MotivoSinFecha = p.MotivoSinFecha,
                ConSeguimientoAgencia = p.ConSeguimientoAgencia,
                AgenciaSeguimiento = p.AgenciaSeguimiento,
                FechaOrientativa = p.FechaOrientativa,
                PedidoProveedorOrientativo = p.PedidoProveedorOrientativo,
                FechaPrevistaProveedorOrientativo = p.FechaPrevistaProveedorOrientativo
            };
        }

        /// <summary>«el componente 40010 ESPUMA LIMPIADORA» (o solo el código).</summary>
        public static string Componente(PartidaFechaEntregaAgenciaDTO p) =>
            string.IsNullOrWhiteSpace(p?.NombreComponente)
                ? $"el componente {p?.ComponenteQueFalta?.Trim()}"
                : $"el componente {p.ComponenteQueFalta.Trim()} {p.NombreComponente.Trim()}";

        /// <summary>De dónde llega el componente, sin fecha de salida: «viene de Reina con la reposición», «llega del pedido a proveedor 220900 (prevista el 25/11)»…</summary>
        public static string LlegadaComponente(PartidaFechaEntregaAgenciaDTO p, Func<DateTime, string> dia)
        {
            PartidaFechaEntregaAgenciaDTO c = ComoComponente(p);
            if (c == null)
            {
                return null;
            }
            switch (c.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    return $"viene de {CalculadoraFechaEntregaAgencia.NombreAlmacen(c.Tienda)} con la reposición";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return "ya viene de camino desde una tienda";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    string pedido = c.PedidoProveedor.HasValue ? $"del pedido a proveedor {c.PedidoProveedor.Value}" : "del proveedor";
                    if (c.LlegadaSupuesta.HasValue && c.FechaPrevistaProveedor.HasValue)
                    {
                        return $"llega {pedido}, cuya fecha prevista ({dia(c.FechaPrevistaProveedor.Value)}) ya pasó";
                    }
                    string llegada = TextosCertezaFechaEntrega.LlegadaProveedor(c, dia);
                    return llegada == null ? $"llega {pedido}" : $"llega {pedido}: {llegada}";
                default:
                    return null;
            }
        }
    }
}
