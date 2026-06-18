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
    /// Estrategia de gestión remota de una agencia (server-side): tramitar un envío contra la API de
    /// la agencia y obtener/reimprimir su etiqueta. Hoy solo la implementa Innovatrans (DataTrans);
    /// GLS/ASM será la siguiente. Las agencias sin integración (Canteras, etc.) NO la implementan: la
    /// factory devuelve null y el flujo común sigue siendo solo BD.
    /// </summary>
    public interface IAgenciaRemota
    {
        /// <summary>Inserta el envío en la agencia (asigna albarán) y obtiene su etiqueta. Crea envío REAL.</summary>
        Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio);

        /// <summary>Reimprime la etiqueta de un albarán ya insertado (no reinserta). Idempotente.</summary>
        Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null);
    }
}
