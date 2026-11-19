using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857f: en qué punto está el envío según la propia agencia.</summary>
    public enum EstadoEnvioAgencia
    {
        EnCamino,
        EnReparto,
        Reprogramada,
        /// <summary>Ya entregado: no pone fecha (la recepción la marca el albarán), pero se guarda el estado.</summary>
        Entregado
    }

    /// <summary>
    /// NestoAPI#857f (nivel 2): lo que dice la AGENCIA de un envío (en su propio correo o al consultarla): el día y el tramo de entrega y el
    /// estado. Entrega null si no da fecha (siempre null si está Entregado).
    /// </summary>
    public class EntregaAgencia
    {
        public EntregaEstimada Entrega { get; set; }
        public EstadoEnvioAgencia? Estado { get; set; }

        /// <summary>¿Dice algo del envío (fecha o estado)? Si no, el correo de la agencia no es de un envío (publicidad, boletín…).</summary>
        public bool DiceAlgo => Entrega != null || Estado.HasValue;
    }

    /// <summary>
    /// NestoAPI#857f: de dónde sale la entrega estimada guardada en un correo (columna OrigenEntrega). Precedencia (<see cref="Prioridad"/>):
    /// lo que dice la agencia (su correo o su consulta) manda sobre lo que dice el aviso del proveedor; sin ninguna, recibido + 24/48 h.
    /// </summary>
    public static class OrigenEntregaCorreo
    {
        public const string AGENCIA = "Agencia";
        public const string PROVEEDOR = "Proveedor";
        public const string CONSULTA = "Consulta";
        public const int LONGITUD = 20;

        /// <summary>2 = la agencia (correo o consulta), 1 = el proveedor o sin apuntar (correos de antes de Issue857f).</summary>
        public static int Prioridad(string origen) =>
            string.Equals(origen, AGENCIA, StringComparison.OrdinalIgnoreCase) || string.Equals(origen, CONSULTA, StringComparison.OrdinalIgnoreCase) ? 2 : 1;

        public static bool EsDeLaAgencia(string origen) => Prioridad(origen) == 2;
    }

    /// <summary>
    /// NestoAPI#857f: el año de una fecha que la agencia da sin año («Jueves 19 de Noviembre»): el del correo, o el siguiente si cae antes del
    /// día del correo (cambio de año: «2 de enero» en un correo del 30/12). PURA. Null si no es una fecha.
    /// </summary>
    public static class ReglaAnioEntregaAgencia
    {
        public static DateTime? Resolver(int dia, int mes, int? anio, DateTime fechaCorreo)
        {
            int a = anio ?? fechaCorreo.Year;
            if (!EsFecha(a, mes, dia))
            {
                return null;
            }
            var fecha = new DateTime(a, mes, dia);
            if (!anio.HasValue && fecha < fechaCorreo.Date)
            {
                return EsFecha(a + 1, mes, dia) ? new DateTime(a + 1, mes, dia) : (DateTime?)null;
            }
            return fecha;
        }

        private static bool EsFecha(int anio, int mes, int dia) =>
            anio >= 2000 && anio <= 2100 && mes >= 1 && mes <= 12 && dia >= 1 && dia <= DateTime.DaysInMonth(anio, mes);
    }

    /// <summary>
    /// NestoAPI#857f (punto de extensión): una agencia que sabe CONSULTAR la entrega estimada de un envío por su código (su API o su web).
    /// Opcional: solo la implementa la clase de la agencia que lo sepa hacer, y todo lo suyo (claves, URL, formato) vive dentro de esa clase.
    /// Hoy no la implementa ninguna. La usa el job «compras-consulta-entrega-agencias» (<see cref="ServicioConsultaEntregaAgencias"/>), siempre
    /// a través de <see cref="ConsultaEntregaSegura"/> (tiempo máximo y nunca lanza).
    /// </summary>
    public interface IConsultaEntregaAgencia
    {
        /// <summary>Null si no lo sabe. Puede lanzar: quien la llama lo recoge.</summary>
        Task<EntregaAgencia> ConsultarEntrega(string codigo, CancellationToken cancelacion);
    }

    /// <summary>NestoAPI#857f: la consulta a una agencia con tiempo máximo; nunca lanza (un fallo o un tiempo agotado = null y se apunta).</summary>
    public static class ConsultaEntregaSegura
    {
        public static readonly TimeSpan TIEMPO_MAXIMO = TimeSpan.FromSeconds(8);

        public static async Task<EntregaAgencia> Consultar(IConsultaEntregaAgencia agencia, string codigo, TimeSpan? tiempoMaximo = null,
            Action<Exception> registrar = null)
        {
            if (agencia == null || string.IsNullOrWhiteSpace(codigo))
            {
                return null;
            }
            TimeSpan limite = tiempoMaximo ?? TIEMPO_MAXIMO;
            using (var cancelacion = new CancellationTokenSource(limite))
            {
                try
                {
                    Task<EntregaAgencia> consulta = agencia.ConsultarEntrega(codigo.Trim(), cancelacion.Token) ?? Task.FromResult<EntregaAgencia>(null);
                    Task terminada = await Task.WhenAny(consulta, Task.Delay(limite)).ConfigureAwait(false);
                    if (terminada != consulta)
                    {
                        cancelacion.Cancel();
                        registrar?.Invoke(new Exception($"[Correos de seguimiento de compras #857f] La consulta de {codigo} a la agencia no ha contestado en {limite.TotalSeconds:0} s."));
                        return null;
                    }
                    return await consulta.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    registrar?.Invoke(new Exception($"[Correos de seguimiento de compras #857f] La consulta de {codigo} a la agencia ha fallado: {ex.Message}", ex));
                    return null;
                }
            }
        }
    }

    /// <summary>NestoAPI#857f: el enlace de seguimiento de un código según su agencia (por el nombre guardado); null si no se sabe. PURA.</summary>
    public static class EnlaceSeguimientoAgencia
    {
        public static string De(string agencia, string codigo, IEnumerable<IAgenciaEntrada> agencias = null)
        {
            if (string.IsNullOrWhiteSpace(agencia) || string.IsNullOrWhiteSpace(codigo))
            {
                return null;
            }
            IAgenciaEntrada a = (agencias ?? AgenciasEntradaRegistradas.Todas)
                .FirstOrDefault(x => string.Equals(x.Nombre, agencia.Trim(), StringComparison.OrdinalIgnoreCase));
            return a?.UrlSeguimiento(codigo.Trim());
        }
    }
}
