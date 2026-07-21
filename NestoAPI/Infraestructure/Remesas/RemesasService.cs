using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    public class RemesasService : IRemesasService
    {
        // Nesto#340 Fase 1C.14 slice 2: la tabla Remesas NO está mapeada en el EDMX de NestoAPI,
        // pero no hace falta mapearla (mismo patrón que Cargos/EstadosCCC/ExtractoInmovilizado):
        // SQL crudo aliasando la columna con acento a ASCII para materializar el DTO directamente.
        public async Task<List<RemesaDTO>> LeerRemesasAsync(string empresa, int? top)
        {
            using (NVEntities db = new NVEntities())
            {
                List<RemesaDTO> remesas = top.HasValue
                    ? await db.Database.SqlQuery<RemesaDTO>(
                        "SELECT TOP (@p1) [Número] AS Numero, Fecha, Importe, Banco FROM Remesas WHERE Empresa = @p0 ORDER BY [Número] DESC",
                        empresa, top.Value).ToListAsync().ConfigureAwait(false)
                    : await db.Database.SqlQuery<RemesaDTO>(
                        "SELECT [Número] AS Numero, Fecha, Importe, Banco FROM Remesas WHERE Empresa = @p0 ORDER BY [Número] DESC",
                        empresa).ToListAsync().ConfigureAwait(false);

                foreach (RemesaDTO remesa in remesas)
                {
                    remesa.Banco = remesa.Banco?.Trim();
                }
                return remesas;
            }
        }

        // Nesto#340 Fase 1C.14 slice 3: sustituye la lectura EF de DbContext.ExtractoCliente del
        // RemesasViewModel. TipoApunte 3 = pago (los efectos que van en la remesa).
        public async Task<List<MovimientoRemesaDTO>> LeerMovimientosAsync(string empresa, int remesa)
        {
            using (NVEntities db = new NVEntities())
            {
                List<ExtractoCliente> movimientos = await db.ExtractosCliente
                    .Where(e => e.Empresa == empresa && e.Remesa == remesa
                        && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.PAGO)
                    .OrderBy(e => e.Nº_Orden)
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Se materializa y se proyecta en memoria para poder usar ?.Trim() (no traducible
                // por EF), igual que en Alquileres.
                return movimientos.Select(MapearMovimiento).ToList();
            }
        }

        // Nesto#340 Fase 1C.14 slice 4: asientos de impagados agrupados (TipoApunte 4), que el
        // RemesasViewModel calculaba con un GROUP BY de EF sobre ExtractoCliente.
        public async Task<List<ImpagadoRemesaDTO>> LeerImpagadosAsync(string empresa, int? top)
        {
            using (NVEntities db = new NVEntities())
            {
                IQueryable<ImpagadoRemesaDTO> consulta = db.ExtractosCliente
                    .Where(e => e.Empresa == empresa && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.IMPAGADO)
                    .GroupBy(e => new { e.Asiento, e.Fecha })
                    .OrderByDescending(g => g.Key.Asiento)
                    .Select(g => new ImpagadoRemesaDTO
                    {
                        Asiento = g.Key.Asiento,
                        Fecha = g.Key.Fecha,
                        Cuenta = g.Count()
                    });

                if (top.HasValue)
                {
                    consulta = consulta.Take(top.Value);
                }

                return await consulta.ToListAsync().ConfigureAwait(false);
            }
        }

        // Nesto#340 Fase 1C.14 slice 5: movimientos de un asiento de impagados (grid derecho).
        public async Task<List<MovimientoRemesaDTO>> LeerMovimientosImpagadoAsync(string empresa, int asiento)
        {
            using (NVEntities db = new NVEntities())
            {
                List<ExtractoCliente> movimientos = await db.ExtractosCliente
                    .Where(e => e.Empresa == empresa && e.Asiento == asiento
                        && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.IMPAGADO)
                    .OrderBy(e => e.Nº_Orden)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return movimientos.Select(MapearMovimiento).ToList();
            }
        }

        // NestoAPI#332: el selector núcleo+estrategias vive en SelectorEfectosCobrables
        // (compartido a futuro con la remesa de tarjetas #181).
        public async Task<List<EfectoCandidatoDTO>> LeerEfectosCandidatosSepaAsync(string empresa)
        {
            using (NVEntities db = new NVEntities())
            {
                return await new SelectorEfectosCobrables(db).CandidatosSepa(empresa).ConfigureAwait(false);
            }
        }

        private static MovimientoRemesaDTO MapearMovimiento(ExtractoCliente e)
        {
            return new MovimientoRemesaDTO
            {
                Id = e.Nº_Orden,
                Cliente = e.Número?.Trim(),
                Contacto = e.Contacto?.Trim(),
                Documento = e.Nº_Documento?.Trim(),
                Efecto = e.Efecto?.Trim(),
                Concepto = e.Concepto?.Trim(),
                Importe = e.Importe,
                Ccc = e.CCC?.Trim(),
                Fecha = e.Fecha
            };
        }
    }
}
