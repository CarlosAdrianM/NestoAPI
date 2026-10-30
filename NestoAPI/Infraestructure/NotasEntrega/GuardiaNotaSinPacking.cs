using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#765: lo que hace falta de un pedido para saber si es una nota de entrega que no ha pasado por el packing.</summary>
    public class EstadoNotaPacking
    {
        public bool NotaEntrega { get; set; }
        public string Ruta { get; set; }
        /// <summary>El de NotasEntregaCabecera; null = sin fila = Entrega (decisión 1 de #755).</summary>
        public string Tipo { get; set; }
        /// <summary>Alguna línea en curso (Estado 1) con picking.</summary>
        public bool TienePicking { get; set; }
        /// <summary>Algún envío del pedido grabado (EnviosAgencia.Estado &gt;= 0: en curso, tramitado, entregado...).</summary>
        public bool TieneEnvio { get; set; }
    }

    public interface IRepositorioEstadoNotaPacking
    {
        /// <summary>Null si el pedido no existe.</summary>
        Task<EstadoNotaPacking> Leer(string empresa, int pedido);
    }

    public class RepositorioEstadoNotaPacking : IRepositorioEstadoNotaPacking
    {
        internal const string SQL_ESTADO = @"
SELECT CAST(c.NotaEntrega AS bit) AS NotaEntrega, RTRIM(c.Ruta) AS Ruta, RTRIM(n.Tipo) AS Tipo,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM LinPedidoVta l
                              WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.Estado = 1 AND l.Picking > 0)
                 THEN 1 ELSE 0 END AS bit) AS TienePicking,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM EnviosAgencia e WHERE e.Pedido = c.[Número] AND e.Estado >= 0)
                 THEN 1 ELSE 0 END AS bit) AS TieneEnvio
FROM CabPedidoVta c
     LEFT JOIN NotasEntregaCabecera n ON n.Empresa = c.Empresa AND n.Pedido = c.[Número]
WHERE c.Empresa = @p0 AND c.[Número] = @p1";

        private readonly NVEntities db;

        public RepositorioEstadoNotaPacking(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<EstadoNotaPacking> Leer(string empresa, int pedido)
        {
            List<EstadoNotaPacking> filas = await db.Database.SqlQuery<EstadoNotaPacking>(SQL_ESTADO, empresa?.Trim(), pedido)
                .ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }
    }

    public interface IGuardiaNotaSinPacking
    {
        /// <summary>
        /// Por qué no se puede procesar la nota sin preguntar (es de ENTREGA y no tiene picking o no tiene envío), o null si se puede
        /// (no es nota, es de recogida o ya ha pasado por el packing).
        /// </summary>
        Task<string> Motivo(string empresa, int pedido);

        /// <summary>¿Es una nota (de entrega o de recogida) de una ruta de reparto propio? Esas las procesa Facturar rutas.</summary>
        Task<bool> EsNotaDeRepartoPropio(string empresa, int pedido);
    }

    /// <summary>
    /// NestoAPI#765 (decisión 4 de #755): TODA nota de ENTREGA, también la de solo texto, pasa por picking y packing; los bultos, la
    /// etiqueta y el reembolso salen del packing. Procesar la nota (FacturarPedido: «Crear albarán» del detalle de Nesto) antes la pasa a
    /// −2 y ya no sale en ningún sitio, así que una nota de entrega sin picking o sin envío necesita la confirmación de quien la procesa
    /// (<see cref="Models.Facturas.FacturarPedidoRequestDTO.SaltarPacking"/>). Las de RECOGIDA no pasan por packing: nunca se para.
    /// </summary>
    public class GuardiaNotaSinPacking : IGuardiaNotaSinPacking
    {
        /// <summary>El código de <see cref="Models.Facturas.PedidoConErrorDTO.Codigo"/> con el que Nesto pregunta y reintenta.</summary>
        public const string CODIGO = "NOTA_SIN_PACKING";
        public const string TIPO_ERROR = "Nota sin packing";

        private readonly IRepositorioEstadoNotaPacking repositorio;

        public GuardiaNotaSinPacking(IRepositorioEstadoNotaPacking repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<string> Motivo(string empresa, int pedido)
            => MotivoSinPacking(await repositorio.Leer(empresa, pedido).ConfigureAwait(false), pedido);

        public async Task<bool> EsNotaDeRepartoPropio(string empresa, int pedido)
            => EsNotaDeRepartoPropio(await repositorio.Leer(empresa, pedido).ConfigureAwait(false));

        internal static string MotivoSinPacking(EstadoNotaPacking estado, int pedido)
        {
            if (estado == null || !estado.NotaEntrega || TiposNotaEntrega.Normalizar(estado.Tipo) == TiposNotaEntrega.RECOGIDA)
            {
                return null;
            }
            if (!estado.TienePicking)
            {
                return $"La nota de entrega {pedido} no ha pasado por el picking ni por el packing: no tiene bultos, etiqueta ni reembolso.";
            }
            if (!estado.TieneEnvio)
            {
                return $"La nota de entrega {pedido} no ha pasado por el packing: no tiene envío (los bultos, la etiqueta y el reembolso salen del packing).";
            }
            return null;
        }

        internal static bool EsNotaDeRepartoPropio(EstadoNotaPacking estado)
            => estado != null && estado.NotaEntrega && RutasRepartoPropio.EsRutaPropia(estado.Ruta);
    }
}
