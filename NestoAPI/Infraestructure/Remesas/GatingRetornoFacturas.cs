using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// NestoAPI#550 (Carlos, 29/09/26): un recibo de una factura cuyo pedido salió con RETORNO
    /// (cambio o «recoger producto») no se gira hasta saber si habrá rectificativa. Caso 32667:
    /// NV2615330 remesada el día de la entrega, el retorno llegó al día siguiente, rectificativa de
    /// 14,19 € y el banco devolvió el recibo de 30,19 €.
    ///
    /// Se retiene (forzable) hasta lo PRIMERO de:
    /// 1. el cliente tiene algo negativo pendiente (la rectificativa ya está hecha): se libera y
    ///    actúa la puerta de neteo de la remesa, que obliga a liquidar y girar la diferencia;
    /// 2. el retorno se recibió hace <see cref="DIAS_LABORABLES_TRAS_RETORNO_POR_DEFECTO"/> días
    ///    laborables sin rectificativa: no había nada que corregir;
    /// 3. tope: <see cref="DIAS_TOPE_TRAS_VENCIMIENTO_POR_DEFECTO"/> días desde el vencimiento,
    ///    haya llegado el retorno o no. Nunca se queda un efecto esperando indefinidamente.
    /// </summary>
    public class GatingRetornoFacturas
    {
        public const int DIAS_LABORABLES_TRAS_RETORNO_POR_DEFECTO = 3;
        public const int DIAS_TOPE_TRAS_VENCIMIENTO_POR_DEFECTO = 15;
        private const short RETORNO_NINGUNO = 0;

        private static readonly CultureInfo ES = new CultureInfo("es-ES");
        private readonly NVEntities db;

        public GatingRetornoFacturas(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>
        /// Para cada factura (Nº de documento, sin relleno) cuyos pedidos tienen algún envío con
        /// retorno: la fecha en que se recibió el retorno (la última si hay varios) o null si alguno
        /// no ha llegado. Las facturas que no aparecen no tienen envíos con retorno.
        /// </summary>
        public async Task<Dictionary<string, DateTime?>> RetornosPorFactura(string empresa, IEnumerable<string> facturas)
        {
            List<string> documentos = (facturas ?? Enumerable.Empty<string>())
                .Select(d => d?.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToList();
            if (!documentos.Any())
            {
                return new Dictionary<string, DateTime?>();
            }

            var facturaPedidos = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && documentos.Contains(l.Nº_Factura))
                .Select(l => new { l.Nº_Factura, Pedido = l.Número })
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            List<int> pedidos = facturaPedidos.Select(fp => fp.Pedido).Distinct().ToList();
            var envios = await db.EnviosAgencias
                .Where(ea => ea.Pedido != null && pedidos.Contains(ea.Pedido.Value)
                    && ea.Retorno != RETORNO_NINGUNO
                    && ea.Estado >= Constantes.Agencias.ESTADO_TRAMITADO)
                .Select(ea => new { Pedido = ea.Pedido.Value, ea.FechaRetornoRecibido })
                .ToListAsync().ConfigureAwait(false);
            if (!envios.Any())
            {
                return new Dictionary<string, DateTime?>();
            }
            Dictionary<int, List<DateTime?>> retornosPorPedido = envios
                .GroupBy(e => e.Pedido)
                .ToDictionary(g => g.Key, g => g.Select(e => e.FechaRetornoRecibido).ToList());

            Dictionary<string, DateTime?> resultado = new Dictionary<string, DateTime?>();
            foreach (var factura in facturaPedidos.GroupBy(fp => fp.Nº_Factura?.Trim()))
            {
                if (factura.Key == null)
                {
                    continue;
                }
                List<DateTime?> retornos = factura.Select(x => x.Pedido).Distinct()
                    .Where(p => retornosPorPedido.ContainsKey(p))
                    .SelectMany(p => retornosPorPedido[p])
                    .ToList();
                if (retornos.Any())
                {
                    resultado[factura.Key] = retornos.Any(r => r == null) ? null : retornos.Max();
                }
            }
            return resultado;
        }

        /// <summary>
        /// Motivo de retención de un efecto cuya factura tiene envíos con retorno, o null si se
        /// libera. Pura para testear sin BD.
        /// </summary>
        /// <param name="retornoRecibido">Fecha de recepción del retorno; null si aún no ha llegado.</param>
        public static string MotivoRetencion(DateTime? retornoRecibido, DateTime vencimiento, DateTime hoy,
            bool clienteConNegativos, int diasLaborablesTrasRetorno, int diasTopeTrasVencimiento)
        {
            if (clienteConNegativos)
            {
                return null; // Rectificativa hecha: manda la puerta de neteo
            }
            hoy = hoy.Date;
            DateTime tope = vencimiento.Date.AddDays(diasTopeTrasVencimiento);
            if (hoy >= tope)
            {
                return null;
            }
            if (retornoRecibido == null)
            {
                return "Retenido: el envío lleva retorno y aún no ha llegado; puede haber rectificativa (#550). " +
                    $"Sale solo el {Fecha(tope)} como muy tarde.";
            }
            DateTime libre = SumarDiasLaborables(retornoRecibido.Value.Date, diasLaborablesTrasRetorno);
            if (libre > tope)
            {
                libre = tope;
            }
            return hoy >= libre
                ? null
                : $"Retenido: el retorno llegó el {Fecha(retornoRecibido.Value)}; se espera por si hay rectificativa hasta el {Fecha(libre)} (#550).";
        }

        /// <summary>Suma días de lunes a viernes (los festivos no se descuentan).</summary>
        public static DateTime SumarDiasLaborables(DateTime desde, int dias)
        {
            DateTime fecha = desde.Date;
            int sumados = 0;
            while (sumados < dias)
            {
                fecha = fecha.AddDays(1);
                if (fecha.DayOfWeek != DayOfWeek.Saturday && fecha.DayOfWeek != DayOfWeek.Sunday)
                {
                    sumados++;
                }
            }
            return fecha;
        }

        private static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", ES);
    }
}
