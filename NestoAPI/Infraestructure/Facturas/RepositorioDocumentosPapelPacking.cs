using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#828: la marca «impresa en el packing» (o «pendiente de imprimir») de cada documento en papel que sale al hacer la
    /// etiqueta. Vive en <c>dbo.DocumentosPapelPacking</c>, fuera del EDMX (Scripts/Issue828_DocumentosPapelPacking.sql). Sin la
    /// tabla, todo sigue como antes: no se lee nada, no se escribe nada y no falla.
    /// </summary>
    public interface IRepositorioDocumentosPapelPacking
    {
        /// <summary>Las marcas de los documentos del pedido; vacía si no hay (o no hay tabla). La Fecha es la de la última modificación.</summary>
        Task<List<DocumentoEnPapelDTO>> Leer(string empresa, int pedido);

        /// <summary>Pone (o cambia) la marca del documento. Nunca lanza.</summary>
        Task Guardar(DocumentoEnPapelDTO marca, string aplicacion, string usuario);

        /// <summary>Las marcas puestas desde <paramref name="desde"/> (para el resumen de Facturar rutas).</summary>
        Task<List<DocumentoEnPapelDTO>> LeerDesde(DateTime desde);
    }

    public class RepositorioDocumentosPapelPacking : IRepositorioDocumentosPapelPacking
    {
        internal const string TABLA = "dbo.DocumentosPapelPacking";

        private const string COLUMNAS =
            "RTRIM(Empresa) AS Empresa, Pedido, Envio, RTRIM(Tipo) AS Tipo, RTRIM(Documento) AS Documento, RTRIM(Estado) AS Estado, " +
            "Impresora, Mensaje, FechaModificacion AS Fecha";

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'" + TABLA + "', N'U') IS NOT NULL " +
            "SELECT " + COLUMNAS + " FROM " + TABLA + " WITH (NOLOCK) WHERE Empresa = @empresa AND Pedido = @pedido ORDER BY Id";

        internal const string SQL_LEER_DESDE =
            "IF OBJECT_ID(N'" + TABLA + "', N'U') IS NOT NULL " +
            "SELECT " + COLUMNAS + " FROM " + TABLA + " WITH (NOLOCK) WHERE FechaModificacion >= @desde ORDER BY Id";

        /// <summary>
        /// NestoAPI#828 (carta del cheque): la marca de la carta solo se guarda si la CHECK de Tipo ya la admite
        /// (Scripts/Issue828b_DocumentosPapelPacking_CartaCheque.sql). Sin el script, ni se intenta: la carta sigue como antes, sin
        /// marca y sin un error en ELMAH por cada carta.
        /// </summary>
        internal const string SQL_GUARDAR = @"
IF OBJECT_ID(N'dbo.DocumentosPapelPacking', N'U') IS NOT NULL
   AND (@tipo <> 'CartaCheque' OR EXISTS (SELECT 1 FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.DocumentosPapelPacking') AND name = N'CK_DocumentosPapelPacking_Tipo'
              AND definition LIKE N'%''CartaCheque''%'))
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    UPDATE dbo.DocumentosPapelPacking WITH (UPDLOCK, HOLDLOCK)
       SET Estado = @estado, Envio = ISNULL(@envio, Envio), Impresora = ISNULL(@impresora, Impresora), Mensaje = @mensaje,
           Aplicacion = @aplicacion, Usuario = @usuario, Intentos = Intentos + 1, FechaModificacion = GETDATE()
     WHERE Empresa = @empresa AND Pedido = @pedido AND Tipo = @tipo AND Documento = @documento;
    IF @@ROWCOUNT = 0
        INSERT INTO dbo.DocumentosPapelPacking (Empresa, Pedido, Envio, Tipo, Documento, Estado, Impresora, Mensaje, Aplicacion, Usuario)
        VALUES (@empresa, @pedido, @envio, @tipo, @documento, @estado, @impresora, @mensaje, @aplicacion, @usuario);
    COMMIT TRANSACTION;
END";

        private readonly Func<NVEntities> crearContexto;
        private readonly Action<Exception> registrar;

        public RepositorioDocumentosPapelPacking(Func<NVEntities> crearContexto = null, Action<Exception> registrar = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public async Task<List<DocumentoEnPapelDTO>> Leer(string empresa, int pedido)
        {
            try
            {
                using (NVEntities db = crearContexto())
                {
                    List<FilaDocumentoPapelPacking> filas = await db.Database.SqlQuery<FilaDocumentoPapelPacking>(SQL_LEER,
                        new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = (empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim() },
                        new SqlParameter("@pedido", pedido)).ToListAsync().ConfigureAwait(false);
                    return filas.Select(f => f.ADto()).ToList();
                }
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[NestoAPI#828] No se han podido leer las marcas del papel del packing del pedido {pedido}: {ex.Message}", ex));
                return new List<DocumentoEnPapelDTO>();
            }
        }

        public async Task<List<DocumentoEnPapelDTO>> LeerDesde(DateTime desde)
        {
            try
            {
                using (NVEntities db = crearContexto())
                {
                    List<FilaDocumentoPapelPacking> filas = await db.Database.SqlQuery<FilaDocumentoPapelPacking>(SQL_LEER_DESDE,
                        new SqlParameter("@desde", desde)).ToListAsync().ConfigureAwait(false);
                    return filas.Select(f => f.ADto()).ToList();
                }
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[NestoAPI#828] No se han podido leer las marcas del papel del packing desde el {desde:dd/MM/yy}: {ex.Message}", ex));
                return new List<DocumentoEnPapelDTO>();
            }
        }

        public async Task Guardar(DocumentoEnPapelDTO marca, string aplicacion, string usuario)
        {
            if (marca == null || string.IsNullOrWhiteSpace(marca.Documento) || string.IsNullOrWhiteSpace(marca.Tipo))
            {
                return;
            }
            try
            {
                using (NVEntities db = crearContexto())
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_GUARDAR,
                        new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = (marca.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim() },
                        new SqlParameter("@pedido", marca.Pedido),
                        new SqlParameter("@envio", SqlDbType.Int) { Value = (object)marca.Envio ?? DBNull.Value },
                        new SqlParameter("@tipo", SqlDbType.VarChar, 12) { Value = marca.Tipo.Trim() },
                        new SqlParameter("@documento", SqlDbType.VarChar, 20) { Value = Cortar(marca.Documento.Trim(), 20) },
                        new SqlParameter("@estado", SqlDbType.VarChar, 10) { Value = marca.Estado == EstadosDocumentoEnPapel.IMPRESO ? EstadosDocumentoEnPapel.IMPRESO : EstadosDocumentoEnPapel.PENDIENTE },
                        new SqlParameter("@impresora", SqlDbType.NVarChar, 128) { Value = (object)Cortar(marca.Impresora?.Trim(), 128) ?? DBNull.Value },
                        new SqlParameter("@mensaje", SqlDbType.NVarChar, 400) { Value = (object)Cortar(marca.Mensaje?.Trim(), 400) ?? DBNull.Value },
                        new SqlParameter("@aplicacion", SqlDbType.VarChar, 10) { Value = Cortar(aplicacion ?? AplicacionesDocumentoEnPapel.ARIADNA, 10) },
                        new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = Cortar(usuario ?? "NestoAPI", 50) }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // La marca nunca rompe ni la etiqueta ni la factura.
                Registrar(new Exception($"[NestoAPI#828] No se ha podido marcar {marca.Descripcion} del pedido {marca.Pedido} como {marca.Estado}: {ex.Message}", ex));
            }
        }

        /// <summary>Una fila de la tabla (SqlQuery necesita las mismas columnas que propiedades).</summary>
        internal class FilaDocumentoPapelPacking
        {
            public string Empresa { get; set; }
            public int Pedido { get; set; }
            public int? Envio { get; set; }
            public string Tipo { get; set; }
            public string Documento { get; set; }
            public string Estado { get; set; }
            public string Impresora { get; set; }
            public string Mensaje { get; set; }
            public DateTime Fecha { get; set; }

            public DocumentoEnPapelDTO ADto() => new DocumentoEnPapelDTO
            {
                Empresa = Empresa?.Trim(),
                Pedido = Pedido,
                Envio = Envio,
                Tipo = Tipo?.Trim(),
                Documento = Documento?.Trim(),
                Estado = Estado?.Trim(),
                Impresora = Impresora?.Trim(),
                Mensaje = Mensaje?.Trim(),
                Fecha = Fecha
            };
        }

        private static string Cortar(string texto, int largo)
            => texto == null || texto.Length <= largo ? texto : texto.Substring(0, largo);

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // Nunca rompe nada.
            }
        }
    }
}
