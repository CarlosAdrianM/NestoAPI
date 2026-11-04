using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#811: PUT api/Eventos/Inscripciones/{id}/Asistentes.</summary>
    public class CambiarAsistentesInscripcionDTO
    {
        /// <summary>Cuántas personas vienen con esta inscripción (mínimo 1).</summary>
        public int? Asistentes { get; set; }
    }

    /// <summary>
    /// NestoAPI#811 (Reina, sugerencia 1006; Carlos 04/11/26): una inscripción es de un centro (cliente/contacto) y puede traer
    /// varias personas. Cada inscripción ocupa <c>Asistentes</c> plazas y, en un evento «gratuito por compra», hace falta una
    /// unidad comprada del producto requerido por asistente. Sin la columna (o sin decir nada), 1: lo de siempre.
    /// </summary>
    public static class ReglasAsistentesEvento
    {
        public const int POR_DEFECTO = 1;

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>Lo que viene en la petición: null = 1; menos de 1, 400.</summary>
        public static int Normalizar(int? asistentes)
        {
            if (!asistentes.HasValue)
            {
                return POR_DEFECTO;
            }
            if (asistentes.Value < 1)
            {
                throw new NestoBusinessException("El número de asistentes tiene que ser al menos 1.");
            }
            return asistentes.Value;
        }

        /// <summary>Los asistentes de una inscripción: los guardados si los hay (y son 1 o más); si no, 1.</summary>
        public static int De(int inscripcion, IReadOnlyDictionary<int, int> asistentes) =>
            asistentes != null && asistentes.TryGetValue(inscripcion, out int n) && n >= 1 ? n : POR_DEFECTO;

        /// <summary>Las plazas que ocupan esas inscripciones: la suma de sus asistentes, solo las que ocupan plaza.</summary>
        public static int PlazasOcupadas(IEnumerable<EventoInscripcion> inscripciones, IReadOnlyDictionary<int, int> asistentes) =>
            (inscripciones ?? Enumerable.Empty<EventoInscripcion>())
                .Where(i => i != null && ReglasInscripcionEvento.OcupaPlaza(i))
                .Sum(i => De(i.Id, asistentes));

        /// <summary>¿Caben <paramref name="asistentes"/> personas más con las plazas ya ocupadas? Sin Plazas, siempre.</summary>
        public static bool Caben(Evento evento, int ocupadas, int asistentes) =>
            evento?.Plazas.HasValue != true || ocupadas + asistentes <= evento.Plazas.Value;

        /// <summary>Plazas libres (nunca negativas). Sin Plazas, null.</summary>
        public static int? Libres(Evento evento, int ocupadas) =>
            evento?.Plazas.HasValue == true ? Math.Max(0, evento.Plazas.Value - ocupadas) : (int?)null;

        /// <summary>Quedan plazas, pero no tantas como asistentes: «solo queda 1 plaza libre y quiere inscribir 2 asistentes».</summary>
        public static string MensajeNoCaben(Evento evento, int libres, int asistentes) =>
            $"En «{evento?.Titulo?.Trim()}» {(libres == 1 ? "solo queda 1 plaza libre" : $"solo quedan {libres} plazas libres")} " +
            $"y quiere inscribir {Personas(asistentes)}. Inscriba como mucho {Personas(libres)}, por favor.";

        /// <summary>Al confirmar la asistencia de una inscripción de varias personas que ya no caben.</summary>
        public static string MensajeNoCabenAlConfirmar(Evento evento, int libres, int asistentes) =>
            $"Lo sentimos: en «{evento?.Titulo?.Trim()}» {(libres == 1 ? "solo queda 1 plaza libre" : $"solo quedan {libres} plazas libres")} " +
            $"y su inscripción es de {Personas(asistentes)}. Su plaza quedó libre cuando nos dijo que no podría ir.";

        public static string Personas(int n) => n == 1 ? "1 asistente" : $"{n} asistentes";

        // ---------------- Requisito de compra por unidades ----------------

        /// <summary>Error de unidades insuficientes: el mismo código que el de la compra (EVENTO_REQUIERE_COMPRA).</summary>
        public const string ERROR_REQUIERE_COMPRA = ReglasProductosRequeridosEvento.ERROR_REQUIERE_COMPRA;

        /// <summary>
        /// Para cuántos asistentes alcanza lo comprado: con el modo «Alguno», la suma de las unidades de todos los productos
        /// requeridos (son alternativas); con «Todos», las del que menos tiene (cada asistente necesita uno de cada).
        /// </summary>
        public static int UnidadesQueCuentan(IEnumerable<ProductoRequeridoLeido> requeridos, IReadOnlyDictionary<string, int> unidades, bool todos)
        {
            List<string> productos = (requeridos ?? Enumerable.Empty<ProductoRequeridoLeido>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Producto))
                .Select(r => r.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!productos.Any())
            {
                return 0;
            }
            var porProducto = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> u in unidades ?? new Dictionary<string, int>())
            {
                if (!string.IsNullOrWhiteSpace(u.Key))
                {
                    porProducto[u.Key.Trim()] = (porProducto.TryGetValue(u.Key.Trim(), out int previas) ? previas : 0) + u.Value;
                }
            }
            IEnumerable<int> deCada = productos.Select(p => porProducto.TryGetValue(p, out int n) ? Math.Max(0, n) : 0);
            return todos ? deCada.Min() : deCada.Sum();
        }

        /// <summary>
        /// «El cliente 41286/0 ha comprado 1 unidad de 45363 (LIFTING COREANO KIT) desde el 21/08/2026 y quiere inscribir 2
        /// asistentes en «…»: hace falta una unidad comprada por asistente.»
        /// </summary>
        public static string MensajeFaltanUnidades(string titulo, string cliente, string contacto, List<ProductoRequeridoLeido> requeridos,
            bool todos, int compradas, int asistentes, DateTime desde)
        {
            string quien = string.IsNullOrWhiteSpace(contacto) ? $"El cliente {cliente}" : $"El cliente {cliente}/{contacto}";
            string que = todos
                ? $"ha comprado todos los productos requeridos para {Personas(compradas)}"
                : $"ha comprado {(compradas == 1 ? "1 unidad" : $"{compradas} unidades")} de {ReglasProductosRequeridosEvento.Lista(requeridos)}";
            string porAsistente = todos
                ? "hace falta una unidad de cada producto requerido por asistente"
                : "hace falta una unidad comprada por asistente";
            return $"{quien} {que} desde el {desde.ToString("dd/MM/yyyy", es)} y quiere inscribir {Personas(asistentes)} en " +
                $"«{titulo?.Trim()}»: {porAsistente}.";
        }

        public static NestoBusinessException FaltanUnidades(string mensaje, List<ProductoRequeridoLeido> requeridos, bool todos,
            int compradas, int asistentes, DateTime desde) =>
            new NestoBusinessException(mensaje,
                new ErrorContext { ErrorCode = ERROR_REQUIERE_COMPRA }
                    .WithData("productos", requeridos.OrderBy(r => r.Producto).Select(r => new ProductoRequeridoEventoDTO { Producto = r.Producto, Nombre = r.Nombre }).ToList())
                    .WithData("desde", desde.Date)
                    .WithData("modo", ReglasProductosRequeridosEvento.Modo(todos))
                    .WithData("unidadesCompradas", compradas)
                    .WithData("asistentes", asistentes))
            { StatusCode = HttpStatusCode.BadRequest, IsWarning = true };

        /// <summary>NestoAPI#745: Dirección inscribe aunque lo comprado no llegue para todos los asistentes.</summary>
        public static string AvisoFaltanUnidadesPorDireccion(string mensaje) =>
            $"Inscrita por Dirección con menos compra de la que pide el evento. {mensaje}";
    }

    /// <summary>
    /// NestoAPI#811: la columna EventosInscripciones.Asistentes (Scripts/Issue811_EventosInscripcionesAsistentes.sql), fuera del
    /// EDMX. Sin la columna: todas valen 1 (lo de antes) y guardar contesta false.
    /// </summary>
    public interface IRepositorioAsistentesEventos
    {
        /// <summary>¿Está la columna?</summary>
        bool Disponible();
        /// <summary>Inscripción → asistentes (solo las que tienen más de 1).</summary>
        Task<Dictionary<int, int>> Leer(IReadOnlyCollection<int> inscripciones);
        /// <summary>Guarda los asistentes. False si la columna todavía no existe.</summary>
        Task<bool> Poner(int inscripcion, int asistentes);
    }

    public class RepositorioAsistentesEventos : IRepositorioAsistentesEventos
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.EventosInscripciones', 'Asistentes') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER = "SELECT Id, Asistentes FROM dbo.EventosInscripciones WITH (NOLOCK) WHERE Asistentes > 1 AND Id IN ({0})";
        internal const string SQL_PONER = "UPDATE dbo.EventosInscripciones SET Asistentes = @asistentes WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos columna = new ComprobadorEsquemaEventos(SQL_EXISTE);
        private readonly NVEntities db;

        public RepositorioAsistentesEventos(NVEntities db)
        {
            this.db = db;
        }

        public bool Disponible() => columna.Existe(db);

        public async Task<Dictionary<int, int>> Leer(IReadOnlyCollection<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            var resultado = new Dictionary<int, int>();
            if (!ids.Any() || !columna.Existe(db))
            {
                return resultado;
            }
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                foreach (FilaAsistentes fila in await db.Database
                    .SqlQuery<FilaAsistentes>(string.Format(SQL_LEER, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false))
                {
                    resultado[fila.Id] = fila.Asistentes;
                }
            }
            return resultado;
        }

        public async Task<bool> Poner(int inscripcion, int asistentes)
        {
            if (!columna.Existe(db))
            {
                return false;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_PONER,
                new SqlParameter("@asistentes", SqlDbType.Int) { Value = Math.Max(1, asistentes) },
                new SqlParameter("@id", SqlDbType.Int) { Value = inscripcion }).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Para contar plazas: si no hay repositorio o algo falla (p. ej. el script a medias), todas valen 1 y se sigue (a ELMAH).
        /// Nunca rompe una inscripción ni una lista.
        /// </summary>
        internal static async Task<Dictionary<int, int>> LeerSinFallar(IRepositorioAsistentesEventos repositorio, IEnumerable<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (repositorio == null || !ids.Any())
            {
                return new Dictionary<int, int>();
            }
            try
            {
                return await repositorio.Leer(ids).ConfigureAwait(false) ?? new Dictionary<int, int>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #811] No se han podido leer los asistentes de las inscripciones (se cuenta 1 por inscripción): {ex.Message}", ex));
                return new Dictionary<int, int>();
            }
        }

        private class FilaAsistentes
        {
            public int Id { get; set; }
            public int Asistentes { get; set; }
        }
    }
}
