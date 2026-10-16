using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>Un cupón por emitir.</summary>
    public class CuponNuevo
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal Importe { get; set; }
        public string Origen { get; set; }
        public int? EventoInscripcionId { get; set; }
        public int? EventoId { get; set; }
        public string EmpresaFacturaOrigen { get; set; }
        public string FacturaOrigen { get; set; }
        public string Motivo { get; set; }
        public DateTime ValidoDesde { get; set; }
        public DateTime Caducidad { get; set; }
        public string Estado { get; set; }
        public string Usuario { get; set; }
    }

    public interface IRepositorioCupones
    {
        Task<CuponLeido> LeerPorCodigo(string codigo);
        Task<CuponLeido> LeerPorId(int id);
        /// <summary>El que está Usado en este pedido (null si ninguno).</summary>
        Task<CuponLeido> LeerDelPedido(int pedido);
        Task<CuponLeido> LeerDeInscripcion(int inscripcionId);
        Task<List<CuponLeido>> LeerDelCliente(string empresa, string cliente);
        Task<Dictionary<string, ProductoParaCupon>> LeerProductos(string empresa, IEnumerable<string> productos);
        /// <summary>% IVA y % RE (ParametrosIVA) del IVA del cliente con el IVA del producto. Null si no está la combinación.</summary>
        Task<PorcentajesIvaCupon> LeerPorcentajesIva(string empresa, string ivaCliente, string ivaProducto);
        /// <summary>
        /// Devolución del curso: Anulado si no está usado (Emitido, Activo, Caducado, o Usado en un pedido que ya no tiene la
        /// línea). Devuelve las filas tocadas (0 si está usado de verdad o ya anulado).
        /// </summary>
        Task<int> Anular(int id, string motivo, string usuario);
        /// <summary>Inserta con un código nuevo (si choca, lo vuelve a intentar con otro). Con inscripción, una sola vez:
        /// si ya tiene cupón, devuelve ese.</summary>
        Task<CuponLeido> Insertar(CuponNuevo cupon, Func<string> nuevoCodigo);
        /// <summary>UPDATE condicionado: false si el cupón ya está en otro pedido vivo (lo ha cogido otro a la vez).</summary>
        Task<bool> Reservar(int id, string empresaPedido, int pedido, string canal, string usuario);
        /// <summary>Suelta los cupones usados en el pedido salvo <paramref name="exceptoId"/> (0 = todos), si su línea no está ya servida.</summary>
        Task<int> LiberarDelPedido(int pedido, int exceptoId, string usuario);
        /// <summary>No asistió: el cupón de la inscripción pasa a Anulado (si no está usado).</summary>
        Task<int> AnularDeInscripcion(int inscripcionId, string motivo, string usuario);
        /// <summary>Se corrige la lista (sí vino): el cupón anulado por no asistir vuelve.</summary>
        Task<int> ReactivarDeInscripcion(int inscripcionId, string motivoAnulacion, string usuario);
        /// <summary>Job diario: Emitido con el día del evento llegado → Activo; Activo con la caducidad pasada → Caducado.</summary>
        Task<int> ActualizarEstados();
        /// <summary>Al facturar el pedido donde se usó (best-effort desde ServicioFacturas).</summary>
        Task<int> ApuntarFacturaDeUso(string empresaPedido, int pedido, string empresaFactura, string factura);
        Task MarcarAviso(int id, string correo);
    }

    /// <summary>
    /// NestoAPI#613: dbo.Cupones por SQL directo (sin EDMX). Sin la tabla (script sin lanzar) las lecturas devuelven nada y
    /// las escrituras fallan: el cupón solo se emite detrás del interruptor del corte 2, y nadie puede tener una línea de
    /// cupón sin el producto, que crea el mismo script.
    /// </summary>
    public class RepositorioCupones : IRepositorioCupones
    {
        private const string COLUMNAS = @"c.Id, RTRIM(c.Codigo) AS Codigo, RTRIM(c.Empresa) AS Empresa, RTRIM(c.Cliente) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       c.Importe, c.Origen, c.EventoInscripcionId, c.EventoId, e.Titulo AS Evento, RTRIM(c.EmpresaFacturaOrigen) AS EmpresaFacturaOrigen,
       RTRIM(c.FacturaOrigen) AS FacturaOrigen, c.Motivo, c.FechaEmision, CAST(c.ValidoDesde AS datetime) AS ValidoDesde,
       CAST(c.Caducidad AS datetime) AS Caducidad, c.Estado, RTRIM(c.EmpresaUso) AS EmpresaUso, c.UsadoEnPedido,
       RTRIM(c.EmpresaFacturaUso) AS EmpresaFacturaUso, RTRIM(c.FacturaUso) AS FacturaUso, c.FechaUso, c.CanalUso, c.MotivoAnulacion, c.Usuario,
       CAST(CASE WHEN c.UsadoEnPedido IS NOT NULL AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.UsadoEnPedido AND l.Producto = @prod)
                 THEN 1 ELSE 0 END AS bit) AS PedidoUsoTieneLaLinea,
       CAST(CASE WHEN c.UsadoEnPedido IS NOT NULL AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.UsadoEnPedido AND l.Producto = @prod AND l.Estado >= 2)
                 THEN 1 ELSE 0 END AS bit) AS LineaUsoServida
FROM dbo.Cupones c LEFT JOIN dbo.Eventos e ON e.Id = c.EventoId";

        private const string VACIO = @"SELECT CAST(0 AS int) AS Id, CAST(NULL AS varchar(20)) AS Codigo, CAST(NULL AS varchar(3)) AS Empresa,
       CAST(NULL AS varchar(10)) AS Cliente, CAST(NULL AS varchar(3)) AS Contacto, CAST(0 AS decimal(10,2)) AS Importe, CAST(NULL AS varchar(10)) AS Origen,
       CAST(NULL AS int) AS EventoInscripcionId, CAST(NULL AS int) AS EventoId, CAST(NULL AS nvarchar(100)) AS Evento,
       CAST(NULL AS varchar(3)) AS EmpresaFacturaOrigen, CAST(NULL AS varchar(10)) AS FacturaOrigen, CAST(NULL AS nvarchar(500)) AS Motivo,
       GETDATE() AS FechaEmision, GETDATE() AS ValidoDesde, GETDATE() AS Caducidad, CAST(NULL AS varchar(10)) AS Estado,
       CAST(NULL AS varchar(3)) AS EmpresaUso, CAST(NULL AS int) AS UsadoEnPedido, CAST(NULL AS varchar(3)) AS EmpresaFacturaUso,
       CAST(NULL AS varchar(10)) AS FacturaUso, CAST(NULL AS datetime) AS FechaUso, CAST(NULL AS varchar(20)) AS CanalUso,
       CAST(NULL AS nvarchar(200)) AS MotivoAnulacion, CAST(NULL AS varchar(50)) AS Usuario, CAST(0 AS bit) AS PedidoUsoTieneLaLinea,
       CAST(0 AS bit) AS LineaUsoServida WHERE 1 = 0";

        /// <summary>Consulta protegida: si no existe la tabla, nada. <paramref name="donde"/> usa @p0/@p1 (y @prod, el producto).</summary>
        // Sin la tabla, la rama del ELSE no se ejecuta (resolución diferida de nombres): no hace falta SQL dinámico.
        internal static string Consulta(string donde) => @"
IF OBJECT_ID('dbo.Cupones') IS NULL
    " + VACIO + @"
ELSE
    SELECT " + COLUMNAS + " WHERE " + donde;

        internal const string SQL_INSERTAR = @"
SET NOCOUNT ON;
INSERT INTO dbo.Cupones (Codigo, Empresa, Cliente, Contacto, Importe, Origen, EventoInscripcionId, EventoId, EmpresaFacturaOrigen, FacturaOrigen,
       Motivo, FechaEmision, ValidoDesde, Caducidad, Estado, Usuario, FechaModificacion)
SELECT @Codigo, @Empresa, @Cliente, @Contacto, @Importe, @Origen, @Inscripcion, @Evento, @EmpresaFactura, @Factura,
       @Motivo, GETDATE(), @ValidoDesde, @Caducidad, @Estado, @Usuario, GETDATE()
WHERE @Inscripcion IS NULL
   OR NOT EXISTS (SELECT 1 FROM dbo.Cupones WITH (UPDLOCK, HOLDLOCK) WHERE EventoInscripcionId = @Inscripcion);
SELECT CAST(ISNULL(SCOPE_IDENTITY(), 0) AS int);";

        // El candado del canje: solo pasa si el cupón está libre (Activo, o Emitido con el día ya llegado: el job puede no
        // haber pasado), ya es de este pedido, o su pedido ya no tiene la línea. Con dos pedidos a la vez, el segundo
        // espera al bloqueo de fila del primero (va en la transacción del guardado) y, cuando el primero confirma, 0 filas.
        internal const string SQL_RESERVAR = @"
UPDATE c
SET Estado = 'Usado', EmpresaUso = @p1, UsadoEnPedido = @p2,
    FechaUso = CASE WHEN c.Estado = 'Usado' AND c.UsadoEnPedido = @p2 THEN c.FechaUso ELSE GETDATE() END,
    CanalUso = CASE WHEN c.Estado = 'Usado' AND c.UsadoEnPedido = @p2 THEN c.CanalUso ELSE @p3 END,
    Usuario = @p4, FechaModificacion = GETDATE()
FROM dbo.Cupones c WITH (UPDLOCK, ROWLOCK)
WHERE c.Id = @p0
      AND ((c.Estado = 'Activo' AND c.Caducidad >= CAST(GETDATE() AS date))
           OR (c.Estado = 'Emitido' AND c.ValidoDesde <= CAST(GETDATE() AS date) AND c.Caducidad >= CAST(GETDATE() AS date))
           OR (c.Estado = 'Usado' AND (c.UsadoEnPedido = @p2
               OR NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.UsadoEnPedido AND l.Producto = @p5))))";

        internal const string SQL_LIBERAR = @"
IF OBJECT_ID('dbo.Cupones') IS NOT NULL
    UPDATE c
    SET Estado = CASE WHEN c.Caducidad < CAST(GETDATE() AS date) THEN 'Caducado'
                      WHEN c.ValidoDesde > CAST(GETDATE() AS date) THEN 'Emitido' ELSE 'Activo' END,
        EmpresaUso = NULL, UsadoEnPedido = NULL, FechaUso = NULL, CanalUso = NULL, Usuario = @p2, FechaModificacion = GETDATE()
    FROM dbo.Cupones c
    WHERE c.UsadoEnPedido = @p0 AND c.Estado = 'Usado' AND c.Id <> @p1
          AND NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = @p0 AND l.Producto = @p3 AND l.Estado >= 2)";

        internal const string SQL_ANULAR_INSCRIPCION = @"
IF OBJECT_ID('dbo.Cupones') IS NOT NULL
    UPDATE dbo.Cupones SET Estado = 'Anulado', MotivoAnulacion = @p1, Usuario = @p2, FechaModificacion = GETDATE()
    WHERE EventoInscripcionId = @p0 AND Estado IN ('Emitido', 'Activo')";

        internal const string SQL_ANULAR = @"
IF OBJECT_ID('dbo.Cupones') IS NOT NULL
    UPDATE c
    SET Estado = 'Anulado', MotivoAnulacion = @p1, EmpresaUso = NULL, UsadoEnPedido = NULL, FechaUso = NULL, CanalUso = NULL,
        Usuario = @p2, FechaModificacion = GETDATE()
    FROM dbo.Cupones c WITH (UPDLOCK, ROWLOCK)
    WHERE c.Id = @p0
          AND (c.Estado IN ('Emitido', 'Activo', 'Caducado')
               OR (c.Estado = 'Usado' AND NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.UsadoEnPedido AND l.Producto = @p3)))";

        internal const string SQL_REACTIVAR_INSCRIPCION = @"
IF OBJECT_ID('dbo.Cupones') IS NOT NULL
    UPDATE dbo.Cupones
    SET Estado = CASE WHEN Caducidad < CAST(GETDATE() AS date) THEN 'Caducado'
                      WHEN ValidoDesde > CAST(GETDATE() AS date) THEN 'Emitido' ELSE 'Activo' END,
        MotivoAnulacion = NULL, Usuario = @p2, FechaModificacion = GETDATE()
    WHERE EventoInscripcionId = @p0 AND Estado = 'Anulado' AND MotivoAnulacion = @p1";

        internal const string SQL_ACTUALIZAR_ESTADOS = @"
IF OBJECT_ID('dbo.Cupones') IS NULL
    SELECT 0
ELSE
BEGIN
    DECLARE @n int = 0;
    UPDATE dbo.Cupones SET Estado = 'Activo', Usuario = 'NestoAPI', FechaModificacion = GETDATE()
    WHERE Estado = 'Emitido' AND ValidoDesde <= CAST(GETDATE() AS date) AND Caducidad >= CAST(GETDATE() AS date);
    SET @n = @@ROWCOUNT;
    UPDATE dbo.Cupones SET Estado = 'Caducado', Usuario = 'NestoAPI', FechaModificacion = GETDATE()
    WHERE Estado IN ('Emitido', 'Activo') AND Caducidad < CAST(GETDATE() AS date);
    SELECT @n + @@ROWCOUNT;
END";

        internal const string SQL_FACTURA_USO = @"
IF OBJECT_ID('dbo.Cupones') IS NULL
    SELECT 0
ELSE
BEGIN
    UPDATE dbo.Cupones SET EmpresaFacturaUso = @p2, FacturaUso = @p3, FechaModificacion = GETDATE()
    WHERE UsadoEnPedido = @p1 AND Estado = 'Usado' AND FacturaUso IS NULL;
    SELECT @@ROWCOUNT;
END";

        private readonly NVEntities db;
        private readonly string productoCupon;

        public RepositorioCupones(NVEntities db, string productoCupon = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.productoCupon = productoCupon ?? ReglasCupon.ProductoCupon;
        }

        private async Task<List<CuponLeido>> Leer(string donde, object p0, object p1 = null) =>
            await db.Database.SqlQuery<CuponLeido>(Consulta(donde),
                new SqlParameter("@p0", p0 ?? DBNull.Value),
                new SqlParameter("@p1", p1 ?? DBNull.Value),
                new SqlParameter("@prod", productoCupon)).ToListAsync().ConfigureAwait(false);

        public async Task<CuponLeido> LeerPorCodigo(string codigo) =>
            string.IsNullOrWhiteSpace(codigo) ? null
                : (await Leer("c.Codigo = @p0", codigo.Trim().ToUpperInvariant()).ConfigureAwait(false)).FirstOrDefault();

        public async Task<CuponLeido> LeerPorId(int id) =>
            (await Leer("c.Id = @p0", id).ConfigureAwait(false)).FirstOrDefault();

        public async Task<CuponLeido> LeerDelPedido(int pedido) =>
            (await Leer("c.UsadoEnPedido = @p0 AND c.Estado = 'Usado'", pedido).ConfigureAwait(false))
                .OrderByDescending(c => c.FechaUso).FirstOrDefault();

        public async Task<CuponLeido> LeerDeInscripcion(int inscripcionId) =>
            (await Leer("c.EventoInscripcionId = @p0", inscripcionId).ConfigureAwait(false)).FirstOrDefault();

        public async Task<List<CuponLeido>> LeerDelCliente(string empresa, string cliente) =>
            await Leer("c.Empresa = @p0 AND c.Cliente = @p1",
                empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente?.Trim() ?? string.Empty).ConfigureAwait(false);

        public async Task<Dictionary<string, ProductoParaCupon>> LeerProductos(string empresa, IEnumerable<string> productos)
        {
            var resultado = new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase);
            List<string> codigos = (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            if (!codigos.Any())
            {
                return resultado;
            }
            var filas = await db.Productos.Where(p => p.Empresa == empresa && codigos.Contains(p.Número))
                .Select(p => new { p.Número, p.Grupo, p.Ficticio, p.IVA_Repercutido }).ToListAsync().ConfigureAwait(false);
            foreach (var p in filas.Where(p => p.Número != null))
            {
                resultado[p.Número.Trim()] = new ProductoParaCupon { Numero = p.Número.Trim(), Grupo = p.Grupo?.Trim(), Ficticio = p.Ficticio, IvaRepercutido = p.IVA_Repercutido?.Trim() };
            }
            return resultado;
        }

        public async Task<PorcentajesIvaCupon> LeerPorcentajesIva(string empresa, string ivaCliente, string ivaProducto)
        {
            string e = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = ivaCliente?.Trim();
            string producto = ivaProducto?.Trim();
            if (string.IsNullOrEmpty(cliente) || string.IsNullOrEmpty(producto))
            {
                return null;
            }
            var fila = await db.ParametrosIVA.Where(p => p.Empresa == e && p.IVA_Cliente_Prov == cliente && p.IVA_Producto == producto)
                .Select(p => new { p.C__IVA, p.C__RE }).FirstOrDefaultAsync().ConfigureAwait(false);
            return fila == null ? null : new PorcentajesIvaCupon { Iva = fila.C__IVA ?? 0, RecargoEquivalencia = fila.C__RE ?? 0 };
        }

        public async Task<int> Anular(int id, string motivo, string usuario) =>
            await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR,
                new SqlParameter("@p0", id), new SqlParameter("@p1", (object)Recortar(motivo, 200) ?? DBNull.Value),
                new SqlParameter("@p2", Usuario(usuario)), new SqlParameter("@p3", productoCupon)).ConfigureAwait(false);

        public async Task<CuponLeido> Insertar(CuponNuevo cupon, Func<string> nuevoCodigo)
        {
            for (int intento = 0; intento < 5; intento++)
            {
                string codigo = nuevoCodigo();
                try
                {
                    int id = (await db.Database.SqlQuery<int>(SQL_INSERTAR,
                        new SqlParameter("@Codigo", codigo),
                        new SqlParameter("@Empresa", cupon.Empresa),
                        new SqlParameter("@Cliente", cupon.Cliente),
                        new SqlParameter("@Contacto", (object)cupon.Contacto ?? DBNull.Value),
                        new SqlParameter("@Importe", cupon.Importe),
                        new SqlParameter("@Origen", cupon.Origen),
                        new SqlParameter("@Inscripcion", (object)cupon.EventoInscripcionId ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.Int },
                        new SqlParameter("@Evento", (object)cupon.EventoId ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.Int },
                        new SqlParameter("@EmpresaFactura", (object)cupon.EmpresaFacturaOrigen ?? DBNull.Value),
                        new SqlParameter("@Factura", (object)cupon.FacturaOrigen ?? DBNull.Value),
                        new SqlParameter("@Motivo", (object)cupon.Motivo ?? DBNull.Value),
                        new SqlParameter("@ValidoDesde", cupon.ValidoDesde.Date),
                        new SqlParameter("@Caducidad", cupon.Caducidad.Date),
                        new SqlParameter("@Estado", cupon.Estado),
                        new SqlParameter("@Usuario", Usuario(cupon.Usuario))).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                    if (id > 0)
                    {
                        return await LeerPorId(id).ConfigureAwait(false);
                    }
                    // Ya tenía cupón esa inscripción: el de antes
                    return cupon.EventoInscripcionId.HasValue ? await LeerDeInscripcion(cupon.EventoInscripcionId.Value).ConfigureAwait(false) : null;
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    // Código repetido (1 entre un millón al mes): otro
                }
            }
            throw new InvalidOperationException("No se ha podido generar un código de cupón libre.");
        }

        public async Task<bool> Reservar(int id, string empresaPedido, int pedido, string canal, string usuario) =>
            await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR,
                new SqlParameter("@p0", id),
                new SqlParameter("@p1", empresaPedido?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p2", pedido),
                new SqlParameter("@p3", (object)Recortar(canal, 20) ?? DBNull.Value),
                new SqlParameter("@p4", Usuario(usuario)),
                new SqlParameter("@p5", productoCupon)).ConfigureAwait(false) > 0;

        public async Task<int> LiberarDelPedido(int pedido, int exceptoId, string usuario) =>
            await db.Database.ExecuteSqlCommandAsync(SQL_LIBERAR,
                new SqlParameter("@p0", pedido), new SqlParameter("@p1", exceptoId), new SqlParameter("@p2", Usuario(usuario)),
                new SqlParameter("@p3", productoCupon)).ConfigureAwait(false);

        public async Task<int> AnularDeInscripcion(int inscripcionId, string motivo, string usuario) =>
            await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR_INSCRIPCION,
                new SqlParameter("@p0", inscripcionId), new SqlParameter("@p1", motivo), new SqlParameter("@p2", Usuario(usuario))).ConfigureAwait(false);

        public async Task<int> ReactivarDeInscripcion(int inscripcionId, string motivoAnulacion, string usuario) =>
            await db.Database.ExecuteSqlCommandAsync(SQL_REACTIVAR_INSCRIPCION,
                new SqlParameter("@p0", inscripcionId), new SqlParameter("@p1", motivoAnulacion), new SqlParameter("@p2", Usuario(usuario))).ConfigureAwait(false);

        public async Task<int> ActualizarEstados() =>
            (await db.Database.SqlQuery<int>(SQL_ACTUALIZAR_ESTADOS).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<int> ApuntarFacturaDeUso(string empresaPedido, int pedido, string empresaFactura, string factura) =>
            (await db.Database.SqlQuery<int>(SQL_FACTURA_USO,
                new SqlParameter("@p0", empresaPedido ?? string.Empty), new SqlParameter("@p1", pedido),
                new SqlParameter("@p2", (object)empresaFactura ?? DBNull.Value), new SqlParameter("@p3", (object)factura ?? DBNull.Value))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task MarcarAviso(int id, string correo) =>
            _ = await db.Database.ExecuteSqlCommandAsync(
                "UPDATE dbo.Cupones SET FechaAvisoCorreo = GETDATE(), CorreoAviso = @p1 WHERE Id = @p0",
                new SqlParameter("@p0", id), new SqlParameter("@p1", (object)Recortar(correo, 200) ?? DBNull.Value)).ConfigureAwait(false);

        internal static string Usuario(string usuario)
        {
            string u = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
            return u.Length > 50 ? u.Substring(0, 50) : u;
        }

        private static string Recortar(string texto, int max) =>
            string.IsNullOrEmpty(texto) ? texto : texto.Length > max ? texto.Substring(0, max) : texto;
    }
}
