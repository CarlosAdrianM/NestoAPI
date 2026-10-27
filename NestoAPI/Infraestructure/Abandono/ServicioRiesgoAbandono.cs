using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>Lo que ha hecho una pasada nocturna (para el log del job y el POST manual).</summary>
    public class ResumenPasadaAbandono
    {
        public DateTime FechaInstantanea { get; set; }
        public ModoAbandono Modo { get; set; }
        public bool TablaExiste { get; set; } = true;
        public int Vendedores { get; set; }
        public int VendedoresConError { get; set; }
        public int Cartera { get; set; }
        public int CompradoresRegulares { get; set; }
        public int MarcadosPorLaRegla { get; set; }
        public int AlertasNuevas { get; set; }
        public int InstantaneasSemanales { get; set; }
        public int FilasInsertadas { get; set; }
        public List<string> Detalle { get; } = new List<string>();
    }

    public class ResumenResolucionAbandono
    {
        public ModoAbandono Modo { get; set; }
        public bool TablaExiste { get; set; } = true;
        public int Resueltas { get; set; }
        public Dictionary<string, int> PorResultado { get; } = new Dictionary<string, int>();
        public int Errores { get; set; }
    }

    /// <summary>
    /// NestoAPI#714 (corte 1): la pasada nocturna y la resolución, sin Hangfire ni SQL (todo por <see cref="IRepositorioRiesgoAbandono"/>).
    /// <para><b>Pasada</b> (<see cref="EjecutarPasada"/>): la instantánea es del DÍA ANTERIOR al de la ejecución (datos completos:
    /// las compras de ese día ya están todas); por cada vendedor activo lee su cartera, evalúa a cada cliente con
    /// <see cref="CalculadoraAbandono.Evaluar"/> y escribe:
    /// <list type="bullet">
    /// <item>una fila <see cref="TiposFilaAbandono.ALERTA"/> por cada comprador regular que la regla marca y que no tiene ya una
    /// alerta sin resolver (episodios: una alerta por cliente hasta que vence su horizonte, como vería el vendedor);</item>
    /// <item>si la instantánea cae en lunes, además una fila <see cref="TiposFilaAbandono.INSTANTANEA"/> por cada comprador
    /// regular (marcado o no): negativos y positivos con sus features para entrenar el modelo (corte 2).</item>
    /// </list>
    /// Best-effort: un vendedor que falle va a ELMAH y se sigue con el siguiente. Apagado o sin tabla: no hace nada.</para>
    /// <para><b>Resolución</b> (<see cref="ResolverVencidas"/>): las filas sin resolver cuyo horizonte ya ha vencido (FechaLimite
    /// anterior a hoy) se etiquetan con <see cref="ResolucionAbandono.Decidir"/>.</para>
    /// </summary>
    public class ServicioRiesgoAbandono
    {
        internal const int MAXIMO_RESOLVER_POR_PASADA = 5000;
        internal const DayOfWeek DIA_INSTANTANEA_SEMANAL = DayOfWeek.Monday;

        private readonly IRepositorioRiesgoAbandono repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Func<ModoAbandono> modo;
        private readonly Action<Exception> registrarError;

        public ServicioRiesgoAbandono(IRepositorioRiesgoAbandono repositorio = null, Func<DateTime> ahora = null,
            Func<ModoAbandono> modo = null, Action<Exception> registrarError = null)
        {
            this.repositorio = repositorio ?? new RepositorioRiesgoAbandonoSql();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.modo = modo ?? (() => InterruptorAbandono.Modo);
            this.registrarError = registrarError ?? (ex => ElmahHelper.Log(ex, "Sistema (riesgo de abandono)"));
        }

        /// <summary>El día de la instantánea de una ejecución: el anterior (completo).</summary>
        public static DateTime DiaInstantanea(DateTime ejecucion) => ejecucion.Date.AddDays(-1);

        public static bool TocaInstantaneaSemanal(DateTime diaInstantanea) => diaInstantanea.DayOfWeek == DIA_INSTANTANEA_SEMANAL;

        public ResumenPasadaAbandono EjecutarPasada()
        {
            DateTime ejecucion = ahora();
            DateTime dia = DiaInstantanea(ejecucion);
            var resumen = new ResumenPasadaAbandono { FechaInstantanea = dia, Modo = modo() };
            if (resumen.Modo == ModoAbandono.Apagado)
            {
                return resumen;
            }
            if (!repositorio.TablaExiste())
            {
                resumen.TablaExiste = false;
                return resumen;
            }
            bool semanal = TocaInstantaneaSemanal(dia);
            List<string> vendedores = repositorio.LeerVendedores() ?? new List<string>();
            resumen.Vendedores = vendedores.Count;
            foreach (string vendedor in vendedores)
            {
                try
                {
                    List<DatosClienteAbandono> cartera = repositorio.LeerCartera(vendedor, dia) ?? new List<DatosClienteAbandono>();
                    HashSet<string> abiertas = repositorio.LeerAlertasAbiertas(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor) ?? new HashSet<string>();
                    List<FilaRiesgoAbandono> filas = PrepararFilas(cartera, dia, semanal, abiertas, resumen.Modo, out int regulares, out int marcados);
                    int insertadas = repositorio.Insertar(filas);
                    resumen.Cartera += cartera.Count;
                    resumen.CompradoresRegulares += regulares;
                    resumen.MarcadosPorLaRegla += marcados;
                    resumen.AlertasNuevas += filas.Count(f => f.Tipo == TiposFilaAbandono.ALERTA);
                    resumen.InstantaneasSemanales += filas.Count(f => f.Tipo == TiposFilaAbandono.INSTANTANEA);
                    resumen.FilasInsertadas += insertadas;
                    resumen.Detalle.Add($"{vendedor}: cartera {cartera.Count}, regulares {regulares}, marcados {marcados}, alertas nuevas {filas.Count(f => f.Tipo == TiposFilaAbandono.ALERTA)}");
                }
                catch (Exception ex)
                {
                    resumen.VendedoresConError++;
                    resumen.Detalle.Add($"{vendedor}: ERROR {ex.Message}");
                    registrarError(new Exception($"[Riesgo de abandono #714] Error en la pasada del {dia:dd/MM/yyyy} para el vendedor {vendedor}: {ex.Message}", ex));
                }
            }
            return resumen;
        }

        /// <summary>Qué filas salen de una cartera un día (función sin efectos: se prueba sola).</summary>
        internal static List<FilaRiesgoAbandono> PrepararFilas(List<DatosClienteAbandono> cartera, DateTime dia, bool semanal,
            HashSet<string> alertasAbiertas, ModoAbandono modo, out int regulares, out int marcados)
        {
            var filas = new List<FilaRiesgoAbandono>();
            regulares = 0;
            marcados = 0;
            foreach (DatosClienteAbandono datos in cartera)
            {
                EvaluacionAbandono evaluacion = CalculadoraAbandono.Evaluar(datos, dia);
                if (!evaluacion.EsCompradorRegular)
                {
                    continue;
                }
                regulares++;
                if (evaluacion.Baseline)
                {
                    marcados++;
                    if (!alertasAbiertas.Contains(RepositorioRiesgoAbandonoSql.Clave(datos.Cliente, datos.Contacto)))
                    {
                        filas.Add(Fila(datos, evaluacion, dia, TiposFilaAbandono.ALERTA, modo));
                    }
                }
                if (semanal)
                {
                    filas.Add(Fila(datos, evaluacion, dia, TiposFilaAbandono.INSTANTANEA, modo));
                }
            }
            return filas;
        }

        private static FilaRiesgoAbandono Fila(DatosClienteAbandono datos, EvaluacionAbandono evaluacion, DateTime dia, string tipo, ModoAbandono modo)
            => new FilaRiesgoAbandono
            {
                Empresa = datos.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Cliente = datos.Cliente,
                Contacto = datos.Contacto,
                Vendedor = datos.Vendedor,
                FechaInstantanea = dia,
                Tipo = tipo,
                Probabilidad = null,
                Baseline = evaluacion.Baseline,
                Cadencia = evaluacion.Cadencia,
                DiasSinComprar = evaluacion.DiasSinComprar,
                Horizonte = evaluacion.Horizonte,
                FechaLimite = evaluacion.FechaLimite,
                Motivo = evaluacion.Motivo,
                Features = evaluacion.FeaturesJson,
                VersionModelo = CalculadoraAbandono.VERSION_REGLA,
                Modo = modo.ToString()
            };

        public ResumenResolucionAbandono ResolverVencidas()
        {
            DateTime hoy = ahora();
            var resumen = new ResumenResolucionAbandono { Modo = modo() };
            if (resumen.Modo == ModoAbandono.Apagado)
            {
                return resumen;
            }
            if (!repositorio.TablaExiste())
            {
                resumen.TablaExiste = false;
                return resumen;
            }
            List<PendienteResolverAbandono> pendientes = repositorio.LeerPendientesResolver(hoy.Date, MAXIMO_RESOLVER_POR_PASADA) ?? new List<PendienteResolverAbandono>();
            foreach (PendienteResolverAbandono pendiente in pendientes)
            {
                try
                {
                    (string resultado, DateTime? fechaEvento) = ResolucionAbandono.Decidir(pendiente.ClienteExiste, pendiente.FechaBaja, pendiente.FechaPrimeraCompra);
                    repositorio.Resolver(pendiente.Id, resultado, fechaEvento, hoy);
                    resumen.Resueltas++;
                    resumen.PorResultado[resultado] = resumen.PorResultado.TryGetValue(resultado, out int n) ? n + 1 : 1;
                }
                catch (Exception ex)
                {
                    resumen.Errores++;
                    registrarError(new Exception($"[Riesgo de abandono #714] Error al resolver la fila {pendiente.Id} ({pendiente.Cliente}/{pendiente.Contacto}): {ex.Message}", ex));
                }
            }
            return resumen;
        }
    }
}
