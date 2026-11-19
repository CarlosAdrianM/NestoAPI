using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#881 (corte 2): el servicio técnico de una nota, tal como está guardado.</summary>
    public class FilaServicioTecnicoNota
    {
        public int Pedido { get; set; }
        public int? TipoServicioTecnicoId { get; set; }
        public string Nombre { get; set; }
        /// <summary>Los del tipo (RepartoTiposServicioTecnico.MinutosEstimados).</summary>
        public int? MinutosTipo { get; set; }
        /// <summary>Los tecleados en la nota (NotasEntregaCabecera.MinutosServicio).</summary>
        public short? MinutosServicio { get; set; }
    }

    /// <summary>
    /// NestoAPI#881 (corte 2): la regla del servicio técnico. PURA. Nombre: sin espacios de más, de 1 a <see cref="MAXIMO_NOMBRE"/>
    /// caracteres (se compara sin mayúsculas). Minutos tecleados: de 1 a <see cref="MAXIMO_MINUTOS"/>. Los que cuentan: los tecleados,
    /// si no los del tipo, si no los de por defecto.
    /// </summary>
    public static class ReglaServicioTecnico
    {
        public const int MAXIMO_NOMBRE = 50;
        public const int MAXIMO_MINUTOS = 600;

        public static string Normalizar(string nombre)
            => string.IsNullOrWhiteSpace(nombre) ? null : Regex.Replace(nombre.Trim(), @"\s+", " ");

        /// <summary>El 400 (de usted) o null.</summary>
        public static string MotivoNombre(string nombre)
        {
            string limpio = Normalizar(nombre);
            if (limpio == null)
            {
                return "Escriba el tipo de servicio técnico.";
            }
            return limpio.Length > MAXIMO_NOMBRE ? $"El tipo de servicio técnico no puede pasar de {MAXIMO_NOMBRE} caracteres." : null;
        }

        public static string MotivoMinutos(int? minutos)
            => minutos.HasValue && (minutos.Value < 1 || minutos.Value > MAXIMO_MINUTOS)
                ? $"Los minutos del servicio técnico tienen que estar entre 1 y {MAXIMO_MINUTOS}."
                : null;

        /// <summary>Lo que hay que validar de una petición antes de tocar nada (el nombre solo si no viene el Id).</summary>
        public static string Motivo(PeticionServicioTecnicoNotaDTO peticion)
        {
            if (peticion == null)
            {
                return null;
            }
            if (!peticion.TipoServicioTecnicoId.HasValue && !string.IsNullOrWhiteSpace(peticion.TipoServicioTecnicoNombre))
            {
                string nombre = MotivoNombre(peticion.TipoServicioTecnicoNombre);
                if (nombre != null)
                {
                    return nombre;
                }
            }
            if (SinTipo(peticion) && peticion.MinutosServicio.HasValue)
            {
                return "Para indicar los minutos del servicio técnico, elija también el tipo de servicio.";
            }
            return MotivoMinutos(peticion.MinutosServicio);
        }

        /// <summary>La petición no trae tipo: la nota no lleva servicio técnico.</summary>
        public static bool SinTipo(PeticionServicioTecnicoNotaDTO peticion)
            => peticion == null || (!peticion.TipoServicioTecnicoId.HasValue && string.IsNullOrWhiteSpace(peticion.TipoServicioTecnicoNombre));

        public static int MinutosQueCuentan(int? tecleados, int? delTipo, double porDefecto)
            => tecleados.HasValue && tecleados.Value > 0 ? tecleados.Value
                : delTipo.HasValue && delTipo.Value > 0 ? delTipo.Value
                : (int)Math.Round(porDefecto, MidpointRounding.AwayFromZero);
    }

    /// <summary>NestoAPI#881: la tabla de tipos y las columnas de la nota (Scripts/Issue881_RepartoFinRutaYTiempos.sql), fuera del EDMX.</summary>
    public interface IRepositorioServicioTecnico
    {
        /// <summary>¿Existen RepartoTiposServicioTecnico y las columnas TipoServicioTecnicoId y MinutosServicio de NotasEntregaCabecera?</summary>
        bool ScriptAplicado();
        Task<List<TipoServicioTecnicoDTO>> LeerTipos(bool soloActivos);
        Task<TipoServicioTecnicoDTO> LeerTipo(int id);
        /// <summary>Sin mayúsculas; null si no existe.</summary>
        Task<TipoServicioTecnicoDTO> LeerTipoPorNombre(string nombre);
        /// <summary>Lo crea; si otra petición lo ha creado a la vez (nombre único), devuelve ese.</summary>
        Task<TipoServicioTecnicoDTO> CrearTipo(string nombre, int? minutos, string usuario);
        /// <summary>El servicio técnico de esas notas (empresa 1); solo las que lo llevan.</summary>
        Task<Dictionary<int, FilaServicioTecnicoNota>> LeerDeNotas(string empresa, IReadOnlyCollection<int> pedidos);
        /// <summary>En UNA transacción: el tipo y los minutos de la nota (sin fila, la crea como Entrega) y la fila de Modificaciones.</summary>
        Task Guardar(string empresa, int pedido, int? tipoId, int? minutos, string usuario, string anterior, string nuevo);
    }

    public class RepositorioServicioTecnicoSql : IRepositorioServicioTecnico
    {
        public const string TABLA = "RepartoTiposServicioTecnico";
        public const string TABLA_RASTRO = "NotaServicioTecnico";
        private const string COLUMNAS = "Id, RTRIM(Nombre) AS Nombre, MinutosEstimados, Activo";

        private readonly NVEntities db;

        public RepositorioServicioTecnicoSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA)
            && ColumnasOpcionales.Existen(db, RepositorioNotasEntregaCabecera.TABLA, "TipoServicioTecnicoId", "MinutosServicio");

        public Task<List<TipoServicioTecnicoDTO>> LeerTipos(bool soloActivos)
            => db.Database.SqlQuery<TipoServicioTecnicoDTO>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) {(soloActivos ? "WHERE Activo = 1 " : string.Empty)}ORDER BY Nombre").ToListAsync();

        public async Task<TipoServicioTecnicoDTO> LeerTipo(int id)
            => (await db.Database.SqlQuery<TipoServicioTecnicoDTO>($"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Id = @p0", id)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<TipoServicioTecnicoDTO> LeerTipoPorNombre(string nombre)
            => (await db.Database.SqlQuery<TipoServicioTecnicoDTO>($"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Nombre = @p0", nombre ?? string.Empty)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<TipoServicioTecnicoDTO> CrearTipo(string nombre, int? minutos, string usuario)
        {
            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(
                    $"INSERT INTO dbo.{TABLA} (Nombre, MinutosEstimados, Activo, Usuario, FechaAlta) VALUES (@Nombre, @Minutos, 1, @Usuario, GETDATE())",
                    new SqlParameter("@Nombre", SqlDbType.NVarChar, ReglaServicioTecnico.MAXIMO_NOMBRE) { Value = nombre },
                    new SqlParameter("@Minutos", SqlDbType.Int) { Value = (object)minutos ?? DBNull.Value },
                    new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty }).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // Otra petición lo ha creado a la vez: vale el suyo.
            }
            return await LeerTipoPorNombre(nombre).ConfigureAwait(false);
        }

        public async Task<Dictionary<int, FilaServicioTecnicoNota>> LeerDeNotas(string empresa, IReadOnlyCollection<int> pedidos)
        {
            List<int> lista = (pedidos ?? new int[0]).Where(p => p > 0).Distinct().ToList();
            if (lista.Count == 0)
            {
                return new Dictionary<int, FilaServicioTecnicoNota>();
            }
            // Enteros en el texto (sin inyección posible).
            string ids = string.Join(", ", lista.Select(p => p.ToString(CultureInfo.InvariantCulture)));
            List<FilaServicioTecnicoNota> filas = await db.Database.SqlQuery<FilaServicioTecnicoNota>(
                $"SELECT n.Pedido, n.TipoServicioTecnicoId, RTRIM(t.Nombre) AS Nombre, t.MinutosEstimados AS MinutosTipo, n.MinutosServicio " +
                $"FROM dbo.{RepositorioNotasEntregaCabecera.TABLA} n WITH (NOLOCK) LEFT JOIN dbo.{TABLA} t WITH (NOLOCK) ON t.Id = n.TipoServicioTecnicoId " +
                $"WHERE n.Empresa = @p0 AND n.Pedido IN ({ids}) AND n.TipoServicioTecnicoId IS NOT NULL", empresa).ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(f => f.Pedido).ToDictionary(g => g.Key, g => g.First());
        }

        internal const string SQL_GUARDAR = "UPDATE dbo." + RepositorioNotasEntregaCabecera.TABLA + " SET TipoServicioTecnicoId = @Tipo, MinutosServicio = @Minutos, " +
            "Usuario = @Usuario, FechaModificacion = GETDATE() WHERE Empresa = @Empresa AND Pedido = @Pedido; " +
            "IF @@ROWCOUNT = 0 INSERT INTO dbo." + RepositorioNotasEntregaCabecera.TABLA + " (Empresa, Pedido, Tipo, Bultos, Usuario, FechaModificacion, " +
            "TipoServicioTecnicoId, MinutosServicio) VALUES (@Empresa, @Pedido, 'Entrega', 1, @Usuario, GETDATE(), @Tipo, @Minutos);";

        public async Task Guardar(string empresa, int pedido, int? tipoId, int? minutos, string usuario, string anterior, string nuevo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                        new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa },
                        new SqlParameter("@Pedido", SqlDbType.Int) { Value = pedido },
                        new SqlParameter("@Tipo", SqlDbType.Int) { Value = (object)tipoId ?? DBNull.Value },
                        new SqlParameter("@Minutos", SqlDbType.SmallInt) { Value = minutos.HasValue ? (object)(short)minutos.Value : DBNull.Value },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty }).ConfigureAwait(false);
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @UsuarioRastro)",
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = TABLA_RASTRO },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty }).ConfigureAwait(false);
                    transaccion.Commit();
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor?.Trim() : valor.Trim().Substring(0, Math.Min(maximo, valor.Trim().Length));
    }

    /// <summary>NestoAPI#881: lo que contesta el servicio del servicio técnico (el controlador lo traduce tal cual).</summary>
    public class ResultadoServicioTecnico<T>
    {
        public HttpStatusCode Estado { get; set; } = HttpStatusCode.OK;
        public T Contenido { get; set; }
        public string Mensaje { get; set; }
        public bool Bien => Estado == HttpStatusCode.OK;

        public static ResultadoServicioTecnico<T> Ok(T contenido) => new ResultadoServicioTecnico<T> { Contenido = contenido };
        public static ResultadoServicioTecnico<T> Mal(HttpStatusCode estado, string mensaje) => new ResultadoServicioTecnico<T> { Estado = estado, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#881 (corte 2): el «servicio técnico» de una nota de entrega o de recogida (lo marcan Alfredo o Santiago al crearla). Un tipo
    /// (combo que empieza vacío y se amplía escribiendo uno nuevo) y, opcional, los minutos tecleados. Sin el script, todo como antes: la
    /// lista de tipos sale vacía y poner uno da 503 (ELMAH una vez).
    /// </summary>
    public class ServicioServicioTecnicoReparto
    {
        public const string MENSAJE_FALTA_SCRIPT = "El servicio técnico de las notas aún no está disponible: falta lanzar el script " +
            "Scripts/Issue881_RepartoFinRutaYTiempos.sql. Avise a Informática.";
        private static bool sinScriptAvisado;

        private readonly IRepositorioServicioTecnico repositorio;
        private readonly IRepositorioNotasEntregaCabecera notas;
        private readonly Func<ConstantesTiemposReparto> constantes;

        public ServicioServicioTecnicoReparto(IRepositorioServicioTecnico repositorio, IRepositorioNotasEntregaCabecera notas,
            Func<ConstantesTiemposReparto> constantes = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.notas = notas;
            this.constantes = constantes ?? ConstantesTiemposReparto.PorDefecto;
        }

        public static ServicioServicioTecnicoReparto DeLaBaseDeDatos(NVEntities db)
            => new ServicioServicioTecnicoReparto(new RepositorioServicioTecnicoSql(db), new RepositorioNotasEntregaCabecera(db),
                () => new ConfiguracionTiemposRepartoParametros().Leer());

        private bool Disponible(bool avisar)
        {
            bool disponible = repositorio.ScriptAplicado();
            if (!disponible && avisar && !sinScriptAvisado)
            {
                sinScriptAvisado = true;
                ElmahHelper.Log(new Exception("[Reparto #881] " + MENSAJE_FALTA_SCRIPT + " (se avisa una vez)"));
            }
            return disponible;
        }

        /// <summary>GET api/RepartoPropio/TiposServicioTecnico: los activos por nombre. Sin script, vacía.</summary>
        public async Task<ResultadoServicioTecnico<List<TipoServicioTecnicoDTO>>> Tipos(bool todos = false)
        {
            if (!Disponible(avisar: false))
            {
                return ResultadoServicioTecnico<List<TipoServicioTecnicoDTO>>.Ok(new List<TipoServicioTecnicoDTO>());
            }
            return ResultadoServicioTecnico<List<TipoServicioTecnicoDTO>>.Ok(await repositorio.LeerTipos(!todos).ConfigureAwait(false) ?? new List<TipoServicioTecnicoDTO>());
        }

        /// <summary>POST api/RepartoPropio/TiposServicioTecnico: el nombre ya existe → el que hay (sin duplicar ni cambiar sus minutos).</summary>
        public async Task<ResultadoServicioTecnico<TipoServicioTecnicoDTO>> CrearTipo(CrearTipoServicioTecnicoDTO peticion, string usuario)
        {
            string motivo = ReglaServicioTecnico.MotivoNombre(peticion?.Nombre) ?? ReglaServicioTecnico.MotivoMinutos(peticion?.MinutosEstimados);
            if (motivo != null)
            {
                return ResultadoServicioTecnico<TipoServicioTecnicoDTO>.Mal(HttpStatusCode.BadRequest, motivo);
            }
            if (!Disponible(avisar: true))
            {
                return ResultadoServicioTecnico<TipoServicioTecnicoDTO>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            TipoServicioTecnicoDTO tipo = await ObtenerOCrear(ReglaServicioTecnico.Normalizar(peticion.Nombre), peticion.MinutosEstimados, usuario).ConfigureAwait(false);
            return ResultadoServicioTecnico<TipoServicioTecnicoDTO>.Ok(tipo);
        }

        private async Task<TipoServicioTecnicoDTO> ObtenerOCrear(string nombre, int? minutos, string usuario)
            => await repositorio.LeerTipoPorNombre(nombre).ConfigureAwait(false)
               ?? await repositorio.CrearTipo(nombre, minutos, usuario).ConfigureAwait(false);

        /// <summary>GET api/NotasEntrega/{empresa}/{pedido}/ServicioTecnico. Sin script o sin servicio técnico: todo a null.</summary>
        public async Task<ResultadoServicioTecnico<ServicioTecnicoNotaDTO>> Leer(string empresa, int pedido)
        {
            empresa = empresa?.Trim();
            var dto = new ServicioTecnicoNotaDTO { Empresa = empresa, Pedido = pedido };
            if (!Disponible(avisar: false))
            {
                return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Ok(dto);
            }
            Dictionary<int, FilaServicioTecnicoNota> filas = await repositorio.LeerDeNotas(empresa, new[] { pedido }).ConfigureAwait(false);
            if (filas != null && filas.TryGetValue(pedido, out FilaServicioTecnicoNota fila))
            {
                Rellenar(dto, fila);
            }
            return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Ok(dto);
        }

        private void Rellenar(ServicioTecnicoNotaDTO dto, FilaServicioTecnicoNota fila)
        {
            dto.TipoServicioTecnicoId = fila.TipoServicioTecnicoId;
            dto.ServicioTecnico = fila.Nombre;
            dto.MinutosServicio = fila.MinutosServicio;
            dto.MinutosEstimados = ReglaServicioTecnico.MinutosQueCuentan(fila.MinutosServicio, fila.MinutosTipo, constantes().MinutosServicioTecnico);
        }

        /// <summary>
        /// PUT api/NotasEntrega/{empresa}/{pedido}/ServicioTecnico: ponerlo, cambiarlo o quitarlo (sin tipo). Solo en notas de entrega o
        /// de recogida (no de cobro) sin entregar. Deja rastro en Modificaciones (Tabla «NotaServicioTecnico»).
        /// </summary>
        public async Task<ResultadoServicioTecnico<ServicioTecnicoNotaDTO>> Poner(string empresa, int pedido, PeticionServicioTecnicoNotaDTO peticion, string usuario)
        {
            empresa = empresa?.Trim();
            peticion = peticion ?? new PeticionServicioTecnicoNotaDTO();
            string motivo = ReglaServicioTecnico.Motivo(peticion);
            if (motivo != null)
            {
                return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.BadRequest, motivo);
            }
            if (!Disponible(avisar: true))
            {
                return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            if (notas != null)
            {
                PedidoNotaEntrega cabecera = await notas.LeerPedido(empresa, pedido).ConfigureAwait(false);
                if (cabecera == null)
                {
                    return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.NotFound, $"No existe el pedido {pedido}.");
                }
                if (!cabecera.NotaEntrega)
                {
                    return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.BadRequest,
                        $"El pedido {pedido} no es una nota de entrega: el servicio técnico solo se indica en notas de entrega o de recogida.");
                }
                FilaNotaEntregaCabecera fila = await notas.Leer(empresa, pedido).ConfigureAwait(false);
                if (TiposNotaEntrega.Normalizar(fila?.Tipo) == TiposNotaEntrega.COBRO)
                {
                    return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.BadRequest,
                        $"La nota {pedido} es una nota de cobro: no lleva servicio técnico.");
                }
                EnvioNotaEntrega envio = await notas.LeerUltimoEnvio(empresa, pedido).ConfigureAwait(false);
                if (envio != null && envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
                {
                    return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.Conflict,
                        $"La nota {pedido} ya está entregada (envío {envio.Numero}): no se puede cambiar su servicio técnico.");
                }
            }

            int? tipoId = null;
            if (!ReglaServicioTecnico.SinTipo(peticion))
            {
                TipoServicioTecnicoDTO tipo;
                if (peticion.TipoServicioTecnicoId.HasValue)
                {
                    tipo = await repositorio.LeerTipo(peticion.TipoServicioTecnicoId.Value).ConfigureAwait(false);
                    if (tipo == null)
                    {
                        return ResultadoServicioTecnico<ServicioTecnicoNotaDTO>.Mal(HttpStatusCode.BadRequest,
                            $"No existe el tipo de servicio técnico {peticion.TipoServicioTecnicoId.Value}.");
                    }
                }
                else
                {
                    tipo = await ObtenerOCrear(ReglaServicioTecnico.Normalizar(peticion.TipoServicioTecnicoNombre), null, usuario).ConfigureAwait(false);
                }
                tipoId = tipo?.Id;
            }
            int? minutos = tipoId.HasValue ? peticion.MinutosServicio : null;

            Dictionary<int, FilaServicioTecnicoNota> antes = await repositorio.LeerDeNotas(empresa, new[] { pedido }).ConfigureAwait(false);
            FilaServicioTecnicoNota anterior = antes != null && antes.TryGetValue(pedido, out FilaServicioTecnicoNota a) ? a : null;
            if (anterior?.TipoServicioTecnicoId == tipoId && (int?)anterior?.MinutosServicio == minutos)
            {
                return await Leer(empresa, pedido).ConfigureAwait(false); // ya estaba así: sin rastro
            }
            await repositorio.Guardar(empresa, pedido, tipoId, minutos, usuario,
                Rastro(empresa, pedido, anterior?.TipoServicioTecnicoId, anterior?.Nombre, anterior?.MinutosServicio),
                Rastro(empresa, pedido, tipoId, null, minutos)).ConfigureAwait(false);
            return await Leer(empresa, pedido).ConfigureAwait(false);
        }

        private static string Rastro(string empresa, int pedido, int? tipo, string nombre, int? minutos)
            => JsonConvert.SerializeObject(new { Empresa = empresa, Pedido = pedido, TipoServicioTecnicoId = tipo, Nombre = nombre, MinutosServicio = minutos });

        /// <summary>
        /// Después de crear la nota (POST api/PedidosVenta o confirmar una recogida solicitada): lo pone si viene algo. Nunca lanza (la nota
        /// ya está creada): devuelve el motivo si no se ha podido, para decirlo.
        /// </summary>
        public async Task<string> PonerAlCrear(string empresa, int pedido, PeticionServicioTecnicoNotaDTO peticion, string usuario)
        {
            if (ReglaServicioTecnico.SinTipo(peticion))
            {
                return null;
            }
            try
            {
                ResultadoServicioTecnico<ServicioTecnicoNotaDTO> resultado = await Poner(empresa, pedido, peticion, usuario).ConfigureAwait(false);
                return resultado.Bien ? null : resultado.Mensaje;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] La nota {pedido} se ha creado, pero no se pudo guardar su servicio técnico: {ex.Message}", ex));
                return "no se pudo guardar el servicio técnico";
            }
        }

        /// <summary>GET api/RepartoPropio/Envios: ServicioTecnico y MinutosServicio de las paradas que son notas. Nunca lanza.</summary>
        public async Task Completar(List<EnvioRepartoDTO> paradas)
        {
            List<EnvioRepartoDTO> notasDeLaLista = (paradas ?? new List<EnvioRepartoDTO>())
                .Where(p => p.Pedido.HasValue && (p.TipoParada == TiposParadaReparto.NOTA_ENTREGA || p.TipoParada == TiposParadaReparto.NOTA_RECOGIDA))
                .ToList();
            if (notasDeLaLista.Count == 0)
            {
                return;
            }
            try
            {
                if (!Disponible(avisar: false))
                {
                    return;
                }
                Dictionary<int, FilaServicioTecnicoNota> filas = await repositorio.LeerDeNotas(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    notasDeLaLista.Select(p => p.Pedido.Value).ToList()).ConfigureAwait(false) ?? new Dictionary<int, FilaServicioTecnicoNota>();
                double porDefecto = constantes().MinutosServicioTecnico;
                foreach (EnvioRepartoDTO parada in notasDeLaLista)
                {
                    if (filas.TryGetValue(parada.Pedido.Value, out FilaServicioTecnicoNota fila) && fila.TipoServicioTecnicoId.HasValue)
                    {
                        parada.ServicioTecnico = string.IsNullOrWhiteSpace(fila.Nombre) ? "Servicio técnico" : fila.Nombre;
                        parada.TipoServicioTecnicoId = fila.TipoServicioTecnicoId;
                        parada.MinutosServicioTecleados = fila.MinutosServicio;
                        parada.MinutosServicio = ReglaServicioTecnico.MinutosQueCuentan(fila.MinutosServicio, fila.MinutosTipo, porDefecto);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] No se pudo leer el servicio técnico de las paradas: {ex.Message}", ex));
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos() => sinScriptAvisado = false;
    }
}
