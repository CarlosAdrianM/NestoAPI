using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>Un día con compra del cliente (un pedido = una fila; varios pedidos el mismo día = un solo día con compra).</summary>
    public class CompraAbandono
    {
        public DateTime Dia { get; set; }
        public decimal Importe { get; set; }
        /// <summary>true si aún no está facturado (pendiente, en curso o albarán): cuenta como día con compra pero no como importe facturado.</summary>
        public bool EnCurso { get; set; }
    }

    /// <summary>Lo último comprado de cada grupo de producto (PEL, COS, APA…) y cuántos pedidos lo llevan en 12 meses.</summary>
    public class CompraGrupoAbandono
    {
        public string Grupo { get; set; }
        public DateTime UltimaCompra { get; set; }
        public int Pedidos12Meses { get; set; }
    }

    /// <summary>
    /// NestoAPI#714: todo lo que la regla y las features necesitan de un cliente de la cartera, leído a fecha <c>d</c> (nada
    /// posterior: sin fuga). Lo llena <see cref="RepositorioRiesgoAbandonoSql"/>; <see cref="CalculadoraAbandono"/> solo calcula.
    /// </summary>
    public class DatosClienteAbandono
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>El vendedor de cuya cartera de llamadas sale (el de la ficha o el del grupo de producto, NestoAPI#620).</summary>
        public string Vendedor { get; set; }
        /// <summary>true si lo lleva por la ficha (cuentan todas las compras salvo las de grupos de otro vendedor); false, solo por grupo.</summary>
        public bool TodosLosGrupos { get; set; }

        /// <summary>Compras de los últimos 12 meses (hasta <c>d</c> incluido): facturadas, más las no facturadas de los últimos 60 días.</summary>
        public List<CompraAbandono> Compras { get; set; } = new List<CompraAbandono>();
        public List<CompraGrupoAbandono> PorGrupo { get; set; } = new List<CompraGrupoAbandono>();
        /// <summary>Pedidos facturados con alguna compra en 24 meses (para la antigüedad mínima conocida).</summary>
        public int Pedidos24Meses { get; set; }
        public DateTime? PrimeraCompra24Meses { get; set; }

        public int Devoluciones12Meses { get; set; }
        public decimal ImporteDevoluciones12Meses { get; set; }

        /// <summary>Apuntes de ExtractoCliente con TipoApunte 4 (impagados) en 12 meses, del cliente (todos sus contactos).</summary>
        public int Impagos12Meses { get; set; }
        /// <summary>ImportePdte de los apuntes con FechaVto anterior a <c>d</c>.</summary>
        public decimal ImporteVencido { get; set; }
        public decimal ImportePendiente { get; set; }

        /// <summary>Rapports (T/V/W) del cliente que cuentan para este vendedor (sin los del otro vendedor del cliente, como la cartera).</summary>
        public int Contactos12Meses { get; set; }
        public int NoContactados12Meses { get; set; }
        public int Contactos3Meses { get; set; }
        public int NoContactados3Meses { get; set; }
        public int RapportsConPedido12Meses { get; set; }
        public DateTime? UltimoContacto { get; set; }
        public DateTime? UltimoIntento { get; set; }

        /// <summary>Último cambio de Clientes.Vendedor (LogModificaciones) anterior a <c>d</c>; null si no consta.</summary>
        public DateTime? UltimoCambioVendedor { get; set; }

        public int Estado { get; set; }
        public string Grupo { get; set; }
        public string Provincia { get; set; }
        public int? Empleados { get; set; }
    }

    /// <summary>
    /// NestoAPI#714: las features que se guardan en el JSON de cada fila (versión <see cref="VERSION"/>). Todas a fecha de la
    /// instantánea y sin nada posterior (sin fuga). Son las baratas de calcular hoy; las que faltan (productos habituales sin
    /// comprar, incidencias de envío, Resultado del rapport) entran en el corte 2 con la CalculadoraFeaturesAbandono común.
    /// </summary>
    public class FeaturesAbandono
    {
        public const string VERSION = "features-1";

        public string Version { get; set; } = VERSION;
        public DateTime Instantanea { get; set; }

        // Recencia (relativa a la cadencia)
        public int Cadencia { get; set; }
        public int DiasSinComprar { get; set; }
        public double RatioRecencia { get; set; }
        public int Horizonte { get; set; }
        /// <summary>Dispersión de la cadencia: diferencia entre el cuartil 3 y el 1 de los días entre compras (0 con menos de 4 huecos).</summary>
        public int RangoIntercuartilCadencia { get; set; }
        public int MaxDiasEntreCompras { get; set; }

        // Frecuencia
        public int DiasConCompra12Meses { get; set; }
        public int DiasConCompra3Meses { get; set; }
        public int DiasConCompra3MesesAnteriores { get; set; }
        public int DiasConCompraMismoMesAnoAnterior { get; set; }
        public int Pedidos24Meses { get; set; }
        public int? MesesDesdePrimeraCompra24Meses { get; set; }

        // Importe
        public decimal Importe12Meses { get; set; }
        public decimal Importe3Meses { get; set; }
        public decimal Importe3MesesAnteriores { get; set; }
        public decimal ImporteMedioPedido12Meses { get; set; }
        public decimal ImporteUltimaCompraFacturada { get; set; }
        public bool PedidoEnCurso { get; set; }

        // Mezcla de grupos
        public int GruposDistintos12Meses { get; set; }
        public Dictionary<string, int> DiasSinComprarPorGrupo { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> PedidosPorGrupo12Meses { get; set; } = new Dictionary<string, int>();

        // Devoluciones e impagos
        public int Devoluciones12Meses { get; set; }
        public decimal ImporteDevoluciones12Meses { get; set; }
        public int Impagos12Meses { get; set; }
        public decimal ImporteVencido { get; set; }
        public decimal ImportePendiente { get; set; }

        // Rapports
        public int Contactos12Meses { get; set; }
        public int NoContactados12Meses { get; set; }
        public int Contactos3Meses { get; set; }
        public int NoContactados3Meses { get; set; }
        public int RapportsConPedido12Meses { get; set; }
        public double? ConversionRapports12Meses { get; set; }
        public int? DiasDesdeUltimoContacto { get; set; }
        public int? DiasDesdeUltimoIntento { get; set; }

        // Vendedor y perfil
        public int? DiasDesdeCambioVendedor { get; set; }
        public bool TodosLosGrupos { get; set; }
        public int Estado { get; set; }
        public string Grupo { get; set; }
        public string Provincia { get; set; }
        public int? Empleados { get; set; }
        public int Mes { get; set; }
    }

    /// <summary>Lo que sale de evaluar un cliente un día: si es comprador regular, su cadencia, su horizonte y si la regla lo marca.</summary>
    public class EvaluacionAbandono
    {
        public bool EsCompradorRegular { get; set; }
        public int DiasConCompra12Meses { get; set; }
        public int Cadencia { get; set; }
        public int DiasSinComprar { get; set; }
        public int Horizonte { get; set; }
        public DateTime FechaLimite { get; set; }
        /// <summary>La regla baseline: <see cref="CalculadoraAbandono.Baseline"/>.</summary>
        public bool Baseline { get; set; }
        public string Motivo { get; set; }
        public FeaturesAbandono Features { get; set; }
        public string FeaturesJson { get; set; }
    }

    /// <summary>
    /// NestoAPI#714: las funciones puras de la etiqueta (compartidas por el job nocturno y, en el corte 2, por el entrenamiento).
    /// <list type="bullet">
    /// <item><b>Cadencia</b>: mediana de los días entre días con compra en los 12 meses anteriores a <c>d</c>, mínimo <see cref="CADENCIA_MINIMA"/>.</item>
    /// <item><b>Horizonte H</b>: clamp(3 × cadencia, 60, 180).</item>
    /// <item><b>Comprador regular</b>: <see cref="MIN_DIAS_CON_COMPRA"/> o más días con compra en 12 meses y última compra hace H días o
    /// menos (el que lleva más de H ya se ha ido: es lo que la etiqueta llama abandono, no un riesgo que detectar).</item>
    /// <item><b>Baseline</b>: díasSinComprar &gt; 1,5 × cadencia. Los regulares que NO marca la regla («en plazo», última compra ≤ 1,5 ×
    /// cadencia) son la población estricta de la issue para entrenar el modelo (corte 2); los que marca son la alerta de hoy.</item>
    /// </list>
    /// </summary>
    public static class CalculadoraAbandono
    {
        public const int MESES_VENTANA = 12;
        public const int MIN_DIAS_CON_COMPRA = 4;
        public const int CADENCIA_MINIMA = 7;
        public const double FACTOR_BASELINE = 1.5;
        public const int FACTOR_HORIZONTE = 3;
        public const int HORIZONTE_MINIMO = 60;
        public const int HORIZONTE_MAXIMO = 180;
        /// <summary>Qué regla ha puesto Baseline y Motivo en la fila (el corte 3 pondrá la versión del zip de ML.NET en Probabilidad).</summary>
        public const string VERSION_REGLA = "baseline-1";

        /// <summary>
        /// Mediana (redondeada, mitad hacia arriba) de los días entre días con compra distintos, con el mínimo
        /// <see cref="CADENCIA_MINIMA"/>. Con menos de dos días con compra no hay cadencia (null).
        /// </summary>
        public static int? Cadencia(IEnumerable<DateTime> diasConCompra)
        {
            List<int> huecos = Huecos(diasConCompra);
            if (huecos.Count == 0)
            {
                return null;
            }
            int mediana = (int)Math.Round(Mediana(huecos), MidpointRounding.AwayFromZero);
            return Math.Max(CADENCIA_MINIMA, mediana);
        }

        public static int Horizonte(int cadencia)
            => Math.Min(HORIZONTE_MAXIMO, Math.Max(HORIZONTE_MINIMO, FACTOR_HORIZONTE * cadencia));

        public static bool EsCompradorRegular(int diasConCompra12Meses, int diasSinComprar, int horizonte)
            => diasConCompra12Meses >= MIN_DIAS_CON_COMPRA && diasSinComprar <= horizonte;

        public static bool Baseline(int diasSinComprar, int cadencia)
            => diasSinComprar > FACTOR_BASELINE * cadencia;

        /// <summary>«Suele pedir cada 21 días y lleva 52 sin pedir».</summary>
        public static string Motivo(int cadencia, int diasSinComprar)
            => $"Suele pedir cada {cadencia} días y lleva {diasSinComprar} sin pedir";

        /// <summary>
        /// Evalúa un cliente el día <paramref name="hoy"/> con lo leído hasta ese día. Si no es comprador regular,
        /// <see cref="EvaluacionAbandono.EsCompradorRegular"/> = false y lo demás vale lo que se haya podido calcular.
        /// </summary>
        public static EvaluacionAbandono Evaluar(DatosClienteAbandono datos, DateTime hoy)
        {
            DateTime d = hoy.Date;
            DateTime desde12 = d.AddMonths(-MESES_VENTANA);
            List<CompraAbandono> compras = (datos.Compras ?? new List<CompraAbandono>())
                .Where(c => c.Dia.Date > desde12 && c.Dia.Date <= d)
                .ToList();
            List<DateTime> dias = compras.Select(c => c.Dia.Date).Distinct().OrderBy(x => x).ToList();

            var evaluacion = new EvaluacionAbandono { DiasConCompra12Meses = dias.Count };
            if (dias.Count == 0)
            {
                return evaluacion;
            }
            int? cadencia = Cadencia(dias);
            if (cadencia == null)
            {
                return evaluacion;
            }
            evaluacion.Cadencia = cadencia.Value;
            evaluacion.DiasSinComprar = (d - dias.Last()).Days;
            evaluacion.Horizonte = Horizonte(cadencia.Value);
            evaluacion.FechaLimite = d.AddDays(evaluacion.Horizonte);
            evaluacion.EsCompradorRegular = EsCompradorRegular(dias.Count, evaluacion.DiasSinComprar, evaluacion.Horizonte);
            evaluacion.Baseline = Baseline(evaluacion.DiasSinComprar, cadencia.Value);
            evaluacion.Motivo = Motivo(cadencia.Value, evaluacion.DiasSinComprar);
            if (evaluacion.EsCompradorRegular)
            {
                evaluacion.Features = ConstruirFeatures(datos, compras, dias, d, cadencia.Value, evaluacion.DiasSinComprar, evaluacion.Horizonte);
                evaluacion.FeaturesJson = Serializar(evaluacion.Features);
            }
            return evaluacion;
        }

        internal static FeaturesAbandono ConstruirFeatures(DatosClienteAbandono datos, List<CompraAbandono> compras, List<DateTime> dias,
            DateTime d, int cadencia, int diasSinComprar, int horizonte)
        {
            DateTime hace3 = d.AddMonths(-3);
            DateTime hace6 = d.AddMonths(-6);
            DateTime hace12 = d.AddMonths(-12);
            DateTime hace11 = d.AddMonths(-11);
            List<int> huecos = Huecos(dias);
            List<CompraAbandono> facturadas = compras.Where(c => !c.EnCurso).ToList();
            int pedidosFacturados = facturadas.Count;
            decimal importe12 = facturadas.Sum(c => c.Importe);
            DateTime? ultimaFacturada = facturadas.Any() ? facturadas.Max(c => c.Dia.Date) : (DateTime?)null;
            int contactos12 = datos.Contactos12Meses;

            var features = new FeaturesAbandono
            {
                Instantanea = d,
                Cadencia = cadencia,
                DiasSinComprar = diasSinComprar,
                RatioRecencia = Math.Round((double)diasSinComprar / cadencia, 3),
                Horizonte = horizonte,
                RangoIntercuartilCadencia = RangoIntercuartil(huecos),
                MaxDiasEntreCompras = huecos.Count == 0 ? 0 : huecos.Max(),

                DiasConCompra12Meses = dias.Count,
                DiasConCompra3Meses = dias.Count(x => x > hace3),
                DiasConCompra3MesesAnteriores = dias.Count(x => x > hace6 && x <= hace3),
                DiasConCompraMismoMesAnoAnterior = dias.Count(x => x > hace12 && x <= hace11),
                Pedidos24Meses = datos.Pedidos24Meses,
                MesesDesdePrimeraCompra24Meses = datos.PrimeraCompra24Meses.HasValue ? MesesEntre(datos.PrimeraCompra24Meses.Value.Date, d) : (int?)null,

                Importe12Meses = importe12,
                Importe3Meses = facturadas.Where(c => c.Dia.Date > hace3).Sum(c => c.Importe),
                Importe3MesesAnteriores = facturadas.Where(c => c.Dia.Date > hace6 && c.Dia.Date <= hace3).Sum(c => c.Importe),
                ImporteMedioPedido12Meses = pedidosFacturados == 0 ? 0 : Math.Round(importe12 / pedidosFacturados, 2),
                ImporteUltimaCompraFacturada = ultimaFacturada.HasValue ? facturadas.Where(c => c.Dia.Date == ultimaFacturada.Value).Sum(c => c.Importe) : 0,
                PedidoEnCurso = compras.Any(c => c.EnCurso),

                GruposDistintos12Meses = (datos.PorGrupo ?? new List<CompraGrupoAbandono>()).Select(g => g.Grupo?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Distinct().Count(),

                Devoluciones12Meses = datos.Devoluciones12Meses,
                ImporteDevoluciones12Meses = datos.ImporteDevoluciones12Meses,
                Impagos12Meses = datos.Impagos12Meses,
                ImporteVencido = datos.ImporteVencido,
                ImportePendiente = datos.ImportePendiente,

                Contactos12Meses = contactos12,
                NoContactados12Meses = datos.NoContactados12Meses,
                Contactos3Meses = datos.Contactos3Meses,
                NoContactados3Meses = datos.NoContactados3Meses,
                RapportsConPedido12Meses = datos.RapportsConPedido12Meses,
                ConversionRapports12Meses = contactos12 == 0 ? (double?)null : Math.Round((double)datos.RapportsConPedido12Meses / contactos12, 3),
                DiasDesdeUltimoContacto = DiasDesde(datos.UltimoContacto, d),
                DiasDesdeUltimoIntento = DiasDesde(datos.UltimoIntento, d),

                DiasDesdeCambioVendedor = DiasDesde(datos.UltimoCambioVendedor, d),
                TodosLosGrupos = datos.TodosLosGrupos,
                Estado = datos.Estado,
                Grupo = datos.Grupo?.Trim(),
                Provincia = datos.Provincia?.Trim(),
                Empleados = datos.Empleados,
                Mes = d.Month
            };
            foreach (CompraGrupoAbandono grupo in (datos.PorGrupo ?? new List<CompraGrupoAbandono>()).Where(g => !string.IsNullOrWhiteSpace(g.Grupo)))
            {
                string clave = grupo.Grupo.Trim();
                features.DiasSinComprarPorGrupo[clave] = Math.Max(0, (d - grupo.UltimaCompra.Date).Days);
                features.PedidosPorGrupo12Meses[clave] = grupo.Pedidos12Meses;
            }
            return features;
        }

        public static string Serializar(FeaturesAbandono features)
            => JsonConvert.SerializeObject(features, new JsonSerializerSettings { DateFormatString = "yyyy-MM-dd", Culture = CultureInfo.InvariantCulture });

        /// <summary>Días entre días con compra consecutivos (distintos y ordenados).</summary>
        internal static List<int> Huecos(IEnumerable<DateTime> diasConCompra)
        {
            List<DateTime> dias = (diasConCompra ?? Enumerable.Empty<DateTime>()).Select(x => x.Date).Distinct().OrderBy(x => x).ToList();
            var huecos = new List<int>();
            for (int i = 1; i < dias.Count; i++)
            {
                huecos.Add((dias[i] - dias[i - 1]).Days);
            }
            return huecos;
        }

        internal static double Mediana(List<int> valores)
        {
            List<int> ordenados = valores.OrderBy(x => x).ToList();
            int n = ordenados.Count;
            return n % 2 == 1 ? ordenados[n / 2] : (ordenados[n / 2 - 1] + ordenados[n / 2]) / 2.0;
        }

        /// <summary>Q3 − Q1 con el método de las medianas de cada mitad; 0 con menos de 4 valores.</summary>
        internal static int RangoIntercuartil(List<int> valores)
        {
            if (valores == null || valores.Count < 4)
            {
                return 0;
            }
            List<int> ordenados = valores.OrderBy(x => x).ToList();
            int mitad = ordenados.Count / 2;
            double q1 = Mediana(ordenados.Take(mitad).ToList());
            double q3 = Mediana(ordenados.Skip(ordenados.Count - mitad).ToList());
            return (int)Math.Round(q3 - q1, MidpointRounding.AwayFromZero);
        }

        private static int? DiasDesde(DateTime? fecha, DateTime d) => fecha.HasValue ? Math.Max(0, (d - fecha.Value.Date).Days) : (int?)null;

        private static int MesesEntre(DateTime desde, DateTime hasta)
        {
            int meses = (hasta.Year - desde.Year) * 12 + hasta.Month - desde.Month;
            if (hasta.Day < desde.Day)
            {
                meses--;
            }
            return Math.Max(0, meses);
        }
    }
}
