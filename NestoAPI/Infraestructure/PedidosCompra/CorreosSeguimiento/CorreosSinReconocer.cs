using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857j (Carlos 26/11/26, caso SEUR de Iberocel «Identificador de envío: A-2609942»): un correo de una agencia conocida que el
    /// lector no reconoce (ni código, ni entrega, ni estado). Se guarda APARTE (dbo.ComprasCorreosSinReconocer) para descubrir formatos nuevos:
    /// no propone proveedor ni pedido, no toca fechas, no sale en «por asignar», no se reenvía ni avisa.
    /// </summary>
    public class CorreoSinReconocerFila
    {
        public int Id { get; set; }
        public string MensajeId { get; set; }
        public DateTime Recibido { get; set; }
        public string Remitente { get; set; }
        public string Agencia { get; set; }
        public string Asunto { get; set; }
        public string Extracto { get; set; }
        public DateTime FechaAlta { get; set; }
        public DateTime? ReconocidoEl { get; set; }
        public int? CorreoId { get; set; }
    }

    /// <summary>LA REGLA (pura) de qué correo de agencia sin reconocer se apunta.</summary>
    public static class ReglaCorreoAgenciaSinReconocer
    {
        public const int LONGITUD_EXTRACTO = 2000;
        public const string MOTIVO_BOLETIN = "Boletín";
        public const string MOTIVO_ENCUESTA = "Encuesta";

        /// <summary>Encuestas y peticiones de valoración de la agencia (sin mayúsculas ni tildes, palabras enteras).</summary>
        internal static readonly string[] FrasesEncuesta =
        {
            "TU OPINION", "SU OPINION", "VALORA TU", "VALORA SU", "VALORANOS", "VALORA NUESTRO", "ENCUESTA", "QUE TAL FUE", "COMO LO HEMOS HECHO",
            "SURVEY", "RATE YOUR", "HOW DID WE DO", "YOUR FEEDBACK", "YOUR OPINION"
        };

        /// <summary>
        /// La agencia cuyo correo es (la primera cuya clase dice que lo manda ella, <see cref="IAgenciaEntrada.EsCorreoPropio"/>), o null si no
        /// es de ninguna. Solo cuenta si el análisis no lo guarda, no es una confirmación de pedido y no es de un remitente ignorado.
        /// </summary>
        public static string Agencia(CorreoCompras correo, AnalisisCorreoSeguimiento analisis, IEnumerable<IAgenciaEntrada> agencias)
        {
            if (correo == null || analisis == null || analisis.Guardar || analisis.Ignorado || analisis.Confirmacion != null)
            {
                return null;
            }
            return (agencias ?? Enumerable.Empty<IAgenciaEntrada>()).FirstOrDefault(a => a != null && a.EsCorreoPropio(correo))?.Nombre;
        }

        /// <summary>
        /// Por qué NO se apunta un correo de la agencia que el lector no reconoce: su publicidad (<see cref="BoletinComercial"/>, lo mismo que ya
        /// la descarta) o una encuesta. Null = se apunta.
        /// </summary>
        public static string MotivoDescarte(CorreoCompras correo)
        {
            if (BoletinComercial.Es(correo))
            {
                return MOTIVO_BOLETIN;
            }
            string texto = " " + TextoCorreo.Normalizar((correo?.Asunto ?? string.Empty) + "\n" + correo?.Texto) + " ";
            return FrasesEncuesta.Any(f => texto.Contains(" " + f + " ")) ? MOTIVO_ENCUESTA : null;
        }

        /// <summary>La fila que se apunta (sin Id). Null si no tiene clave.</summary>
        public static CorreoSinReconocerFila Fila(CorreoCompras correo, string agencia)
        {
            string clave = correo?.Clave();
            if (clave == null || string.IsNullOrWhiteSpace(agencia))
            {
                return null;
            }
            string texto = (correo.Texto ?? string.Empty).Trim();
            return new CorreoSinReconocerFila
            {
                MensajeId = clave,
                Recibido = correo.Recibido,
                Remitente = Recortar(correo.RemitenteDireccion?.Trim().ToLowerInvariant(), 255),
                Agencia = Recortar(agencia.Trim(), 50),
                Asunto = Recortar(correo.Asunto?.Trim(), ServicioCorreosSeguimientoCompras.LONGITUD_ASUNTO),
                Extracto = Recortar(texto, LONGITUD_EXTRACTO)
            };
        }

        private static string Recortar(string texto, int longitud) =>
            texto == null ? null : texto.Length > longitud ? texto.Substring(0, longitud) : texto;
    }

    public interface IRepositorioCorreosSinReconocer
    {
        /// <summary>¿Existe dbo.ComprasCorreosSinReconocer (Scripts/Issue857j)? Con caché; nunca lanza.</summary>
        bool Disponible();
        /// <summary>False si ya estaba (por MensajeId).</summary>
        Task<bool> Insertar(CorreoSinReconocerFila fila);
        /// <summary>Lo marca como reconocido (Reanalizar ya lo ha guardado como correo normal). False si no estaba o ya lo estaba.</summary>
        Task<bool> MarcarReconocido(string mensajeId, int? correoId);
        /// <summary>Los no reconocidos recibidos desde ese día, lo más reciente primero.</summary>
        Task<List<CorreoSinReconocerFila>> Leer(DateTime desde);
    }

    public class RepositorioCorreosSinReconocer : IRepositorioCorreosSinReconocer
    {
        private static readonly TimeSpan CACHE = TimeSpan.FromMinutes(10);
        private static readonly object bloqueo = new object();
        private static bool? existe;
        private static DateTime comprobadaUtc;

        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ComprasCorreosSinReconocer', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_INSERTAR = @"
IF NOT EXISTS (SELECT 1 FROM dbo.ComprasCorreosSinReconocer WITH (UPDLOCK, HOLDLOCK) WHERE MensajeId = @mensajeId)
    INSERT INTO dbo.ComprasCorreosSinReconocer (MensajeId, Recibido, Remitente, Agencia, Asunto, Extracto)
    VALUES (@mensajeId, @recibido, @remitente, @agencia, @asunto, @extracto);";
        internal const string SQL_MARCAR = @"
UPDATE dbo.ComprasCorreosSinReconocer SET ReconocidoEl = GETDATE(), CorreoId = @correoId
WHERE MensajeId = @mensajeId AND ReconocidoEl IS NULL";
        internal const string SQL_LEER = @"
SELECT Id, MensajeId, Recibido, Remitente, Agencia, Asunto, Extracto, FechaAlta, ReconocidoEl, CorreoId
FROM dbo.ComprasCorreosSinReconocer WITH (NOLOCK)
WHERE Recibido >= @desde AND ReconocidoEl IS NULL
ORDER BY Recibido DESC, Id DESC";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioCorreosSinReconocer(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
        }

        public bool Disponible()
        {
            lock (bloqueo)
            {
                if (existe.HasValue && DateTime.UtcNow - comprobadaUtc < CACHE)
                {
                    return existe.Value;
                }
            }
            bool resultado;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    resultado = db.Database.SqlQuery<bool>(SQL_EXISTE).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                resultado = false;
            }
            lock (bloqueo)
            {
                existe = resultado;
                comprobadaUtc = DateTime.UtcNow;
            }
            return resultado;
        }

        public async Task<bool> Insertar(CorreoSinReconocerFila fila)
        {
            using (NVEntities db = crearContexto())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR,
                        Texto("@mensajeId", fila.MensajeId, 450),
                        new SqlParameter("@recibido", SqlDbType.DateTime) { Value = fila.Recibido },
                        Texto("@remitente", fila.Remitente, 255),
                        Texto("@agencia", fila.Agencia, 50),
                        Texto("@asunto", fila.Asunto, 255),
                        Texto("@extracto", fila.Extracto, ReglaCorreoAgenciaSinReconocer.LONGITUD_EXTRACTO)).ConfigureAwait(false);
                    return filas > 0;
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    return false; // otro lo ha apuntado a la vez
                }
            }
        }

        public async Task<bool> MarcarReconocido(string mensajeId, int? correoId)
        {
            using (NVEntities db = crearContexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_MARCAR, Texto("@mensajeId", mensajeId, 450),
                    new SqlParameter("@correoId", SqlDbType.Int) { Value = (object)correoId ?? DBNull.Value }).ConfigureAwait(false);
                return filas > 0;
            }
        }

        public async Task<List<CorreoSinReconocerFila>> Leer(DateTime desde)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<CorreoSinReconocerFila>(SQL_LEER, new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        private static SqlParameter Texto(string nombre, string valor, int longitud)
        {
            string recortado = valor == null ? null : valor.Length > longitud ? valor.Substring(0, longitud) : valor;
            return new SqlParameter(nombre, SqlDbType.NVarChar, longitud) { Value = (object)recortado ?? DBNull.Value };
        }
    }
}
