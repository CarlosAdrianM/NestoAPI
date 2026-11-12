using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos
{
    /// <summary>
    /// NestoAPI#853 (modelo en sombra): un pedido de compra con líneas de producto (TipoLínea '1') tal como lo lee de noche
    /// <see cref="RepositorioModeloPlazos.SQL_HISTORICO"/>. El día del envío es CabPedidoCmp.[Fecha Modificación] (como la regla).
    /// </summary>
    public class PedidoCompraHistorico
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public DateTime FechaEnvio { get; set; }
        public int DiasEnServir { get; set; }
        /// <summary>Líneas de producto.</summary>
        public int Lineas { get; set; }
        /// <summary>Suma de la base imponible de las líneas de producto.</summary>
        public double Importe { get; set; }
        /// <summary>Días distintos de albarán de sus líneas de producto (vacía si no ha llegado nada).</summary>
        public List<DateTime> Albaranes { get; set; } = new List<DateTime>();

        [JsonIgnore]
        public DateTime? PrimerAlbaran => Albaranes == null || Albaranes.Count == 0 ? (DateTime?)null : Albaranes.Min().Date;

        /// <summary>Días naturales del envío al primer albarán (la etiqueta). Null si no ha llegado.</summary>
        [JsonIgnore]
        public int? Dias => PrimerAlbaran.HasValue ? (PrimerAlbaran.Value - FechaEnvio.Date).Days : (int?)null;

        /// <summary>Ha llegado y el plazo es creíble: de 0 a <see cref="ConstructorRasgosPlazos.MAX_DIAS"/> días.</summary>
        [JsonIgnore]
        public bool EtiquetaValida => Dias.HasValue && Dias.Value >= 0 && Dias.Value <= ConstructorRasgosPlazos.MAX_DIAS;

        internal bool EsElMismo(string empresa, int? pedido)
            => pedido.HasValue && Pedido == pedido.Value && string.Equals(Empresa?.Trim(), empresa?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lo que el modelo recibe (ML.NET): todo conocido el día del envío.</summary>
    public class RasgosPlazo
    {
        /// <summary>Proveedor (categórico); los de pocos pedidos van como <see cref="MapaProveedoresPlazos.OTROS"/>.</summary>
        public string Proveedor { get; set; }
        /// <summary>0 = domingo … 6 = sábado.</summary>
        public float DiaSemana { get; set; }
        public float Mes { get; set; }
        public float Lineas { get; set; }
        /// <summary>log(1 + importe).</summary>
        public float LogImporte { get; set; }
        public float DiasEnServir { get; set; }
        /// <summary>Plazos conocidos del proveedor antes del envío (como mucho <see cref="ConstructorRasgosPlazos.ULTIMOS_N"/>).</summary>
        public float HistCuantos { get; set; }
        /// <summary>Mediana de sus últimos plazos conocidos; -1 sin historia.</summary>
        public float HistMediana { get; set; }
        /// <summary>P80 de sus últimos plazos conocidos; -1 sin historia.</summary>
        public float HistP80 { get; set; }
        /// <summary>Plazo del último pedido suyo ya recibido; -1 sin historia.</summary>
        public float HistUltimo { get; set; }
        /// <summary>Días desde su pedido anterior; -1 si no hay.</summary>
        public float DiasDesdeAnterior { get; set; }
        /// <summary>Pedidos suyos de los últimos <see cref="ConstructorRasgosPlazos.DIAS_EN_EL_AIRE"/> días aún sin llegar ese día.</summary>
        public float EnElAire { get; set; }
    }

    /// <summary>Fila de entrenamiento: los rasgos y la etiqueta en log(1 + días).</summary>
    public class FilaPlazo : RasgosPlazo
    {
        public float Label { get; set; }
    }

    /// <summary>Salida del regresor (log(1 + días) medio).</summary>
    public class PrediccionPlazo
    {
        public float Score { get; set; }
    }

    /// <summary>Un ejemplo: el pedido, sus rasgos (con el proveedor sin agrupar), los días reales y los de la regla «como si».</summary>
    public class EjemploPlazo
    {
        public PedidoCompraHistorico Pedido { get; set; }
        public RasgosPlazo Rasgos { get; set; }
        public int Dias { get; set; }
        /// <summary>Días de la regla del P80 recalculada con lo anterior al envío (solo se calcula en validación). Null sin datos.</summary>
        public int? DiasRegla { get; set; }
        public DateTime FechaEnvio => Pedido.FechaEnvio.Date;
    }

    /// <summary>
    /// NestoAPI#853: construcción de los rasgos del modelo de plazos. PURA. Regla de oro: ningún rasgo usa nada posterior al envío
    /// (solo pedidos del proveedor enviados ANTES de ese día y solo plazos cuyo primer albarán fue ANTES de ese día).
    /// </summary>
    public static class ConstructorRasgosPlazos
    {
        public const int MAX_DIAS = 120;
        public const int ULTIMOS_N = 10;
        public const int DIAS_EN_EL_AIRE = 120;

        /// <summary>Un ejemplo por pedido recibido con etiqueta válida, en orden de envío.</summary>
        public static List<EjemploPlazo> Construir(IEnumerable<PedidoCompraHistorico> pedidos)
        {
            var ejemplos = new List<EjemploPlazo>();
            foreach (IGrouping<string, PedidoCompraHistorico> grupo in Limpios(pedidos).GroupBy(p => p.Proveedor.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                List<PedidoCompraHistorico> delProveedor = grupo.ToList();
                foreach (PedidoCompraHistorico pedido in delProveedor.Where(p => p.EtiquetaValida))
                {
                    ejemplos.Add(new EjemploPlazo
                    {
                        Pedido = pedido,
                        Dias = pedido.Dias.Value,
                        Rasgos = Rasgos(delProveedor, grupo.Key, pedido.FechaEnvio, pedido.DiasEnServir, pedido.Lineas, pedido.Importe, pedido.Empresa, pedido.Pedido)
                    });
                }
            }
            return ejemplos.OrderBy(e => e.FechaEnvio).ThenBy(e => e.Pedido.Empresa).ThenBy(e => e.Pedido.Pedido).ToList();
        }

        /// <summary>
        /// Los rasgos de un envío del día <paramref name="fechaEnvio"/>. <paramref name="delProveedor"/> puede traer cualquier cosa
        /// (también pedidos posteriores o el propio pedido): solo se mira lo anterior a ese día.
        /// </summary>
        public static RasgosPlazo Rasgos(IEnumerable<PedidoCompraHistorico> delProveedor, string proveedor, DateTime fechaEnvio, int diasEnServir,
            int lineas, double importe, string empresa = null, int? pedido = null)
        {
            DateTime dia = fechaEnvio.Date;
            List<PedidoCompraHistorico> anteriores = (delProveedor ?? Enumerable.Empty<PedidoCompraHistorico>())
                .Where(p => p != null && p.FechaEnvio.Date < dia && !p.EsElMismo(empresa, pedido))
                .ToList();
            List<PedidoCompraHistorico> conocidos = anteriores
                .Where(p => p.EtiquetaValida && p.PrimerAlbaran.Value < dia)
                .OrderByDescending(p => p.FechaEnvio).ThenByDescending(p => p.Pedido)
                .ToList();
            List<int> ultimos = conocidos.Take(ULTIMOS_N).Select(p => p.Dias.Value).ToList();

            return new RasgosPlazo
            {
                Proveedor = proveedor?.Trim(),
                DiaSemana = (int)dia.DayOfWeek,
                Mes = dia.Month,
                Lineas = Math.Max(0, lineas),
                LogImporte = (float)Math.Log(1 + Math.Max(0, importe)),
                DiasEnServir = Math.Max(0, diasEnServir),
                HistCuantos = ultimos.Count,
                HistMediana = ReglasFotosFechaEntrega.Percentil(ultimos, 0.5) ?? -1,
                HistP80 = ReglasFotosFechaEntrega.Percentil(ultimos, 0.8) ?? -1,
                HistUltimo = conocidos.Any() ? conocidos[0].Dias.Value : -1,
                DiasDesdeAnterior = anteriores.Any() ? (dia - anteriores.Max(p => p.FechaEnvio.Date)).Days : -1,
                EnElAire = anteriores.Count(p => p.FechaEnvio.Date >= dia.AddDays(-DIAS_EN_EL_AIRE) && (!p.PrimerAlbaran.HasValue || p.PrimerAlbaran.Value >= dia))
            };
        }

        /// <summary>
        /// Lo que se guarda junto al modelo para calcular los rasgos al enviar: de cada proveedor sus <see cref="ULTIMOS_N"/> × 2 últimos
        /// pedidos recibidos con plazo válido y todos los de los últimos <see cref="DIAS_EN_EL_AIRE"/> días (lleguen o no).
        /// </summary>
        public static List<PedidoCompraHistorico> Recortar(IEnumerable<PedidoCompraHistorico> pedidos, DateTime hoy)
        {
            DateTime desdeAire = hoy.Date.AddDays(-DIAS_EN_EL_AIRE - 10);
            var resultado = new List<PedidoCompraHistorico>();
            foreach (IGrouping<string, PedidoCompraHistorico> grupo in Limpios(pedidos).GroupBy(p => p.Proveedor.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<PedidoCompraHistorico> recibidos = grupo.Where(p => p.EtiquetaValida)
                    .OrderByDescending(p => p.FechaEnvio).ThenByDescending(p => p.Pedido).Take(ULTIMOS_N * 2);
                IEnumerable<PedidoCompraHistorico> recientes = grupo.Where(p => p.FechaEnvio.Date >= desdeAire);
                resultado.AddRange(recibidos.Concat(recientes).Distinct());
            }
            return resultado;
        }

        private static IEnumerable<PedidoCompraHistorico> Limpios(IEnumerable<PedidoCompraHistorico> pedidos)
            => (pedidos ?? Enumerable.Empty<PedidoCompraHistorico>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Proveedor));
    }

    /// <summary>
    /// NestoAPI#853: la regla del P80 «como si» se hubiera aplicado el día de cada envío (para comparar con el modelo en validación). PURA.
    /// Mismo cálculo que el job nocturno: recepciones (pedido + día de albarán, días ≥ 0) de los 24 meses anteriores, pero SOLO las de
    /// albarán anterior al envío; P80 propio con ≥ 5 recepciones y, si no, el genérico (<see cref="ReglaFechaRecepcionEstimada"/>.FechaRegla).
    /// </summary>
    public class ReglaPlazosComoSi
    {
        private readonly List<(string Proveedor, DateTime Albaran, int Dias)> recepciones;

        public ReglaPlazosComoSi(IEnumerable<PedidoCompraHistorico> pedidos)
        {
            recepciones = (pedidos ?? Enumerable.Empty<PedidoCompraHistorico>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Proveedor) && p.Albaranes != null)
                .SelectMany(p => p.Albaranes.Select(a => a.Date).Distinct()
                    .Select(a => (Proveedor: p.Proveedor.Trim(), Albaran: a, Dias: (a - p.FechaEnvio.Date).Days)))
                .Where(r => r.Dias >= 0)
                .OrderBy(r => r.Albaran)
                .ToList();
        }

        /// <summary>Días (FechaRegla − día del envío) o null sin ningún plazo.</summary>
        public int? Dias(string proveedor, DateTime fechaEnvio, int diasEnServir)
        {
            DateTime dia = fechaEnvio.Date;
            DateTime desde = dia.AddMonths(-CalculoPlazosProveedores.MESES_HISTORICO);
            var todos = new List<int>();
            var suyos = new List<int>();
            string clave = proveedor?.Trim();
            foreach ((string Proveedor, DateTime Albaran, int Dias) r in recepciones)
            {
                if (r.Albaran >= dia)
                {
                    break;
                }
                if (r.Albaran < desde)
                {
                    continue;
                }
                todos.Add(r.Dias);
                if (string.Equals(r.Proveedor, clave, StringComparison.OrdinalIgnoreCase))
                {
                    suyos.Add(r.Dias);
                }
            }
            PlazoProveedor propio = new PlazoProveedor { Proveedor = clave, Recepciones = suyos.Count, DiasP80 = ReglasFotosFechaEntrega.Percentil(suyos, 0.8) };
            PlazoProveedor generico = todos.Any()
                ? new PlazoProveedor { Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = todos.Count, DiasP80 = ReglasFotosFechaEntrega.Percentil(todos, 0.8) }
                : null;
            DateTime? fechaRegla = ReglaFechaRecepcionEstimada.Calcular(dia, diasEnServir, propio, generico).FechaRegla;
            return fechaRegla.HasValue ? (fechaRegla.Value - dia).Days : (int?)null;
        }
    }

    /// <summary>Proveedor → categoría del modelo: los que tienen menos de <see cref="MINIMO_PEDIDOS"/> ejemplos de entrenamiento van juntos.</summary>
    public class MapaProveedoresPlazos
    {
        public const string OTROS = "(otros)";
        public const int MINIMO_PEDIDOS = 10;
        private readonly HashSet<string> conocidos;

        public MapaProveedoresPlazos(IEnumerable<string> conocidos)
        {
            this.conocidos = new HashSet<string>((conocidos ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        public static MapaProveedoresPlazos Desde(IEnumerable<EjemploPlazo> entrenamiento)
            => new MapaProveedoresPlazos((entrenamiento ?? Enumerable.Empty<EjemploPlazo>())
                .GroupBy(e => e.Rasgos.Proveedor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= MINIMO_PEDIDOS)
                .Select(g => g.Key));

        public IReadOnlyCollection<string> Conocidos => conocidos.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

        public string Mapear(string proveedor)
            => !string.IsNullOrWhiteSpace(proveedor) && conocidos.Contains(proveedor.Trim()) ? proveedor.Trim() : OTROS;

        /// <summary>Copia de los rasgos con el proveedor ya agrupado (la entrada del modelo).</summary>
        public RasgosPlazo Aplicar(RasgosPlazo r) => Copiar(r, new RasgosPlazo(), Mapear(r.Proveedor));

        public FilaPlazo Fila(EjemploPlazo e)
        {
            var fila = (FilaPlazo)Copiar(e.Rasgos, new FilaPlazo(), Mapear(e.Rasgos.Proveedor));
            fila.Label = (float)Math.Log(1 + e.Dias);
            return fila;
        }

        private static RasgosPlazo Copiar(RasgosPlazo origen, RasgosPlazo destino, string proveedor)
        {
            destino.Proveedor = proveedor;
            destino.DiaSemana = origen.DiaSemana;
            destino.Mes = origen.Mes;
            destino.Lineas = origen.Lineas;
            destino.LogImporte = origen.LogImporte;
            destino.DiasEnServir = origen.DiasEnServir;
            destino.HistCuantos = origen.HistCuantos;
            destino.HistMediana = origen.HistMediana;
            destino.HistP80 = origen.HistP80;
            destino.HistUltimo = origen.HistUltimo;
            destino.DiasDesdeAnterior = origen.DiasDesdeAnterior;
            destino.EnElAire = origen.EnElAire;
            return destino;
        }
    }
}
