using System;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#699: qué ha hecho <see cref="ReenvioEntregadoPorLaAgencia"/> con un envío que la agencia acaba de entregar.</summary>
    public enum ResultadoReenvioEntregado
    {
        /// <summary>El envío no tenía decisión Reenviar (lo normal): nada.</summary>
        SinReenvio,
        /// <summary>El reenvío aún no tenía etiqueta: borrado, fila EntregadoPorLaAgencia y aviso.</summary>
        ReenvioAnulado,
        /// <summary>El reenvío ya estaba tramitado: no se toca nada y se avisa de que hay dos envíos vivos.</summary>
        DosEnviosVivos
    }

    /// <summary>
    /// NestoAPI#699 (Carlos 04/11/26): un envío incidentado que se decidió volver a enviar y que la agencia acaba ENTREGANDO.
    /// Cerrar a mano el viejo no sirve de nada si la agencia lo sigue teniendo; lo que hace falta es no mandarlo dos veces:
    /// <list type="bullet">
    /// <item>Si el envío nuevo aún no tiene etiqueta (Estado &lt; 1, en curso o pendiente), se BORRA (como el DELETE de Agencias),
    /// la fila de EnviosAgenciaReenvios pasa a <see cref="DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA"/>, queda constancia en la
    /// historia del viejo y se avisa al vendedor y a Logística.</item>
    /// <item>Si ya está tramitado (etiqueta impresa), no se toca nada: se avisa de que hay dos envíos vivos para que lo resuelvan
    /// a mano.</item>
    /// </list>
    /// Lo llaman el seguimiento (job y consulta a demanda) en la transición a ENTREGADO (<see cref="PasaAEntregado"/>).
    /// </summary>
    public class ReenvioEntregadoPorLaAgencia
    {
        /// <summary>Usuario de la historia y de la fila (lo hace el seguimiento, no una persona).</summary>
        public const string USUARIO = "SeguimientoEnvios";

        private readonly IDatosReenviosEnvio datos;
        private readonly Func<EnviosAgencia, RegistroReenvioEnvio, EnviosAgencia, bool, Task> avisar;

        /// <param name="avisar">(envío viejo, fila de la decisión, envío nuevo, anulado) → correo al vendedor y a Logística.</param>
        public ReenvioEntregadoPorLaAgencia(IDatosReenviosEnvio datos, Func<EnviosAgencia, RegistroReenvioEnvio, EnviosAgencia, bool, Task> avisar)
        {
            this.datos = datos ?? throw new ArgumentNullException(nameof(datos));
            this.avisar = avisar;
        }

        /// <summary>
        /// ¿El envío acaba de pasar a ENTREGADO? Solo la transición. Desde DEVUELTO no cuenta: un devuelto «entregado» es la caja
        /// que vuelve a nosotros, y ahí el reenvío es justo lo que hay que mandar.
        /// </summary>
        public static bool PasaAEntregado(short estadoAnterior, EnviosAgencia envio) =>
            envio != null
            && estadoAnterior != Constantes.Agencias.ESTADO_ENTREGADO
            && estadoAnterior != Constantes.Agencias.ESTADO_DEVUELTO
            && envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO;

        /// <summary>
        /// Novedad 1366 (Carlos 18/11/26, «cerrado a mano = entregado»): LA REGLA de cuándo sobra el reenvío pendiente. El original está
        /// terminado si está Entregado (2) o Cerrado a mano (5), llegue como llegue (seguimiento, repartidor, «Dar por cerrado», PUT).
        /// </summary>
        public static bool EstaTerminado(short estado) => estado == Constantes.Agencias.ESTADO_ENTREGADO || estado == Constantes.Agencias.ESTADO_CERRADO_A_MANO;

        /// <summary>
        /// Novedad 1366: la transición de una PERSONA (repartidor, «Dar por cerrado», PUT) a terminado. Desde Devuelto también cuenta (lo
        /// cierra alguien a sabiendas); el seguimiento de la agencia sigue con <see cref="PasaAEntregado"/>.
        /// </summary>
        public static bool PasaATerminado(short estadoAnterior, short estadoNuevo) => EstaTerminado(estadoNuevo) && !EstaTerminado(estadoAnterior);

        /// <summary>La línea de la historia del envío viejo. Sin <paramref name="motivo"/>, la del seguimiento de la agencia.</summary>
        public static string TextoHistoria(int envioNuevo, string motivo = null)
            => $"{(string.IsNullOrWhiteSpace(motivo) ? "La agencia acabó entregándolo" : motivo.Trim())}: se anula el reenvío {envioNuevo}.";

        /// <summary>El motivo de la historia según cómo quedó el original (para los caminos que no son el seguimiento).</summary>
        public static string MotivoPorEstado(short estado)
            => estado == Constantes.Agencias.ESTADO_CERRADO_A_MANO ? "Se dio por cerrado" : "Ya está entregado";

        /// <param name="usuario">Quién lo hace (sin él, <see cref="USUARIO"/>, el seguimiento).</param>
        /// <param name="motivo">Para la historia (sin él, «La agencia acabó entregándolo»).</param>
        public async Task<ResultadoReenvioEntregado> ResolverAsync(EnviosAgencia origen, string usuario = null, string motivo = null)
        {
            if (origen == null || !EstaTerminado(origen.Estado) || !datos.Reenvios.TablaDisponible())
            {
                return ResultadoReenvioEntregado.SinReenvio;
            }
            RegistroReenvioEnvio fila = await datos.Reenvios.LeerAsync(origen.Numero).ConfigureAwait(false);
            if (fila == null || fila.Decision?.Trim() != DecisionesReenvio.REENVIAR || !fila.EnvioNuevo.HasValue)
            {
                return ResultadoReenvioEntregado.SinReenvio;
            }
            int numeroNuevo = fila.EnvioNuevo.Value;
            EnviosAgencia nuevo = await datos.LeerEnvioAsync(numeroNuevo).ConfigureAwait(false);
            if (nuevo == null)
            {
                return ResultadoReenvioEntregado.SinReenvio;
            }

            ResultadoReenvioEntregado resultado;
            if (nuevo.Estado < Constantes.Agencias.ESTADO_TRAMITADO)
            {
                ResultadoAnulacionReenvio anulacion = await datos.Reenvios
                    .AnularPorEntregaAsync(origen.Numero, numeroNuevo, string.IsNullOrWhiteSpace(usuario) ? USUARIO : usuario, TextoHistoria(numeroNuevo, motivo)).ConfigureAwait(false);
                if (anulacion == ResultadoAnulacionReenvio.SinReenvioPendiente)
                {
                    // Otra pasada (o la consulta a demanda) se ha adelantado: ya está hecho y avisado.
                    return ResultadoReenvioEntregado.SinReenvio;
                }
                if (anulacion == ResultadoAnulacionReenvio.Anulado)
                {
                    resultado = ResultadoReenvioEntregado.ReenvioAnulado;
                }
                else
                {
                    // Lo han tramitado entre la lectura y la anulación: se lee como esté ahora para el aviso.
                    nuevo = await datos.LeerEnvioAsync(numeroNuevo).ConfigureAwait(false) ?? nuevo;
                    resultado = ResultadoReenvioEntregado.DosEnviosVivos;
                }
            }
            else
            {
                resultado = ResultadoReenvioEntregado.DosEnviosVivos;
            }

            if (avisar != null)
            {
                try
                {
                    await avisar(origen, fila, nuevo, resultado == ResultadoReenvioEntregado.ReenvioAnulado).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Registrar(origen, ex);
                }
            }
            return resultado;
        }

        /// <summary>Punto de entrada del seguimiento: con su propio contexto, para no mezclarse con el de la pasada. Nunca lanza.</summary>
        public static Task ResolverConContextoPropioAsync(EnviosAgencia envio) => ResolverConContextoPropioAsync(envio, null, null);

        /// <summary>
        /// Novedad 1366: lo mismo desde los caminos de una persona (repartidor, «Dar por cerrado», PUT, borrar el reenvío que sobra), con
        /// quién y el motivo de la historia. Nunca lanza (si falla, SinReenvio y ELMAH).
        /// </summary>
        public static async Task<ResultadoReenvioEntregado> ResolverConContextoPropioAsync(EnviosAgencia envio, string usuario, string motivo)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    var avisador = new AvisadorIncidenciasEnvio(db);
                    return await new ReenvioEntregadoPorLaAgencia(new DatosReenviosEnvioEF(db), avisador.AvisarReenvioEntregadoAsync)
                        .ResolverAsync(envio, usuario, motivo).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Registrar(envio, ex);
                return ResultadoReenvioEntregado.SinReenvio;
            }
        }

        private static void Registrar(EnviosAgencia envio, Exception ex)
        {
            try
            {
                ElmahHelper.Log(new Exception(
                    $"[Reenvío #699] No se pudo resolver el reenvío del envío {envio?.Numero} (pedido {envio?.Pedido}), que la agencia ha entregado: " +
                    $"{ex.Message}. Revise en Agencias › Devueltos si hay dos envíos vivos del mismo pedido.", ex),
                    "Sistema (seguimiento de envíos)");
            }
            catch
            {
                // nunca romper el seguimiento
            }
        }
    }
}
