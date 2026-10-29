using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532: un recordatorio ya registrado (enviado o apuntado como grupo de control).</summary>
    public class EnvioPrevioReposicion
    {
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>
        /// La última compra que tenía el cliente cuando se le recordó (RecordatoriosReposicionEnvios.UltimaCompra). Si hoy su
        /// última compra es posterior, lo ha vuelto a comprar y se le puede recordar otra vez. Null = se mira la fecha del aviso.
        /// </summary>
        public DateTime? UltimaCompra { get; set; }
        /// <summary><see cref="RepositorioRecordatorioReposicionSql.GRUPO_ENVIADO"/> o <see cref="RepositorioRecordatorioReposicionSql.GRUPO_CONTROL"/>.</summary>
        public string Grupo { get; set; }
    }

    /// <summary>
    /// NestoAPI#532: quien ha pedido no recibir correos posventa (CorreosPostCompraBajas), por cliente o por correo.
    /// Con Tipo NULL la baja es de todos (<see cref="Clientes"/> y <see cref="Emails"/>, como las primeras que se metieron a
    /// mano); con Tipo, solo de ese tipo de correo (<see cref="TiposCorreoCliente"/>).
    /// </summary>
    public class BajasCorreosPostCompra
    {
        /// <summary>Bajas de todos los correos por cliente (Tipo NULL).</summary>
        public HashSet<string> Clientes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Bajas de todos los correos por dirección (Tipo NULL).</summary>
        public HashSet<string> Emails { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> clientesPorTipo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> emailsPorTipo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Añade una fila de la tabla. Tipo vacío = baja de todos.</summary>
        public void Agregar(string cliente, string email, string tipo)
        {
            bool general = string.IsNullOrWhiteSpace(tipo);
            if (!string.IsNullOrWhiteSpace(cliente))
            {
                _ = general ? Clientes.Add(cliente.Trim()) : clientesPorTipo.Add(Clave(tipo, cliente));
            }
            if (!string.IsNullOrWhiteSpace(email))
            {
                _ = general ? Emails.Add(email.Trim()) : emailsPorTipo.Add(Clave(tipo, email));
            }
        }

        /// <summary>De baja de todo (cualquier tipo).</summary>
        public bool Incluye(string cliente, string email)
            => (!string.IsNullOrWhiteSpace(cliente) && Clientes.Contains(cliente.Trim()))
                || (!string.IsNullOrWhiteSpace(email) && Emails.Contains(email.Trim()));

        /// <summary>De baja de este tipo de correo: por la baja general o por la de su tipo.</summary>
        public bool Incluye(string cliente, string email, string tipo)
            => Incluye(cliente, email) || IncluyeSoloTipo(cliente, email, tipo);

        /// <summary>Solo las bajas de ese tipo (sin contar la general).</summary>
        public bool IncluyeSoloTipo(string cliente, string email, string tipo)
            => !string.IsNullOrWhiteSpace(tipo)
                && ((!string.IsNullOrWhiteSpace(cliente) && clientesPorTipo.Contains(Clave(tipo, cliente)))
                    || (!string.IsNullOrWhiteSpace(email) && emailsPorTipo.Contains(Clave(tipo, email))));

        private static string Clave(string tipo, string valor) => tipo.Trim() + "|" + valor.Trim();
    }

    public interface IRepositorioRecordatorioReposicion
    {
        /// <summary>Si existen las tablas del script Issue532_RecordatorioReposicionActivo.sql. Sin ellas no se escribe a nadie.</summary>
        Task<bool> ExistenTablas();
        Task<BajasCorreosPostCompra> LeerBajas(string empresa);
        Task<List<EnvioPrevioReposicion>> LeerEnviosDesde(string empresa, DateTime desde);
        /// <summary>Guarda el texto de la semana y devuelve su Id.</summary>
        Task<int> GuardarTexto(string empresa, DateTime semana, TextoRecordatorioReposicion texto);
        /// <summary>
        /// Una fila por producto del correo. NestoAPI#736: con las columnas del script Issue736, también la P1 del modelo y si
        /// habría quedado fuera; sin ellas, como antes (no falla).
        /// </summary>
        Task RegistrarEnvio(string empresa, RecordatorioReposicionClienteDTO correo, string grupo, DateTime fecha, int? textoId, string version);
        /// <summary>
        /// NestoAPI#736: los envíos (Enviado y Control) con Fecha en [desde, hasta) y las líneas de pedido de ese producto que ese
        /// cliente hizo en los <paramref name="dias"/> días siguientes. Vacío si no existen las tablas.
        /// </summary>
        Task<List<FilaConversionReposicion>> LeerConversion(string empresa, DateTime desde, DateTime hasta, int dias);
    }

    /// <summary>
    /// NestoAPI#532: SQL a mano (fuera del EDMX, como las tablas de sugerencias de #670) de RecordatoriosReposicionEnvios,
    /// RecordatoriosReposicionTextos y CorreosPostCompraBajas (con Tipo desde Issue532_BajasPorTipo.sql). Son tablas pequeñas: unos cientos de filas por semana.
    /// </summary>
    public class RepositorioRecordatorioReposicionSql : IRepositorioRecordatorioReposicion
    {
        public const string GRUPO_ENVIADO = "Enviado";
        public const string GRUPO_CONTROL = "Control";

        internal const string SQL_EXISTEN_TABLAS = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.RecordatoriosReposicionEnvios', N'U') IS NULL
                   OR OBJECT_ID(N'dbo.RecordatoriosReposicionTextos', N'U') IS NULL
                   OR OBJECT_ID(N'dbo.CorreosPostCompraBajas', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_BAJAS = @"
SELECT RTRIM(Cliente) AS Cliente, LTRIM(RTRIM(Email)) AS Email, RTRIM(Tipo) AS Tipo
FROM dbo.CorreosPostCompraBajas WITH (NOLOCK) WHERE Empresa = @empresa";

        internal const string SQL_ENVIOS_DESDE = @"
SELECT RTRIM(Cliente) AS Cliente, RTRIM(Producto) AS Producto, Fecha, CAST(UltimaCompra AS datetime) AS UltimaCompra, Grupo
FROM dbo.RecordatoriosReposicionEnvios WITH (NOLOCK) WHERE Empresa = @empresa AND Fecha >= @desde";

        internal const string SQL_GUARDAR_TEXTO = @"
INSERT INTO dbo.RecordatoriosReposicionTextos (Empresa, Semana, Origen, Version, Asunto, Entrada, Cierre, MotivoFija)
OUTPUT INSERTED.Id
VALUES (@empresa, @semana, @origen, @version, @asunto, @entrada, @cierre, @motivoFija)";

        internal const string SQL_REGISTRAR_ENVIO = @"
INSERT INTO dbo.RecordatoriosReposicionEnvios (Empresa, Cliente, Email, Vendedor, Producto, NombreProducto, UltimaCompra,
    IntervaloDias, CantidadHabitual, Grupo, Fecha, TextoId, VersionTexto)
VALUES (@empresa, @cliente, @email, @vendedor, @producto, @nombreProducto, @ultimaCompra, @intervaloDias, @cantidadHabitual,
    @grupo, @fecha, @textoId, @version)";

        /// <summary>NestoAPI#736: las columnas de la puerta del modelo (Scripts/Issue736_RecordatorioReposicionModelo.sql).</summary>
        internal const string SQL_EXISTEN_COLUMNAS_MODELO = @"
SELECT CAST(CASE WHEN COL_LENGTH('dbo.RecordatoriosReposicionEnvios', 'ProbabilidadModelo') IS NULL
                   OR COL_LENGTH('dbo.RecordatoriosReposicionEnvios', 'FueraPorModelo') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_REGISTRAR_ENVIO_MODELO = @"
INSERT INTO dbo.RecordatoriosReposicionEnvios (Empresa, Cliente, Email, Vendedor, Producto, NombreProducto, UltimaCompra,
    IntervaloDias, CantidadHabitual, Grupo, Fecha, TextoId, VersionTexto, ProbabilidadModelo, FueraPorModelo)
VALUES (@empresa, @cliente, @email, @vendedor, @producto, @nombreProducto, @ultimaCompra, @intervaloDias, @cantidadHabitual,
    @grupo, @fecha, @textoId, @version, @probabilidadModelo, @fueraPorModelo)";

        /// <summary>
        /// NestoAPI#736: cada envío con sus líneas de pedido de ese producto en la ventana (pedido con fecha desde el día del
        /// envío, sin presupuestos: como #707). {0} y {1}: la P1 y FueraPorModelo, o NULL si aún no existen las columnas. El cruce
        /// con LinPedidoVta va por cliente y producto (índices que empiezan por Nº Cliente o Producto) y con NOLOCK.
        /// </summary>
        internal static readonly string SQL_CONVERSION = @"
SELECT e.Fecha, RTRIM(e.Cliente) AS Cliente, RTRIM(e.Producto) AS Producto, RTRIM(e.Grupo) AS Grupo,
       CAST(e.UltimaCompra AS datetime) AS UltimaCompra, e.IntervaloDias,
       {0} AS ProbabilidadModelo, {1} AS FueraPorModelo, ISNULL(c.Lineas, 0) AS Lineas
FROM dbo.RecordatoriosReposicionEnvios e WITH (NOLOCK)
OUTER APPLY (
    SELECT COUNT(*) AS Lineas
    FROM dbo.LinPedidoVta l WITH (NOLOCK)
    INNER JOIN dbo.CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
    WHERE l.Empresa = e.Empresa AND l.[Nº Cliente] = e.Cliente AND l.Producto = e.Producto
      AND l.TipoLinea = 1 AND l.Estado >= " + Constantes.EstadosLineaVenta.PENDIENTE + @" AND l.Cantidad > 0
      AND cab.Fecha >= CAST(CAST(e.Fecha AS date) AS datetime)
      AND cab.Fecha < DATEADD(day, @dias, CAST(CAST(e.Fecha AS date) AS datetime))
) c
WHERE e.Empresa = @empresa AND e.Fecha >= @desde AND e.Fecha < @hasta";

        internal static string SqlConversion(bool conColumnasModelo)
            => conColumnasModelo
                ? string.Format(SQL_CONVERSION, "CAST(e.ProbabilidadModelo AS decimal(9, 6))", "e.FueraPorModelo")
                : string.Format(SQL_CONVERSION, "CAST(NULL AS decimal(9, 6))", "CAST(NULL AS bit)");

        internal class FilaBaja
        {
            public string Cliente { get; set; }
            public string Email { get; set; }
            public string Tipo { get; set; }
        }

        private readonly NVEntities db;
        private bool? columnasModelo;

        public RepositorioRecordatorioReposicionSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<bool> ExistenTablas()
            => await db.Database.SqlQuery<bool>(SQL_EXISTEN_TABLAS).FirstOrDefaultAsync().ConfigureAwait(false);

        public async Task<BajasCorreosPostCompra> LeerBajas(string empresa)
        {
            List<FilaBaja> filas = await db.Database.SqlQuery<FilaBaja>(SQL_BAJAS, new SqlParameter("@empresa", empresa))
                .ToListAsync().ConfigureAwait(false);
            return ConstruirBajas(filas);
        }

        internal static BajasCorreosPostCompra ConstruirBajas(IEnumerable<FilaBaja> filas)
        {
            var bajas = new BajasCorreosPostCompra();
            foreach (FilaBaja fila in filas ?? Enumerable.Empty<FilaBaja>())
            {
                bajas.Agregar(fila.Cliente, fila.Email, fila.Tipo);
            }
            return bajas;
        }

        public async Task<List<EnvioPrevioReposicion>> LeerEnviosDesde(string empresa, DateTime desde)
            => await db.Database.SqlQuery<EnvioPrevioReposicion>(SQL_ENVIOS_DESDE,
                    new SqlParameter("@empresa", empresa), new SqlParameter("@desde", desde))
                .ToListAsync().ConfigureAwait(false);

        public async Task<int> GuardarTexto(string empresa, DateTime semana, TextoRecordatorioReposicion texto)
            => await db.Database.SqlQuery<int>(SQL_GUARDAR_TEXTO,
                    new SqlParameter("@empresa", empresa),
                    new SqlParameter("@semana", semana.Date),
                    new SqlParameter("@origen", texto.Origen ?? TextoRecordatorioReposicion.ORIGEN_FIJA),
                    new SqlParameter("@version", texto.Version ?? TextoRecordatorioReposicion.VERSION_FIJA),
                    new SqlParameter("@asunto", texto.Asunto ?? string.Empty),
                    new SqlParameter("@entrada", texto.Entrada ?? string.Empty),
                    new SqlParameter("@cierre", texto.Cierre ?? string.Empty),
                    new SqlParameter("@motivoFija", Nulo(Recortar(texto.MotivoFija, 400))))
                .FirstAsync().ConfigureAwait(false);

        /// <summary>NestoAPI#736: si están las columnas de la puerta del modelo (una vez por instancia; si falla, como si no).</summary>
        internal async Task<bool> ExistenColumnasModelo()
        {
            if (columnasModelo == null)
            {
                try
                {
                    columnasModelo = await db.Database.SqlQuery<bool>(SQL_EXISTEN_COLUMNAS_MODELO).FirstOrDefaultAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    columnasModelo = false;
                }
            }
            return columnasModelo.Value;
        }

        public async Task RegistrarEnvio(string empresa, RecordatorioReposicionClienteDTO correo, string grupo, DateTime fecha,
            int? textoId, string version)
        {
            bool conModelo = await ExistenColumnasModelo().ConfigureAwait(false);
            foreach (CandidatoReposicionDTO producto in correo.Productos)
            {
                var parametros = new List<SqlParameter>
                {
                    new SqlParameter("@empresa", empresa),
                    new SqlParameter("@cliente", correo.Cliente?.Trim()),
                    new SqlParameter("@email", Nulo(Recortar(correo.Email?.Trim(), 255))),
                    new SqlParameter("@vendedor", Nulo(correo.Vendedor?.Trim())),
                    new SqlParameter("@producto", producto.Producto?.Trim()),
                    new SqlParameter("@nombreProducto", Nulo(Recortar(producto.NombreProducto?.Trim(), 100))),
                    new SqlParameter("@ultimaCompra", producto.UltimaCompra.Date),
                    new SqlParameter("@intervaloDias", producto.IntervaloDias),
                    new SqlParameter("@cantidadHabitual", producto.CantidadHabitual),
                    new SqlParameter("@grupo", grupo),
                    new SqlParameter("@fecha", fecha),
                    new SqlParameter("@textoId", (object)textoId ?? DBNull.Value),
                    new SqlParameter("@version", version ?? TextoRecordatorioReposicion.VERSION_FIJA)
                };
                if (conModelo)
                {
                    parametros.Add(new SqlParameter("@probabilidadModelo", SqlDbType.Decimal)
                    {
                        Precision = 6,
                        Scale = 5,
                        Value = producto.ProbabilidadModelo.HasValue
                            ? (object)Math.Round((decimal)producto.ProbabilidadModelo.Value, 5, MidpointRounding.AwayFromZero)
                            : DBNull.Value
                    });
                    parametros.Add(new SqlParameter("@fueraPorModelo", SqlDbType.Bit)
                    {
                        Value = producto.FueraPorModelo.HasValue ? (object)producto.FueraPorModelo.Value : DBNull.Value
                    });
                }
                _ = await db.Database.ExecuteSqlCommandAsync(conModelo ? SQL_REGISTRAR_ENVIO_MODELO : SQL_REGISTRAR_ENVIO,
                    parametros.Cast<object>().ToArray()).ConfigureAwait(false);
            }
        }

        public async Task<List<FilaConversionReposicion>> LeerConversion(string empresa, DateTime desde, DateTime hasta, int dias)
        {
            if (!await ExistenTablas().ConfigureAwait(false))
            {
                return new List<FilaConversionReposicion>();
            }
            bool conModelo = await ExistenColumnasModelo().ConfigureAwait(false);
            return await db.Database.SqlQuery<FilaConversionReposicion>(SqlConversion(conModelo),
                    new SqlParameter("@empresa", empresa),
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                    new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta },
                    new SqlParameter("@dias", SqlDbType.Int) { Value = dias })
                .ToListAsync().ConfigureAwait(false);
        }

        private static object Nulo(string valor) => string.IsNullOrEmpty(valor) ? (object)DBNull.Value : valor;

        private static string Recortar(string valor, int maximo)
            => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
