using System;
using System.Data.Entity;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: implementación con SQL crudo sobre NVEntities (la tabla NO está en el EDMX).
    /// </summary>
    public class AmazonCredencialStore : IAmazonCredencialStore
    {
        private readonly NVEntities _db;

        public AmazonCredencialStore(NVEntities db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public AmazonSpApiCredencial Obtener()
        {
            return _db.Database.SqlQuery<AmazonSpApiCredencial>(
                "SELECT TOP 1 Id, ClientId, ClientSecret, RefreshToken, SecretExpiry, OldSecretExpiry, FechaModificacion, Usuario " +
                "FROM dbo.AmazonSpApiCredencial ORDER BY Id").FirstOrDefault();
        }

        public void GuardarSecretoNuevo(string clientId, string nuevoSecret, DateTime? secretExpiry, DateTime? oldSecretExpiry, string usuario)
        {
            _ = _db.Database.ExecuteSqlCommand(
                "UPDATE dbo.AmazonSpApiCredencial SET ClientSecret=@p0, SecretExpiry=@p1, OldSecretExpiry=@p2, FechaModificacion=GETDATE(), Usuario=@p3 WHERE ClientId=@p4",
                nuevoSecret,
                (object)secretExpiry ?? DBNull.Value,
                (object)oldSecretExpiry ?? DBNull.Value,
                (object)usuario ?? DBNull.Value,
                clientId);
        }

        public void MarcarRotacionSolicitada(string clientId)
        {
            _ = _db.Database.ExecuteSqlCommand(
                "UPDATE dbo.AmazonSpApiCredencial SET OldSecretExpiry=DATEADD(day,7,GETDATE()), FechaModificacion=GETDATE() WHERE ClientId=@p0",
                clientId);
        }
    }
}
