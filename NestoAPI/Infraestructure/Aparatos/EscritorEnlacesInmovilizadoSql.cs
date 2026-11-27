using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Aparatos
{
    /// <summary>
    /// NestoAPI#913 corte 2: escribe el enlace aparato ↔ inmovilizado donde vive hoy, CabAlquileres.Inmovilizado (lo mismo que teclea
    /// Santiago en la ventana de Alquileres: el número del inmovilizado, FK a Inmovilizados por Empresa + Número), con Usuario y
    /// FechaModificación del registro y su rastro en Modificaciones, en UNA transacción por enlace. Solo si el registro sigue como se
    /// leyó (mismo producto y serie, sin inmovilizado) y el inmovilizado sigue vivo y en ningún otro registro. Con el script Issue913b,
    /// también Producto y NumeroSerie en la ficha del inmovilizado (si están vacíos).
    /// </summary>
    public class EscritorEnlacesInmovilizadoSql : IEscritorEnlacesInmovilizado
    {
        public const string TABLA_MODIFICACIONES = "CabAlquileres";

        private readonly NVEntities db;

        public EscritorEnlacesInmovilizadoSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string SQL_ENLAZAR = @"
UPDATE dbo.CabAlquileres
SET Inmovilizado = @Inmovilizado, Usuario = @Usuario, [FechaModificación] = GETDATE()
WHERE Empresa = @Empresa AND [Número] = @Registro
  AND (Inmovilizado IS NULL OR LTRIM(RTRIM(Inmovilizado)) = '')
  AND Producto = @Producto AND UPPER(LTRIM(RTRIM(NumeroSerie))) = @Serie
  AND EXISTS (SELECT 1 FROM dbo.Inmovilizados i WHERE i.Empresa = @Empresa AND i.[Número] = @Inmovilizado AND i.Estado >= 0)
  AND NOT EXISTS (SELECT 1 FROM dbo.CabAlquileres o WHERE o.Empresa = @Empresa AND o.Inmovilizado = @Inmovilizado)";

        internal const string SQL_RASTRO = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @UsuarioRastro)";

        /// <summary>Solo con el script Issue913b (si no, ni se intenta): el aparato también en la ficha, si no tiene ya otro.</summary>
        internal const string SQL_COLUMNAS_FICHA = @"
IF COL_LENGTH('dbo.Inmovilizados', 'NumeroSerie') IS NOT NULL AND COL_LENGTH('dbo.Inmovilizados', 'Producto') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.Inmovilizados SET Producto = @Producto, NumeroSerie = @NumeroSerie
        WHERE Empresa = @Empresa AND [Número] = @Inmovilizado AND (NumeroSerie IS NULL OR LTRIM(RTRIM(NumeroSerie)) = '''')',
        N'@Producto char(15), @NumeroSerie varchar(30), @Empresa char(3), @Inmovilizado char(10)',
        @Producto = @Producto, @NumeroSerie = @NumeroSerie, @Empresa = @Empresa, @Inmovilizado = @Inmovilizado";

        public async Task<bool> Enlazar(EnlaceInmovilizado enlace, string usuario)
        {
            if (enlace == null)
            {
                throw new ArgumentNullException(nameof(enlace));
            }
            string quien = Recortar(string.IsNullOrWhiteSpace(usuario) ? UsuarioAuditoriaHelper.DESCONOCIDO : usuario, UsuarioAuditoriaHelper.LONGITUD_MAXIMA);
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_ENLAZAR,
                        new SqlParameter("@Inmovilizado", SqlDbType.Char, 10) { Value = enlace.Inmovilizado?.Trim() ?? string.Empty },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 30) { Value = quien },
                        new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = enlace.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                        new SqlParameter("@Registro", SqlDbType.Int) { Value = enlace.Registro },
                        new SqlParameter("@Producto", SqlDbType.Char, 15) { Value = enlace.Producto?.Trim() ?? string.Empty },
                        new SqlParameter("@Serie", SqlDbType.VarChar, 30) { Value = ReglaSituacionAparato.Clave(enlace.NumeroSerie) }).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = TABLA_MODIFICACIONES },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = JsonConvert.SerializeObject(new { Registro = enlace.Registro, Inmovilizado = (string)null }) },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1)
                        {
                            Value = JsonConvert.SerializeObject(new
                            {
                                Registro = enlace.Registro,
                                Inmovilizado = enlace.Inmovilizado?.Trim(),
                                Producto = enlace.Producto?.Trim(),
                                NumeroSerie = enlace.NumeroSerie?.Trim(),
                                Origen = "NestoAPI#913 enlace aparato-inmovilizado"
                            })
                        },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = quien }).ConfigureAwait(false);
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_COLUMNAS_FICHA,
                        new SqlParameter("@Producto", SqlDbType.Char, 15) { Value = enlace.Producto?.Trim() ?? string.Empty },
                        new SqlParameter("@NumeroSerie", SqlDbType.VarChar, 30) { Value = Recortar(enlace.NumeroSerie?.Trim() ?? string.Empty, 30) },
                        new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = enlace.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                        new SqlParameter("@Inmovilizado", SqlDbType.Char, 10) { Value = enlace.Inmovilizado?.Trim() ?? string.Empty }).ConfigureAwait(false);
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
}
