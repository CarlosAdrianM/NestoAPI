using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>
    /// NestoAPI#544 (c): la memoria del aviso de facturas vencidas (tabla AvisosFacturasVencidas).
    /// </summary>
    public interface IAlmacenAvisosFacturasVencidas
    {
        /// <summary>El ÚLTIMO aviso registrado de cada efecto (por Nº_Orden). Solo los que tienen alguno.</summary>
        Task<Dictionary<int, AvisoFacturaVencidaRegistrado>> UltimoAvisoPorEfecto(string empresa, IEnumerable<int> numerosOrden);

        /// <summary>Apunta los avisos que se acaban de mandar de verdad.</summary>
        Task Registrar(IEnumerable<AvisoFacturaVencidaRegistrado> avisos);
    }

    /// <summary>
    /// Implementación con SQL parametrizado sobre NVEntities: la tabla NO está en el EDMX (misma
    /// decisión que AmazonSpApiCredencial, #225): solo la toca este job, y así no hay que editar a
    /// mano el EDMX ni arriesgar sus metadatos. Script: Scripts/Issue544_AvisosFacturasVencidas.sql.
    /// </summary>
    public class AlmacenAvisosFacturasVencidasSql : IAlmacenAvisosFacturasVencidas
    {
        private readonly NVEntities db;

        public AlmacenAvisosFacturasVencidasSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<Dictionary<int, AvisoFacturaVencidaRegistrado>> UltimoAvisoPorEfecto(string empresa, IEnumerable<int> numerosOrden)
        {
            List<int> ordenes = (numerosOrden ?? Enumerable.Empty<int>()).Distinct().ToList();
            Dictionary<int, AvisoFacturaVencidaRegistrado> resultado = new Dictionary<int, AvisoFacturaVencidaRegistrado>();
            if (!ordenes.Any())
            {
                return resultado;
            }

            // En trozos, que la lista de IN no crezca sin límite
            const int TROZO = 500;
            for (int i = 0; i < ordenes.Count; i += TROZO)
            {
                List<int> trozo = ordenes.Skip(i).Take(TROZO).ToList();
                List<object> parametros = new List<object> { new SqlParameter("@empresa", empresa) };
                List<string> marcas = new List<string>();
                for (int j = 0; j < trozo.Count; j++)
                {
                    marcas.Add("@o" + j);
                    parametros.Add(new SqlParameter("@o" + j, trozo[j]));
                }
                string sql =
                    "SELECT a.Id, a.Empresa, a.Cliente, a.Contacto, a.NumOrden, a.Factura, a.NumeroAviso, a.Fecha, a.ImportePendiente, a.Destinatarios " +
                    "FROM dbo.AvisosFacturasVencidas a " +
                    "INNER JOIN (SELECT NumOrden, MAX(Id) AS Id FROM dbo.AvisosFacturasVencidas " +
                    "            WHERE Empresa = @empresa AND NumOrden IN (" + string.Join(",", marcas) + ") GROUP BY NumOrden) u " +
                    "    ON u.Id = a.Id";
                List<AvisoFacturaVencidaRegistrado> filas = await db.Database
                    .SqlQuery<AvisoFacturaVencidaRegistrado>(sql, parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false);
                foreach (AvisoFacturaVencidaRegistrado fila in filas)
                {
                    fila.Empresa = fila.Empresa?.Trim();
                    fila.Cliente = fila.Cliente?.Trim();
                    fila.Contacto = fila.Contacto?.Trim();
                    fila.Factura = fila.Factura?.Trim();
                    resultado[fila.NumOrden] = fila;
                }
            }
            return resultado;
        }

        public async Task Registrar(IEnumerable<AvisoFacturaVencidaRegistrado> avisos)
        {
            foreach (AvisoFacturaVencidaRegistrado aviso in avisos ?? Enumerable.Empty<AvisoFacturaVencidaRegistrado>())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(
                    "INSERT INTO dbo.AvisosFacturasVencidas (Empresa, Cliente, Contacto, NumOrden, Factura, NumeroAviso, Fecha, ImportePendiente, Destinatarios, Usuario) " +
                    "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)",
                    aviso.Empresa,
                    aviso.Cliente,
                    aviso.Contacto,
                    aviso.NumOrden,
                    (object)aviso.Factura ?? DBNull.Value,
                    aviso.NumeroAviso,
                    aviso.Fecha,
                    aviso.ImportePendiente,
                    (object)aviso.Destinatarios ?? DBNull.Value,
                    "NestoAPI#544").ConfigureAwait(false);
            }
        }
    }
}
