using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#350: motor de balances y cuentas de resultados definidos en Balances/LinBalance
    /// (BPY, PGP...). Replica EXACTAMENTE la semántica del Nesto viejo, calibrada el 17/08/26
    /// contra los PDFs de junio/26 (80/80 líneas cuadradas; la única diferencia fue un asiento
    /// de apertura regenerado después de imprimirlos):
    /// - Línea de detalle: suma, con el signo LITERAL de la fórmula, del saldo Debe−Haber de
    ///   las cuentas que casan cada patrón ([Nº Cuenta] Like '20%'). El comodín '99999%' del
    ///   viejo (no casa nada) vale 0 y permite empezar restando.
    /// - Tipo 'P' (panel del pasivo): el total de la línea se niega (las cuentas acreedoras
    ///   tienen saldo D−H negativo y se presentan en positivo).
    /// - Línea de total: su fórmula referencia GRUPOS ("1+2", "4+5+6+7") y suma los valores de
    ///   las líneas de detalle de esos grupos.
    /// - Columna N−1: mismas fechas desplazadas un año (el desde incluye el asiento de apertura).
    /// </summary>
    public class GestorBalances
    {
        private readonly NVEntities db;

        public GestorBalances(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>Definición de una línea, leída de LinBalance (fuera del EDMX, SQL crudo).</summary>
        public class LineaBalanceDefinicion
        {
            public int Orden { get; set; }
            public string Descripcion { get; set; }
            public string Tipo { get; set; }
            public int Grupo { get; set; }
            public bool EsTotal { get; set; }
            public string Formula { get; set; }
        }

        internal class TerminoFormula
        {
            public int Signo { get; set; }
            public string Prefijo { get; set; }
        }

        private static readonly Regex RegexTermino = new Regex(
            @"(?<pre>[^\[]*)\[Nº Cuenta\] Like '(?<pat>[0-9]+)%'", RegexOptions.Compiled);

        /// <summary>
        /// Tokens (signo, prefijo de cuenta) de una fórmula de LinBalance. El signo de cada
        /// término es el último +/− que aparece antes de él (los paréntesis del viejo son solo
        /// asociatividad por la izquierda y no cambian ningún signo); sin operador previo = +.
        /// </summary>
        internal static List<TerminoFormula> ParsearFormula(string formula)
        {
            var terminos = new List<TerminoFormula>();
            if (string.IsNullOrWhiteSpace(formula))
            {
                return terminos;
            }
            foreach (Match coincidencia in RegexTermino.Matches(formula))
            {
                string previo = coincidencia.Groups["pre"].Value;
                int signo = 1;
                for (int i = previo.Length - 1; i >= 0; i--)
                {
                    if (previo[i] == '+') { break; }
                    if (previo[i] == '-') { signo = -1; break; }
                }
                terminos.Add(new TerminoFormula { Signo = signo, Prefijo = coincidencia.Groups["pat"].Value });
            }
            return terminos;
        }

        internal static decimal EvaluarDetalle(List<TerminoFormula> terminos,
            IReadOnlyDictionary<string, decimal> saldosPorCuenta, bool esPasivo)
        {
            decimal total = 0;
            foreach (TerminoFormula termino in terminos)
            {
                decimal saldo = saldosPorCuenta
                    .Where(s => s.Key.StartsWith(termino.Prefijo, StringComparison.Ordinal))
                    .Sum(s => s.Value);
                total += termino.Signo * saldo;
            }
            return esPasivo ? -total : total;
        }

        /// <summary>% de variación N vs N−1, dividiendo por el año anterior CON SU SIGNO (así lo
        /// hace el viejo: de −384k a −491k muestra +27,95 — "empeora un 27,95%"; verificado
        /// contra los PDFs de junio/26). Sin año anterior (0) no hay criterio: null (el viejo
        /// pintaba ruido tipo "−100,00" o "4.829,65" sobre bases despreciables).</summary>
        internal static decimal? CalcularPorcentaje(decimal actual, decimal anterior)
        {
            return anterior == 0
                ? (decimal?)null
                : Math.Round((actual - anterior) / anterior * 100, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Núcleo PURO del cálculo: evalúa todas las líneas contra los saldos (Debe−Haber por
        /// cuenta) de cada periodo. Orden de presentación: Grupo y dentro NºOrden (la línea
        /// "Total A) Patrimonio neto" tiene NºOrden posterior al total general pero se presenta
        /// en su grupo). Estático para testear sin BD.
        /// </summary>
        internal static List<LineaBalanceInformeDTO> Evaluar(List<LineaBalanceDefinicion> definiciones,
            IReadOnlyDictionary<string, decimal> saldosActual, IReadOnlyDictionary<string, decimal> saldosAnterior)
        {
            var porGrupoActual = new Dictionary<int, decimal>();
            var porGrupoAnterior = new Dictionary<int, decimal>();
            var resultado = new List<LineaBalanceInformeDTO>();

            foreach (LineaBalanceDefinicion definicion in definiciones.OrderBy(d => d.Grupo).ThenBy(d => d.Orden))
            {
                var linea = new LineaBalanceInformeDTO
                {
                    Orden = definicion.Orden,
                    Descripcion = definicion.Descripcion?.Trim(),
                    Tipo = definicion.Tipo?.Trim(),
                    Grupo = definicion.Grupo,
                    EsTotal = definicion.EsTotal
                };

                if (definicion.EsTotal)
                {
                    decimal actual = 0, anterior = 0;
                    foreach (string parte in (definicion.Formula ?? string.Empty).Split('+'))
                    {
                        if (int.TryParse(parte.Trim(), out int grupo))
                        {
                            actual += porGrupoActual.TryGetValue(grupo, out decimal a) ? a : 0;
                            anterior += porGrupoAnterior.TryGetValue(grupo, out decimal b) ? b : 0;
                        }
                    }
                    linea.SaldoActual = actual;
                    linea.SaldoAnterior = anterior;
                }
                else
                {
                    List<TerminoFormula> terminos = ParsearFormula(definicion.Formula);
                    if (!terminos.Any())
                    {
                        linea.EsCabecera = true; // epígrafe sin fórmula: sin importes
                        resultado.Add(linea);
                        continue;
                    }
                    bool esPasivo = linea.Tipo == "P";
                    linea.SaldoActual = EvaluarDetalle(terminos, saldosActual, esPasivo);
                    linea.SaldoAnterior = EvaluarDetalle(terminos, saldosAnterior, esPasivo);
                    porGrupoActual[definicion.Grupo] =
                        (porGrupoActual.TryGetValue(definicion.Grupo, out decimal ga) ? ga : 0) + linea.SaldoActual.Value;
                    porGrupoAnterior[definicion.Grupo] =
                        (porGrupoAnterior.TryGetValue(definicion.Grupo, out decimal gb) ? gb : 0) + linea.SaldoAnterior.Value;
                }

                linea.Porcentaje = CalcularPorcentaje(linea.SaldoActual.Value, linea.SaldoAnterior.Value);
                resultado.Add(linea);
            }
            return resultado;
        }

        public async Task<BalanceInformeDTO> CalcularAsync(string empresa, string numero, DateTime desde, DateTime hasta)
        {
            empresa = empresa?.Trim();
            numero = numero?.Trim();

            List<LineaBalanceDefinicion> definiciones = await db.Database
                .SqlQuery<LineaBalanceDefinicion>(
                    "SELECT NºOrden AS Orden, RTRIM([Descripción]) AS Descripcion, RTRIM(Tipo) AS Tipo, " +
                    "       Grupo, Total AS EsTotal, [Fórmula] AS Formula " +
                    "FROM LinBalance WHERE Empresa = @p0 AND [Número] = @p1",
                    new SqlParameter("@p0", empresa), new SqlParameter("@p1", numero))
                .ToListAsync().ConfigureAwait(false);
            if (!definiciones.Any())
            {
                return null;
            }

            string descripcion = (await db.Database
                .SqlQuery<string>("SELECT RTRIM([Descripción]) FROM Balances WHERE Empresa = @p0 AND [Número] = @p1",
                    new SqlParameter("@p0", empresa), new SqlParameter("@p1", numero))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

            IReadOnlyDictionary<string, decimal> saldosActual =
                await LeerSaldos(empresa, desde, hasta).ConfigureAwait(false);
            IReadOnlyDictionary<string, decimal> saldosAnterior =
                await LeerSaldos(empresa, desde.AddYears(-1), hasta.AddYears(-1)).ConfigureAwait(false);

            Empresa fichaEmpresa = await db.Empresas
                .FirstOrDefaultAsync(e => e.Número == empresa).ConfigureAwait(false);

            return new BalanceInformeDTO
            {
                Empresa = empresa,
                NombreEmpresa = fichaEmpresa?.Nombre?.Trim(),
                Numero = numero,
                Descripcion = descripcion ?? numero,
                Desde = desde.Date,
                Hasta = hasta.Date,
                Lineas = Evaluar(definiciones, saldosActual, saldosAnterior)
            };
        }

        /// <summary>Saldo Debe−Haber por cuenta del mayor en [desde, hasta] (ambos incluidos;
        /// el desde arranca en la apertura del ejercicio para que el balance sea a-la-fecha).</summary>
        private async Task<IReadOnlyDictionary<string, decimal>> LeerSaldos(string empresa, DateTime desde, DateTime hasta)
        {
            DateTime hastaExclusivo = hasta.Date.AddDays(1);
            DateTime desdeInclusivo = desde.Date;
            var saldos = await db.Contabilidades
                .Where(c => c.Empresa == empresa && c.Fecha >= desdeInclusivo && c.Fecha < hastaExclusivo)
                .GroupBy(c => c.Nº_Cuenta)
                .Select(g => new { Cuenta = g.Key, Saldo = g.Sum(x => x.Debe - x.Haber) })
                .ToListAsync().ConfigureAwait(false);
            return saldos.ToDictionary(s => s.Cuenta.Trim(), s => s.Saldo);
        }
    }
}
