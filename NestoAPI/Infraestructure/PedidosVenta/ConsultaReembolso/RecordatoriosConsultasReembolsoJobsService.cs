using Hangfire;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>Una consulta abierta a la que hay que volver a avisar en esta pasada.</summary>
    public class ConsultaReembolsoARecordar
    {
        public ConsultaReembolsoListadoDTO Consulta { get; set; }
        /// <summary>Lleva más de un día laborable sin contestar: además de a Administración, a Dirección.</summary>
        public bool AvisarDireccion { get; set; }
    }

    /// <summary>Lo que ha hecho una pasada del recordatorio.</summary>
    public class ResultadoRecordatoriosConsultasReembolso
    {
        public int Recordadas { get; set; }
        public int Escaladas { get; set; }
        public List<int> PedidosRecordados { get; set; } = new List<int>();
        public List<int> PedidosEscalados { get; set; } = new List<int>();
    }

    /// <summary>
    /// NestoAPI#734 (pedido 929559, consulta del 04/11/26 08:38): los cinco de Administración leyeron el aviso y nadie contestó en
    /// siete días, sin un segundo aviso. El núcleo (sin BD) decide qué consultas abiertas se recuerdan y a quién:
    /// <list type="bullet">
    /// <item>Abiertas desde hace al menos <see cref="ESPERA_MINIMA"/>: otra vez a la campana de Administración, con el mismo tipo y
    /// acción que el aviso original (al pulsarlo se abre el diálogo de respuesta).</item>
    /// <item>Abiertas desde hace más de un día laborable (sin sábados, domingos ni festivos de Algete): además, a Dirección.</item>
    /// </list>
    /// Una vez por consulta y pasada; sin columna de «último recordatorio»: con dos pasadas al día, se recuerda en cada una.
    /// </summary>
    public static class RecordatoriosConsultasReembolso
    {
        /// <summary>Lo que tiene que llevar abierta una consulta para recordarla (las recién hechas ya tienen su primer aviso).</summary>
        public static readonly TimeSpan ESPERA_MINIMA = TimeSpan.FromHours(4);

        public static List<ConsultaReembolsoARecordar> Decidir(IEnumerable<ConsultaReembolsoListadoDTO> consultas, DateTime ahora, Func<DateTime, bool> esFestivo)
        {
            return (consultas ?? Enumerable.Empty<ConsultaReembolsoListadoDTO>())
                .Where(c => c != null && c.Abierta && ahora - c.Fecha >= ESPERA_MINIMA)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .OrderBy(c => c.Fecha)
                .Select(c => new ConsultaReembolsoARecordar { Consulta = c, AvisarDireccion = MasDeUnDiaLaborable(c.Fecha, ahora, esFestivo) })
                .ToList();
        }

        /// <summary>
        /// True si desde <paramref name="desde"/> ha pasado más de un día laborable: ya es (o ha pasado) la misma hora del siguiente
        /// día laborable. Consulta el miércoles a las 8:38 → desde el jueves a las 8:38; el viernes a las 12:00 → desde el lunes a las 12:00.
        /// </summary>
        public static bool MasDeUnDiaLaborable(DateTime desde, DateTime ahora, Func<DateTime, bool> esFestivo)
        {
            DateTime dia = desde.Date.AddDays(1);
            for (int i = 0; i < 30 && !EsLaborable(dia, esFestivo); i++)
            {
                dia = dia.AddDays(1);
            }
            return ahora >= dia + desde.TimeOfDay;
        }

        internal static bool EsLaborable(DateTime dia, Func<DateTime, bool> esFestivo)
        {
            if (dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }
            try
            {
                return esFestivo == null || !esFestivo(dia.Date);
            }
            catch
            {
                // Sin calendario, como laborable: antes avisar a Dirección un día antes que no avisarla.
                return true;
            }
        }
    }

    /// <summary>
    /// NestoAPI#734: job <c>consultas-reembolso-sin-contestar</c>, de lunes a viernes a las 10:30 y a las 15:30 (hora local; en los
    /// festivos de Algete no hace nada, <see cref="JobsFestivos"/>). Recuerda a Administración las consultas de reembolso abiertas
    /// y escala a Dirección las que llevan más de un día laborable (<see cref="RecordatoriosConsultasReembolso"/>). Nunca rompe: lo
    /// que falle va a ELMAH y no se reintenta. Sin la tabla no hace nada.
    /// </summary>
    public static class RecordatoriosConsultasReembolsoJobsService
    {
        public const string ID_JOB = "consultas-reembolso-sin-contestar";
        public const string CRON = "30 10,15 * * 1-5";
        private const string USUARIO_SISTEMA = "Sistema (consulta reembolso)";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Avisar()
        {
            try
            {
                if (JobsFestivos.SaltarSiFestivo("[Consulta reembolso #734] Hoy es festivo en Algete: no se recuerdan las consultas sin contestar.",
                    USUARIO_SISTEMA, JobsFestivos.EsFestivoAlgete, () => DateTime.Now))
                {
                    return;
                }
                _ = await Procesar(new RepositorioConsultasReembolso(), new AvisadorConsultaReembolso(), DateTime.Now, JobsFestivos.EsFestivoAlgete)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Consulta reembolso #734] Error en el recordatorio de consultas sin contestar: {ex.Message}", ex), USUARIO_SISTEMA);
            }
        }

        /// <summary>El núcleo con todo inyectado (tests).</summary>
        internal static async Task<ResultadoRecordatoriosConsultasReembolso> Procesar(IRepositorioConsultasReembolso repositorio, IAvisadorConsultaReembolso avisador,
            DateTime ahora, Func<DateTime, bool> esFestivo, Action<Exception> registrar = null, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            registrar = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_SISTEMA));
            var resultado = new ResultadoRecordatoriosConsultasReembolso();
            if (!repositorio.TablaExiste())
            {
                return resultado;
            }
            List<ConsultaReembolsoListadoDTO> abiertas = await repositorio.LeerListado(empresa, soloAbiertas: true).ConfigureAwait(false)
                ?? new List<ConsultaReembolsoListadoDTO>();

            foreach (ConsultaReembolsoARecordar recordar in RecordatoriosConsultasReembolso.Decidir(abiertas, ahora, esFestivo))
            {
                int pedido = recordar.Consulta.Pedido;
                try
                {
                    var notificacion = TextosConsultaReembolso.NotificacionRecordatorio(recordar.Consulta, ahora, recordar.AvisarDireccion);
                    int avisados = await avisador.AvisarAdministracion(notificacion).ConfigureAwait(false);
                    if (recordar.AvisarDireccion)
                    {
                        avisados += await avisador.AvisarDireccion(notificacion).ConfigureAwait(false);
                        resultado.Escaladas++;
                        resultado.PedidosEscalados.Add(pedido);
                    }
                    if (avisados == 0)
                    {
                        registrar(new Exception($"[Consulta reembolso #734] Pedido {pedido}: consulta sin contestar y no hay nadie a quien recordársela."));
                    }
                    resultado.Recordadas++;
                    resultado.PedidosRecordados.Add(pedido);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Consulta reembolso #734] Pedido {pedido}: no se ha podido recordar la consulta sin contestar: {ex.Message}", ex));
                }
            }
            return resultado;
        }
    }
}
