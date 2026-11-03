using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>NestoAPI#790: o se ha parado por los bloqueantes (<see cref="Bloqueado"/>), o lo que se ha contabilizado.</summary>
    public class ResultadoContabilizarInventario
    {
        /// <summary>No null: había bloqueantes y no se ha llamado al SP (409 con esto).</summary>
        public ComprobacionesInventarioDTO Bloqueado { get; set; }
        public ContabilizacionInventarioDTO Contabilizacion { get; set; }
    }

    public interface IServicioContabilizarInventario
    {
        /// <summary>null si el almacén no existe en la empresa.</summary>
        Task<ComprobacionesInventarioDTO> Comprobar(string empresa, string almacen, DateTime fecha);
        /// <summary>null si el almacén no existe en la empresa.</summary>
        Task<ResultadoContabilizarInventario> Contabilizar(string empresa, string almacen, DateTime fecha);
        /// <param name="valoracion"><see cref="PermisosInventario.VALORACION_PVP"/> o <see cref="PermisosInventario.VALORACION_COSTE"/>.</param>
        Task<DiferenciasInventarioDTO> Diferencias(string empresa, string almacen, DateTime? fecha, IList<int> traspasos, string valoracion);
    }

    /// <summary>
    /// NestoAPI#790 (épica #784): contabilizar el inventario de un almacén desde Nesto en vez de lanzar el SP a mano en SSMS.
    /// Primero las comprobaciones (<see cref="ComprobadorInventario"/>); con bloqueantes no se toca nada. Después, un
    /// prdContabilizarInventario por cada usuario con filas pendientes ese día (el SP, desde #787, solo toca las de @Delegación y
    /// es transaccional: todo o nada por usuario). Si falla uno se anota y se sigue con los demás. Síncrono, sin Hangfire.
    /// </summary>
    public class ServicioContabilizarInventario : IServicioContabilizarInventario
    {
        private readonly IRepositorioContabilizarInventario repositorio;

        public ServicioContabilizarInventario(IRepositorioContabilizarInventario repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ComprobacionesInventarioDTO> Comprobar(string empresa, string almacen, DateTime fecha)
        {
            bool? controlUbicaciones = await repositorio.LeerControlUbicaciones(empresa, almacen).ConfigureAwait(false);
            if (controlUbicaciones == null)
            {
                return null;
            }
            List<FilaInventarioPendiente> pendientes = await repositorio.LeerPendientes(empresa, almacen).ConfigureAwait(false);
            List<UbicacionAlmacenInventario> ubicaciones = controlUbicaciones.Value
                ? await repositorio.LeerUbicaciones(almacen).ConfigureAwait(false)
                : new List<UbicacionAlmacenInventario>();
            return ComprobadorInventario.Comprobar(almacen, fecha, controlUbicaciones.Value, pendientes, ubicaciones);
        }

        public async Task<ResultadoContabilizarInventario> Contabilizar(string empresa, string almacen, DateTime fecha)
        {
            ComprobacionesInventarioDTO comprobaciones = await Comprobar(empresa, almacen, fecha).ConfigureAwait(false);
            if (comprobaciones == null)
            {
                return null;
            }
            if (comprobaciones.Bloqueantes.Any())
            {
                return new ResultadoContabilizarInventario { Bloqueado = comprobaciones };
            }

            var contabilizacion = new ContabilizacionInventarioDTO { Almacen = almacen, Fecha = fecha.Date };
            foreach (RecuentoUsuarioInventarioDTO recuento in comprobaciones.Recuento)
            {
                try
                {
                    int resultado = await repositorio.Contabilizar(empresa, recuento.Usuario, almacen).ConfigureAwait(false);
                    if (resultado < 0)
                    {
                        contabilizacion.Errores.Add(new ErrorContabilizarInventarioDTO
                        {
                            Usuario = recuento.Usuario,
                            Mensaje = $"El procedimiento de contabilizar ha devuelto {resultado} sin decir por qué: no se ha contabilizado."
                        });
                        continue;
                    }
                    int? traspaso = await repositorio.LeerTraspaso(empresa, almacen, recuento.Usuario, fecha).ConfigureAwait(false);
                    contabilizacion.Contabilizados.Add(new UsuarioContabilizadoInventarioDTO
                    {
                        Usuario = recuento.Usuario,
                        Filas = recuento.Filas,
                        Traspaso = traspaso
                    });
                }
                catch (Exception ex)
                {
                    contabilizacion.Errores.Add(new ErrorContabilizarInventarioDTO { Usuario = recuento.Usuario, Mensaje = MensajeError(ex) });
                }
            }
            return new ResultadoContabilizarInventario { Contabilizacion = contabilizacion };
        }

        public async Task<DiferenciasInventarioDTO> Diferencias(string empresa, string almacen, DateTime? fecha, IList<int> traspasos, string valoracion)
        {
            bool aPvp = string.Equals(valoracion, PermisosInventario.VALORACION_PVP, StringComparison.OrdinalIgnoreCase);
            List<int> listaTraspasos = (traspasos ?? new List<int>()).Distinct().ToList();
            List<LineaInventarioConDiferencia> filas = await repositorio
                .LeerDiferencias(empresa, almacen, listaTraspasos.Any() ? null : fecha?.Date, listaTraspasos).ConfigureAwait(false);
            return CalcularDiferencias(almacen, fecha?.Date, listaTraspasos, aPvp, filas);
        }

        internal static DiferenciasInventarioDTO CalcularDiferencias(string almacen, DateTime? fecha, List<int> traspasos, bool aPvp,
            IEnumerable<LineaInventarioConDiferencia> filas)
        {
            List<LineaDiferenciaInventarioDTO> lineas = (filas ?? Enumerable.Empty<LineaInventarioConDiferencia>())
                .Select(f =>
                {
                    int diferencia = f.StockReal - f.StockCalculado;
                    decimal unitario = aPvp ? f.PVP : f.Coste;
                    return new LineaDiferenciaInventarioDTO
                    {
                        Producto = f.Producto,
                        Descripcion = f.Descripcion,
                        Grupo = f.Grupo,
                        Familia = f.Familia,
                        Pasillo = f.Pasillo,
                        Fila = f.Fila,
                        Columna = f.Columna,
                        StockCalculado = f.StockCalculado,
                        StockReal = f.StockReal,
                        Diferencia = diferencia,
                        ValorUnitario = unitario,
                        ValorDiferencia = RoundingHelper.DosDecimalesRound(diferencia * unitario),
                        Usuario = f.Usuario
                    };
                })
                .Where(l => l.Diferencia != 0)
                .OrderByDescending(l => Math.Abs(l.ValorDiferencia))
                .ThenBy(l => l.Producto)
                .ToList();

            return new DiferenciasInventarioDTO
            {
                Almacen = almacen,
                Fecha = fecha,
                Traspasos = traspasos ?? new List<int>(),
                Valoracion = aPvp ? PermisosInventario.VALORACION_PVP : PermisosInventario.VALORACION_COSTE,
                Lineas = lineas,
                Totales = new TotalesDiferenciasInventarioDTO
                {
                    Filas = lineas.Count,
                    Unidades = lineas.Sum(l => l.Diferencia),
                    Valor = lineas.Sum(l => l.ValorDiferencia)
                },
                PorGrupo = lineas
                    .GroupBy(l => l.Grupo ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new TotalGrupoDiferenciasInventarioDTO
                    {
                        Grupo = g.Key,
                        Filas = g.Count(),
                        Unidades = g.Sum(l => l.Diferencia),
                        Valor = g.Sum(l => l.ValorDiferencia)
                    })
                    .OrderBy(g => g.Grupo, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                PorFamilia = lineas
                    .GroupBy(l => l.Familia ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new TotalFamiliaDiferenciasInventarioDTO
                    {
                        Familia = g.Key,
                        Filas = g.Count(),
                        Unidades = g.Sum(l => l.Diferencia),
                        Valor = g.Sum(l => l.ValorDiferencia)
                    })
                    .OrderBy(g => g.Familia, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        /// <summary>El RAISERROR del SP, sin el ruido de las transacciones (266, 3902, 3903).</summary>
        internal static string MensajeError(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is SqlException sql)
                {
                    List<string> negocio = sql.Errors.Cast<SqlError>()
                        .Where(e => e.Number != 266 && e.Number != 3902 && e.Number != 3903)
                        .Select(e => e.Message?.Trim())
                        .Where(m => !string.IsNullOrEmpty(m))
                        .Distinct()
                        .ToList();
                    return negocio.Any() ? string.Join(" ", negocio) : sql.Message;
                }
            }
            return ex.Message;
        }
    }
}
