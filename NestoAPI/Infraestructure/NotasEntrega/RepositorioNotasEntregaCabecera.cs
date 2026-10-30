using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#757: una fila de dbo.NotasEntregaCabecera (Scripts/Issue757_NotasEntregaCabecera.sql).</summary>
    public class FilaNotaEntregaCabecera
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Tipo { get; set; }
        public short Bultos { get; set; }
        public string FacturaOriginal { get; set; }
        public int? PedidoAbono { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#757: lo que hace falta saber de la cabecera del pedido para su nota.</summary>
    public class PedidoNotaEntrega
    {
        public string Cliente { get; set; }
        public bool NotaEntrega { get; set; }
    }

    /// <summary>NestoAPI#757: el último envío del pedido.</summary>
    public class EnvioNotaEntrega
    {
        public int Numero { get; set; }
        public short Estado { get; set; }
    }

    public interface IRepositorioNotasEntregaCabecera
    {
        /// <summary>¿Existe la tabla (script lanzado)?</summary>
        bool ScriptAplicado();
        /// <summary>La fila de la nota, o null (sin fila o sin script).</summary>
        Task<FilaNotaEntregaCabecera> Leer(string empresa, int pedido);
        /// <summary>Inserta o actualiza la fila (Tipo, Bultos, FacturaOriginal, Usuario) y, si viene, la Modificación, juntas.</summary>
        Task Guardar(FilaNotaEntregaCabecera fila, Modificacion modificacion);
        /// <summary>La cabecera del pedido, o null si no existe.</summary>
        Task<PedidoNotaEntrega> LeerPedido(string empresa, int pedido);
        /// <summary>NotasEntrega.Numero de las líneas del pedido, o null.</summary>
        Task<int?> LeerNumeroNota(string empresa, int pedido);
        /// <summary>¿Alguna línea en −2 (nota procesada)?</summary>
        Task<bool> TieneLineasProcesadas(string empresa, int pedido);
        /// <summary>El último envío (EnviosAgencia) del pedido, o null.</summary>
        Task<EnvioNotaEntrega> LeerUltimoEnvio(string empresa, int pedido);
        /// <summary>El cliente de la factura (recortado), o null si la factura no existe en la empresa.</summary>
        Task<string> LeerClienteFactura(string empresa, string factura);
    }

    /// <summary>
    /// NestoAPI#757: la tabla NotasEntregaCabecera va por SQL, fuera del EDMX (como EnviosAgenciaPruebasEntrega, #739): si
    /// estuviera en el EDMX y el script no se hubiera lanzado, tumbaría el contexto. El pedido, sus líneas, el envío y la
    /// factura se leen por EF como siempre.
    /// </summary>
    public class RepositorioNotasEntregaCabecera : IRepositorioNotasEntregaCabecera
    {
        public const string TABLA = "NotasEntregaCabecera";

        internal const string SQL_LEER = "SELECT RTRIM(Empresa) AS Empresa, Pedido, RTRIM(Tipo) AS Tipo, Bultos, RTRIM(FacturaOriginal) AS FacturaOriginal, " +
            "PedidoAbono, RTRIM(Usuario) AS Usuario, FechaModificacion FROM dbo." + TABLA + " WITH (NOLOCK) WHERE Empresa = @p0 AND Pedido = @p1";

        internal const string SQL_GUARDAR = "UPDATE dbo." + TABLA + " SET Tipo = @Tipo, Bultos = @Bultos, FacturaOriginal = @Factura, Usuario = @Usuario, " +
            "FechaModificacion = GETDATE() WHERE Empresa = @Empresa AND Pedido = @Pedido; " +
            "IF @@ROWCOUNT = 0 INSERT INTO dbo." + TABLA + " (Empresa, Pedido, Tipo, Bultos, FacturaOriginal, Usuario, FechaModificacion) " +
            "VALUES (@Empresa, @Pedido, @Tipo, @Bultos, @Factura, @Usuario, GETDATE());";

        private readonly NVEntities db;

        public RepositorioNotasEntregaCabecera(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public async Task<FilaNotaEntregaCabecera> Leer(string empresa, int pedido)
        {
            if (!ScriptAplicado())
            {
                return null;
            }
            List<FilaNotaEntregaCabecera> filas = await db.Database.SqlQuery<FilaNotaEntregaCabecera>(SQL_LEER, empresa, pedido)
                .ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public async Task Guardar(FilaNotaEntregaCabecera fila, Modificacion modificacion)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                        Parametro("@Empresa", SqlDbType.Char, fila.Empresa),
                        Parametro("@Pedido", SqlDbType.Int, fila.Pedido),
                        Parametro("@Tipo", SqlDbType.VarChar, fila.Tipo),
                        Parametro("@Bultos", SqlDbType.SmallInt, fila.Bultos),
                        Parametro("@Factura", SqlDbType.VarChar, fila.FacturaOriginal),
                        Parametro("@Usuario", SqlDbType.VarChar, Recortar(fila.Usuario, 50))).ConfigureAwait(false);
                    if (modificacion != null)
                    {
                        _ = db.Modificaciones.Add(modificacion);
                        _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                    transaccion.Commit();
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        public async Task<PedidoNotaEntrega> LeerPedido(string empresa, int pedido)
        {
            List<PedidoNotaEntrega> cabeceras = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Número == pedido)
                .Select(c => new PedidoNotaEntrega { Cliente = c.Nº_Cliente, NotaEntrega = c.NotaEntrega })
                .ToListAsync().ConfigureAwait(false);
            PedidoNotaEntrega cabecera = cabeceras.FirstOrDefault();
            if (cabecera != null)
            {
                cabecera.Cliente = cabecera.Cliente?.Trim();
            }
            return cabecera;
        }

        public Task<int?> LeerNumeroNota(string empresa, int pedido)
            => (from l in db.LinPedidoVtas
                join n in db.NotasEntregas on l.Nº_Orden equals n.NºOrden
                where l.Empresa == empresa && l.Número == pedido
                select (int?)n.Numero).MaxAsync();

        public Task<bool> TieneLineasProcesadas(string empresa, int pedido)
        {
            int procesada = Constantes.EstadosLineaVenta.NOTA_ENTREGA;
            return db.LinPedidoVtas.AnyAsync(l => l.Empresa == empresa && l.Número == pedido && l.Estado == procesada);
        }

        public async Task<EnvioNotaEntrega> LeerUltimoEnvio(string empresa, int pedido)
        {
            List<EnvioNotaEntrega> envios = await db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Empresa == empresa && e.Pedido == pedido)
                .OrderByDescending(e => e.Numero)
                .Select(e => new EnvioNotaEntrega { Numero = e.Numero, Estado = e.Estado })
                .Take(1)
                .ToListAsync().ConfigureAwait(false);
            return envios.FirstOrDefault();
        }

        public async Task<string> LeerClienteFactura(string empresa, string factura)
        {
            List<string> clientes = await db.CabsFacturasVtas.AsNoTracking()
                .Where(f => f.Empresa == empresa && f.Número == factura)
                .Select(f => f.Nº_Cliente)
                .ToListAsync().ConfigureAwait(false);
            return clientes.FirstOrDefault()?.Trim();
        }

        private static SqlParameter Parametro(string nombre, SqlDbType tipo, object valor)
            => new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
