using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.EnlacesDescarga
{
    /// <summary>
    /// NestoAPI#737: dbo.EnlacesDescargaDocumentos con SQL a mano, fuera del EDMX (como ClientesRiesgoAbandono #714 o
    /// PedidosPromesaRiesgo #693). Sin Scripts/Issue737_EnlacesDescargaDocumentos.sql la API no se cae: crear un enlace contesta
    /// 503 con el motivo y los enlaces (que no puede haber) dan la página de «no encontrado».
    /// </summary>
    public class RepositorioEnlacesDescargaSql : IRepositorioEnlacesDescarga
    {
        internal const string TABLA = "dbo.EnlacesDescargaDocumentos";
        internal const string MOTIVO_SIN_TABLA = "Los enlaces de descarga todavía no están activados en el servidor (falta lanzar el script de NestoAPI#737).";
        private const int OBJETO_NO_VALIDO = 208;

        internal const string SQL_INSERTAR = "INSERT INTO " + TABLA +
            " (Token, Tipo, Empresa, Numero, Creado, Caduca, Usuario, Descargas) VALUES (@Token, @Tipo, @Empresa, @Numero, @Creado, @Caduca, @Usuario, 0)";
        internal const string SQL_LEER = "SELECT Token, Tipo, RTRIM(Empresa) Empresa, RTRIM(Numero) Numero, Creado, Caduca, Usuario, Descargas, UltimaDescarga FROM " + TABLA +
            " WHERE Token = @Token";
        internal const string SQL_DESCARGA = "UPDATE " + TABLA + " SET Descargas = Descargas + 1, UltimaDescarga = @Cuando WHERE Token = @Token";

        public async Task Guardar(EnlaceDescargaDocumento enlace)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR,
                        new SqlParameter("@Token", enlace.Token),
                        new SqlParameter("@Tipo", enlace.Tipo),
                        new SqlParameter("@Empresa", enlace.Empresa),
                        new SqlParameter("@Numero", enlace.Numero),
                        new SqlParameter("@Creado", enlace.Creado),
                        new SqlParameter("@Caduca", enlace.Caduca),
                        new SqlParameter("@Usuario", (object)enlace.Usuario ?? DBNull.Value)).ConfigureAwait(false);
                }
            }
            catch (SqlException ex) when (ex.Number == OBJETO_NO_VALIDO)
            {
                throw new NestoBusinessException(MOTIVO_SIN_TABLA, ex) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
        }

        public async Task<EnlaceDescargaDocumento> Leer(string token)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    return (await db.Database.SqlQuery<EnlaceDescargaDocumento>(SQL_LEER, new SqlParameter("@Token", token))
                        .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                }
            }
            catch (SqlException ex) when (ex.Number == OBJETO_NO_VALIDO)
            {
                return null;
            }
        }

        public async Task RegistrarDescarga(string token, DateTime cuando)
        {
            using (var db = new NVEntities())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_DESCARGA,
                    new SqlParameter("@Cuando", cuando),
                    new SqlParameter("@Token", token)).ConfigureAwait(false);
            }
        }
    }
}
