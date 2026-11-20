using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>Los estados de un envío interno (los mismos que el CHECK de RepartoEnviosInternos).</summary>
    public static class EstadosEnvioInterno
    {
        public const string PENDIENTE = "Pendiente";
        public const string EN_CAMINO = "EnCamino";
        public const string ENTREGADO = "Entregado";
        public const string ANULADO = "Anulado";
    }

    /// <summary>Sugerencia 1277: una fila de dbo.RepartoEnviosInternos.</summary>
    public class EnvioInternoReparto
    {
        public int Id { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Descripcion { get; set; }
        public int Bultos { get; set; }
        /// <summary>Lo guardado: Pendiente, Entregado o Anulado (EnCamino se deduce de los bultos).</summary>
        public string Estado { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaAlta { get; set; }
        public string UsuarioRecepcion { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public string NombreReceptor { get; set; }
        public Guid? ClaveEntrega { get; set; }
        public string DetalleEntrega { get; set; }
        public string MotivoAnulacion { get; set; }
    }

    /// <summary>Dónde está un bulto de un envío interno.</summary>
    public static class SituacionesBultoEnvioInterno
    {
        public const string SIN_CARGAR = "SinCargar";
        public const string EN_FURGONETA = "EnFurgoneta";
        public const string ENTREGADO = "Entregado";
        public const string DESCARGADO = "Descargado";
    }

    /// <summary>Una parada que genera un envío interno vivo.</summary>
    public class ParadaEnvioInterno
    {
        public int EnvioId { get; set; }
        public bool EsRecogida { get; set; }
        /// <summary>La sede de la parada (el origen en la recogida, el destino en la entrega).</summary>
        public string Sede { get; set; }
        /// <summary>Quien lleva sus bultos (usuario con dominio); null si nadie.</summary>
        public string Repartidor { get; set; }
        public int Bultos { get; set; }
        public int BultosCargados { get; set; }
    }

    /// <summary>
    /// Sugerencia 1277 (Alfredo): envíos internos entre sedes (material sin stock, aparatos de demostración o de clientes). NO son stock:
    /// ni almacenes ni ExtractoProducto, ni cliente ni pedido ficticio. Cada bulto lleva su etiqueta <c>EI{id}-{n}</c> y su historia en
    /// RepartoBultosMovimientos como los demás (Cargado, Recogido, Entregado, Descargado; EnvioId = <see cref="EnvioIdEntrega"/>). LAS
    /// REGLAS, puras:
    /// <list type="bullet">
    /// <item>Sedes: ALG, REI y ALC, origen y destino distintos; 1..50 bultos; descripción hasta 200 (<see cref="ValidarAlta"/>).</item>
    /// <item>Paradas (<see cref="Paradas"/>): una de RECOGIDA en el origen si no es Algete mientras quede algún bulto allí (se leen con
    /// «Recoger») y una de ENTREGA en el destino mientras esté vivo (se entrega con «Entregar»). Desde Algete, los bultos se cargan
    /// con «Cargar». Ids de parada negativos por debajo de −<see cref="BASE_PARADAS"/> (las de reposición, −Id, quedan por encima).</item>
    /// <item>Estado: Pendiente sin ningún bulto movido, EnCamino con alguno, Entregado y Anulado guardados (<see cref="Estado"/>).</item>
    /// <item>Dónde está cada bulto (<see cref="DondeEsta"/>): en el origen sin cargar, en la furgoneta de X, entregado o descargado.</item>
    /// <item>Minutos de parada para el fin de ruta: los de una reposición (<see cref="MinutosParadaReparto.DeTipo"/>).</item>
    /// </list>
    /// </summary>
    public static class ReglasEnviosInternos
    {
        public const string TABLA = "RepartoEnviosInternos";
        public const int MAXIMO_BULTOS = 50;
        public const int MAXIMO_DESCRIPCION = 200;
        public const int BASE_PARADAS = 1000000000;
        public const string TABLA_MODIFICACIONES_ANULADO = "EnvioInternoAnulado";
        public const string MENSAJE_FALTA_SCRIPT = "Falta lanzar Scripts/RepartoEnviosInternos.sql (envíos internos entre sedes). Avise a Informática.";
        public const string MENSAJE_SIN_PERMISO = "Solo Almacén, Dirección e Informática pueden dar de alta, imprimir o anular envíos internos.";

        public static readonly IReadOnlyList<string> SEDES = new[] { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        private static readonly Regex patron = new Regex(@"^(?:\][A-Z][0-9A-Z])?EI([0-9]+)[^\p{L}\p{N}]+([0-9]+)$", RegexOptions.CultureInvariant);

        public static string Sede(string almacen)
        {
            string limpio = almacen?.Trim().ToUpperInvariant();
            return SEDES.Contains(limpio) ? limpio : null;
        }

        /// <returns>El error de usted, o null si vale.</returns>
        public static string ValidarAlta(string origen, string destino, string descripcion, int bultos)
        {
            string o = Sede(origen);
            string d = Sede(destino);
            if (o == null || d == null)
            {
                return "El origen y el destino tienen que ser una sede: ALG (Algete), REI (Reina) o ALC (Alcobendas).";
            }
            if (o == d)
            {
                return "El origen y el destino no pueden ser la misma sede.";
            }
            if (string.IsNullOrWhiteSpace(descripcion))
            {
                return "Escriba qué se manda (la descripción).";
            }
            if (descripcion.Trim().Length > MAXIMO_DESCRIPCION)
            {
                return $"La descripción no puede pasar de {MAXIMO_DESCRIPCION} caracteres.";
            }
            if (bultos < 1 || bultos > MAXIMO_BULTOS)
            {
                return $"Los bultos tienen que estar entre 1 y {MAXIMO_BULTOS}.";
            }
            return null;
        }

        public static string Codigo(int id, int bulto) => string.Format(CultureInfo.InvariantCulture, "EI{0}-{1}", id, bulto);

        public static string Documento(int id) => "EI" + id.ToString(CultureInfo.InvariantCulture);

        public static List<string> Codigos(int id, int bultos) => Enumerable.Range(1, Math.Max(bultos, 1)).Select(n => Codigo(id, n)).ToList();

        /// <summary>«EI12-1» (con los separadores raros y el prefijo AIM, como las R).</summary>
        public static bool TryLeer(string codigo, out int id, out int bulto)
        {
            id = bulto = 0;
            string limpio = codigo?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(limpio))
            {
                return false;
            }
            Match m = patron.Match(limpio);
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id)
                && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out bulto) && id > 0 && bulto > 0;
        }

        public static int EnvioIdEntrega(int id) => -(BASE_PARADAS + id * 2);

        public static int EnvioIdRecogida(int id) => -(BASE_PARADAS + id * 2 + 1);

        public static bool EsParadaEnvioInterno(int envioId) => envioId <= -BASE_PARADAS;

        public static int IdDeParada(int envioId) => (-envioId - BASE_PARADAS) / 2;

        public static bool EsParadaRecogida(int envioId) => EsParadaEnvioInterno(envioId) && (-envioId - BASE_PARADAS) % 2 == 1;

        public static bool EsVivo(EnvioInternoReparto e)
            => e != null && e.Estado != EstadosEnvioInterno.ENTREGADO && e.Estado != EstadosEnvioInterno.ANULADO;

        /// <summary>Los últimos movimientos de los bultos de ese envío interno.</summary>
        public static List<MovimientoBultoReparto> Ultimos(EnvioInternoReparto e, IEnumerable<MovimientoBultoReparto> historia)
        {
            HashSet<string> codigos = new HashSet<string>(Codigos(e.Id, e.Bultos), StringComparer.OrdinalIgnoreCase);
            return NucleoFurgoneta.Ultimos((historia ?? Enumerable.Empty<MovimientoBultoReparto>())
                .Where(m => m != null && m.Codigo != null && codigos.Contains(m.Codigo.Trim())));
        }

        /// <summary>El estado de cara a fuera: lo guardado, o EnCamino si está Pendiente y algún bulto se ha movido.</summary>
        public static string Estado(EnvioInternoReparto e, IEnumerable<MovimientoBultoReparto> historia)
            => !EsVivo(e) ? e?.Estado : Ultimos(e, historia).Count > 0 ? EstadosEnvioInterno.EN_CAMINO : EstadosEnvioInterno.PENDIENTE;

        public static BultoEnvioInternoDTO DondeEsta(EnvioInternoReparto e, int bulto, MovimientoBultoReparto ultimo)
        {
            var dto = new BultoEnvioInternoDTO { Codigo = Codigo(e.Id, bulto), Bulto = bulto };
            if (ultimo == null)
            {
                dto.Situacion = SituacionesBultoEnvioInterno.SIN_CARGAR;
                dto.Texto = $"En {ServicioRepartoPropio.NombreTienda(e.Origen)}, sin cargar";
            }
            else if (TiposMovimientoBulto.DejaEnFurgoneta(ultimo.Tipo))
            {
                dto.Situacion = SituacionesBultoEnvioInterno.EN_FURGONETA;
                dto.Repartidor = ServicioRepartoPropio.NombreRepartidor(ultimo.Usuario);
                dto.Texto = $"En la furgoneta de {dto.Repartidor}";
            }
            else if (ultimo.Tipo == TiposMovimientoBulto.ENTREGADO)
            {
                dto.Situacion = SituacionesBultoEnvioInterno.ENTREGADO;
                dto.Texto = $"Entregado en {ServicioRepartoPropio.NombreTienda(e.Destino)}";
            }
            else
            {
                dto.Situacion = SituacionesBultoEnvioInterno.DESCARGADO;
                dto.Texto = "Descargado en el almacén";
            }
            dto.Desde = ultimo?.FechaHora;
            return dto;
        }

        public static List<BultoEnvioInternoDTO> Bultos(EnvioInternoReparto e, IEnumerable<MovimientoBultoReparto> historia)
        {
            List<MovimientoBultoReparto> ultimos = Ultimos(e, historia);
            return Enumerable.Range(1, Math.Max(e.Bultos, 1))
                .Select(n => DondeEsta(e, n, ultimos.FirstOrDefault(u => string.Equals(u.Codigo?.Trim(), Codigo(e.Id, n), StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        /// <summary>Las paradas que genera (ninguna si ya no está vivo).</summary>
        public static List<ParadaEnvioInterno> Paradas(EnvioInternoReparto e, IEnumerable<MovimientoBultoReparto> historia)
        {
            var paradas = new List<ParadaEnvioInterno>();
            if (!EsVivo(e))
            {
                return paradas;
            }
            List<MovimientoBultoReparto> ultimos = Ultimos(e, historia);
            int total = Math.Max(e.Bultos, 1);
            int sinMover = total - ultimos.Count;
            if (Sede(e.Origen) != Constantes.Almacenes.ALGETE && sinMover > 0)
            {
                // De nadie hasta que alguien lee allí un EI suyo; entonces, de quien lo recogió.
                string recogedor = ultimos.Where(u => u.Tipo == TiposMovimientoBulto.RECOGIDO).OrderBy(u => u.FechaHora).ThenBy(u => u.Id)
                    .LastOrDefault()?.Usuario?.Trim();
                paradas.Add(new ParadaEnvioInterno
                {
                    EnvioId = EnvioIdRecogida(e.Id),
                    EsRecogida = true,
                    Repartidor = recogedor,
                    Sede = Sede(e.Origen),
                    Bultos = total,
                    BultosCargados = total - sinMover
                });
            }
            List<MovimientoBultoReparto> encima = ultimos.Where(u => TiposMovimientoBulto.DejaEnFurgoneta(u.Tipo)).OrderBy(u => u.FechaHora).ThenBy(u => u.Id).ToList();
            string repartidor = encima.LastOrDefault()?.Usuario?.Trim();
            paradas.Add(new ParadaEnvioInterno
            {
                EnvioId = EnvioIdEntrega(e.Id),
                EsRecogida = false,
                Sede = Sede(e.Destino),
                Repartidor = repartidor,
                Bultos = total,
                BultosCargados = repartidor == null ? 0 : encima.Count(u => ServicioRepartoPropio.MismoUsuario(u.Usuario, repartidor))
            });
            return paradas;
        }

        /// <summary>«Envío interno 12: Reina → Alcobendas».</summary>
        public static string Descripcion(EnvioInternoReparto e)
            => $"Envío interno {e.Id}: {ServicioRepartoPropio.NombreTienda(e.Origen)} → {ServicioRepartoPropio.NombreTienda(e.Destino)}";

        public static EnvioInternoDTO ADto(EnvioInternoReparto e, IEnumerable<MovimientoBultoReparto> historia)
            => new EnvioInternoDTO
            {
                Id = e.Id,
                Origen = Sede(e.Origen) ?? e.Origen?.Trim(),
                Destino = Sede(e.Destino) ?? e.Destino?.Trim(),
                Descripcion = e.Descripcion?.Trim(),
                Bultos = e.Bultos,
                Estado = Estado(e, historia),
                Usuario = e.Usuario?.Trim(),
                FechaAlta = e.FechaAlta,
                NombreReceptor = e.NombreReceptor?.Trim(),
                UsuarioRecepcion = e.UsuarioRecepcion?.Trim(),
                FechaRecepcion = e.FechaRecepcion,
                DetalleEntrega = e.DetalleEntrega?.Trim(),
                Codigos = Codigos(e.Id, e.Bultos),
                BultosDetalle = Bultos(e, historia)
            };

        /// <summary>La etiqueta de cada bulto: la plantilla del reparto propio con «ENVÍO INTERNO», de dónde a dónde y qué lleva.</summary>
        public static string Zpl(EnvioInternoReparto e, DateTime fecha)
        {
            TiendasRecogidaDestino(e.Destino, out string direccion, out string cp, out string poblacion, out string telefono);
            var datos = new DatosEnvioRemoto
            {
                NumeroEnvio = e.Id,
                Nombre = $"DE {ServicioRepartoPropio.NombreTienda(e.Origen)?.ToUpperInvariant()} A {ServicioRepartoPropio.NombreTienda(e.Destino)?.ToUpperInvariant()}",
                Direccion = direccion,
                CodigoPostal = cp,
                Poblacion = poblacion,
                Telefono = telefono,
                Observaciones = e.Descripcion?.Trim(),
                Bultos = Math.Max(e.Bultos, 1),
                Reembolso = 0m
            };
            var plantilla = new PlantillaEtiquetaRepartoPropio
            {
                Titulo = "ENVÍO INTERNO",
                EtiquetaReferencia = "Envío: ",
                Referencia = Documento(e.Id),
                Bultos = Math.Max(e.Bultos, 1),
                CodigoBulto = n => Codigo(e.Id, n),
                Pie = "Nueva Visión - Envío interno (no es stock)"
            };
            return EtiquetaRepartoPropio.ConstruirZpl(datos, fecha, plantilla);
        }

        private static void TiendasRecogidaDestino(string almacen, out string direccion, out string cp, out string poblacion, out string telefono)
        {
            PedidosVenta.TiendasRecogida.Tienda tienda = PedidosVenta.TiendasRecogida.Buscar(almacen);
            direccion = tienda?.Direccion;
            cp = tienda?.CodigoPostal;
            poblacion = tienda?.Poblacion ?? ServicioRepartoPropio.NombreTienda(almacen);
            telefono = tienda?.Telefono;
        }
    }

    /// <summary>Sugerencia 1277: dbo.RepartoEnviosInternos (Scripts/RepartoEnviosInternos.sql), fuera del EDMX.</summary>
    public interface IRepositorioEnviosInternos
    {
        bool ScriptAplicado();
        Task<int> Crear(EnvioInternoReparto envio);
        Task<EnvioInternoReparto> Leer(int id);
        /// <summary>Los dados de alta desde <paramref name="desde"/> (con soloVivos, solo Pendiente; también los vivos de antes).</summary>
        Task<List<EnvioInternoReparto>> LeerLista(DateTime desde, bool soloVivos);
        /// <summary>Los vivos (Pendiente) de los últimos días, para el reparto.</summary>
        Task<List<EnvioInternoReparto>> LeerVivos(DateTime desde);
        /// <summary>Lo da por entregado si sigue Pendiente. False si no.</summary>
        Task<bool> Entregar(int id, Guid clave, DateTime fechaHora, string usuario, string receptor, string detalle);
        /// <summary>Lo anula (si sigue Pendiente) con una fila en Modificaciones. False si no.</summary>
        Task<bool> Anular(int id, string motivo, string usuario, string anterior);
    }

    public class RepositorioEnviosInternosSql : IRepositorioEnviosInternos
    {
        private const string T = "dbo." + ReglasEnviosInternos.TABLA;
        private const string COLUMNAS = "Id, RTRIM(Origen) AS Origen, RTRIM(Destino) AS Destino, Descripcion, CAST(Bultos AS int) AS Bultos, RTRIM(Estado) AS Estado, " +
            "RTRIM(Usuario) AS Usuario, FechaAlta, RTRIM(UsuarioRecepcion) AS UsuarioRecepcion, FechaRecepcion, NombreReceptor, ClaveEntrega, DetalleEntrega, MotivoAnulacion";

        private readonly NVEntities db;

        public RepositorioEnviosInternosSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, ReglasEnviosInternos.TABLA);

        public async Task<int> Crear(EnvioInternoReparto e)
            => (await db.Database.SqlQuery<int>(
                $"INSERT INTO {T} (Origen, Destino, Descripcion, Bultos, Estado, Usuario, FechaAlta) " +
                "VALUES (@Origen, @Destino, @Descripcion, @Bultos, @Estado, @Usuario, @FechaAlta); SELECT CAST(SCOPE_IDENTITY() AS int);",
                new SqlParameter("@Origen", SqlDbType.Char, 3) { Value = e.Origen },
                new SqlParameter("@Destino", SqlDbType.Char, 3) { Value = e.Destino },
                new SqlParameter("@Descripcion", SqlDbType.NVarChar, ReglasEnviosInternos.MAXIMO_DESCRIPCION) { Value = e.Descripcion },
                new SqlParameter("@Bultos", SqlDbType.SmallInt) { Value = (short)e.Bultos },
                new SqlParameter("@Estado", SqlDbType.VarChar, 12) { Value = EstadosEnvioInterno.PENDIENTE },
                new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(e.Usuario, 50) },
                new SqlParameter("@FechaAlta", SqlDbType.DateTime) { Value = e.FechaAlta }).ToListAsync().ConfigureAwait(false)).First();

        public async Task<EnvioInternoReparto> Leer(int id)
            => (await db.Database.SqlQuery<EnvioInternoReparto>($"SELECT {COLUMNAS} FROM {T} WHERE Id = @p0", id).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<EnvioInternoReparto>> LeerLista(DateTime desde, bool soloVivos)
            => db.Database.SqlQuery<EnvioInternoReparto>(
                $"SELECT {COLUMNAS} FROM {T} WHERE " + (soloVivos ? "Estado = @p1" : "(FechaAlta >= @p0 OR Estado = @p1)") + " ORDER BY Id DESC",
                desde, EstadosEnvioInterno.PENDIENTE).ToListAsync();

        public Task<List<EnvioInternoReparto>> LeerVivos(DateTime desde)
            => db.Database.SqlQuery<EnvioInternoReparto>($"SELECT {COLUMNAS} FROM {T} WHERE Estado = @p0 AND FechaAlta >= @p1 ORDER BY Id",
                EstadosEnvioInterno.PENDIENTE, desde).ToListAsync();

        public async Task<bool> Entregar(int id, Guid clave, DateTime fechaHora, string usuario, string receptor, string detalle)
            => await db.Database.ExecuteSqlCommandAsync(
                $"UPDATE {T} SET Estado = @Entregado, FechaRecepcion = @FechaHora, UsuarioRecepcion = @Usuario, NombreReceptor = @Receptor, " +
                "ClaveEntrega = @Clave, DetalleEntrega = @Detalle WHERE Id = @Id AND Estado = @Pendiente",
                new SqlParameter("@Entregado", SqlDbType.VarChar, 12) { Value = EstadosEnvioInterno.ENTREGADO },
                new SqlParameter("@Pendiente", SqlDbType.VarChar, 12) { Value = EstadosEnvioInterno.PENDIENTE },
                new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = fechaHora },
                new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                new SqlParameter("@Receptor", SqlDbType.NVarChar, 100) { Value = (object)receptor ?? DBNull.Value },
                new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = clave },
                new SqlParameter("@Detalle", SqlDbType.NVarChar, 200) { Value = (object)detalle ?? DBNull.Value },
                new SqlParameter("@Id", SqlDbType.Int) { Value = id }).ConfigureAwait(false) > 0;

        public async Task<bool> Anular(int id, string motivo, string usuario, string anterior)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(
                        $"UPDATE {T} SET Estado = @Anulado, MotivoAnulacion = @Motivo, UsuarioAnulacion = @Usuario, FechaAnulacion = GETDATE() " +
                        "WHERE Id = @Id AND Estado = @Pendiente",
                        new SqlParameter("@Anulado", SqlDbType.VarChar, 12) { Value = EstadosEnvioInterno.ANULADO },
                        new SqlParameter("@Pendiente", SqlDbType.VarChar, 12) { Value = EstadosEnvioInterno.PENDIENTE },
                        new SqlParameter("@Motivo", SqlDbType.NVarChar, 200) { Value = (object)motivo ?? DBNull.Value },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @UsuarioRastro)",
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = ReglasEnviosInternos.TABLA_MODIFICACIONES_ANULADO },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = JsonConvert.SerializeObject(new { Id = id, Estado = EstadosEnvioInterno.ANULADO, Motivo = motivo }) },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) }).ConfigureAwait(false);
                    transaccion.Commit();
                    return true;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        private static string Recortar(string valor, int maximo) => valor == null ? string.Empty : valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>
    /// Sugerencia 1277: el alta, la impresión, la lista y la anulación de los envíos internos (lo de oficina; el reparto está en
    /// ServicioRepartoPropio.EnviosInternos.cs). Sin el script, 503.
    /// </summary>
    public class ServicioEnviosInternos
    {
        private readonly IRepositorioEnviosInternos repositorio;
        private readonly IRepositorioFurgoneta furgoneta;
        private readonly IImpresoraEtiquetas impresora;
        private readonly Func<string, string> impresoraDelUsuario;
        private readonly Func<DateTime> ahora;

        /// <param name="impresoraDelUsuario">La impresora de etiquetas de ese usuario; lanza ArgumentException si no tiene.</param>
        public ServicioEnviosInternos(IRepositorioEnviosInternos repositorio, IRepositorioFurgoneta furgoneta, IImpresoraEtiquetas impresora,
            Func<string, string> impresoraDelUsuario, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.furgoneta = furgoneta;
            this.impresora = impresora;
            this.impresoraDelUsuario = impresoraDelUsuario;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        private static ResultadoReparto<T> SinScript<T>() => ResultadoReparto<T>.Mal(TipoResultadoReparto.NoDisponible, ReglasEnviosInternos.MENSAJE_FALTA_SCRIPT);

        private bool FurgonetaDisponible() => furgoneta != null && furgoneta.ScriptAplicado();

        private async Task<List<MovimientoBultoReparto>> Historia(IEnumerable<EnvioInternoReparto> envios)
        {
            List<int> ids = envios.Select(e => ReglasEnviosInternos.EnvioIdEntrega(e.Id)).ToList();
            if (ids.Count == 0 || !FurgonetaDisponible())
            {
                return new List<MovimientoBultoReparto>();
            }
            return await furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Envios = ids }).ConfigureAwait(false) ?? new List<MovimientoBultoReparto>();
        }

        public async Task<ResultadoReparto<EnvioInternoCreadoDTO>> Crear(CrearEnvioInternoDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return SinScript<EnvioInternoCreadoDTO>();
            }
            string error = peticion == null ? "Faltan los datos del envío interno."
                : ReglasEnviosInternos.ValidarAlta(peticion.Origen, peticion.Destino, peticion.Descripcion, peticion.Bultos);
            if (error != null)
            {
                return ResultadoReparto<EnvioInternoCreadoDTO>.Mal(TipoResultadoReparto.Invalido, error);
            }
            var envio = new EnvioInternoReparto
            {
                Origen = ReglasEnviosInternos.Sede(peticion.Origen),
                Destino = ReglasEnviosInternos.Sede(peticion.Destino),
                Descripcion = peticion.Descripcion.Trim(),
                Bultos = peticion.Bultos,
                Estado = EstadosEnvioInterno.PENDIENTE,
                Usuario = usuario,
                FechaAlta = ahora()
            };
            envio.Id = await repositorio.Crear(envio).ConfigureAwait(false);
            bool impresas = Imprimir(envio, peticion.Impresora, usuario);
            return ResultadoReparto<EnvioInternoCreadoDTO>.Bien(new EnvioInternoCreadoDTO
            {
                Id = envio.Id,
                Origen = envio.Origen,
                Destino = envio.Destino,
                Descripcion = envio.Descripcion,
                Bultos = envio.Bultos,
                Estado = envio.Estado,
                Codigos = ReglasEnviosInternos.Codigos(envio.Id, envio.Bultos),
                EtiquetasImpresas = impresas
            });
        }

        public async Task<ResultadoReparto<EtiquetasEnvioInternoDTO>> ImprimirEtiquetas(int id, string impresoraPedida, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return SinScript<EtiquetasEnvioInternoDTO>();
            }
            EnvioInternoReparto envio = await repositorio.Leer(id).ConfigureAwait(false);
            if (envio == null)
            {
                return ResultadoReparto<EtiquetasEnvioInternoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío interno {id}.");
            }
            return ResultadoReparto<EtiquetasEnvioInternoDTO>.Bien(new EtiquetasEnvioInternoDTO
            {
                Id = id,
                Codigos = ReglasEnviosInternos.Codigos(id, envio.Bultos),
                EtiquetasImpresas = Imprimir(envio, impresoraPedida, usuario)
            });
        }

        /// <summary>Nunca lanza: si no se puede imprimir, false (y ELMAH).</summary>
        private bool Imprimir(EnvioInternoReparto envio, string impresoraPedida, string usuario)
        {
            try
            {
                string destino = string.IsNullOrWhiteSpace(impresoraPedida) ? impresoraDelUsuario?.Invoke(usuario) : impresoraPedida.Trim();
                if (string.IsNullOrWhiteSpace(destino) || impresora == null)
                {
                    return false;
                }
                impresora.Imprimir(destino, ReglasEnviosInternos.Zpl(envio, ahora()), $"Envío interno {envio.Id}");
                return true;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Envíos internos] El envío interno {envio.Id} está creado, pero no se han podido imprimir sus etiquetas: {ex.Message}", ex));
                return false;
            }
        }

        public async Task<ResultadoReparto<List<EnvioInternoDTO>>> Lista(int dias, bool soloVivos)
        {
            if (!repositorio.ScriptAplicado())
            {
                return SinScript<List<EnvioInternoDTO>>();
            }
            int d = Math.Max(1, Math.Min(dias, 365));
            List<EnvioInternoReparto> envios = await repositorio.LeerLista(ahora().Date.AddDays(1 - d), soloVivos).ConfigureAwait(false) ?? new List<EnvioInternoReparto>();
            List<MovimientoBultoReparto> historia = await Historia(envios).ConfigureAwait(false);
            return ResultadoReparto<List<EnvioInternoDTO>>.Bien(envios.Select(e => ReglasEnviosInternos.ADto(e, historia)).ToList());
        }

        /// <summary>Anula un envío interno vivo. 409 EN_FURGONETA si algún bulto va en una furgoneta (como los envíos, punto 1).</summary>
        public async Task<ResultadoReparto<EnvioInternoDTO>> Anular(int id, string motivo, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return SinScript<EnvioInternoDTO>();
            }
            EnvioInternoReparto envio = await repositorio.Leer(id).ConfigureAwait(false);
            if (envio == null)
            {
                return ResultadoReparto<EnvioInternoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío interno {id}.");
            }
            if (!ReglasEnviosInternos.EsVivo(envio))
            {
                return ResultadoReparto<EnvioInternoDTO>.Mal(TipoResultadoReparto.Conflicto, envio.Estado == EstadosEnvioInterno.ANULADO
                    ? $"El envío interno {id} ya está anulado."
                    : $"El envío interno {id} ya está entregado: no se puede anular.");
            }
            List<MovimientoBultoReparto> historia = await Historia(new[] { envio }).ConfigureAwait(false);
            BultosEnFurgoneta enFurgoneta = CandadoFurgonetaEnvio.EnFurgoneta(ReglasEnviosInternos.Ultimos(envio, historia));
            if (enFurgoneta != null)
            {
                return new ResultadoReparto<EnvioInternoDTO>
                {
                    Tipo = TipoResultadoReparto.Conflicto,
                    Mensaje = CandadoFurgonetaEnvio.Mensaje("el envío interno " + ReglasEnviosInternos.Documento(id), enFurgoneta, ahora()),
                    Codigo = CandadoFurgonetaEnvio.CODIGO_EN_FURGONETA,
                    Detalles = enFurgoneta
                };
            }
            string anterior = JsonConvert.SerializeObject(ReglasEnviosInternos.ADto(envio, historia));
            string limpio = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            if (limpio != null && limpio.Length > ReglasEnviosInternos.MAXIMO_DESCRIPCION)
            {
                limpio = limpio.Substring(0, ReglasEnviosInternos.MAXIMO_DESCRIPCION);
            }
            if (!await repositorio.Anular(id, limpio, usuario, anterior).ConfigureAwait(false))
            {
                return ResultadoReparto<EnvioInternoDTO>.Mal(TipoResultadoReparto.Conflicto, $"El envío interno {id} ha cambiado mientras: actualice la lista.");
            }
            envio.Estado = EstadosEnvioInterno.ANULADO;
            envio.MotivoAnulacion = limpio;
            return ResultadoReparto<EnvioInternoDTO>.Bien(ReglasEnviosInternos.ADto(envio, historia));
        }
    }
}
