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

        /// <summary>La línea de la historia del envío viejo.</summary>
        public static string TextoHistoria(int envioNuevo) => $"La agencia acabó entregándolo: se anula el reenvío {envioNuevo}.";

        public async Task<ResultadoReenvioEntregado> ResolverAsync(EnviosAgencia origen)
        {
            if (origen == null || !datos.Reenvios.TablaDisponible())
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
                    .AnularPorEntregaAsync(origen.Numero, numeroNuevo, USUARIO, TextoHistoria(numeroNuevo)).ConfigureAwait(false);
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
        public static async Task ResolverConContextoPropioAsync(EnviosAgencia envio)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    var avisador = new AvisadorIncidenciasEnvio(db);
                    _ = await new ReenvioEntregadoPorLaAgencia(new DatosReenviosEnvioEF(db), avisador.AvisarReenvioEntregadoAsync)
                        .ResolverAsync(envio).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Registrar(envio, ex);
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
