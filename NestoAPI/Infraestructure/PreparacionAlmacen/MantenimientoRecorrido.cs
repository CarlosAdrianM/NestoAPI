using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#642: una calle tal como está en RecorridoCalles.</summary>
    public class CalleRecorridoGuardada
    {
        public int Id { get; set; }
        public string Calle { get; set; }
        public int Orden { get; set; }
        public string Sentido { get; set; }
        public string Descripcion { get; set; }
    }

    /// <summary>NestoAPI#642: un hueco con algo ubicado (Ubicaciones estado 0, suma &gt; 0).</summary>
    public class HuecoRecorrido
    {
        public string Pasillo { get; set; }
        public string Columna { get; set; }
        public string Fila { get; set; }
    }

    /// <summary>NestoAPI#642: lo que no se puede guardar porque falta Scripts/Issue641_RecorridoCalles.sql.</summary>
    public class RecorridoSinTablasException : Exception
    {
        public RecorridoSinTablasException()
            : base("Faltan las tablas del recorrido en la base de datos (Scripts/Issue641_RecorridoCalles.sql). Avise a informática.")
        {
        }
    }

    /// <summary>NestoAPI#642: lectura y escritura de las tablas del recorrido (fuera del EDMX, SQL a mano).</summary>
    public interface IRepositorioRecorrido
    {
        Task<bool> TablasCreadas();
        Task<List<CalleRecorridoGuardada>> LeerCalles(string empresa, string almacen);
        Task<List<PasilloRecorrido>> LeerPasillos(string empresa, string almacen);
        Task<int?> LeerFilaAMano(string empresa, string almacen);
        Task<List<HuecoRecorrido>> LeerHuecosEnUso(string empresa, string almacen);
        /// <summary>Sustituye calles y pasillos del almacén (y FilaAMano si no es null) en UNA transacción.</summary>
        Task Guardar(string empresa, string almacen, RecorridoNormalizado recorrido, string usuario);
    }

    public interface IServicioRecorridoAlmacen
    {
        Task<RecorridoAlmacenDTO> Leer(string empresa, string almacen);

        /// <exception cref="ArgumentException">Lo pedido no vale: el mensaje es para el usuario.</exception>
        /// <exception cref="RecorridoSinTablasException">No se ha lanzado el script de las tablas.</exception>
        Task<RecorridoAlmacenDTO> Guardar(string empresa, string almacen, PeticionRecorridoAlmacenDTO peticion, string usuario, bool ensayo);
    }

    /// <summary>NestoAPI#642: el recorrido de un PUT ya validado y limpio.</summary>
    public class RecorridoNormalizado
    {
        public int? FilaAMano { get; set; }
        public List<CalleNormalizada> Calles { get; set; } = new List<CalleNormalizada>();
        /// <summary>Pasillos sin calle con nombre.</summary>
        public List<PasilloNormalizado> PasillosSinCalle { get; set; } = new List<PasilloNormalizado>();
    }

    public class CalleNormalizada
    {
        public string Calle { get; set; }
        public int Orden { get; set; }
        public string Sentido { get; set; }
        public string Descripcion { get; set; }
        public List<PasilloNormalizado> Pasillos { get; set; } = new List<PasilloNormalizado>();
    }

    public class PasilloNormalizado
    {
        public string Pasillo { get; set; }
        public int? Posicion { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>NestoAPI#642: las reglas del mantenimiento, sin base de datos (validar el PUT y montar la vista con la vista previa).</summary>
    public static class ReglasMantenimientoRecorrido
    {
        public const int LARGO_CALLE = 10;
        public const int LARGO_DESCRIPCION = 100;
        public const int LARGO_NOMBRE_PASILLO = 50;
        public const int LARGO_PASILLO = 3;
        public const int MAXIMO_PASILLOS_POR_CALLE = 255;
        public const int MAXIMO_FILA_A_MANO = 255;
        public const int SALTO_ORDEN = 10;
        public const string NIVEL_A_MANO = "AMano";
        public const string NIVEL_ESCALERA = "Escalera";

        /// <exception cref="ArgumentException">El primer problema encontrado, para el usuario.</exception>
        public static RecorridoNormalizado Validar(PeticionRecorridoAlmacenDTO peticion)
        {
            if (peticion == null)
            {
                throw new ArgumentException("Falta el recorrido (calles y pasillos).");
            }
            if (peticion.FilaAMano.HasValue && (peticion.FilaAMano.Value < 1 || peticion.FilaAMano.Value > MAXIMO_FILA_A_MANO))
            {
                throw new ArgumentException($"La fila desde la que se llega con la mano tiene que estar entre 1 y {MAXIMO_FILA_A_MANO}.");
            }

            var resultado = new RecorridoNormalizado { FilaAMano = peticion.FilaAMano };
            var nombresCalle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pasillosVistos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int numero = 0;
            foreach (CalleRecorridoDTO calle in peticion.Calles ?? new List<CalleRecorridoDTO>())
            {
                numero++;
                if (calle == null)
                {
                    throw new ArgumentException($"La calle número {numero} viene vacía.");
                }
                string nombre = calle.Calle?.Trim();
                if (string.IsNullOrEmpty(nombre))
                {
                    throw new ArgumentException($"La calle número {numero} no tiene nombre.");
                }
                if (nombre.Length > LARGO_CALLE)
                {
                    throw new ArgumentException($"El nombre de la calle «{nombre}» no puede pasar de {LARGO_CALLE} caracteres.");
                }
                if (!nombresCalle.Add(nombre))
                {
                    throw new ArgumentException($"La calle «{nombre}» está repetida.");
                }
                string sentido = string.IsNullOrWhiteSpace(calle.Sentido) ? OrdenRecorrido.SENTIDO_ASCENDENTE : calle.Sentido.Trim().ToUpperInvariant();
                if (sentido != OrdenRecorrido.SENTIDO_ASCENDENTE && sentido != OrdenRecorrido.SENTIDO_DESCENDENTE)
                {
                    throw new ArgumentException($"El sentido de la calle «{nombre}» tiene que ser «A» (ida) o «D» (vuelta).");
                }
                string descripcion = Vacio(calle.Descripcion);
                if (descripcion != null && descripcion.Length > LARGO_DESCRIPCION)
                {
                    throw new ArgumentException($"La descripción de la calle «{nombre}» no puede pasar de {LARGO_DESCRIPCION} caracteres.");
                }
                List<PasilloRecorridoAlmacenDTO> pasillos = calle.Pasillos ?? new List<PasilloRecorridoAlmacenDTO>();
                if (pasillos.Count > MAXIMO_PASILLOS_POR_CALLE)
                {
                    throw new ArgumentException($"La calle «{nombre}» no puede tener más de {MAXIMO_PASILLOS_POR_CALLE} pasillos.");
                }
                var normalizada = new CalleNormalizada
                {
                    Calle = nombre,
                    Orden = numero * SALTO_ORDEN,
                    Sentido = sentido,
                    Descripcion = descripcion
                };
                foreach (PasilloRecorridoAlmacenDTO pasillo in pasillos)
                {
                    PasilloNormalizado p = Pasillo(pasillo, $"la calle «{nombre}»", pasillosVistos);
                    p.Posicion = normalizada.Pasillos.Count + 1;
                    normalizada.Pasillos.Add(p);
                }
                resultado.Calles.Add(normalizada);
            }

            foreach (PasilloRecorridoAlmacenDTO pasillo in peticion.PasillosSinCalle ?? new List<PasilloRecorridoAlmacenDTO>())
            {
                PasilloNormalizado p = Pasillo(pasillo, "los pasillos sin calle", pasillosVistos);
                if (p.Nombre != null)
                {
                    resultado.PasillosSinCalle.Add(p);
                }
            }
            return resultado;
        }

        private static PasilloNormalizado Pasillo(PasilloRecorridoAlmacenDTO pasillo, string donde, Dictionary<string, string> vistos)
        {
            string clave = OrdenRecorrido.NormalizarPasillo(pasillo?.Pasillo);
            if (clave == null)
            {
                throw new ArgumentException($"Hay un pasillo sin número en {donde}.");
            }
            if (clave.Length > LARGO_PASILLO)
            {
                throw new ArgumentException($"«{clave}» no es un pasillo: son {LARGO_PASILLO} caracteres como mucho (por ejemplo, 004).");
            }
            if (vistos.TryGetValue(clave, out string antes))
            {
                throw new ArgumentException($"El pasillo {clave} está en {antes} y también en {donde}: cada pasillo solo puede ir en un sitio.");
            }
            vistos[clave] = donde;
            string nombre = Vacio(pasillo.Nombre);
            if (nombre != null && nombre.Length > LARGO_NOMBRE_PASILLO)
            {
                throw new ArgumentException($"El nombre del pasillo {clave} no puede pasar de {LARGO_NOMBRE_PASILLO} caracteres.");
            }
            return new PasilloNormalizado { Pasillo = clave, Nombre = nombre };
        }

        private static string Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        /// <summary>
        /// La vista del mantenimiento: calles con sus pasillos, pasillos sin calle (con huecos en uso o con nombre) y la vista previa
        /// de los tramos con la regla única <see cref="OrdenRecorrido"/> (la misma que usan picking, reposiciones y ubicar).
        /// </summary>
        public static RecorridoAlmacenDTO ConstruirVista(string empresa, string almacen, bool tablasCreadas, IEnumerable<CalleRecorridoGuardada> calles,
            IEnumerable<PasilloRecorrido> pasillos, int? filaAMano, IEnumerable<HuecoRecorrido> huecos)
        {
            List<HuecoRecorrido> enUso = (huecos ?? Enumerable.Empty<HuecoRecorrido>())
                .Where(h => OrdenRecorrido.NormalizarPasillo(h?.Pasillo) != null)
                .ToList();
            Dictionary<string, int> huecosPorPasillo = enUso
                .GroupBy(h => OrdenRecorrido.NormalizarPasillo(h.Pasillo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            int HuecosDe(string pasillo) => huecosPorPasillo.TryGetValue(pasillo, out int n) ? n : 0;

            List<CalleRecorridoGuardada> listaCalles = (calles ?? Enumerable.Empty<CalleRecorridoGuardada>())
                .OrderBy(c => c.Orden).ThenBy(c => c.Id).ToList();
            List<PasilloRecorrido> listaPasillos = (pasillos ?? Enumerable.Empty<PasilloRecorrido>())
                .Where(p => OrdenRecorrido.NormalizarPasillo(p?.Pasillo) != null)
                .ToList();
            var idsCalles = new HashSet<int>(listaCalles.Select(c => c.Id));

            var vista = new RecorridoAlmacenDTO
            {
                Empresa = empresa,
                Almacen = almacen,
                TablasCreadas = tablasCreadas,
                FilaAMano = filaAMano.HasValue && filaAMano.Value > 0 ? filaAMano.Value : OrdenRecorrido.FILA_A_MANO_POR_DEFECTO
            };

            // Para la regla: cada pasillo con los datos de su calle (como los lee LectorOrdenRecorridoSql)
            var paraLaRegla = new List<PasilloRecorrido>();
            foreach (CalleRecorridoGuardada calle in listaCalles)
            {
                var dto = new CalleRecorridoDTO
                {
                    Id = calle.Id > 0 ? calle.Id : (int?)null,
                    Calle = calle.Calle?.Trim(),
                    Orden = calle.Orden,
                    Sentido = string.IsNullOrWhiteSpace(calle.Sentido) ? OrdenRecorrido.SENTIDO_ASCENDENTE : calle.Sentido.Trim().ToUpperInvariant(),
                    Descripcion = calle.Descripcion?.Trim()
                };
                foreach (PasilloRecorrido p in listaPasillos.Where(p => p.IdCalle == calle.Id)
                    .OrderBy(p => p.Posicion ?? int.MaxValue).ThenBy(p => OrdenRecorrido.NormalizarPasillo(p.Pasillo), StringComparer.Ordinal))
                {
                    string clave = OrdenRecorrido.NormalizarPasillo(p.Pasillo);
                    dto.Pasillos.Add(new PasilloRecorridoAlmacenDTO { Pasillo = clave, Posicion = p.Posicion, Nombre = p.Nombre?.Trim(), Huecos = HuecosDe(clave) });
                    paraLaRegla.Add(new PasilloRecorrido
                    {
                        Pasillo = clave,
                        Nombre = p.Nombre,
                        IdCalle = calle.Id,
                        Calle = dto.Calle,
                        OrdenCalle = calle.Orden,
                        Sentido = dto.Sentido,
                        Posicion = p.Posicion
                    });
                }
                vista.Calles.Add(dto);
            }

            var conCalle = new HashSet<string>(paraLaRegla.Select(p => p.Pasillo), StringComparer.OrdinalIgnoreCase);
            var sinCalle = new Dictionary<string, PasilloRecorridoAlmacenDTO>(StringComparer.OrdinalIgnoreCase);
            foreach (PasilloRecorrido p in listaPasillos.Where(p => !p.IdCalle.HasValue || !idsCalles.Contains(p.IdCalle.Value)))
            {
                string clave = OrdenRecorrido.NormalizarPasillo(p.Pasillo);
                if (!conCalle.Contains(clave) && !sinCalle.ContainsKey(clave))
                {
                    sinCalle[clave] = new PasilloRecorridoAlmacenDTO { Pasillo = clave, Nombre = string.IsNullOrWhiteSpace(p.Nombre) ? null : p.Nombre.Trim(), Huecos = HuecosDe(clave) };
                    paraLaRegla.Add(new PasilloRecorrido { Pasillo = clave, Nombre = p.Nombre });
                }
            }
            foreach (string clave in huecosPorPasillo.Keys.Where(k => !conCalle.Contains(k) && !sinCalle.ContainsKey(k)).ToList())
            {
                sinCalle[clave] = new PasilloRecorridoAlmacenDTO { Pasillo = clave, Huecos = HuecosDe(clave) };
            }
            vista.PasillosSinCalle = sinCalle.Values.OrderBy(p => p.Pasillo, StringComparer.Ordinal).ToList();

            var orden = new OrdenRecorrido(paraLaRegla, vista.FilaAMano);
            TramoRecorridoDTO tramo = null;
            string claveTramo = null;
            foreach (var h in enUso
                .Select(h => new { Hueco = h, Clave = orden.Clave(h.Pasillo, h.Columna, h.Fila) })
                .OrderBy(x => x.Clave))
            {
                string nivel = !h.Clave.ConCalle ? null : h.Clave.Nivel == ClaveRecorrido.NIVEL_ESCALERA ? NIVEL_ESCALERA : NIVEL_A_MANO;
                string pasillo = OrdenRecorrido.NormalizarPasillo(h.Hueco.Pasillo);
                string columna = h.Hueco.Columna?.Trim();
                string clave = $"{pasillo}|{columna}|{nivel}";
                if (tramo == null || clave != claveTramo)
                {
                    tramo = new TramoRecorridoDTO
                    {
                        Orden = vista.Tramos.Count + 1,
                        Calle = h.Clave.ConCalle ? paraLaRegla.FirstOrDefault(p => string.Equals(p.Pasillo, pasillo, StringComparison.OrdinalIgnoreCase))?.Calle : null,
                        Pasillo = pasillo,
                        Columna = columna,
                        Nivel = nivel
                    };
                    claveTramo = clave;
                    vista.Tramos.Add(tramo);
                }
                tramo.Filas.Add(h.Hueco.Fila?.Trim());
            }
            return vista;
        }

        /// <summary>La vista de un PUT en ensayo: lo pedido como si estuviera guardado (las calles sin Id).</summary>
        public static RecorridoAlmacenDTO VistaDeEnsayo(string empresa, string almacen, RecorridoNormalizado recorrido, int? filaAManoGuardada,
            IEnumerable<HuecoRecorrido> huecos)
        {
            var calles = new List<CalleRecorridoGuardada>();
            var pasillos = new List<PasilloRecorrido>();
            int id = 0;
            foreach (CalleNormalizada calle in recorrido.Calles)
            {
                // Ids provisionales negativos: la vista los deja sin Id
                id--;
                calles.Add(new CalleRecorridoGuardada { Id = id, Calle = calle.Calle, Orden = calle.Orden, Sentido = calle.Sentido, Descripcion = calle.Descripcion });
                pasillos.AddRange(calle.Pasillos.Select(p => new PasilloRecorrido { Pasillo = p.Pasillo, Nombre = p.Nombre, IdCalle = id, Posicion = p.Posicion }));
            }
            pasillos.AddRange(recorrido.PasillosSinCalle.Select(p => new PasilloRecorrido { Pasillo = p.Pasillo, Nombre = p.Nombre }));
            RecorridoAlmacenDTO vista = ConstruirVista(empresa, almacen, true, calles, pasillos, recorrido.FilaAMano ?? filaAManoGuardada, huecos);
            vista.Ensayo = true;
            return vista;
        }
    }

    /// <summary>NestoAPI#642: GET y PUT api/Almacen/Recorrido.</summary>
    public class ServicioRecorridoAlmacen : IServicioRecorridoAlmacen
    {
        private readonly IRepositorioRecorrido repositorio;

        public ServicioRecorridoAlmacen(IRepositorioRecorrido repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<RecorridoAlmacenDTO> Leer(string empresa, string almacen)
        {
            List<HuecoRecorrido> huecos = await repositorio.LeerHuecosEnUso(empresa, almacen).ConfigureAwait(false);
            int? filaAMano = await repositorio.LeerFilaAMano(empresa, almacen).ConfigureAwait(false);
            if (!await repositorio.TablasCreadas().ConfigureAwait(false))
            {
                return ReglasMantenimientoRecorrido.ConstruirVista(empresa, almacen, false, null, null, filaAMano, huecos);
            }
            List<CalleRecorridoGuardada> calles = await repositorio.LeerCalles(empresa, almacen).ConfigureAwait(false);
            List<PasilloRecorrido> pasillos = await repositorio.LeerPasillos(empresa, almacen).ConfigureAwait(false);
            return ReglasMantenimientoRecorrido.ConstruirVista(empresa, almacen, true, calles, pasillos, filaAMano, huecos);
        }

        public async Task<RecorridoAlmacenDTO> Guardar(string empresa, string almacen, PeticionRecorridoAlmacenDTO peticion, string usuario, bool ensayo)
        {
            RecorridoNormalizado recorrido = ReglasMantenimientoRecorrido.Validar(peticion);
            if (!await repositorio.TablasCreadas().ConfigureAwait(false))
            {
                throw new RecorridoSinTablasException();
            }
            if (ensayo)
            {
                List<HuecoRecorrido> huecos = await repositorio.LeerHuecosEnUso(empresa, almacen).ConfigureAwait(false);
                int? filaAMano = await repositorio.LeerFilaAMano(empresa, almacen).ConfigureAwait(false);
                return ReglasMantenimientoRecorrido.VistaDeEnsayo(empresa, almacen, recorrido, filaAMano, huecos);
            }
            await repositorio.Guardar(empresa, almacen, recorrido, usuario).ConfigureAwait(false);
            // Sin caché que invalidar: cada petición monta su OrdenRecorrido (ILectorOrdenRecorrido es Scoped), así que el
            // siguiente picking, reposición o ubicar ya sale con el recorrido nuevo.
            return await Leer(empresa, almacen).ConfigureAwait(false);
        }
    }

    /// <summary>NestoAPI#642: las tablas de Scripts/Issue641_RecorridoCalles.sql con SQL a mano (no están en el EDMX).</summary>
    public class RepositorioRecorridoSql : IRepositorioRecorrido, IDisposable
    {
        internal const string SQL_TABLAS_CREADAS = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.RecorridoPasillos') IS NOT NULL AND OBJECT_ID('dbo.RecorridoCalles') IS NOT NULL
                 THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_CALLES = @"
SELECT Id, RTRIM(Calle) AS Calle, Orden, Sentido, Descripcion
FROM dbo.RecorridoCalles
WHERE Empresa = @p0 AND Almacen = @p1";

        internal const string SQL_PASILLOS = @"
SELECT RTRIM(Pasillo) AS Pasillo, Nombre, IdCalle, CAST(Posicion AS int) AS Posicion
FROM dbo.RecorridoPasillos
WHERE Empresa = @p0 AND Almacen = @p1";

        // Los huecos del almacén físico: la empresa pedida y la espejo (el stock de ALG es de la 1 y la 3)
        internal const string SQL_HUECOS_EN_USO = @"
SELECT RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Columna) AS Columna, RTRIM(u.Fila) AS Fila
FROM Ubicaciones u
WHERE u.Empresa IN (@p0, @p2) AND u.[Almacén] = @p1 AND u.Estado = 0
      AND u.Pasillo IS NOT NULL AND u.Fila IS NOT NULL AND u.Columna IS NOT NULL
GROUP BY u.Pasillo, u.Columna, u.Fila
HAVING SUM(u.Cantidad) > 0";

        internal const string SQL_BORRAR = @"
DELETE FROM dbo.RecorridoPasillos WHERE Empresa = @p0 AND Almacen = @p1;
DELETE FROM dbo.RecorridoCalles WHERE Empresa = @p0 AND Almacen = @p1;";

        internal const string SQL_INSERTAR_CALLE = @"
INSERT INTO dbo.RecorridoCalles (Empresa, Almacen, Calle, Orden, Sentido, Descripcion, Usuario)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6);
SELECT CAST(SCOPE_IDENTITY() AS int);";

        internal const string SQL_INSERTAR_PASILLO = @"
INSERT INTO dbo.RecorridoPasillos (Empresa, Almacen, Pasillo, IdCalle, Posicion, Nombre, Usuario)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6);";

        internal const string SQL_FILA_A_MANO = @"
IF COL_LENGTH('dbo.Almacenes', 'FilaAMano') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.Almacenes SET FilaAMano = @f WHERE Empresa = @e AND [Número] = @a',
                       N'@f tinyint, @e char(3), @a char(3)', @f = @p2, @e = @p0, @a = @p1;";

        private readonly NVEntities db;

        public RepositorioRecorridoSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioRecorridoSql ConContextoPropio() => new RepositorioRecorridoSql(new NVEntities());

        private static SqlParameter Char(string nombre, int largo, string valor)
            => new SqlParameter(nombre, System.Data.SqlDbType.Char, largo) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter VarChar(string nombre, int largo, string valor)
            => new SqlParameter(nombre, System.Data.SqlDbType.VarChar, largo) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter NVarChar(string nombre, int largo, string valor)
            => new SqlParameter(nombre, System.Data.SqlDbType.NVarChar, largo) { Value = (object)valor ?? DBNull.Value };

        public async Task<bool> TablasCreadas()
        {
            return (await db.Database.SqlQuery<bool>(SQL_TABLAS_CREADAS).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public Task<List<CalleRecorridoGuardada>> LeerCalles(string empresa, string almacen)
        {
            return db.Database.SqlQuery<CalleRecorridoGuardada>(SQL_CALLES, Char("@p0", 3, empresa), Char("@p1", 3, almacen)).ToListAsync();
        }

        public Task<List<PasilloRecorrido>> LeerPasillos(string empresa, string almacen)
        {
            return db.Database.SqlQuery<PasilloRecorrido>(SQL_PASILLOS, Char("@p0", 3, empresa), Char("@p1", 3, almacen)).ToListAsync();
        }

        public async Task<int?> LeerFilaAMano(string empresa, string almacen)
        {
            return (await db.Database.SqlQuery<int?>(LectorOrdenRecorridoSql.SQL_FILA_A_MANO, Char("@p0", 3, empresa), Char("@p1", 3, almacen))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public Task<List<HuecoRecorrido>> LeerHuecosEnUso(string empresa, string almacen)
        {
            return db.Database.SqlQuery<HuecoRecorrido>(SQL_HUECOS_EN_USO,
                Char("@p0", 3, empresa), Char("@p1", 3, almacen), Char("@p2", 3, Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)).ToListAsync();
        }

        public async Task Guardar(string empresa, string almacen, RecorridoNormalizado recorrido, string usuario)
        {
            string quien = string.IsNullOrWhiteSpace(usuario) ? null : (usuario.Length > 50 ? usuario.Substring(0, 50) : usuario);
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                await db.Database.ExecuteSqlCommandAsync(SQL_BORRAR, Char("@p0", 3, empresa), Char("@p1", 3, almacen)).ConfigureAwait(false);
                foreach (CalleNormalizada calle in recorrido.Calles)
                {
                    int id = (await db.Database.SqlQuery<int>(SQL_INSERTAR_CALLE,
                        Char("@p0", 3, empresa), Char("@p1", 3, almacen), VarChar("@p2", 10, calle.Calle), new SqlParameter("@p3", calle.Orden),
                        Char("@p4", 1, calle.Sentido), NVarChar("@p5", 100, calle.Descripcion), VarChar("@p6", 50, quien))
                        .ToListAsync().ConfigureAwait(false)).Single();
                    foreach (PasilloNormalizado pasillo in calle.Pasillos)
                    {
                        await InsertarPasillo(empresa, almacen, pasillo, id, quien).ConfigureAwait(false);
                    }
                }
                foreach (PasilloNormalizado pasillo in recorrido.PasillosSinCalle)
                {
                    await InsertarPasillo(empresa, almacen, pasillo, null, quien).ConfigureAwait(false);
                }
                if (recorrido.FilaAMano.HasValue)
                {
                    await db.Database.ExecuteSqlCommandAsync(SQL_FILA_A_MANO,
                        Char("@p0", 3, empresa), Char("@p1", 3, almacen), new SqlParameter("@p2", System.Data.SqlDbType.TinyInt) { Value = (byte)recorrido.FilaAMano.Value })
                        .ConfigureAwait(false);
                }
                transaccion.Commit();
            }
        }

        private Task<int> InsertarPasillo(string empresa, string almacen, PasilloNormalizado pasillo, int? idCalle, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_PASILLO,
                Char("@p0", 3, empresa), Char("@p1", 3, almacen), Char("@p2", 3, pasillo.Pasillo),
                new SqlParameter("@p3", System.Data.SqlDbType.Int) { Value = (object)idCalle ?? DBNull.Value },
                new SqlParameter("@p4", System.Data.SqlDbType.TinyInt) { Value = pasillo.Posicion.HasValue ? (object)(byte)pasillo.Posicion.Value : DBNull.Value },
                NVarChar("@p5", 50, pasillo.Nombre), VarChar("@p6", 50, usuario));
        }

        public void Dispose() => db?.Dispose();
    }
}
