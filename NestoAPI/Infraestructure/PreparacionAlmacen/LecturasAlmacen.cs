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
    /// <summary>
    /// Ariadna#43: lo que Ariadna apunta de las lecturas, aparte de los escaneos: las que no casan (con el código leído) y por
    /// qué se teclea un producto que tiene código válido («No se lee» = código que se lee mal, para el informe semanal de
    /// NestoAPI#674).
    /// </summary>
    public interface IServicioLecturasAlmacen
    {
        Task<ResultadoLecturasNoCasadasDTO> RegistrarNoCasadas(string empresa, IList<LecturaNoCasadaDTO> lecturas, string usuario);
        Task<EstadoMotivoTecleoDTO> LeerMotivo(string empresa, string producto);
        /// <summary>ArgumentException si el motivo no vale o el producto no existe.</summary>
        Task<EstadoMotivoTecleoDTO> GuardarMotivo(string empresa, MotivoTecleoDTO motivo, string usuario);
    }

    /// <summary>Una fila de ProductosMotivoTecleo.</summary>
    public class MotivoTecleoGuardado
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Motivo { get; set; }
        public string Codigo { get; set; }
        public int Veces { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    public interface IRepositorioLecturasAlmacen
    {
        Task<int> InsertarNoCasadas(string empresa, IList<LecturaNoCasadaDTO> lecturas, string usuario);
        Task<MotivoTecleoGuardado> LeerMotivo(string empresa, string producto);
        Task<bool> ExisteProducto(string empresa, string producto);
        /// <summary>Si ya había fila, cambia el motivo (el último manda) y suma una vez.</summary>
        Task GuardarMotivo(string empresa, string producto, string motivo, string codigo, string usuario, string dispositivo);
    }

    public class ServicioLecturasAlmacen : IServicioLecturasAlmacen
    {
        /// <summary>LecturasNoCasadasAlmacen.Codigo es varchar(50).</summary>
        public const int LONGITUD_CODIGO = 50;
        /// <summary>Como mucho, en un envío.</summary>
        public const int MAXIMO_POR_ENVIO = 100;

        private readonly IRepositorioLecturasAlmacen repositorio;

        public ServicioLecturasAlmacen(IRepositorioLecturasAlmacen repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ResultadoLecturasNoCasadasDTO> RegistrarNoCasadas(string empresa, IList<LecturaNoCasadaDTO> lecturas, string usuario)
        {
            List<LecturaNoCasadaDTO> todas = (lecturas ?? new List<LecturaNoCasadaDTO>()).Where(l => l != null).ToList();
            if (todas.Count > MAXIMO_POR_ENVIO)
            {
                throw new ArgumentException($"Como mucho {MAXIMO_POR_ENVIO} lecturas en cada envío.");
            }
            List<LecturaNoCasadaDTO> validas = todas.Where(EsValida).Select(Limpiar).ToList();
            int guardadas = validas.Count == 0 ? 0 : await repositorio.InsertarNoCasadas(empresa, validas, usuario).ConfigureAwait(false);
            return new ResultadoLecturasNoCasadasDTO { Guardadas = guardadas, Descartadas = todas.Count - validas.Count };
        }

        internal static bool EsValida(LecturaNoCasadaDTO lectura)
        {
            string codigo = lectura?.Codigo?.Trim();
            return !string.IsNullOrEmpty(codigo) && codigo.Length <= LONGITUD_CODIGO;
        }

        private static LecturaNoCasadaDTO Limpiar(LecturaNoCasadaDTO l) => new LecturaNoCasadaDTO
        {
            Codigo = l.Codigo.Trim(),
            ProductoEsperado = Corto(l.ProductoEsperado, 15),
            ProductoLeido = Corto(l.ProductoLeido, 15),
            Pantalla = Corto(l.Pantalla, 20),
            TipoOrigen = Corto(l.TipoOrigen?.ToUpperInvariant(), 4),
            NumeroOrigen = l.NumeroOrigen,
            ConEscaner = l.ConEscaner,
            Dispositivo = Corto(l.Dispositivo, 50),
            // Una PDA con la hora mal (o sin fecha) no puede romper el insert: la del servidor
            FechaLectura = l.FechaLectura.Year < 2026 || l.FechaLectura > DateTime.Now.AddDays(1) ? DateTime.Now : l.FechaLectura
        };

        private static string Corto(string texto, int longitud)
        {
            string limpio = texto?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }

        public async Task<EstadoMotivoTecleoDTO> LeerMotivo(string empresa, string producto)
        {
            string numero = producto?.Trim();
            if (string.IsNullOrEmpty(numero))
            {
                throw new ArgumentException("Falta el producto.");
            }
            MotivoTecleoGuardado guardado = await repositorio.LeerMotivo(empresa, numero).ConfigureAwait(false);
            return Estado(numero, guardado);
        }

        public async Task<EstadoMotivoTecleoDTO> GuardarMotivo(string empresa, MotivoTecleoDTO motivo, string usuario)
        {
            string producto = motivo?.Producto?.Trim();
            if (string.IsNullOrEmpty(producto) || producto.Length > 15)
            {
                throw new ArgumentException("Falta el producto.");
            }
            string clave = motivo.Motivo?.Trim().ToUpperInvariant();
            if (!MotivosTecleo.TODOS.Contains(clave))
            {
                throw new ArgumentException("El motivo tiene que ser NOSELEE, SINCODIGO u OTRO.");
            }
            if (!await repositorio.ExisteProducto(empresa, producto).ConfigureAwait(false))
            {
                throw new ArgumentException($"El producto {producto} no existe.");
            }
            string codigo = Corto(motivo.Codigo, 20);
            await repositorio.GuardarMotivo(empresa, producto, clave, codigo, UsuarioAuditoriaHelper.ParaAuditoria(usuario),
                Corto(motivo.Dispositivo, 50)).ConfigureAwait(false);
            EstadoMotivoTecleoDTO estado = Estado(producto, await repositorio.LeerMotivo(empresa, producto).ConfigureAwait(false));
            estado.Mensaje = clave == MotivosTecleo.NO_SE_LEE
                ? "Apuntado: el código de este producto se lee mal. Compras lo verá en el informe de los lunes."
                : "Apuntado.";
            return estado;
        }

        private static EstadoMotivoTecleoDTO Estado(string producto, MotivoTecleoGuardado guardado) => guardado == null
            ? new EstadoMotivoTecleoDTO { Producto = producto, Preguntado = false }
            : new EstadoMotivoTecleoDTO
            {
                Producto = producto,
                Preguntado = true,
                Motivo = guardado.Motivo?.Trim(),
                Codigo = guardado.Codigo?.Trim(),
                Fecha = guardado.FechaModificacion
            };
    }

    /// <summary>
    /// Ariadna#43: las tablas LecturasNoCasadasAlmacen y ProductosMotivoTecleo (script Ariadna43_LecturasYMotivosTecleo.sql).
    /// No están en el EDMX: SQL a mano, como los avisos de ficha.
    /// </summary>
    public class RepositorioLecturasAlmacenSql : IRepositorioLecturasAlmacen, IDisposable
    {
        internal const string SQL_INSERTAR_NO_CASADA = @"
INSERT INTO LecturasNoCasadasAlmacen (Empresa, Codigo, ProductoEsperado, ProductoLeido, Pantalla, TipoOrigen, NumeroOrigen,
    ConEscaner, Usuario, Dispositivo, FechaLectura)
VALUES (@empresa, @codigo, @esperado, @leido, @pantalla, @tipo, @numero, @conEscaner, @usuario, @dispositivo, @fecha)";

        internal const string SQL_LEER_MOTIVO = @"
SELECT RTRIM(m.Producto) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(m.Motivo) AS Motivo, RTRIM(m.Codigo) AS Codigo, m.Veces,
       m.Usuario, m.FechaModificacion
FROM ProductosMotivoTecleo m
     LEFT JOIN Productos p ON p.Empresa = m.Empresa AND p.[Número] = m.Producto
WHERE m.Empresa = @empresa AND m.Producto = @producto";

        internal const string SQL_EXISTE_PRODUCTO = @"
SELECT COUNT(*) FROM Productos WHERE Empresa = @empresa AND [Número] = @producto";

        // El último que contesta manda (puede pasar de «Otro» a «No se lee»); Veces cuenta cuántas veces se ha contestado
        internal const string SQL_GUARDAR_MOTIVO = @"
MERGE ProductosMotivoTecleo WITH (HOLDLOCK) AS t
USING (SELECT @empresa AS Empresa, @producto AS Producto) AS s ON t.Empresa = s.Empresa AND t.Producto = s.Producto
WHEN MATCHED THEN
    UPDATE SET Motivo = @motivo, Codigo = ISNULL(@codigo, t.Codigo), Veces = t.Veces + 1, Usuario = @usuario,
               Dispositivo = @dispositivo, FechaModificacion = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (Empresa, Producto, Motivo, Codigo, Veces, Usuario, Dispositivo)
    VALUES (@empresa, @producto, @motivo, @codigo, 1, @usuario, @dispositivo);";

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioLecturasAlmacenSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioLecturasAlmacenSql ConContextoPropio() => new RepositorioLecturasAlmacenSql(new NVEntities()) { contextoPropio = true };

        public async Task<int> InsertarNoCasadas(string empresa, IList<LecturaNoCasadaDTO> lecturas, string usuario)
        {
            int guardadas = 0;
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            foreach (LecturaNoCasadaDTO l in lecturas)
            {
                guardadas += await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_NO_CASADA,
                    Char("@empresa", empresa, 3), VarChar("@codigo", l.Codigo, 50), Char("@esperado", l.ProductoEsperado, 15),
                    Char("@leido", l.ProductoLeido, 15), VarChar("@pantalla", l.Pantalla, 20), Char("@tipo", l.TipoOrigen, 4),
                    new SqlParameter("@numero", System.Data.SqlDbType.Int) { Value = (object)l.NumeroOrigen ?? DBNull.Value },
                    new SqlParameter("@conEscaner", System.Data.SqlDbType.Bit) { Value = l.ConEscaner.HasValue ? (object)l.ConEscaner.Value : DBNull.Value },
                    VarChar("@usuario", quien, 50), VarChar("@dispositivo", l.Dispositivo, 50),
                    new SqlParameter("@fecha", System.Data.SqlDbType.DateTime) { Value = l.FechaLectura }).ConfigureAwait(false);
            }
            return guardadas;
        }

        public async Task<MotivoTecleoGuardado> LeerMotivo(string empresa, string producto)
            => (await db.Database.SqlQuery<MotivoTecleoGuardado>(SQL_LEER_MOTIVO, Char("@empresa", empresa, 3), Char("@producto", producto, 15))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<bool> ExisteProducto(string empresa, string producto)
            => await db.Database.SqlQuery<int>(SQL_EXISTE_PRODUCTO, Char("@empresa", empresa, 3), Char("@producto", producto, 15))
                .FirstOrDefaultAsync().ConfigureAwait(false) > 0;

        public Task GuardarMotivo(string empresa, string producto, string motivo, string codigo, string usuario, string dispositivo)
            => db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR_MOTIVO, Char("@empresa", empresa, 3), Char("@producto", producto, 15),
                VarChar("@motivo", motivo, 10), VarChar("@codigo", codigo, 20), VarChar("@usuario", usuario, 50), VarChar("@dispositivo", dispositivo, 50));

        // Tipados como las columnas: con nvarchar SQL Server convertiría cada fila y no usaría el índice
        private static SqlParameter Char(string nombre, string valor, int longitud)
            => new SqlParameter(nombre, System.Data.SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter VarChar(string nombre, string valor, int longitud)
            => new SqlParameter(nombre, System.Data.SqlDbType.VarChar, longitud) { Value = (object)valor ?? DBNull.Value };

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }
}
