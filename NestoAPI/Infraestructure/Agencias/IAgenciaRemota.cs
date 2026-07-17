using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Datos de un envío a tramitar con una agencia remota, independientes de la agencia concreta.
    /// Cada estrategia (<see cref="IAgenciaRemota"/>) los traduce a su propio formato. El
    /// <see cref="Reembolso"/> usa el centinela de EnviosAgencia (&lt; 0 = no cobrar). Las dimensiones
    /// son opcionales: si vienen a 0, la estrategia aplica un tamaño estándar.
    /// </summary>
    public class DatosEnvioRemoto
    {
        public string Referencia { get; set; }
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Direccion { get; set; }
        public decimal Peso { get; set; }
        public int Bultos { get; set; }
        public decimal Reembolso { get; set; }
        public string Observaciones { get; set; }
        public decimal Largo { get; set; }
        public decimal Alto { get; set; }
        public decimal Ancho { get; set; }
    }

    /// <summary>
    /// Resultado de tramitar un envío con la agencia remota: el albarán que asigna la agencia, los
    /// bultos y la etiqueta (ZPL para la Zebra). <see cref="Exito"/> false trae el <see cref="Error"/>.
    /// </summary>
    public class ResultadoTramitacionRemota
    {
        public bool Exito { get; set; }
        public string Albaran { get; set; }
        public int Bultos { get; set; }
        public EtiquetaDataTrans Etiqueta { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Resultado de una operación remota sin etiqueta (hoy: anular un envío, #316). Si
    /// <see cref="Exito"/> es false, <see cref="Error"/> trae el motivo DE LA AGENCIA tal cual
    /// (p. ej. "excedido el tiempo de borrado" cuando la ventana de edición del día ya cerró):
    /// el usuario necesita saber que llegó tarde y que toca abrir incidencia con la agencia.
    /// </summary>
    public class ResultadoOperacionRemota
    {
        public bool Exito { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Estado de seguimiento de un envío, NORMALIZADO e independiente de la agencia. Los valores
    /// coinciden con <c>EnviosAgencia.Estado</c> (#247), de modo que persistir es un cast directo
    /// (sin mapas ni switches): <c>envio.Estado = (short)seguimiento.Estado</c>.
    /// </summary>
    public enum EstadoEnvioSeguimiento : short
    {
        // NestoAPI#264: la agencia no pudo determinar el estado (p. ej. GLS devuelve "no se encuentra la
        // expedición": uid de seguimiento incorrecta, o envío aún no registrado en la agencia). NO es un
        // estado real y NUNCA debe persistirse: AplicarSeguimiento lo trata como "sin cambio" para no
        // pisar un Incidentado/Entregado existente con un Tramitado falso. Valor fuera del rango 0-4.
        Desconocido = -1,
        EnCurso = 0,
        Tramitado = 1,
        Entregado = 2,
        Incidentado = 3,
        // Devuelto a origen: la agencia no pudo entregar y el paquete ha vuelto. Terminal y distinto
        // de Entregado (no llegó al cliente) y de Incidentado (eso es un problema aún en curso).
        Devuelto = 4
    }

    /// <summary>
    /// Resultado de consultar el seguimiento de un envío en la agencia, ya traducido al modelo común.
    /// Cada agencia interpreta SU propio servicio (códigos/estados/incidencias) y devuelve esto;
    /// el job y la persistencia no saben de agencias. <see cref="FechaEntrega"/> solo viene informada
    /// cuando hay entrega real; <see cref="Detalle"/> es el texto de la agencia (auditoría/diagnóstico).
    /// </summary>
    public class SeguimientoEnvioRemoto
    {
        public EstadoEnvioSeguimiento Estado { get; set; }
        public System.DateTime? FechaEntrega { get; set; }
        public string Detalle { get; set; }
    }

    /// <summary>
    /// SEGUIMIENTO remoto de una agencia (solo lectura): consultar el estado de un envío por su albarán.
    /// Lo cumplen tanto las agencias con tramitación server-side (Innovatrans) como las que SOLO
    /// exponen seguimiento (GLS/ASM, vía su web de tracking). Interfaz separado de la tramitación para
    /// que una agencia que solo sigue (GLS) no tenga que implementar Insertar/Reimprimir.
    /// </summary>
    public interface ISeguimientoAgenciaRemota
    {
        /// <summary>Consulta el seguimiento del envío (por su albarán) y lo devuelve normalizado.</summary>
        Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran);

        /// <summary>
        /// Si es true, la agencia emite logging DETALLADO en ELMAH (estados no contemplados, bultos
        /// discrepantes, fallos de tramitación...). Pensado para vigilar de cerca una agencia recién
        /// integrada (hoy Innovatrans): se pone a false cuando ya está rodada y a true en la siguiente
        /// agencia que se integre. No afecta a la operativa, solo a la verbosidad del log (NestoAPI#259).
        /// </summary>
        bool LoggingDetallado { get; }
    }

    /// <summary>
    /// Estrategia de gestión remota de una agencia (server-side): tramitar un envío contra la API de
    /// la agencia y obtener/reimprimir su etiqueta (ADEMÁS de su seguimiento, vía
    /// <see cref="ISeguimientoAgenciaRemota"/>). Hoy solo la implementa Innovatrans (DataTrans). Las
    /// agencias sin integración de tramitación (GLS, Canteras…) NO la implementan: la factory devuelve
    /// null en <c>Crear</c> y el flujo común sigue siendo solo BD (GLS sí hace seguimiento por su lado).
    /// </summary>
    public interface IAgenciaRemota : ISeguimientoAgenciaRemota
    {
        /// <summary>Inserta el envío en la agencia (asigna albarán) y obtiene su etiqueta. Crea envío REAL.</summary>
        Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio);

        /// <summary>Reimprime la etiqueta de un albarán ya insertado (no reinserta). Idempotente.</summary>
        Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null);

        /// <summary>
        /// Anula en la agencia un envío YA registrado, por su albarán (#316). Tras el éxito, el
        /// albarán deja de existir en la agencia (no se recoge ni se factura). La regla del llamante
        /// es API primero, BD después: solo si la agencia confirma se toca nuestra BD. Las agencias
        /// suelen limitar la ventana de anulación (Innovatrans: hasta el cierre del día).
        /// </summary>
        Task<ResultadoOperacionRemota> AnularAsync(string albaran);

        /// <summary>
        /// Modifica en la agencia un envío YA registrado (por su albarán) con los datos corregidos
        /// y devuelve la etiqueta REIMPRESA (#317): la etiqueta lleva CP/población impresos, así que
        /// modificar obliga a re-etiquetar. API primero, BD después, como en AnularAsync.
        /// </summary>
        Task<ResultadoTramitacionRemota> ModificarYEtiquetarAsync(DatosEnvioRemoto envio, string albaran);

        /// <summary>
        /// Intercambios crudos (petición + respuesta) hechos contra la API de la agencia durante las
        /// operaciones de esta instancia. Para auditar/depurar (sobre todo el primer envío real).
        /// </summary>
        IReadOnlyList<IntercambioRemoto> Intercambios { get; }
    }
}
