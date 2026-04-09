using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Informes;

namespace NestoAPI.Infraestructure.Informes
{
    public class InformesService : IInformesService
    {
        private readonly NVEntities db;

        public InformesService()
        {
            db = new NVEntities();
        }

        internal InformesService(NVEntities db)
        {
            this.db = db;
        }

        public async Task<List<ResumenVentasDTO>> LeerResumenVentasAsync(DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas)
        {
            SqlParameter fechaDesdeParam = new SqlParameter("@FechaDesde", SqlDbType.DateTime)
            {
                Value = fechaDesde
            };
            SqlParameter fechaHastaParam = new SqlParameter("@FechaHasta", SqlDbType.DateTime)
            {
                Value = fechaHasta
            };
            SqlParameter soloFacturasParam = new SqlParameter("@soloFacturas", SqlDbType.Bit)
            {
                Value = soloFacturas
            };

            return await db.Database
                .SqlQuery<ResumenVentasDTO>(
                    "prdInformeResumenVentas @FechaDesde, @FechaHasta, @soloFacturas",
                    fechaDesdeParam, fechaHastaParam, soloFacturasParam)
                .ToListAsync()
                .ConfigureAwait(false);
        }
    }
}
