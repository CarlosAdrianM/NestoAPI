using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega
{
    /// <summary>
    /// NestoAPI#853 (corte 2): lo que suele tardar un proveedor desde que se le pide hasta que llega (una fila de
    /// dbo.ProveedoresPlazosEntrega, fuera del EDMX). La fila <see cref="CalculoPlazosProveedores.PROVEEDOR_GENERICO"/> lleva el
    /// plazo de todos juntos, para los proveedores sin datos suficientes.
    /// </summary>
    public class PlazoProveedor
    {
        public string Empresa { get; set; }
        public string Proveedor { get; set; }
        /// <summary>Recepciones (pedido + día de albarán) de los últimos 24 meses con plazo válido.</summary>
        public int Recepciones { get; set; }
        /// <summary>Días naturales (albarán − día en que se pidió), mediana. Null sin recepciones.</summary>
        public int? DiasMediana { get; set; }
        /// <summary>Días naturales, percentil 80 (lo prudente). Null sin recepciones.</summary>
        public int? DiasP80 { get; set; }
        /// <summary>Días entre pedidos de producto consecutivos al proveedor (último año), mediana. Null con menos de 3 pedidos.</summary>
        public int? IntervaloPedidosMediano { get; set; }
        /// <summary>Día del último pedido de producto al proveedor (últimos 24 meses).</summary>
        public DateTime? UltimoPedido { get; set; }
        public DateTime FechaCalculo { get; set; }

        public bool EsGenerico => string.Equals(Proveedor?.Trim(), CalculoPlazosProveedores.PROVEEDOR_GENERICO, StringComparison.Ordinal);

        /// <summary>¿Tiene plazo propio? Al menos <see cref="CalculoPlazosProveedores.RECEPCIONES_MINIMAS"/> recepciones.</summary>
        public bool TienePlazoPropio => !EsGenerico && Recepciones >= CalculoPlazosProveedores.RECEPCIONES_MINIMAS && DiasP80.HasValue;
    }

    /// <summary>Una recepción de compra: un pedido de compra y un día de albarán (líneas de producto).</summary>
    public class RecepcionCompra
    {
        public string Empresa { get; set; }
        public string Proveedor { get; set; }
        public int Pedido { get; set; }
        /// <summary>El día en que se pidió: CabPedidoCmp.[Fecha Modificación] (CabPedidoCmp.Fecha se mueve después).</summary>
        public DateTime FechaPedido { get; set; }
        public DateTime FechaAlbaran { get; set; }
    }

    /// <summary>Un pedido de compra con líneas de producto (para la cadencia de pedidos del proveedor).</summary>
    public class PedidoCompraDia
    {
        public string Proveedor { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#853: el cálculo nocturno de dbo.ProveedoresPlazosEntrega. Puro.</summary>
    public static class CalculoPlazosProveedores
    {
        /// <summary>La fila del plazo de todos los proveedores juntos.</summary>
        public const string PROVEEDOR_GENERICO = "*";
        public const int RECEPCIONES_MINIMAS = 5;
        public const int MESES_HISTORICO = 24;
        public const int DIAS_CADENCIA = 365;
        public const int PEDIDOS_MINIMOS_CADENCIA = 3;

        /// <param name="empresa">La empresa con la que se guardan las filas (la 1: la espejo se cuenta con ella).</param>
        public static List<PlazoProveedor> Calcular(string empresa, IEnumerable<RecepcionCompra> recepciones, IEnumerable<PedidoCompraDia> pedidos,
            DateTime ahora)
        {
            DateTime hoy = ahora.Date;
            List<(string Proveedor, int Dias)> validas = (recepciones ?? Enumerable.Empty<RecepcionCompra>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Proveedor))
                .Select(r => (Proveedor: r.Proveedor.Trim(), Dias: (r.FechaAlbaran.Date - r.FechaPedido.Date).Days))
                .Where(r => r.Dias >= 0) // pedido modificado después del albarán: el día del pedido no vale
                .ToList();
            List<PedidoCompraDia> listaPedidos = (pedidos ?? Enumerable.Empty<PedidoCompraDia>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Proveedor))
                .ToList();

            IEnumerable<string> proveedores = validas.Select(v => v.Proveedor)
                .Concat(listaPedidos.Select(p => p.Proveedor.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var filas = new List<PlazoProveedor>();
            foreach (string proveedor in proveedores.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                List<int> dias = validas.Where(v => string.Equals(v.Proveedor, proveedor, StringComparison.OrdinalIgnoreCase)).Select(v => v.Dias).ToList();
                List<DateTime> suyos = listaPedidos.Where(p => string.Equals(p.Proveedor.Trim(), proveedor, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Fecha.Date).ToList();
                filas.Add(new PlazoProveedor
                {
                    Empresa = empresa,
                    Proveedor = proveedor,
                    Recepciones = dias.Count,
                    DiasMediana = ReglasFotosFechaEntrega.Percentil(dias, 0.5),
                    DiasP80 = ReglasFotosFechaEntrega.Percentil(dias, 0.8),
                    IntervaloPedidosMediano = Cadencia(suyos, hoy),
                    UltimoPedido = suyos.Any() ? suyos.Max() : (DateTime?)null,
                    FechaCalculo = ahora
                });
            }
            List<int> todos = validas.Select(v => v.Dias).ToList();
            if (todos.Any())
            {
                filas.Add(new PlazoProveedor
                {
                    Empresa = empresa,
                    Proveedor = PROVEEDOR_GENERICO,
                    Recepciones = todos.Count,
                    DiasMediana = ReglasFotosFechaEntrega.Percentil(todos, 0.5),
                    DiasP80 = ReglasFotosFechaEntrega.Percentil(todos, 0.8),
                    FechaCalculo = ahora
                });
            }
            return filas;
        }

        /// <summary>
        /// Mediana de los días entre pedidos consecutivos del último año (días distintos: dos pedidos el mismo día son una vez que se
        /// pide). Null con menos de <see cref="PEDIDOS_MINIMOS_CADENCIA"/> días de pedido.
        /// </summary>
        internal static int? Cadencia(IEnumerable<DateTime> fechas, DateTime hoy)
        {
            List<DateTime> dias = (fechas ?? Enumerable.Empty<DateTime>())
                .Select(f => f.Date)
                .Where(f => f > hoy.AddDays(-DIAS_CADENCIA) && f <= hoy)
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            if (dias.Count < PEDIDOS_MINIMOS_CADENCIA)
            {
                return null;
            }
            List<int> intervalos = dias.Skip(1).Select((f, i) => (f - dias[i]).Days).ToList();
            return ReglasFotosFechaEntrega.Percentil(intervalos, 0.5);
        }
    }

    /// <summary>Los plazos leídos de la tabla, por proveedor, con la fila genérica aparte.</summary>
    public class PlazosProveedores
    {
        private readonly Dictionary<string, PlazoProveedor> porProveedor;

        public PlazosProveedores(IEnumerable<PlazoProveedor> filas)
        {
            List<PlazoProveedor> lista = (filas ?? Enumerable.Empty<PlazoProveedor>()).Where(f => f != null && f.Proveedor != null).ToList();
            Generico = lista.FirstOrDefault(f => f.EsGenerico);
            porProveedor = lista.Where(f => !f.EsGenerico)
                .GroupBy(f => f.Proveedor.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public PlazoProveedor Generico { get; }

        /// <summary>¿Hay algo con lo que calcular? (la tabla existe y tiene filas).</summary>
        public bool TieneDatos => Generico != null || porProveedor.Any();

        /// <summary>La fila del proveedor (tenga o no plazo propio), o null.</summary>
        public PlazoProveedor DeProveedor(string proveedor) =>
            !string.IsNullOrWhiteSpace(proveedor) && porProveedor.TryGetValue(proveedor.Trim(), out PlazoProveedor plazo) ? plazo : null;

        public IReadOnlyCollection<PlazoProveedor> Todos => porProveedor.Values.Concat(Generico == null ? new PlazoProveedor[0] : new[] { Generico }).ToList();
    }

    /// <summary>Qué fecha de recepción se pone al ENVIAR un pedido de compra, y cuál habría dado la regla.</summary>
    public class FechaRecepcionEstimada
    {
        public const string ORIGEN_PLAZO_PROVEEDOR = "PlazoProveedor";
        public const string ORIGEN_DIAS_EN_SERVIR = "DiasEnServir";
        public const string ORIGEN_PLAZO_GENERICO = "PlazoGenerico";
        public const string ORIGEN_HOY = "Hoy";
        /// <summary>NestoAPI#853: manda el modelo (Compras.FechaRecepcionQuienManda = «Modelo»).</summary>
        public const string ORIGEN_MODELO = "Modelo";
        /// <summary>NestoAPI#853: manda el modelo, pero hoy + DíasEnServir era más tarde.</summary>
        public const string ORIGEN_MODELO_DIAS_EN_SERVIR = "ModeloDiasEnServir";

        public DateTime Fecha { get; set; }
        /// <summary>Hoy + P80 del proveedor (o el genérico si no tiene plazo propio), aunque no se haya usado. Null sin plazos.</summary>
        public DateTime? FechaRegla { get; set; }
        /// <summary>NestoAPI#853: hoy + días prudentes del modelo ML.NET, aunque no se haya usado. Null sin modelo o si falla o tarda.</summary>
        public DateTime? FechaModelo { get; set; }
        public string Origen { get; set; }
    }

    /// <summary>
    /// NestoAPI#853 (corte 2): la fecha de recepción de un pedido de compra al enviarlo. Pura.
    /// <list type="number">
    /// <item>Proveedor con plazo propio (≥ <see cref="CalculoPlazosProveedores.RECEPCIONES_MINIMAS"/> recepciones): hoy + su P80, salvo que
    /// <c>DíasEnServir</c> del pedido sea mayor (el dato a mano de Compras manda si es MÁS prudente).</item>
    /// <item>Si no, hoy + <c>DíasEnServir</c> si es mayor que 0.</item>
    /// <item>Si no, hoy + el P80 genérico.</item>
    /// <item>Sin plazos: hoy + <c>DíasEnServir</c> (lo de siempre).</item>
    /// </list>
    /// </summary>
    public static class ReglaFechaRecepcionEstimada
    {
        public static FechaRecepcionEstimada Calcular(DateTime hoy, int diasEnServir, PlazoProveedor propio, PlazoProveedor generico)
        {
            hoy = hoy.Date;
            int dias = Math.Max(0, diasEnServir);
            bool conPropio = propio != null && propio.TienePlazoPropio;
            int? p80Generico = generico?.DiasP80;
            var resultado = new FechaRecepcionEstimada
            {
                FechaRegla = conPropio ? hoy.AddDays(propio.DiasP80.Value) : p80Generico.HasValue ? hoy.AddDays(p80Generico.Value) : (DateTime?)null
            };
            if (conPropio)
            {
                bool mandaCompras = dias > propio.DiasP80.Value;
                resultado.Fecha = hoy.AddDays(mandaCompras ? dias : propio.DiasP80.Value);
                resultado.Origen = mandaCompras ? FechaRecepcionEstimada.ORIGEN_DIAS_EN_SERVIR : FechaRecepcionEstimada.ORIGEN_PLAZO_PROVEEDOR;
            }
            else if (dias > 0)
            {
                resultado.Fecha = hoy.AddDays(dias);
                resultado.Origen = FechaRecepcionEstimada.ORIGEN_DIAS_EN_SERVIR;
            }
            else if (p80Generico.HasValue)
            {
                resultado.Fecha = hoy.AddDays(p80Generico.Value);
                resultado.Origen = FechaRecepcionEstimada.ORIGEN_PLAZO_GENERICO;
            }
            else
            {
                resultado.Fecha = hoy;
                resultado.Origen = FechaRecepcionEstimada.ORIGEN_HOY;
            }
            return resultado;
        }
    }

    /// <summary>
    /// NestoAPI#853 (caso 1): cuándo podría LLEGAR lo que aún no se ha pedido al proveedor. Pura. Llegada = hoy + días hasta el próximo
    /// pedido a ese proveedor (max(1, cadencia − días desde el último)) + su P80 (o el genérico). Sin cadencia conocida, no hay estimación.
    /// Es orientativa: no entra en ninguna fecha prometida.
    /// </summary>
    public static class ReglaFechaOrientativaSinPedir
    {
        public static DateTime? LlegadaEstimada(DateTime hoy, PlazoProveedor propio, PlazoProveedor generico)
        {
            hoy = hoy.Date;
            if (propio?.IntervaloPedidosMediano == null || !propio.UltimoPedido.HasValue)
            {
                return null;
            }
            int? plazo = propio.TienePlazoPropio ? propio.DiasP80 : generico?.DiasP80;
            if (!plazo.HasValue)
            {
                return null;
            }
            int desdeElUltimo = Math.Max(0, (hoy - propio.UltimoPedido.Value.Date).Days);
            int hastaElPedido = Math.Max(1, propio.IntervaloPedidosMediano.Value - desdeElUltimo);
            return hoy.AddDays(hastaElPedido + plazo.Value);
        }
    }
}
