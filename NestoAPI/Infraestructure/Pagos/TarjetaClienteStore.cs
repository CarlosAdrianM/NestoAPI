using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#178: implementación con SQL crudo sobre NVEntities, porque la tabla
    /// TarjetasClientes NO está en el EDMX (mismo patrón que AmazonSpApiCredencial, #225).
    /// Cada operación abre su propio contexto: el servicio de pagos ya trabaja así.
    /// </summary>
    public class TarjetaClienteStore : ITarjetaClienteStore
    {
        private const string ColumnasSelect =
            "SELECT Id, Empresa, Cliente, Contacto, TokenRedsys, CofTxnId, UltimosDigitos, " +
            "TipoTarjeta, MarcaTarjeta, FechaCaducidad, FechaCreacion, FechaUltimoUso, Activa, " +
            "MotivoDesactivacion, FechaDesactivacion, UsuarioCreacion, IntentosFallidosConsecutivos " +
            "FROM dbo.TarjetasClientes ";

        public List<TarjetaCliente> ListarActivas(string empresa, string cliente)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<TarjetaCliente>(
                    ColumnasSelect +
                    "WHERE Empresa = @p0 AND Cliente = @p1 AND Activa = 1 " +
                    "ORDER BY FechaUltimoUso DESC, FechaCreacion DESC",
                    empresa?.Trim(), cliente?.Trim()).ToList();
            }
        }

        public TarjetaCliente ObtenerPorId(int id)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<TarjetaCliente>(
                    ColumnasSelect + "WHERE Id = @p0", id).FirstOrDefault();
            }
        }

        public void GuardarOActualizar(TarjetaCliente tarjeta)
        {
            if (tarjeta == null
                || string.IsNullOrWhiteSpace(tarjeta.TokenRedsys)
                || string.IsNullOrWhiteSpace(tarjeta.Cliente))
            {
                return;
            }

            using (NVEntities db = new NVEntities())
            {
                int actualizadas = db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.TarjetasClientes SET " +
                    "FechaUltimoUso = GETDATE(), IntentosFallidosConsecutivos = 0, " +
                    "Activa = 1, MotivoDesactivacion = NULL, FechaDesactivacion = NULL, " +
                    "CofTxnId = COALESCE(@p3, CofTxnId), " +
                    "FechaCaducidad = COALESCE(@p4, FechaCaducidad), " +
                    "UltimosDigitos = COALESCE(NULLIF(@p5, ''), UltimosDigitos) " +
                    "WHERE Empresa = @p0 AND Cliente = @p1 AND TokenRedsys = @p2",
                    tarjeta.Empresa?.Trim(), tarjeta.Cliente?.Trim(), tarjeta.TokenRedsys,
                    (object)tarjeta.CofTxnId ?? DBNull.Value,
                    (object)tarjeta.FechaCaducidad ?? DBNull.Value,
                    (object)tarjeta.UltimosDigitos ?? DBNull.Value);

                if (actualizadas == 0)
                {
                    _ = db.Database.ExecuteSqlCommand(
                        "INSERT INTO dbo.TarjetasClientes " +
                        "(Empresa, Cliente, Contacto, TokenRedsys, CofTxnId, UltimosDigitos, " +
                        "TipoTarjeta, MarcaTarjeta, FechaCaducidad, FechaUltimoUso, UsuarioCreacion) " +
                        "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, GETDATE(), @p9)",
                        tarjeta.Empresa?.Trim(), tarjeta.Cliente?.Trim(),
                        (object)tarjeta.Contacto?.Trim() ?? DBNull.Value,
                        tarjeta.TokenRedsys,
                        (object)tarjeta.CofTxnId ?? DBNull.Value,
                        (object)tarjeta.UltimosDigitos ?? DBNull.Value,
                        (object)tarjeta.TipoTarjeta ?? DBNull.Value,
                        (object)tarjeta.MarcaTarjeta ?? DBNull.Value,
                        (object)tarjeta.FechaCaducidad ?? DBNull.Value,
                        (object)tarjeta.UsuarioCreacion ?? DBNull.Value);
                }
            }
        }

        public void Desactivar(int id, string motivo)
        {
            using (NVEntities db = new NVEntities())
            {
                _ = db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.TarjetasClientes SET Activa = 0, MotivoDesactivacion = @p1, " +
                    "FechaDesactivacion = GETDATE() WHERE Id = @p0",
                    id, (object)motivo ?? DBNull.Value);
            }
        }

        public void RegistrarUso(int id, bool cobroAutorizado)
        {
            using (NVEntities db = new NVEntities())
            {
                _ = db.Database.ExecuteSqlCommand(
                    cobroAutorizado
                        ? "UPDATE dbo.TarjetasClientes SET FechaUltimoUso = GETDATE(), " +
                          "IntentosFallidosConsecutivos = 0 WHERE Id = @p0"
                        : "UPDATE dbo.TarjetasClientes SET " +
                          "IntentosFallidosConsecutivos = IntentosFallidosConsecutivos + 1 WHERE Id = @p0",
                    id);
            }
        }
    }
}
