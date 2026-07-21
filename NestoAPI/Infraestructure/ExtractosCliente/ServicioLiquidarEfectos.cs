using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ExtractosCliente
{
    /// <summary>
    /// NestoAPI#333 (v1 de las acciones sobre el extracto): liquidar dos movimientos del
    /// extracto de cliente ejecutando prdLiquidar, con las validaciones del SP ADELANTADAS
    /// en C#. Esto no es cosmético: varios raiserror del SP van con severidad 1 (aviso), que
    /// NO llega como SqlException a EF — sin validación previa esos fallos serían SILENCIOSOS.
    /// Único call site del SP prdLiquidar en NestoAPI: los flujos futuros (revisión de #332,
    /// ventana Nesto#419) deben pasar por aquí.
    /// </summary>
    public interface IServicioLiquidarEfectos
    {
        Task<ResultadoLiquidacionEfectos> Liquidar(string empresa, int origen, int destino, string usuario);
    }

    public class ResultadoLiquidacionEfectos
    {
        public bool Exito { get; set; }
        public List<string> Errores { get; } = new List<string>();
        /// <summary>Importes pendientes tras liquidar (liquidación parcial nativa: el mayor
        /// absorbe la diferencia y el menor queda a 0), para refrescar la UI sin recargar.</summary>
        public decimal ImportePdteOrigen { get; set; }
        public decimal ImportePdteDestino { get; set; }
    }

    /// <summary>Datos mínimos de un movimiento para validar la liquidación.</summary>
    public class EfectoParaLiquidar
    {
        public string Cliente { get; set; }
        public decimal ImportePdte { get; set; }
        public string Remesa { get; set; }
        public bool EstadoBloqueaLiquidacion { get; set; }
        public string DescripcionEstado { get; set; }
    }

    public class ServicioLiquidarEfectos : IServicioLiquidarEfectos
    {
        private readonly NVEntities db;

        public ServicioLiquidarEfectos(NVEntities db)
        {
            this.db = db;
        }

        public async Task<ResultadoLiquidacionEfectos> Liquidar(string empresa, int origen, int destino, string usuario)
        {
            var resultado = new ResultadoLiquidacionEfectos();

            EfectoParaLiquidar efectoOrigen = await CargarEfecto(empresa, origen).ConfigureAwait(false);
            EfectoParaLiquidar efectoDestino = await CargarEfecto(empresa, destino).ConfigureAwait(false);

            resultado.Errores.AddRange(ErroresLiquidacion(efectoOrigen, efectoDestino, origen, destino));
            if (resultado.Errores.Any())
            {
                return resultado;
            }

            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(
                    "EXEC prdLiquidar @Empresa, @Origen, @Destino, @Usuario",
                    new SqlParameter("@Empresa", System.Data.SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@Origen", origen),
                    new SqlParameter("@Destino", destino),
                    new SqlParameter("@Usuario", System.Data.SqlDbType.VarChar, 30) { Value = (object)usuario ?? DBNull.Value })
                    .ConfigureAwait(false);
            }
            catch (SqlException ex)
            {
                // #296: el RAISERROR de negocio llega enterrado en ruido de desajuste de
                // transacciones (error 266): quedarnos con el motivo real.
                resultado.Errores.Add(Contabilidad.ContabilidadService.ComponerMensajeSinRuidoDeTransacciones(
                    ex.Errors.Cast<SqlError>().Select(e => new KeyValuePair<int, string>(e.Number, e.Message))));
                return resultado;
            }

            // Verificación posterior: los raiserror de severidad 1 del SP no lanzan excepción
            // (fallo silencioso, p. ej. carrera con otro usuario que remesó entre la validación
            // y el EXEC). Si ninguno de los dos importes cambió, la liquidación NO se aplicó.
            decimal pdteOrigenAntes = efectoOrigen.ImportePdte;
            decimal pdteDestinoAntes = efectoDestino.ImportePdte;
            EfectoParaLiquidar origenDespues = await CargarEfecto(empresa, origen).ConfigureAwait(false);
            EfectoParaLiquidar destinoDespues = await CargarEfecto(empresa, destino).ConfigureAwait(false);
            resultado.ImportePdteOrigen = origenDespues?.ImportePdte ?? 0;
            resultado.ImportePdteDestino = destinoDespues?.ImportePdte ?? 0;

            if (resultado.ImportePdteOrigen == pdteOrigenAntes && resultado.ImportePdteDestino == pdteDestinoAntes)
            {
                resultado.Errores.Add($"La liquidación de {origen} contra {destino} no se aplicó " +
                    "(el estado de los movimientos pudo cambiar entre la validación y la ejecución). " +
                    "Refresque el extracto y vuelva a intentarlo.");
                return resultado;
            }

            resultado.Exito = true;
            return resultado;
        }

        // ExtractoCliente está en el EDMX; el flag de bloqueo vive en EstadosExtracto, que NO
        // lo está → SQL crudo con alias ASCII (patrón Cargos/EstadosCCC/Remesas).
        private async Task<EfectoParaLiquidar> CargarEfecto(string empresa, int numOrden)
        {
            List<EfectoParaLiquidar> filas = await db.Database.SqlQuery<EfectoParaLiquidar>(
                "SELECT e.[Número] AS Cliente, e.ImportePdte, e.Remesa, " +
                "       CAST(ISNULL(ee.[BloquearLiquidación], 0) AS bit) AS EstadoBloqueaLiquidacion, " +
                "       ee.[Descripción] AS DescripcionEstado " +
                "FROM ExtractoCliente e " +
                "LEFT JOIN EstadosExtracto ee ON ee.Empresa = e.Empresa AND ee.[Número] = e.Estado " +
                "WHERE e.Empresa = @p0 AND e.[Nº Orden] = @p1",
                empresa, numOrden).ToListAsync().ConfigureAwait(false);
            EfectoParaLiquidar efecto = filas.FirstOrDefault();
            if (efecto != null)
            {
                efecto.Cliente = efecto.Cliente?.Trim();
                efecto.Remesa = efecto.Remesa?.Trim();
                efecto.DescripcionEstado = efecto.DescripcionEstado?.Trim();
            }
            return efecto;
        }

        /// <summary>
        /// Réplica en C# de las validaciones de prdLiquidar (varias fallan con severidad 1 =
        /// silenciosas desde EF). Pura y estática para testearla sin BD.
        /// </summary>
        internal static List<string> ErroresLiquidacion(EfectoParaLiquidar origen, EfectoParaLiquidar destino,
            int origenId, int destinoId)
        {
            var errores = new List<string>();
            if (origen == null)
            {
                errores.Add($"No existe el movimiento origen {origenId} en el extracto.");
            }
            if (destino == null)
            {
                errores.Add($"No existe el movimiento destino {destinoId} en el extracto.");
            }
            if (origen == null || destino == null)
            {
                return errores;
            }
            if (origenId == destinoId)
            {
                errores.Add("El origen y el destino no pueden ser el mismo movimiento.");
                return errores;
            }
            if (!string.Equals(origen.Cliente, destino.Cliente, StringComparison.OrdinalIgnoreCase))
            {
                errores.Add($"Los dos movimientos deben ser del mismo cliente " +
                    $"(origen: {origen.Cliente}, destino: {destino.Cliente}).");
            }
            if (!Contabilidad.ContabilidadService.EsParLiquidable(origen.ImportePdte, destino.ImportePdte))
            {
                errores.Add($"Los importes pendientes deben tener signo contrario y ser distintos de 0 " +
                    $"(origen: {origen.ImportePdte:C}, destino: {destino.ImportePdte:C}).");
            }
            if (!string.IsNullOrEmpty(origen.Remesa) && !string.IsNullOrEmpty(destino.Remesa))
            {
                // Además de abortar el SP, es la regla clave de #332: liquidar ANTES de remesar.
                errores.Add($"Ambos movimientos están ya remesados (origen: remesa {origen.Remesa}, " +
                    $"destino: remesa {destino.Remesa}): no se pueden liquidar entre sí.");
            }
            if (origen.EstadoBloqueaLiquidacion)
            {
                errores.Add($"El movimiento origen {origenId} tiene un estado que bloquea la liquidación" +
                    $"{(string.IsNullOrEmpty(origen.DescripcionEstado) ? "" : $" ({origen.DescripcionEstado})")}.");
            }
            if (destino.EstadoBloqueaLiquidacion)
            {
                errores.Add($"El movimiento destino {destinoId} tiene un estado que bloquea la liquidación" +
                    $"{(string.IsNullOrEmpty(destino.DescripcionEstado) ? "" : $" ({destino.DescripcionEstado})")}.");
            }
            return errores;
        }
    }
}
