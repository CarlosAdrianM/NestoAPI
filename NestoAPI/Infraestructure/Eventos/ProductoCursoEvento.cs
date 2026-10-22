using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Configuration;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>POST api/Eventos/{id}/ProductoCurso (para Nesto#517).</summary>
    public class PeticionProductoCursoDTO
    {
        /// <summary>«Reembolsable» o «NoReembolsable».</summary>
        public string Modalidad { get; set; }
    }

    /// <summary>La respuesta de POST api/Eventos/{id}/ProductoCurso.</summary>
    public class ProductoCursoDTO
    {
        /// <summary>EVENTO00123 (reembolsable) o EVENTO00123NR (no reembolsable).</summary>
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>true si se ha dado de alta ahora; false si ya existía (idempotente).</summary>
        public bool Creado { get; set; }
    }

    /// <summary>
    /// Decisión de Carlos (22/10/26): la referencia del producto de un curso. Un evento puede llevar:
    /// <list type="bullet">
    /// <item>su propia referencia, creada con POST api/Eventos/{id}/ProductoCurso: <c>EVENTO{Id:00000}</c> para la reembolsable
    /// y <c>EVENTO{Id:00000}NR</c> para la no reembolsable. La referencia dice el evento y la modalidad.</item>
    /// <item>un producto FORZADO: un curso que ya existe (grupo CUR, p. ej. 90077), que pueden compartir varios eventos. Una sola
    /// referencia para todas las modalidades: la modalidad sale de lo pagado.</item>
    /// </list>
    /// En los dos casos se guarda en Eventos.ProductoCurso.
    /// </summary>
    public static class ReglasProductoCurso
    {
        public const string PREFIJO = "EVENTO";
        public const string SUFIJO_NO_REEMBOLSABLE = "NR";
        /// <summary>El curso cuya ficha se copia al crear la referencia de un evento (Web.config Eventos:ProductoCursoPlantilla).</summary>
        public const string CLAVE_PLANTILLA = "Eventos:ProductoCursoPlantilla";
        public const string PLANTILLA_POR_DEFECTO = "90077";
        /// <summary>Productos.Nombre es char(50).</summary>
        public const int LONGITUD_NOMBRE = 50;

        private static readonly Regex convencion = new Regex(@"^EVENTO(\d{5,9})(NR)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Plantilla
        {
            get
            {
                string valor = ConfigurationManager.AppSettings[CLAVE_PLANTILLA]?.Trim();
                return string.IsNullOrEmpty(valor) ? PLANTILLA_POR_DEFECTO : valor;
            }
        }

        /// <summary>EVENTO00123 / EVENTO00123NR. Solo para las dos modalidades de pago.</summary>
        public static string Referencia(int eventoId, ModalidadInscripcionEvento modalidad)
        {
            if (modalidad == ModalidadInscripcionEvento.Gratuita)
            {
                throw new NestoBusinessException("Un evento gratuito no se factura: no necesita producto.");
            }
            return PREFIJO + eventoId.ToString("00000", CultureInfo.InvariantCulture)
                + (modalidad == ModalidadInscripcionEvento.NoReembolsable ? SUFIJO_NO_REEMBOLSABLE : string.Empty);
        }

        /// <summary>Si la referencia sigue la convención, el evento y la modalidad que dice; si no (un producto forzado), null.</summary>
        public static (int EventoId, ModalidadInscripcionEvento Modalidad)? Interpretar(string producto)
        {
            Match m = convencion.Match(producto?.Trim() ?? string.Empty);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                return null;
            }
            return (id, m.Groups[2].Success ? ModalidadInscripcionEvento.NoReembolsable : ModalidadInscripcionEvento.Reembolsable);
        }

        /// <summary>¿Es una referencia propia de ESTE evento (EVENTO{Id} o EVENTO{Id}NR)?</summary>
        public static bool EsDelEvento(string producto, int eventoId) => Interpretar(producto)?.EventoId == eventoId;

        /// <summary>
        /// El producto con el que se factura el curso de una inscripción: si el evento tiene su propia referencia, la de su
        /// modalidad (EVENTO00123NR para la no reembolsable), siempre que exista; si no, Eventos.ProductoCurso tal cual (el
        /// forzado, o la única referencia propia que se haya creado).
        /// </summary>
        public static string ParaFacturar(Evento evento, ModalidadInscripcionEvento modalidad, Func<string, bool> existe)
        {
            string producto = evento?.ProductoCurso?.Trim();
            if (string.IsNullOrEmpty(producto) || modalidad == ModalidadInscripcionEvento.Gratuita || !EsDelEvento(producto, evento.Id))
            {
                return producto;
            }
            string propia = Referencia(evento.Id, modalidad);
            return propia != producto && existe != null && existe(propia) ? propia : producto;
        }

        /// <summary>
        /// La que queda en Eventos.ProductoCurso al crear <paramref name="nueva"/>: si el evento no tenía, o tenía un producto
        /// forzado, la nueva (quien la crea quiere su propia referencia); si ya tenía una propia, se prefiere la reembolsable.
        /// </summary>
        public static string ProductoDelEvento(Evento evento, string nueva)
        {
            string actual = evento?.ProductoCurso?.Trim();
            if (string.IsNullOrEmpty(actual) || !EsDelEvento(actual, evento.Id))
            {
                return nueva;
            }
            return Interpretar(nueva)?.Modalidad == ModalidadInscripcionEvento.Reembolsable ? nueva : actual;
        }

        /// <summary>El título del evento, recortado a Productos.Nombre (char(50)).</summary>
        public static string Nombre(Evento evento)
        {
            string titulo = evento?.Titulo?.Trim() ?? string.Empty;
            return titulo.Length > LONGITUD_NOMBRE ? titulo.Substring(0, LONGITUD_NOMBRE).TrimEnd() : titulo;
        }
    }

    /// <summary>El alta de la referencia del curso en Productos (por SQL, copiando la ficha de un curso existente).</summary>
    public interface IRepositorioProductoCurso
    {
        /// <summary>El nombre del producto, o null si no existe en esa empresa.</summary>
        Task<string> LeerNombre(string empresa, string producto);
        /// <summary>
        /// Da de alta <paramref name="producto"/> copiando la ficha de <paramref name="plantilla"/> (grupo, subgrupo, familia,
        /// ficticio, IVA…) con su nombre y PVP. Devuelve las filas insertadas (0 si ya existía o si la plantilla no está en esa empresa).
        /// </summary>
        Task<int> CrearDesdePlantilla(string empresa, string producto, string plantilla, string nombre, decimal pvp, string comentarios, string usuario);
    }

    /// <summary>
    /// No hay procedimiento de alta de productos en el repo: se sigue el patrón de Scripts/Issue613_Cupones.sql (CUPONEVENTO,
    /// INSERT … SELECT de la ficha de un producto modelo). Se da de alta en la empresa del evento y en la espejo (3) si la
    /// plantilla está allí, porque la factura del curso puede traspasarse a la empresa espejo.
    /// </summary>
    public class RepositorioProductoCurso : IRepositorioProductoCurso
    {
        internal const string SQL_CREAR = @"
INSERT INTO dbo.Productos (Empresa, [Número], Nombre, Grupo, PVP, [IVA Soportado], [IVA Repercutido], Comentarios, Estado,
       CodBarras, [Aplicar Dto], SubGrupo, [Tamaño], UnidadMedida, Familia, Foto, PrecioMedio, Ficticio, FechaInicio, FechaFinal,
       NumeroSesiones, MateriaPrima, UnidadesPorEtiqueta, VariasOpciones, Ubicar, ProductoAnterior, GestionStockManual,
       RoturaStockProveedor, ProductosPorCaja, CantidadMinimaPedido, Revisado, ComentariosFactura, NecesitaNumSerie, Usuario,
       [Fecha Modificación], ExclusivoProfesional)
SELECT t.Empresa, @producto, @nombre, t.Grupo, @pvp, t.[IVA Soportado], t.[IVA Repercutido], @comentarios, t.Estado,
       NULL, t.[Aplicar Dto], t.SubGrupo, t.[Tamaño], t.UnidadMedida, t.Familia, NULL, 0, t.Ficticio, NULL, NULL,
       t.NumeroSesiones, t.MateriaPrima, t.UnidadesPorEtiqueta, t.VariasOpciones, t.Ubicar, NULL, t.GestionStockManual,
       t.RoturaStockProveedor, t.ProductosPorCaja, t.CantidadMinimaPedido, t.Revisado, NULL, t.NecesitaNumSerie, @usuario,
       GETDATE(), t.ExclusivoProfesional
FROM dbo.Productos t
WHERE t.Empresa = @empresa AND t.[Número] = @plantilla
      AND NOT EXISTS (SELECT 1 FROM dbo.Productos x WHERE x.Empresa = @empresa AND x.[Número] = @producto)";

        private readonly NVEntities db;

        public RepositorioProductoCurso(NVEntities db)
        {
            this.db = db;
        }

        public async Task<string> LeerNombre(string empresa, string producto)
        {
            var fila = await db.Productos.Where(p => p.Empresa == empresa && p.Número == producto)
                .Select(p => new { p.Nombre }).FirstOrDefaultAsync().ConfigureAwait(false);
            return fila == null ? null : (fila.Nombre?.Trim() ?? string.Empty);
        }

        public Task<int> CrearDesdePlantilla(string empresa, string producto, string plantilla, string nombre, decimal pvp, string comentarios, string usuario) =>
            db.Database.ExecuteSqlCommandAsync(SQL_CREAR,
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@producto", producto),
                new SqlParameter("@plantilla", plantilla),
                new SqlParameter("@nombre", nombre),
                new SqlParameter("@pvp", pvp),
                new SqlParameter("@comentarios", comentarios),
                new SqlParameter("@usuario", usuario));
    }
}
