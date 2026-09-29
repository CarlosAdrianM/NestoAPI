using NestoAPI.Models;
using NestoAPI.Models.Traspasos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Traspasos
{
    public interface IServicioTraspasos
    {
        /// <summary>
        /// NestoAPI#553: propuesta de reposición (solo lectura). Lanza <see cref="ArgumentException"/>
        /// si los parámetros no son válidos y <see cref="ReposicionPendienteException"/> si el destino
        /// tiene una reposición sin contabilizar.
        /// </summary>
        Task<PropuestaTraspasoDTO> LeerPropuesta(string empresa, string origen, string destino);
    }

    public class ServicioTraspasos : IServicioTraspasos
    {
        /// <summary>Almacenes entre los que se repone (los mismos que @almacenesRepo del SP).</summary>
        public static readonly IReadOnlyCollection<string> ALMACENES_REPOSICION = new[]
        {
            Constantes.Almacenes.ALGETE,
            Constantes.Almacenes.REINA,
            Constantes.Almacenes.ALCOBENDAS
        };

        private readonly IRepositorioTraspasos repositorio;
        private readonly Func<DateTime> ahora;

        public ServicioTraspasos() : this(new RepositorioTraspasos(), () => DateTime.Now)
        {
        }

        public ServicioTraspasos(IRepositorioTraspasos repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<PropuestaTraspasoDTO> LeerPropuesta(string empresa, string origen, string destino)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa)
                ? Constantes.Empresas.EMPRESA_POR_DEFECTO
                : empresa.Trim();
            string origenLimpio = NormalizarAlmacen(origen, "origen");
            string destinoLimpio = NormalizarAlmacen(destino, "destino");
            if (origenLimpio == destinoLimpio)
            {
                throw new ArgumentException("El almacén de origen y el de destino no pueden ser el mismo");
            }
            if (empresaLimpia.Length > 3)
            {
                throw new ArgumentException($"La empresa «{empresaLimpia}» no es válida");
            }

            List<LineaReposicionStockSP> filas = await repositorio
                .LeerPropuesta(empresaLimpia, origenLimpio, destinoLimpio)
                .ConfigureAwait(false) ?? new List<LineaReposicionStockSP>();

            return new PropuestaTraspasoDTO
            {
                Empresa = empresaLimpia,
                Origen = origenLimpio,
                Destino = destinoLimpio,
                Fecha = ahora(),
                Lineas = filas
                    .Where(f => f.CantidadReposicion.GetValueOrDefault() > 0)
                    .Select(f => new LineaPropuestaTraspasoDTO
                    {
                        Producto = f.Número?.Trim(),
                        Nombre = f.Texto?.Trim(),
                        Grupo = f.Grupo?.Trim(),
                        StockOrigen = f.StockOrigen,
                        StockDestino = f.StockDestino,
                        CantidadMaximaDestino = f.CantidadMaximaDestino,
                        CantidadPendienteServirOrigen = f.CantidadPendienteServirOrigen,
                        CantidadPendienteServirDestino = f.CantidadPendienteServirDestino,
                        CantidadReposicion = f.CantidadReposicion.Value,
                        Multiplos = f.Multiplos
                    })
                    .OrderBy(l => l.Producto)
                    .ToList()
            };
        }

        private static string NormalizarAlmacen(string almacen, string nombreParametro)
        {
            if (string.IsNullOrWhiteSpace(almacen))
            {
                throw new ArgumentException($"Falta el almacén de {nombreParametro}");
            }
            string limpio = almacen.Trim().ToUpperInvariant();
            if (!ALMACENES_REPOSICION.Contains(limpio))
            {
                throw new ArgumentException($"El almacén de {nombreParametro} «{limpio}» no es de reposición (solo {string.Join(", ", ALMACENES_REPOSICION)})");
            }
            return limpio;
        }
    }
}
