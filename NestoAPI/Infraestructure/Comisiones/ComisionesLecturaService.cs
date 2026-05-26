using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Comisiones;

namespace NestoAPI.Infraestructure.Comisiones
{
    public class ComisionesLecturaService : IComisionesLecturaService
    {
        private readonly NVEntities db;

        public ComisionesLecturaService()
        {
            db = new NVEntities();
        }

        internal ComisionesLecturaService(NVEntities db)
        {
            this.db = db;
        }

        public async Task<ComisionesAntiguasDTO> LeerComisionesAntiguasAsync(
            string empresa, DateTime fechaDesde, DateTime fechaHasta, string vendedor)
        {
            // El SP prdComisionesPorVendedor devuelve 6 columnas con prefijo % en el
            // nombre (%ComisionCos, %ComisionFijaCos, %ComisionApa, etc.). El EDMX
            // legacy de Nesto las mapeaba a propiedades C_Comision* via ScalarProperty;
            // SqlQuery<T> empareja columnas a propiedades por nombre directo y %
            // no es válido en identificadores C#, así que esas 6 propiedades quedaban
            // siempre a 0 (NestoAPI#210, regresión Nesto#340). Se usa SqlDataReader
            // y un mapper explícito para preservar la equivalencia legacy.
            var connection = (SqlConnection)db.Database.Connection;
            var huboQueAbrir = connection.State != ConnectionState.Open;
            if (huboQueAbrir)
            {
                await connection.OpenAsync().ConfigureAwait(false);
            }
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = "prdComisionesPorVendedor";
                    command.Parameters.Add(new SqlParameter("@Empresa", SqlDbType.Char) { Value = empresa ?? "1" });
                    command.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = fechaDesde });
                    command.Parameters.Add(new SqlParameter("@Hasta", SqlDbType.DateTime) { Value = fechaHasta });
                    command.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.Char) { Value = vendedor ?? string.Empty });
                    command.Parameters.Add(new SqlParameter("@RellenarTabla", SqlDbType.Bit) { Value = false });

                    using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            return MapearComisionesAntiguas(reader);
                        }
                        return null;
                    }
                }
            }
            finally
            {
                if (huboQueAbrir)
                {
                    connection.Close();
                }
            }
        }

        internal static ComisionesAntiguasDTO MapearComisionesAntiguas(IDataRecord record)
        {
            decimal Decimal(string columna)
            {
                var ordinal = record.GetOrdinal(columna);
                return record.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(record.GetValue(ordinal));
            }

            return new ComisionesAntiguasDTO
            {
                VentaCos = Decimal("VentaCos"),
                VentaFDMCos = Decimal("VentaFDMCos"),
                VentaSabrinaCos = Decimal("VentaSabrinaCos"),
                TotalCos = Decimal("TotalCos"),
                C_ComisionCos = Decimal("%ComisionCos"),
                TotalComisionCos = Decimal("TotalComisionCos"),
                C_ComisionFijaCos = Decimal("%ComisionFijaCos"),
                TotalComisionFijaCos = Decimal("TotalComisionFijaCos"),
                SiguienteTramoCos = Decimal("SiguienteTramoCos"),
                DiferenciaTramoCos = Decimal("DiferenciaTramoCos"),
                ImporteRutaCos = Decimal("ImporteRutaCos"),
                VentaAPA = Decimal("VentaAPA"),
                VentaFDMApa = Decimal("VentaFDMApa"),
                VentaSabrinaApa = Decimal("VentaSabrinaApa"),
                TotalApa = Decimal("TotalApa"),
                C_ComisionApa = Decimal("%ComisionApa"),
                TotalComisionApa = Decimal("TotalComisionApa"),
                C_ComisionFijaApa = Decimal("%ComisionFijaApa"),
                TotalComisionFijaApa = Decimal("TotalComisionFijaApa"),
                SiguienteTramoApa = Decimal("SiguienteTramoApa"),
                DiferenciaTramoApa = Decimal("DiferenciaTramoApa"),
                ImporteRutaApa = Decimal("ImporteRutaApa"),
                VentaAcp = Decimal("VentaAcp"),
                VentaFDMAcp = Decimal("VentaFDMAcp"),
                VentaSabrinaAcp = Decimal("VentaSabrinaAcp"),
                TotalAcp = Decimal("TotalAcp"),
                C_ComisionAcp = Decimal("%ComisionAcp"),
                TotalComisionAcp = Decimal("TotalComisionAcp"),
                C_ComisionFijaAcp = Decimal("%ComisionFijaAcp"),
                TotalComisionFijaAcp = Decimal("TotalComisionFijaAcp"),
                SiguienteTramoAcp = Decimal("SiguienteTramoAcp"),
                DiferenciaTramoAcp = Decimal("DiferenciaTramoAcp"),
                ImporteRutaAcp = Decimal("ImporteRutaAcp"),
                TotalComision = Decimal("TotalComision"),
                PremioEva = Decimal("PremioEva"),
                EvaFdM = Decimal("EvaFdM"),
                CifraRuta = Decimal("CifraRuta"),
                VentaCur = Decimal("VentaCur"),
                TotalComisionCur = Decimal("TotalComisionCur"),
                VentaUL = Decimal("VentaUL"),
                TotalComisionUL = Decimal("TotalComisionUL"),
            };
        }

        public async Task<List<PedidoVendedorComisionDTO>> LeerPedidosVendedorAsync(string vendedor)
        {
            // La vista vstLinPedidoVtaConVendedor no está importada en el EDMX del API, por
            // lo que se ejecuta la consulta directamente con alias limpios.
            const string sql = @"
                SELECT
                    [Nº Orden] AS NumeroOrden,
                    rtrim(Vendedor) AS Vendedor,
                    rtrim(Ruta) AS Ruta,
                    rtrim(Nombre) AS Nombre,
                    rtrim(Dirección) AS Direccion,
                    rtrim(CodPostal) AS CodPostal,
                    rtrim(Población) AS Poblacion,
                    Número AS Numero,
                    rtrim(Producto) AS Producto,
                    rtrim(Texto) AS Texto,
                    rtrim(Familia) AS Familia,
                    [Fecha Entrega] AS FechaEntrega,
                    Cantidad,
                    Estado,
                    Picking,
                    rtrim(Empresa) AS Empresa,
                    rtrim([Nº Cliente]) AS NumeroCliente,
                    rtrim(Contacto) AS Contacto,
                    [Base Imponible] AS BaseImponible
                FROM vstLinPedidoVtaConVendedor
                WHERE (Empresa = '1' OR Empresa = '3')
                    AND Estado BETWEEN -1 AND 1
                    AND Vendedor = @Vendedor
                ORDER BY Número, [Nº Orden]";

            return await db.Database
                .SqlQuery<PedidoVendedorComisionDTO>(sql,
                    new SqlParameter("@Vendedor", SqlDbType.NVarChar) { Value = vendedor ?? string.Empty })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<VentaVendedorComisionDTO>> LeerVentasVendedorAsync(
            DateTime fechaDesde, DateTime fechaHasta, string vendedor)
        {
            // La vista vstLinPedidoVtaComisione sí está mapeada en el EDMX. Se usa LINQ
            // con proyección al DTO limpio.
            var lineas = await db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    ((l.Estado == 4 && l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta) ||
                     (l.Estado == 2 && l.Fecha_Albarán >= fechaDesde && l.Fecha_Albarán <= fechaHasta)))
                .ToListAsync()
                .ConfigureAwait(false);

            return lineas.Select(l => new VentaVendedorComisionDTO
            {
                NumeroOrden = l.Nº_Orden,
                Empresa = l.Empresa?.Trim(),
                NumeroCliente = l.Nº_Cliente?.Trim(),
                Contacto = l.Contacto?.Trim(),
                Vendedor = l.Vendedor?.Trim(),
                Ruta = l.Ruta?.Trim(),
                Nombre = l.Nombre?.Trim(),
                Direccion = l.Dirección?.Trim(),
                CodPostal = l.CodPostal?.Trim(),
                Poblacion = l.Población?.Trim(),
                Numero = l.Número,
                Producto = l.Producto?.Trim(),
                Texto = l.Texto?.Trim(),
                Familia = l.Familia?.Trim(),
                FechaEntrega = l.Fecha_Entrega,
                Cantidad = l.Cantidad,
                Estado = l.Estado,
                Picking = l.Picking,
                BaseImponible = l.Base_Imponible,
                FechaAlbaran = l.Fecha_Albarán,
                NumeroAlbaran = l.Nº_Albarán,
                FechaFactura = l.Fecha_Factura,
                NumeroFactura = l.Nº_Factura?.Trim(),
                Grupo = l.Grupo?.Trim(),
                SubGrupo = l.SubGrupo?.Trim(),
                EstadoFamilia = l.EstadoFamilia,
                PrecioTarifa = l.PrecioTarifa
            }).ToList();
        }
    }
}
