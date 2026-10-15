using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#641 (corte 3 de #639): un pasillo del almacén tal como lo guardan las tablas RecorridoPasillos y
    /// RecorridoCalles (Scripts/Issue641_RecorridoCalles.sql). Es la fila que lee <see cref="LectorOrdenRecorridoSql"/>
    /// y la que leerá y escribirá el mantenimiento del corte 4 (#642 / Nesto#528).
    /// </summary>
    public class PasilloRecorrido
    {
        /// <summary>char(3), como en Ubicaciones («004»).</summary>
        public string Pasillo { get; set; }
        /// <summary>Para que alguien nuevo lo encuentre («Esmaltes»). Null: sin nombre. Aún no cambia el orden.</summary>
        public string Nombre { get; set; }
        /// <summary>RecorridoCalles.Id. Null: el pasillo no está en ninguna calle (va al final, como hoy).</summary>
        public int? IdCalle { get; set; }
        /// <summary>El nombre corto de la calle («a», «b»…).</summary>
        public string Calle { get; set; }
        /// <summary>RecorridoCalles.Orden (con saltos 10, 20… para poder meter una entre dos).</summary>
        public int? OrdenCalle { get; set; }
        /// <summary>'A' columnas ascendentes (ida), 'D' descendentes (vuelta).</summary>
        public string Sentido { get; set; }
        /// <summary>El orden del pasillo dentro de su calle (en la h, el 9 antes que el 8).</summary>
        public int? Posicion { get; set; }
    }

    /// <summary>
    /// NestoAPI#641: la regla ÚNICA del orden de recorrido de un almacén (picking, reposiciones, pendiente de ubicar y
    /// huecos alternativos). Sin base de datos: la monta <see cref="ILectorOrdenRecorrido"/> con las calles del almacén.
    ///
    /// <para><b>Calle</b>: un grupo de pasillos que se recorren a la vez (las dos paredes del mismo pasillo físico), con
    /// su orden y su sentido. Dentro de una calle, por cada columna (orientada según el sentido): primero lo que se coge a
    /// mano (fila ≥ <see cref="FilaAMano"/>) de todos los pasillos de la calle, en su orden; después, con la escalera,
    /// las filas de arriba (1 hasta FilaAMano - 1) de todos esos pasillos; luego la siguiente columna.</para>
    ///
    /// <para><b>Filas dentro de cada nivel</b> (decisión del corte 3): las filas se numeran de arriba abajo. A mano, en
    /// orden ascendente (de la más alta que se alcanza hacia el suelo, como hoy); con la escalera, descendente (3, 2, 1:
    /// se va subiendo peldaño a peldaño y se baja una vez). La fila 000 (suelo o palé, sin altura: pasillos 12, 13 y del
    /// 20 en adelante) y cualquier fila que no sea un número cuentan como «a mano».</para>
    ///
    /// <para><b>Pasillo sin calle</b>: va después de todas las calles, por pasillo, columna y fila ascendentes, sin
    /// niveles, exactamente como hoy. Por eso, <b>sin filas en las tablas el orden es idéntico al de antes</b>, y una
    /// columna o una fila nuevas en un pasillo con calle heredan su sitio solas.</para>
    /// </summary>
    public sealed class OrdenRecorrido
    {
        public const int FILA_A_MANO_POR_DEFECTO = 4;
        public const string SENTIDO_ASCENDENTE = "A";
        public const string SENTIDO_DESCENDENTE = "D";

        /// <summary>Sin calles: pasillo, columna y fila ascendentes (el orden de siempre).</summary>
        public static readonly OrdenRecorrido ComoHoy = new OrdenRecorrido(null, FILA_A_MANO_POR_DEFECTO);

        private readonly Dictionary<string, PasilloRecorrido> pasillos;

        public OrdenRecorrido(IEnumerable<PasilloRecorrido> pasillos, int? filaAMano = null)
        {
            this.pasillos = new Dictionary<string, PasilloRecorrido>(StringComparer.OrdinalIgnoreCase);
            foreach (PasilloRecorrido pasillo in pasillos ?? Enumerable.Empty<PasilloRecorrido>())
            {
                string clave = NormalizarPasillo(pasillo?.Pasillo);
                if (clave != null && !this.pasillos.ContainsKey(clave))
                {
                    this.pasillos[clave] = pasillo;
                }
            }
            FilaAMano = filaAMano.HasValue && filaAMano.Value > 0 ? filaAMano.Value : FILA_A_MANO_POR_DEFECTO;
        }

        /// <summary>Desde qué fila se llega con la mano (Almacenes.FilaAMano; 4 si no se dice).</summary>
        public int FilaAMano { get; }

        /// <summary>Si algún pasillo está en una calle (si no, el orden es el de siempre).</summary>
        public bool TieneCalles => pasillos.Values.Any(p => p.IdCalle.HasValue);

        /// <summary>Los pasillos que tienen fila en RecorridoPasillos (con calle o solo con nombre).</summary>
        public IReadOnlyCollection<PasilloRecorrido> Pasillos => pasillos.Values;

        /// <summary>El nombre que se le ha puesto al pasillo («Esmaltes»), o null.</summary>
        public string NombrePasillo(string pasillo)
        {
            string clave = NormalizarPasillo(pasillo);
            string nombre = clave != null && pasillos.TryGetValue(clave, out PasilloRecorrido fila) ? fila.Nombre?.Trim() : null;
            return string.IsNullOrEmpty(nombre) ? null : nombre;
        }

        /// <summary>«4» y « 004 » son el pasillo «004». Null si viene en blanco.</summary>
        public static string NormalizarPasillo(string pasillo)
        {
            string limpio = pasillo?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            return limpio.Length < 3 && limpio.All(char.IsDigit) ? limpio.PadLeft(3, '0') : limpio;
        }

        /// <summary>
        /// Dónde va un hueco en el recorrido. Lo que no tiene pasillo cae en la parte «sin calle»; quien lo quiera al
        /// final (el picking) lo aparta antes, como siempre.
        /// </summary>
        public ClaveRecorrido Clave(string pasillo, string columna, string fila)
        {
            string clave = NormalizarPasillo(pasillo);
            if (clave != null && pasillos.TryGetValue(clave, out PasilloRecorrido calle) && calle.IdCalle.HasValue)
            {
                int nivel = EsDeEscalera(fila) ? ClaveRecorrido.NIVEL_ESCALERA : ClaveRecorrido.NIVEL_A_MANO;
                return ClaveRecorrido.EnCalle(
                    calle.OrdenCalle ?? int.MaxValue,
                    calle.IdCalle.Value,
                    columna?.Trim(),
                    string.Equals(calle.Sentido?.Trim(), SENTIDO_DESCENDENTE, StringComparison.OrdinalIgnoreCase),
                    nivel,
                    calle.Posicion ?? int.MaxValue,
                    clave,
                    fila?.Trim());
            }
            return ClaveRecorrido.SinCalle(pasillo?.Trim(), columna?.Trim(), fila?.Trim());
        }

        /// <summary>Las filas 1 hasta FilaAMano - 1 necesitan escalera. La 000 o una que no sea número, no.</summary>
        public bool EsDeEscalera(string fila)
        {
            return int.TryParse(fila?.Trim(), out int numero) && numero >= 1 && numero < FilaAMano;
        }

        /// <summary>Ordena cualquier cosa con hueco por el recorrido (se puede seguir con ThenBy).</summary>
        public IOrderedEnumerable<T> Ordenar<T>(IEnumerable<T> elementos, Func<T, string> pasillo, Func<T, string> columna, Func<T, string> fila)
        {
            return (elementos ?? Enumerable.Empty<T>()).OrderBy(e => Clave(pasillo(e), columna(e), fila(e)));
        }
    }

    /// <summary>
    /// NestoAPI#641: la clave de orden de un hueco: (orden de la calle, columna orientada según el sentido, nivel [0 a
    /// mano, 1 escalera], posición del pasillo en la calle, fila). Los huecos sin calle van detrás, por pasillo, columna y
    /// fila (comparación ordinal, como hasta ahora).
    /// </summary>
    public sealed class ClaveRecorrido : IComparable<ClaveRecorrido>, IComparable
    {
        public const int NIVEL_A_MANO = 0;
        public const int NIVEL_ESCALERA = 1;

        private ClaveRecorrido() { }

        public bool ConCalle { get; private set; }
        public int OrdenCalle { get; private set; }
        public int IdCalle { get; private set; }
        public string Columna { get; private set; }
        public bool Descendente { get; private set; }
        public int Nivel { get; private set; }
        public int Posicion { get; private set; }
        public string Pasillo { get; private set; }
        public string Fila { get; private set; }

        internal static ClaveRecorrido EnCalle(int ordenCalle, int idCalle, string columna, bool descendente, int nivel, int posicion, string pasillo, string fila)
        {
            return new ClaveRecorrido
            {
                ConCalle = true,
                OrdenCalle = ordenCalle,
                IdCalle = idCalle,
                Columna = columna,
                Descendente = descendente,
                Nivel = nivel,
                Posicion = posicion,
                Pasillo = pasillo,
                Fila = fila
            };
        }

        internal static ClaveRecorrido SinCalle(string pasillo, string columna, string fila)
        {
            return new ClaveRecorrido { Pasillo = pasillo, Columna = columna, Fila = fila };
        }

        public int CompareTo(ClaveRecorrido otra)
        {
            if (otra == null)
            {
                return -1;
            }
            if (ConCalle != otra.ConCalle)
            {
                return ConCalle ? -1 : 1;
            }
            int c;
            if (!ConCalle)
            {
                // Exactamente el orden de siempre: pasillo, columna, fila
                c = string.CompareOrdinal(Pasillo, otra.Pasillo);
                if (c != 0) return c;
                c = string.CompareOrdinal(Columna, otra.Columna);
                return c != 0 ? c : string.CompareOrdinal(Fila, otra.Fila);
            }

            c = OrdenCalle.CompareTo(otra.OrdenCalle);
            if (c != 0) return c;
            c = IdCalle.CompareTo(otra.IdCalle);
            if (c != 0) return c;
            c = string.CompareOrdinal(Columna, otra.Columna);
            if (c != 0) return Descendente ? -c : c;
            c = Nivel.CompareTo(otra.Nivel);
            if (c != 0) return c;
            c = Posicion.CompareTo(otra.Posicion);
            if (c != 0) return c;
            c = string.CompareOrdinal(Pasillo, otra.Pasillo);
            if (c != 0) return c;
            c = string.CompareOrdinal(Fila, otra.Fila);
            // A mano, de la más alta que se alcanza hacia el suelo; con la escalera, de abajo arriba (3, 2, 1)
            return Nivel == NIVEL_ESCALERA ? -c : c;
        }

        int IComparable.CompareTo(object obj) => CompareTo(obj as ClaveRecorrido);
    }

    /// <summary>NestoAPI#641: monta el <see cref="OrdenRecorrido"/> de un almacén. Uno por petición: lee cada almacén una vez.</summary>
    public interface ILectorOrdenRecorrido
    {
        /// <summary>Nunca null: sin calles (o sin tablas, o si falla la lectura), <see cref="OrdenRecorrido.ComoHoy"/>.</summary>
        Task<OrdenRecorrido> DelAlmacen(string empresa, string almacen);
    }

    /// <summary>
    /// NestoAPI#641: lee RecorridoPasillos + RecorridoCalles y Almacenes.FilaAMano con SQL a mano (no están en el EDMX).
    /// Si no se ha lanzado Scripts/Issue641_RecorridoCalles.sql, las consultas no tocan las tablas que faltan y todo va
    /// como hoy. Las calles son del almacén físico: si la empresa no tiene las suyas, se usan las de la empresa por
    /// defecto (Algete de la empresa 3 es el mismo almacén que el de la 1).
    /// </summary>
    public class LectorOrdenRecorridoSql : ILectorOrdenRecorrido, IDisposable
    {
        // OBJECT_ID primero: sin el script no existe la tabla y la rama del ELSE devuelve las mismas columnas, vacías
        internal const string SQL_PASILLOS = @"
IF OBJECT_ID('dbo.RecorridoPasillos') IS NOT NULL AND OBJECT_ID('dbo.RecorridoCalles') IS NOT NULL
BEGIN
    DECLARE @empresa char(3) = CASE WHEN EXISTS (SELECT 1 FROM dbo.RecorridoPasillos WHERE Empresa = @p0 AND Almacen = @p1)
                                    THEN @p0 ELSE @p2 END;
    SELECT RTRIM(p.Pasillo) AS Pasillo, p.Nombre, p.IdCalle, RTRIM(c.Calle) AS Calle, c.Orden AS OrdenCalle,
           c.Sentido, CAST(p.Posicion AS int) AS Posicion
    FROM dbo.RecorridoPasillos p
         LEFT JOIN dbo.RecorridoCalles c ON c.Id = p.IdCalle
    WHERE p.Empresa = @empresa AND p.Almacen = @p1;
END
ELSE
    SELECT CAST(NULL AS varchar(3)) AS Pasillo, CAST(NULL AS nvarchar(50)) AS Nombre, CAST(NULL AS int) AS IdCalle,
           CAST(NULL AS varchar(10)) AS Calle, CAST(NULL AS int) AS OrdenCalle, CAST(NULL AS char(1)) AS Sentido,
           CAST(NULL AS int) AS Posicion
    WHERE 1 = 0;";

        // La columna se añade con el mismo script: sin él, la consulta dinámica no llega a compilarse
        internal const string SQL_FILA_A_MANO = @"
IF COL_LENGTH('dbo.Almacenes', 'FilaAMano') IS NOT NULL
    EXEC sp_executesql N'SELECT CAST(FilaAMano AS int) FROM dbo.Almacenes WHERE Empresa = @e AND [Número] = @a',
                       N'@e char(3), @a char(3)', @e = @p0, @a = @p1;
ELSE
    SELECT CAST(NULL AS int) WHERE 1 = 0;";

        private readonly Database baseDeDatos;
        private readonly NVEntities dbPropio;
        private readonly ConcurrentDictionary<string, Task<OrdenRecorrido>> leidos = new ConcurrentDictionary<string, Task<OrdenRecorrido>>();

        public LectorOrdenRecorridoSql(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos;
        }

        private LectorOrdenRecorridoSql(NVEntities db) : this(db.Database)
        {
            dbPropio = db;
        }

        public static LectorOrdenRecorridoSql ConContextoPropio() => new LectorOrdenRecorridoSql(new NVEntities());

        public Task<OrdenRecorrido> DelAlmacen(string empresa, string almacen)
        {
            string e = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string a = almacen?.Trim();
            if (string.IsNullOrEmpty(a) || baseDeDatos == null)
            {
                return Task.FromResult(OrdenRecorrido.ComoHoy);
            }
            return leidos.GetOrAdd($"{e}|{a}".ToUpperInvariant(), _ => Leer(e, a));
        }

        private async Task<OrdenRecorrido> Leer(string empresa, string almacen)
        {
            try
            {
                List<PasilloRecorrido> pasillos = await baseDeDatos.SqlQuery<PasilloRecorrido>(SQL_PASILLOS,
                        Char("@p0", empresa), Char("@p1", almacen), Char("@p2", Constantes.Empresas.EMPRESA_POR_DEFECTO))
                    .ToListAsync().ConfigureAwait(false);
                if (pasillos.Count == 0)
                {
                    return OrdenRecorrido.ComoHoy;
                }
                int? filaAMano = (await baseDeDatos.SqlQuery<int?>(SQL_FILA_A_MANO, Char("@p0", empresa), Char("@p1", almacen))
                    .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                return new OrdenRecorrido(pasillos, filaAMano);
            }
            catch (Exception ex)
            {
                // Un orden peor no puede dejar a nadie sin picking: se sigue como hoy y se deja constancia
                try
                {
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"NestoAPI#641: no se han podido leer las calles del recorrido de {empresa}/{almacen}; se usa el orden de siempre.", ex)));
                }
                catch
                {
                    // sin ELMAH (pruebas): nada
                }
                return OrdenRecorrido.ComoHoy;
            }
        }

        private static SqlParameter Char(string nombre, string valor)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Char, 3) { Value = (object)valor ?? DBNull.Value };
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }
    }
}
