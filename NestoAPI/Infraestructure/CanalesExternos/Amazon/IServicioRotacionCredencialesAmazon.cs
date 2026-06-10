using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: orquestación de la rotación automática del client_secret LWA de Amazon.
    /// </summary>
    public interface IServicioRotacionCredencialesAmazon
    {
        /// <summary>
        /// Sondea la cola SQS: persiste cualquier secreto nuevo recibido y, si el secreto
        /// almacenado está dentro del umbral de caducidad, dispara una rotación.
        /// </summary>
        Task<ResultadoProcesoRotacion> ProcesarColaAsync();

        /// <summary>Fuerza una rotación inmediata (uso manual/administrativo). Devuelve true si rotó.</summary>
        Task<bool> RotarAhoraAsync();

        /// <summary>Credencial actual almacenada (para servirla a los clientes vía API).</summary>
        AmazonSpApiCredencial ObtenerCredencialActual();
    }

    public class ResultadoProcesoRotacion
    {
        public int MensajesRecibidos { get; set; }
        public int SecretosPersistidos { get; set; }
        public bool RotacionDisparada { get; set; }
        public List<string> Detalles { get; } = new List<string>();

        public string Resumen()
        {
            return $"MensajesRecibidos={MensajesRecibidos}, SecretosPersistidos={SecretosPersistidos}, " +
                   $"RotacionDisparada={RotacionDisparada}. " + string.Join(" | ", Detalles);
        }
    }
}
