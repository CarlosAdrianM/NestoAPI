using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Un envío del pedido en curso o más (EnviosAgencia.Estado &gt;= 0).</summary>
    public class EnvioBorrarPedidoVenta
    {
        public int Numero { get; set; }
        public int Estado { get; set; }
    }

    /// <summary>Novedades 1283: lo que impide borrar un pedido de venta, leído ANTES de tocar nada.</summary>
    public class DatosBorrarPedidoVenta
    {
        /// <summary>Pickings de las líneas pendientes o en curso (Picking distinto de 0/null).</summary>
        public List<int> Pickings { get; set; } = new List<int>();
        /// <summary>Albaranes de las líneas en albarán (Estado 2-3).</summary>
        public List<int> Albaranes { get; set; } = new List<int>();
        /// <summary>Facturas de las líneas facturadas (Estado &gt;= 4).</summary>
        public List<string> Facturas { get; set; } = new List<string>();
        /// <summary>Líneas ya procesadas como nota de entrega o de recogida (Estado -2).</summary>
        public int LineasNotaProcesada { get; set; }
        /// <summary>Envíos en curso o más; las etiquetas pendientes (Estado &lt; 0) no cuentan: se borran con el pedido, como siempre.</summary>
        public List<EnvioBorrarPedidoVenta> Envios { get; set; } = new List<EnvioBorrarPedidoVenta>();
        /// <summary>Filas de Prepagos del pedido y su suma.</summary>
        public int NumeroPrepagos { get; set; }
        public decimal ImportePrepagos { get; set; }
        /// <summary>Lo cobrado con enlaces de pago del pedido (<see cref="EnlacesPagoPedido.ImporteAutorizado"/>; los pendientes no cuentan).</summary>
        public decimal CobradoPorEnlaces { get; set; }
    }

    /// <summary>
    /// LA REGLA (Novedades 1283, Carlos 20/11/26: «nada puede desaparecer mientras haya mercancía o dinero en marcha»; sin excepciones por
    /// grupo): un pedido de venta NO se puede borrar si tiene (a) líneas con picking, en albarán o factura, o ya procesadas como nota de
    /// entrega/recogida (-2); (b) un envío en curso o más (las etiquetas pendientes no impiden: se borran con el pedido); (c) un prepago (suma
    /// distinta de 0) o algo cobrado con un enlace de pago. Un pago a plazos solo solicitado no impide (se cancela al borrar, como siempre).
    /// Una ampliación en preparación pendiente o una retención por falta solo existen con el picking hecho: ya las cubre (a). PURA.
    /// </summary>
    public static class ReglaBorrarPedidoVenta
    {
        public const string ERROR_CODE = "PEDIDO_NO_SE_PUEDE_BORRAR";
        public const string ERROR_SIN_COMPROBAR = "PEDIDO_BORRAR_SIN_COMPROBAR";
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>Lo que impide borrarlo, en una frase con qué hacer; null si se puede borrar.</summary>
        public static string MotivoNoSePuedeBorrar(int pedido, DatosBorrarPedidoVenta datos)
        {
            List<(string Causa, string QueHacer)> motivos = Motivos(datos);
            if (!motivos.Any())
            {
                return null;
            }
            string causas = Unir(motivos.Select(m => m.Causa).ToList());
            string queHacer = string.Join(" ", motivos.Select(m => m.QueHacer).Distinct());
            return $"El pedido {pedido} no se puede borrar: {causas}. {queHacer}";
        }

        internal static List<(string Causa, string QueHacer)> Motivos(DatosBorrarPedidoVenta datos)
        {
            var motivos = new List<(string, string)>();
            if (datos == null)
            {
                return motivos;
            }
            List<int> pickings = (datos.Pickings ?? new List<int>()).Where(p => p != 0).Distinct().OrderBy(p => p).ToList();
            if (pickings.Any())
            {
                motivos.Add(pickings.Count == 1
                    ? ($"tiene líneas en el picking {pickings[0]}", "Anule antes el picking.")
                    : ($"tiene líneas en los pickings {Unir(pickings.Select(p => p.ToString(es)).ToList())}", "Anule antes los pickings."));
            }
            const string SERVIDO = "Lo que ya ha salido no se borra: si hace falta, haga un abono.";
            // Una línea en albarán o factura sin número también cuenta (0 / vacío): solo no se nombra
            List<int> albaranes = (datos.Albaranes ?? new List<int>()).Where(a => a != 0).Distinct().OrderBy(a => a).ToList();
            if ((datos.Albaranes ?? new List<int>()).Any())
            {
                motivos.Add((albaranes.Count == 0 ? "tiene líneas en albarán" : albaranes.Count == 1 ? $"tiene líneas en el albarán {albaranes[0]}"
                    : $"tiene líneas en los albaranes {Unir(albaranes.Select(a => a.ToString(es)).ToList())}", SERVIDO));
            }
            List<string> facturas = (datos.Facturas ?? new List<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct().OrderBy(f => f).ToList();
            if ((datos.Facturas ?? new List<string>()).Any())
            {
                motivos.Add((facturas.Count == 0 ? "tiene líneas facturadas" : facturas.Count == 1 ? $"tiene líneas en la factura {facturas[0]}"
                    : $"tiene líneas en las facturas {Unir(facturas)}", SERVIDO));
            }
            if (datos.LineasNotaProcesada > 0)
            {
                motivos.Add(("ya está procesado como nota de entrega", SERVIDO));
            }
            List<EnvioBorrarPedidoVenta> envios = (datos.Envios ?? new List<EnvioBorrarPedidoVenta>())
                .Where(e => e != null && e.Estado >= Constantes.Agencias.ESTADO_EN_CURSO).OrderBy(e => e.Numero).ToList();
            if (envios.Any())
            {
                motivos.Add((envios.Count == 1
                    ? $"tiene el envío {envios[0].Numero} {Estado(envios[0].Estado)}"
                    : $"tiene los envíos {Unir(envios.Select(e => $"{e.Numero} ({Estado(e.Estado)})").ToList())}",
                    envios.Count == 1 ? "Anule antes el envío (o pídaselo a Almacén)." : "Anule antes los envíos (o pídaselo a Almacén)."));
            }
            const string DINERO = "Devuélvalo o páselo a otro pedido antes.";
            if (datos.NumeroPrepagos > 0 && Math.Abs(datos.ImportePrepagos) >= 0.005M)
            {
                motivos.Add((datos.NumeroPrepagos == 1 ? $"tiene un prepago de {Euros(datos.ImportePrepagos)}" : $"tiene prepagos por {Euros(datos.ImportePrepagos)}", DINERO));
            }
            if (datos.CobradoPorEnlaces >= 0.005M)
            {
                motivos.Add(($"tiene {Euros(datos.CobradoPorEnlaces)} cobrados con un enlace de pago", DINERO));
            }
            return motivos;
        }

        /// <summary>El 409 de negocio ({ error: { code, message, details }, Message }), o null si se puede borrar.</summary>
        public static NestoBusinessException Error(string empresa, int pedido, DatosBorrarPedidoVenta datos)
        {
            string motivo = MotivoNoSePuedeBorrar(pedido, datos);
            return motivo == null ? null : new NestoBusinessException(motivo,
                new ErrorContext { ErrorCode = ERROR_CODE, Empresa = empresa?.Trim(), Pedido = pedido }
                    .WithData("pickings", datos.Pickings)
                    .WithData("albaranes", datos.Albaranes)
                    .WithData("facturas", datos.Facturas)
                    .WithData("lineasNotaProcesada", datos.LineasNotaProcesada)
                    .WithData("envios", datos.Envios)
                    .WithData("importePrepagos", datos.ImportePrepagos)
                    .WithData("cobradoPorEnlaces", datos.CobradoPorEnlaces))
            { StatusCode = HttpStatusCode.Conflict };
        }

        public static string Estado(int estado)
        {
            switch (estado)
            {
                case Constantes.Agencias.ESTADO_EN_CURSO: return "en curso";
                case Constantes.Agencias.ESTADO_TRAMITADO: return "tramitado";
                case Constantes.Agencias.ESTADO_ENTREGADO: return "entregado";
                case Constantes.Agencias.ESTADO_INCIDENTADO: return "incidentado";
                case Constantes.Agencias.ESTADO_DEVUELTO: return "devuelto";
                case Constantes.Agencias.ESTADO_CERRADO_A_MANO: return "cerrado";
                default: return "en marcha";
            }
        }

        private static string Euros(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero).ToString("N2", es) + " €";

        private static string Unir(List<string> partes) =>
            partes.Count <= 1 ? string.Join(string.Empty, partes) : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes.Last();
    }

    /// <summary>Novedades 1283: lee lo que necesita <see cref="ReglaBorrarPedidoVenta"/> (sustituible en los tests).</summary>
    public interface ILectorBorrarPedidoVenta
    {
        Task<DatosBorrarPedidoVenta> Leer(string empresa, int pedido);
    }

    /// <summary>
    /// Contra la BD: líneas, envíos y prepagos en UNA consulta; lo cobrado con enlaces, con la lectura de siempre
    /// (<see cref="LectorEnlacesPagoPedido"/>, la misma que la disponibilidad del pago a plazos).
    /// </summary>
    public class LectorBorrarPedidoVentaSql : ILectorBorrarPedidoVenta
    {
        internal const string SQL = @"
SELECT DISTINCT CAST('P' AS char(1)) AS Tipo, l.Picking AS Numero, CAST(NULL AS nvarchar(20)) AS Texto, CAST(0 AS int) AS Cuantos, CAST(0 AS decimal(19, 4)) AS Importe
FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = @empresa AND l.[Número] = @pedido AND ISNULL(l.Picking, 0) <> 0 AND l.Estado BETWEEN -1 AND 1
UNION ALL
SELECT DISTINCT 'A', ISNULL(l.[Nº Albarán], 0), NULL, 0, 0 FROM dbo.LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.[Número] = @pedido AND l.Estado IN (2, 3)
UNION ALL
SELECT DISTINCT 'F', 0, CAST(RTRIM(l.[Nº Factura]) AS nvarchar(20)), 0, 0 FROM dbo.LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.[Número] = @pedido AND l.Estado >= 4
UNION ALL
SELECT 'N', 0, NULL, COUNT(*), 0 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = @empresa AND l.[Número] = @pedido AND l.Estado = -2 HAVING COUNT(*) > 0
UNION ALL
SELECT 'E', e.Numero, NULL, CAST(e.Estado AS int), 0 FROM dbo.EnviosAgencia e WITH (NOLOCK) WHERE e.Empresa = @empresa AND e.Pedido = @pedido AND e.Estado >= 0
UNION ALL
SELECT 'R', 0, NULL, COUNT(*), CAST(SUM(p.Importe) AS decimal(19, 4)) FROM dbo.Prepagos p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.Pedido = @pedido HAVING COUNT(*) > 0";

        private class Fila
        {
            public string Tipo { get; set; }
            public int? Numero { get; set; }
            public string Texto { get; set; }
            public int Cuantos { get; set; }
            public decimal? Importe { get; set; }
        }

        private readonly NVEntities db;

        public LectorBorrarPedidoVentaSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<DatosBorrarPedidoVenta> Leer(string empresa, int pedido)
        {
            List<Fila> filas = await db.Database.SqlQuery<Fila>(SQL,
                new SqlParameter("@empresa", System.Data.SqlDbType.Char, 3) { Value = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                new SqlParameter("@pedido", System.Data.SqlDbType.Int) { Value = pedido }).ToListAsync().ConfigureAwait(true);
            Fila prepagos = filas.FirstOrDefault(f => f.Tipo == "R");
            return new DatosBorrarPedidoVenta
            {
                Pickings = filas.Where(f => f.Tipo == "P" && f.Numero.HasValue).Select(f => f.Numero.Value).ToList(),
                Albaranes = filas.Where(f => f.Tipo == "A" && f.Numero.HasValue).Select(f => f.Numero.Value).ToList(),
                Facturas = filas.Where(f => f.Tipo == "F").Select(f => f.Texto).ToList(),
                LineasNotaProcesada = filas.Where(f => f.Tipo == "N").Sum(f => f.Cuantos),
                Envios = filas.Where(f => f.Tipo == "E" && f.Numero.HasValue).Select(f => new EnvioBorrarPedidoVenta { Numero = f.Numero.Value, Estado = f.Cuantos }).ToList(),
                NumeroPrepagos = prepagos?.Cuantos ?? 0,
                ImportePrepagos = prepagos?.Importe ?? 0,
                CobradoPorEnlaces = EnlacesPagoPedido.ImporteAutorizado(LectorEnlacesPagoPedido.Leer(db, empresa?.Trim(), pedido))
            };
        }
    }
}
