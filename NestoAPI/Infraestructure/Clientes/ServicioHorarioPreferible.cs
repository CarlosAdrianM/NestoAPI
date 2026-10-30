using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>NestoAPI#763: una fila de dbo.ClientesHorarioPreferible.</summary>
    public class FilaHorarioPreferible
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public byte DiaSemana { get; set; }
        public TimeSpan Desde { get; set; }
        public TimeSpan Hasta { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#763: una fila de dbo.ClientesHorarioPreferiblePropuestas con los datos del cliente.</summary>
    public class FilaPropuestaHorario
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string Poblacion { get; set; }
        public string Ruta { get; set; }
        public string TextoOrigen { get; set; }
        public string Estado { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>
    /// NestoAPI#763: el horario preferible guardado y las propuestas sacadas de los comentarios. Fuera del EDMX a propósito
    /// (Scripts/Issue763_HorarioPreferible.sql): sin el script no hay horarios ni propuestas y guardar da 400.
    /// </summary>
    public interface IRepositorioHorarioPreferible
    {
        bool ScriptAplicado();
        Task<bool> ExisteContacto(string empresa, string cliente, string contacto);
        Task<List<FilaHorarioPreferible>> Leer(string empresa, string cliente, string contacto);
        /// <summary>Los horarios de varios contactos a la vez (las paradas del reparto). Clave: «empresa|cliente|contacto» recortados.</summary>
        Task<List<FilaHorarioPreferible>> LeerVarios(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos);
        /// <summary>Sustituye las franjas del contacto (ninguna = borrar) y da por resuelta su propuesta pendiente, en una transacción.</summary>
        Task Guardar(string empresa, string cliente, string contacto, IReadOnlyCollection<FranjaHorario> franjas, string usuario, string textoAceptado);
        Task<List<FilaPropuestaHorario>> LeerPropuestas(string estado, int? id = null, string empresa = null, string cliente = null, string contacto = null);
        /// <summary>Pasa la propuesta de «PorRevisar» a <paramref name="estado"/>. False si ya no estaba por revisar.</summary>
        Task<bool> ResolverPropuesta(int id, string estado, string usuario, string textoAceptado);
    }

    public class RepositorioHorarioPreferible : IRepositorioHorarioPreferible
    {
        public const string TABLA = "ClientesHorarioPreferible";
        public const string TABLA_PROPUESTAS = "ClientesHorarioPreferiblePropuestas";
        /// <summary>Para no montar un IN gigante: las paradas de un día no llegan ni de lejos.</summary>
        private const int MAXIMO_CONTACTOS = 500;

        private const string SELECT_HORARIO = "SELECT RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, DiaSemana, " +
            "CAST(Desde AS time) AS Desde, CAST(Hasta AS time) AS Hasta, RTRIM(Usuario) AS Usuario, FechaModificacion FROM dbo." + TABLA + " WITH (NOLOCK) ";

        private readonly NVEntities db;

        public RepositorioHorarioPreferible(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA) && ColumnasOpcionales.ExisteTabla(db, TABLA_PROPUESTAS);

        public Task<bool> ExisteContacto(string empresa, string cliente, string contacto)
            => db.Clientes.AsNoTracking().AnyAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto);

        public Task<List<FilaHorarioPreferible>> Leer(string empresa, string cliente, string contacto)
            => db.Database.SqlQuery<FilaHorarioPreferible>(SELECT_HORARIO + "WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2 ORDER BY DiaSemana, Desde",
                empresa, cliente, contacto).ToListAsync();

        public async Task<List<FilaHorarioPreferible>> LeerVarios(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos)
        {
            var lista = (contactos ?? new List<(string, string, string)>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Cliente))
                .Select(c => (Empresa: c.Empresa?.Trim() ?? string.Empty, Cliente: c.Cliente.Trim(), Contacto: c.Contacto?.Trim() ?? string.Empty))
                .Distinct()
                .Take(MAXIMO_CONTACTOS)
                .ToList();
            if (lista.Count == 0)
            {
                return new List<FilaHorarioPreferible>();
            }
            // Clientes distintos por parámetro; el contacto se filtra en memoria (pocas filas por cliente).
            List<string> clientes = lista.Select(c => c.Cliente).Distinct().ToList();
            string parametros = string.Join(", ", clientes.Select((c, i) => "@p" + i));
            List<FilaHorarioPreferible> filas = await db.Database.SqlQuery<FilaHorarioPreferible>(
                SELECT_HORARIO + $"WHERE Cliente IN ({parametros})", clientes.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false);
            var buscados = new HashSet<string>(lista.Select(c => Clave(c.Empresa, c.Cliente, c.Contacto)), StringComparer.OrdinalIgnoreCase);
            return filas.Where(f => buscados.Contains(Clave(f.Empresa, f.Cliente, f.Contacto))).ToList();
        }

        public static string Clave(string empresa, string cliente, string contacto)
            => (empresa?.Trim() ?? string.Empty) + "|" + (cliente?.Trim() ?? string.Empty) + "|" + (contacto?.Trim() ?? string.Empty);

        public async Task Guardar(string empresa, string cliente, string contacto, IReadOnlyCollection<FranjaHorario> franjas, string usuario, string textoAceptado)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                _ = await db.Database.ExecuteSqlCommandAsync($"DELETE FROM dbo.{TABLA} WHERE Empresa = @Empresa AND Cliente = @Cliente AND Contacto = @Contacto",
                    Clave3(empresa, cliente, contacto)).ConfigureAwait(false);
                byte franja = 0;
                int dia = 0;
                foreach (FranjaHorario f in FormatoHorarioPreferible.Ordenar(franjas))
                {
                    franja = f.DiaSemana == dia ? (byte)(franja + 1) : (byte)1;
                    dia = f.DiaSemana;
                    var parametros = Clave3(empresa, cliente, contacto).Concat(new[]
                    {
                        new SqlParameter("@DiaSemana", SqlDbType.TinyInt) { Value = (byte)f.DiaSemana },
                        new SqlParameter("@Franja", SqlDbType.TinyInt) { Value = franja },
                        new SqlParameter("@Desde", SqlDbType.Time) { Value = f.Desde },
                        new SqlParameter("@Hasta", SqlDbType.Time) { Value = f.Hasta },
                        new SqlParameter("@Usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty }
                    }).ToArray();
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        $"INSERT INTO dbo.{TABLA} (Empresa, Cliente, Contacto, DiaSemana, Franja, Desde, Hasta, Usuario, FechaModificacion) " +
                        "VALUES (@Empresa, @Cliente, @Contacto, @DiaSemana, @Franja, @Desde, @Hasta, @Usuario, GETDATE())", parametros).ConfigureAwait(false);
                }
                // Lo que había por revisar de este contacto ya está resuelto: lo ha decidido una persona.
                _ = await db.Database.ExecuteSqlCommandAsync(
                    $"UPDATE dbo.{TABLA_PROPUESTAS} SET Estado = @Estado, UsuarioRevision = @Usuario, FechaRevision = GETDATE(), HorarioAceptado = @Texto " +
                    "WHERE Empresa = @Empresa AND Cliente = @Cliente AND Contacto = @Contacto AND Estado = @PorRevisar",
                    Clave3(empresa, cliente, contacto).Concat(new[]
                    {
                        new SqlParameter("@Estado", SqlDbType.VarChar, 12) { Value = PropuestaHorarioPreferibleDTO.ESTADO_ACEPTADA },
                        new SqlParameter("@PorRevisar", SqlDbType.VarChar, 12) { Value = PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR },
                        new SqlParameter("@Usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty },
                        new SqlParameter("@Texto", SqlDbType.NVarChar, 200) { Value = (object)Recortar(textoAceptado, 200) ?? DBNull.Value }
                    }).ToArray()).ConfigureAwait(false);
                transaccion.Commit();
            }
        }

        public Task<List<FilaPropuestaHorario>> LeerPropuestas(string estado, int? id = null, string empresa = null, string cliente = null, string contacto = null)
        {
            string sql = "SELECT p.Id, RTRIM(p.Empresa) AS Empresa, RTRIM(p.Cliente) AS Cliente, RTRIM(p.Contacto) AS Contacto, RTRIM(c.Nombre) AS Nombre, " +
                "RTRIM(c.[Dirección]) AS Direccion, RTRIM(c.[Población]) AS Poblacion, RTRIM(c.Ruta) AS Ruta, p.TextoOrigen, RTRIM(p.Estado) AS Estado, p.Fecha " +
                $"FROM dbo.{TABLA_PROPUESTAS} p WITH (NOLOCK) LEFT JOIN dbo.Clientes c WITH (NOLOCK) " +
                "ON c.Empresa = p.Empresa AND c.[Nº Cliente] = p.Cliente AND c.Contacto = p.Contacto WHERE p.Estado = @p0";
            var parametros = new List<object> { estado ?? PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR };
            if (id.HasValue)
            {
                sql += " AND p.Id = @p" + parametros.Count;
                parametros.Add(id.Value);
            }
            if (!string.IsNullOrWhiteSpace(cliente))
            {
                sql += $" AND p.Empresa = @p{parametros.Count} AND p.Cliente = @p{parametros.Count + 1} AND p.Contacto = @p{parametros.Count + 2}";
                parametros.Add(empresa ?? string.Empty);
                parametros.Add(cliente);
                parametros.Add(contacto ?? string.Empty);
            }
            sql += " ORDER BY c.Ruta, p.Cliente, p.Contacto";
            return db.Database.SqlQuery<FilaPropuestaHorario>(sql, parametros.ToArray()).ToListAsync();
        }

        public async Task<bool> ResolverPropuesta(int id, string estado, string usuario, string textoAceptado)
        {
            int filas = await db.Database.ExecuteSqlCommandAsync(
                $"UPDATE dbo.{TABLA_PROPUESTAS} SET Estado = @Estado, UsuarioRevision = @Usuario, FechaRevision = GETDATE(), HorarioAceptado = @Texto " +
                "WHERE Id = @Id AND Estado = @PorRevisar",
                new SqlParameter("@Id", SqlDbType.Int) { Value = id },
                new SqlParameter("@Estado", SqlDbType.VarChar, 12) { Value = estado },
                new SqlParameter("@PorRevisar", SqlDbType.VarChar, 12) { Value = PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR },
                new SqlParameter("@Usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty },
                new SqlParameter("@Texto", SqlDbType.NVarChar, 200) { Value = (object)Recortar(textoAceptado, 200) ?? DBNull.Value }).ConfigureAwait(false);
            return filas > 0;
        }

        private static SqlParameter[] Clave3(string empresa, string cliente, string contacto) => new[]
        {
            new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa ?? string.Empty },
            new SqlParameter("@Cliente", SqlDbType.Char, 10) { Value = cliente ?? string.Empty },
            new SqlParameter("@Contacto", SqlDbType.Char, 3) { Value = contacto ?? string.Empty }
        };

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>NestoAPI#763: un resultado del servicio (bien, o por qué no).</summary>
    public class ResultadoHorarioPreferible<T>
    {
        public bool Ok => Error == null;
        /// <summary>Null si ha ido bien; si no, el código HTTP (400, 404, 409).</summary>
        public System.Net.HttpStatusCode? Error { get; set; }
        public string Mensaje { get; set; }
        public T Contenido { get; set; }

        public static ResultadoHorarioPreferible<T> Bien(T contenido) => new ResultadoHorarioPreferible<T> { Contenido = contenido };
        public static ResultadoHorarioPreferible<T> Mal(System.Net.HttpStatusCode error, string mensaje) => new ResultadoHorarioPreferible<T> { Error = error, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#763 (Nesto#584): el «horario preferible» de entrega por cliente y contacto. Solo lo respeta nuestra ruta propia
    /// (agencia 14: el orden sugerido de las paradas avisa de las que llegarían fuera de él); las agencias externas no tienen
    /// obligación. Las propuestas sacadas de los comentarios (Scripts/Issue763_CargaPropuestasHorario.sql) no cuentan hasta que
    /// una persona las acepta.
    /// </summary>
    public class ServicioHorarioPreferible
    {
        public const string MENSAJE_FALTA_SCRIPT = "Falta lanzar el script Scripts/Issue763_HorarioPreferible.sql (horario preferible): " +
            "pídaselo a Informática.";

        private readonly IRepositorioHorarioPreferible repositorio;

        public ServicioHorarioPreferible(IRepositorioHorarioPreferible repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <summary>
        /// Como las rutas, el horario solo se guarda en la empresa 1: las fichas de las otras empresas son copias de las de la 1 y
        /// los envíos del reparto propio son de la 1. Cualquier empresa se lee y se guarda con la 1.
        /// </summary>
        internal static string Empresa(string empresa) => Constantes.Empresas.EMPRESA_POR_DEFECTO;
        private static string Contacto(string contacto) => string.IsNullOrWhiteSpace(contacto) ? "0" : contacto.Trim();

        public async Task<ResultadoHorarioPreferible<HorarioPreferibleDTO>> Leer(string empresa, string cliente, string contacto)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest, "Falta el cliente.");
            }
            empresa = Empresa(empresa);
            cliente = cliente.Trim();
            contacto = Contacto(contacto);
            var dto = new HorarioPreferibleDTO { Empresa = empresa, Cliente = cliente, Contacto = contacto, Texto = string.Empty };
            if (!repositorio.ScriptAplicado())
            {
                // Sin el script la ficha sale igual, sin horario.
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Bien(dto);
            }
            List<FilaHorarioPreferible> filas = await repositorio.Leer(empresa, cliente, contacto).ConfigureAwait(false) ?? new List<FilaHorarioPreferible>();
            Completar(dto, filas);
            FilaPropuestaHorario pendiente = (await repositorio.LeerPropuestas(PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR, null, empresa, cliente, contacto)
                .ConfigureAwait(false))?.FirstOrDefault();
            dto.PropuestaPendiente = pendiente == null ? null : APropuesta(pendiente);
            return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Bien(dto);
        }

        private static void Completar(HorarioPreferibleDTO dto, List<FilaHorarioPreferible> filas)
        {
            List<FranjaHorario> franjas = filas.Select(f => new FranjaHorario(f.DiaSemana, f.Desde, f.Hasta)).ToList();
            dto.Franjas = FormatoHorarioPreferible.Ordenar(franjas).Select(f => f.ADto()).ToList();
            dto.Texto = FormatoHorarioPreferible.Texto(franjas);
            FilaHorarioPreferible ultima = filas.OrderByDescending(f => f.FechaModificacion).FirstOrDefault();
            dto.Usuario = ultima?.Usuario;
            dto.FechaModificacion = ultima?.FechaModificacion;
        }

        /// <summary>Las franjas de la petición: <see cref="HorarioPreferibleDTO.Franjas"/> si vienen; si no, <see cref="HorarioPreferibleDTO.Texto"/>.</summary>
        internal static List<FranjaHorario> FranjasDe(HorarioPreferibleDTO peticion, out string error)
        {
            if (peticion?.Franjas != null && peticion.Franjas.Count > 0)
            {
                return FormatoHorarioPreferible.Leer(peticion.Franjas, out error);
            }
            return FormatoHorarioPreferible.Leer(peticion?.Texto, out error);
        }

        /// <summary>PUT: guarda (sustituye) el horario del contacto. Sin franjas, lo borra.</summary>
        public async Task<ResultadoHorarioPreferible<HorarioPreferibleDTO>> Guardar(string empresa, string cliente, string contacto,
            HorarioPreferibleDTO peticion, string usuario)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest, "Falta el cliente.");
            }
            if (peticion == null)
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest,
                    "Falta el horario: mande las franjas (o ninguna para quitarlo).");
            }
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest, MENSAJE_FALTA_SCRIPT);
            }
            List<FranjaHorario> franjas = FranjasDe(peticion, out string error);
            if (franjas == null)
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest, error);
            }
            empresa = Empresa(empresa);
            cliente = cliente.Trim();
            contacto = Contacto(contacto);
            if (!await repositorio.ExisteContacto(empresa, cliente, contacto).ConfigureAwait(false))
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.NotFound,
                    $"No existe el cliente {cliente} con el contacto {contacto}.");
            }
            await repositorio.Guardar(empresa, cliente, contacto, franjas, usuario, FormatoHorarioPreferible.Texto(franjas)).ConfigureAwait(false);
            return await Leer(empresa, cliente, contacto).ConfigureAwait(false);
        }

        /// <summary>GET Propuestas: las que esperan revisión, con lo que proponen las reglas.</summary>
        public async Task<List<PropuestaHorarioPreferibleDTO>> Propuestas()
        {
            if (!repositorio.ScriptAplicado())
            {
                return new List<PropuestaHorarioPreferibleDTO>();
            }
            return ((await repositorio.LeerPropuestas(PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR).ConfigureAwait(false)) ?? new List<FilaPropuestaHorario>())
                .Select(APropuesta)
                .ToList();
        }

        internal static PropuestaHorarioPreferibleDTO APropuesta(FilaPropuestaHorario fila)
        {
            List<FranjaHorario> franjas = ExtractorHorarioComentarios.Proponer(fila.TextoOrigen);
            return new PropuestaHorarioPreferibleDTO
            {
                Id = fila.Id,
                Empresa = fila.Empresa,
                Cliente = fila.Cliente,
                Contacto = fila.Contacto,
                Nombre = fila.Nombre,
                Direccion = fila.Direccion,
                Poblacion = fila.Poblacion,
                Ruta = fila.Ruta,
                TextoOrigen = fila.TextoOrigen,
                HorarioPropuesto = franjas == null ? null : FormatoHorarioPreferible.Texto(franjas),
                Franjas = franjas?.Select(f => f.ADto()).ToList() ?? new List<FranjaHorarioPreferibleDTO>(),
                Estado = fila.Estado,
                Fecha = fila.Fecha
            };
        }

        /// <summary>
        /// POST Propuestas/{id}/Aceptar: guarda como horario lo que mande la persona (puede haber corregido la propuesta) o, si no
        /// manda nada, lo que proponen las reglas.
        /// </summary>
        public async Task<ResultadoHorarioPreferible<HorarioPreferibleDTO>> Aceptar(int id, HorarioPreferibleDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest, MENSAJE_FALTA_SCRIPT);
            }
            FilaPropuestaHorario propuesta = (await repositorio.LeerPropuestas(PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR, id).ConfigureAwait(false))?.FirstOrDefault();
            if (propuesta == null)
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.NotFound,
                    $"La propuesta {id} no existe o ya está revisada.");
            }
            bool manda = peticion != null && ((peticion.Franjas != null && peticion.Franjas.Count > 0) || !string.IsNullOrWhiteSpace(peticion.Texto));
            List<FranjaHorario> franjas;
            if (manda)
            {
                franjas = FranjasDe(peticion, out string error);
                if (franjas == null)
                {
                    return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest, error);
                }
            }
            else
            {
                franjas = ExtractorHorarioComentarios.Proponer(propuesta.TextoOrigen);
                if (franjas == null)
                {
                    return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest,
                        "No hay horario propuesto para esta ficha: escríbalo usted o descarte la propuesta.");
                }
            }
            if (franjas.Count == 0)
            {
                return ResultadoHorarioPreferible<HorarioPreferibleDTO>.Mal(System.Net.HttpStatusCode.BadRequest,
                    "Para aceptar la propuesta hace falta al menos una franja; si el cliente no tiene horario, descártela.");
            }
            // Guardar ya da por aceptada la propuesta pendiente del contacto.
            return await Guardar(propuesta.Empresa, propuesta.Cliente, propuesta.Contacto, new HorarioPreferibleDTO
            {
                Franjas = franjas.Select(f => f.ADto()).ToList()
            }, usuario).ConfigureAwait(false);
        }

        /// <summary>POST Propuestas/{id}/Descartar: los comentarios no dicen un horario que valga.</summary>
        public async Task<ResultadoHorarioPreferible<bool>> Descartar(int id, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoHorarioPreferible<bool>.Mal(System.Net.HttpStatusCode.BadRequest, MENSAJE_FALTA_SCRIPT);
            }
            bool hecho = await repositorio.ResolverPropuesta(id, PropuestaHorarioPreferibleDTO.ESTADO_DESCARTADA, usuario, null).ConfigureAwait(false);
            return hecho
                ? ResultadoHorarioPreferible<bool>.Bien(true)
                : ResultadoHorarioPreferible<bool>.Mal(System.Net.HttpStatusCode.NotFound, $"La propuesta {id} no existe o ya está revisada.");
        }

        /// <summary>
        /// Reparto propio: el horario de varios contactos a la vez, por <see cref="RepositorioHorarioPreferible.Clave"/>. Sin el
        /// script, vacío. Nunca lanza (el horario es un añadido: sin él, las paradas salen igual).
        /// </summary>
        public async Task<Dictionary<string, List<FranjaHorario>>> HorariosDe(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos)
        {
            var resultado = new Dictionary<string, List<FranjaHorario>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (contactos == null || contactos.Count == 0 || !repositorio.ScriptAplicado())
                {
                    return resultado;
                }
                // Se busca siempre en la empresa 1 (ver Empresa); cada clave del resultado lleva la empresa con la que se pidió.
                ILookup<string, string> pedidas = contactos.ToLookup(
                    c => RepositorioHorarioPreferible.Clave(Empresa(c.Empresa), c.Cliente, c.Contacto),
                    c => RepositorioHorarioPreferible.Clave(c.Empresa, c.Cliente, c.Contacto),
                    StringComparer.OrdinalIgnoreCase);
                List<(string, string, string)> enLaUno = contactos.Select(c => (Empresa(c.Empresa), c.Cliente, c.Contacto)).ToList();
                foreach (FilaHorarioPreferible fila in await repositorio.LeerVarios(enLaUno).ConfigureAwait(false) ?? new List<FilaHorarioPreferible>())
                {
                    foreach (string clave in pedidas[RepositorioHorarioPreferible.Clave(fila.Empresa, fila.Cliente, fila.Contacto)].Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!resultado.TryGetValue(clave, out List<FranjaHorario> franjas))
                        {
                            resultado[clave] = franjas = new List<FranjaHorario>();
                        }
                        franjas.Add(new FranjaHorario(fila.DiaSemana, fila.Desde, fila.Hasta));
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Horario preferible #763] No se han podido leer los horarios de las paradas: {ex.Message}", ex));
            }
            return resultado;
        }
    }
}
