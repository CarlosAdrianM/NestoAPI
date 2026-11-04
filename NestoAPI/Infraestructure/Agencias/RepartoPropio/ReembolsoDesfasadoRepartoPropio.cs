using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#810 (pedido 928723, envío RP250518 con 480,02 € y factura GB2601741 de 510,15 €; Alfredo, sugerencia 1005): el
    /// reembolso de un envío del reparto propio se calcula al crearlo; si después se amplía el pedido y se factura más, se queda
    /// desfasado. Al facturar NO se cambia solo (Carlos 04/11/26): la respuesta lleva los desfasados y el usuario decide
    /// (<c>PUT api/EnviosAgencias/{id}/Reembolso</c>, <see cref="CambioReembolsoRepartoPropio"/>). La regla, sin base de datos.
    /// </summary>
    public static class ReembolsoDesfasadoRepartoPropio
    {
        /// <summary>Envío del reparto propio (agencia 14) de un pedido y aún sin entregar (Estado &lt; 2): su reembolso se puede cuadrar.</summary>
        public static bool EsEnvioACuadrar(EnviosAgencia envio)
            => envio != null && envio.Pedido.HasValue && envio.Estado < Constantes.Agencias.ESTADO_ENTREGADO
                && ReembolsoRepartoPropio.EsAgenciaRepartoPropio(envio.Agencia);

        /// <summary>
        /// Lo que tiene que cobrar el repartidor de un pedido ya facturado: lo que queda PENDIENTE en efectivo de sus facturas en el
        /// extracto del cliente (efectos EFC, la misma regla que el reembolso de un pedido facturado, NestoAPI#513/#785), en
        /// CUALQUIER empresa (la factura GB está en la 3 con el pedido en la 1, NestoAPI#807).
        /// </summary>
        public static decimal PendienteEfectivoDeLasFacturas(string cliente, IEnumerable<string> facturas, IEnumerable<ExtractoCliente> extracto)
        {
            string clienteLimpio = Limpia(cliente);
            var documentos = new HashSet<string>((facturas ?? Enumerable.Empty<string>()).Select(Limpia).Where(f => f != null), StringComparer.Ordinal);
            if (clienteLimpio == null || documentos.Count == 0)
            {
                return 0m;
            }
            decimal pendiente = (extracto ?? Enumerable.Empty<ExtractoCliente>())
                .Where(e => e != null && Limpia(e.Número) == clienteLimpio
                    && documentos.Contains(Limpia(e.Nº_Documento) ?? string.Empty)
                    && Limpia(e.TipoApunte) == Constantes.TiposApunteExtracto.EFECTO
                    && Limpia(e.FormaPago) == Constantes.FormasPago.EFECTIVO)
                .Sum(e => e.ImportePdte);
            return RoundingHelper.DosDecimalesRound(pendiente);
        }

        /// <summary>
        /// Los envíos del reparto propio sin entregar cuyo reembolso no es lo que queda por cobrar de las facturas de su pedido. Solo
        /// pedidos con factura (sin factura no hay con qué comparar). Si un pedido tiene varios envíos así, a cada uno le toca lo
        /// pendiente menos lo que llevan los otros (nunca negativo). Las listas pueden traer datos de otros pedidos: se filtran aquí.
        /// </summary>
        public static List<ReembolsoDesfasadoDTO> Detectar(IEnumerable<EnviosAgencia> envios, IEnumerable<FacturaPedidoReparto> facturasPedido,
            IEnumerable<ExtractoCliente> extracto)
        {
            var resultado = new List<ReembolsoDesfasadoDTO>();
            List<EnviosAgencia> aCuadrar = (envios ?? Enumerable.Empty<EnviosAgencia>()).Where(EsEnvioACuadrar).ToList();
            List<FacturaPedidoReparto> facturas = (facturasPedido ?? Enumerable.Empty<FacturaPedidoReparto>())
                .Where(f => f != null && Limpia(f.Factura) != null).ToList();
            List<ExtractoCliente> movimientos = (extracto ?? Enumerable.Empty<ExtractoCliente>()).ToList();

            foreach (IGrouping<int, EnviosAgencia> delPedido in aCuadrar.GroupBy(e => e.Pedido.Value).OrderBy(g => g.Key))
            {
                List<string> suyas = facturas.Where(f => f.Pedido == delPedido.Key).Select(f => Limpia(f.Factura)).Distinct().ToList();
                if (suyas.Count == 0)
                {
                    continue;
                }
                foreach (EnviosAgencia envio in delPedido.OrderBy(e => e.Numero))
                {
                    decimal pendiente = PendienteEfectivoDeLasFacturas(envio.Cliente, suyas, movimientos);
                    decimal otros = delPedido.Where(o => o.Numero != envio.Numero).Sum(o => o.Reembolso);
                    decimal facturado = RoundingHelper.DosDecimalesRound(pendiente - otros);
                    if (facturado < 0m)
                    {
                        facturado = 0m;
                    }
                    decimal actual = RoundingHelper.DosDecimalesRound(envio.Reembolso);
                    if (actual != facturado)
                    {
                        resultado.Add(new ReembolsoDesfasadoDTO
                        {
                            Envio = envio.Numero,
                            Pedido = delPedido.Key,
                            ReembolsoActual = actual,
                            ReembolsoFacturado = facturado
                        });
                    }
                }
            }
            return resultado;
        }

        private static string Limpia(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    /// <summary>NestoAPI#810: los reembolsos desfasados de los pedidos recién facturados (doble en los tests).</summary>
    public interface IDetectorReembolsosDesfasados
    {
        Task<List<ReembolsoDesfasadoDTO>> Detectar(IEnumerable<int> pedidos);
    }

    /// <summary>
    /// NestoAPI#810: <see cref="ReembolsoDesfasadoRepartoPropio.Detectar"/> leyendo la base de datos con tres consultas para todos los
    /// pedidos (envíos del reparto propio sin entregar, facturas de sus pedidos y lo pendiente en efectivo de esas facturas).
    /// </summary>
    public class DetectorReembolsosDesfasados : IDetectorReembolsosDesfasados
    {
        private readonly NVEntities db;

        public DetectorReembolsosDesfasados(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<ReembolsoDesfasadoDTO>> Detectar(IEnumerable<int> pedidos)
        {
            List<int> lista = (pedidos ?? Enumerable.Empty<int>()).Where(p => p > 0).Distinct().ToList();
            if (lista.Count == 0)
            {
                return new List<ReembolsoDesfasadoDTO>();
            }
            int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;
            short entregado = Constantes.Agencias.ESTADO_ENTREGADO;
            List<EnviosAgencia> envios = await db.EnviosAgencias
                .Where(e => e.Pedido.HasValue && lista.Contains(e.Pedido.Value) && e.Agencia == agencia && e.Estado < entregado)
                .ToListAsync().ConfigureAwait(false);
            if (envios.Count == 0)
            {
                return new List<ReembolsoDesfasadoDTO>();
            }

            List<int> conEnvio = envios.Select(e => e.Pedido.Value).Distinct().ToList();
            // Por número de pedido, sin empresa: el pedido de la 1 se factura en la 3 (GB, NestoAPI#807)
            List<FacturaPedidoReparto> facturas = (await db.LinPedidoVtas
                .Where(l => conEnvio.Contains(l.Número) && l.Nº_Factura != null && l.Nº_Factura != "")
                .Select(l => new { l.Número, l.Empresa, l.Nº_Factura })
                .Distinct()
                .ToListAsync().ConfigureAwait(false))
                .Select(l => new FacturaPedidoReparto { Pedido = l.Número, Empresa = l.Empresa, Factura = l.Nº_Factura })
                .ToList();
            if (facturas.Count == 0)
            {
                return new List<ReembolsoDesfasadoDTO>();
            }

            List<string> clientes = envios.Where(e => !string.IsNullOrWhiteSpace(e.Cliente)).Select(e => e.Cliente).Distinct().ToList();
            List<string> documentos = facturas.Select(f => f.Factura).Distinct().ToList();
            string efecto = Constantes.TiposApunteExtracto.EFECTO;
            string efectivo = Constantes.FormasPago.EFECTIVO;
            List<ExtractoCliente> pendientes = clientes.Count == 0 ? new List<ExtractoCliente>() : await db.ExtractosCliente
                .Where(x => clientes.Contains(x.Número) && documentos.Contains(x.Nº_Documento) && x.ImportePdte != 0
                    && x.TipoApunte == efecto && x.FormaPago == efectivo)
                .ToListAsync().ConfigureAwait(false);

            return ReembolsoDesfasadoRepartoPropio.Detectar(envios, facturas, pendientes);
        }
    }

    /// <summary>NestoAPI#810: por qué no se puede cambiar el reembolso de un envío (400 o 409).</summary>
    public class RechazoCambioReembolso
    {
        public HttpStatusCode Estado { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#810: <c>PUT api/EnviosAgencias/{id}/Reembolso</c>. Cambia SOLO el campo Reembolso de un envío del reparto propio sin
    /// entregar, con su fila en EnviosHistoria, y NO toca la contabilidad: el reparto propio no contabiliza nada al tramitar (cobra al
    /// entregar contra la caja del repartidor, #739/#778; la agencia 14 no tiene CuentaReembolsos). Ariadna lo ve al releer las paradas.
    /// </summary>
    public static class CambioReembolsoRepartoPropio
    {
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Null si se puede cambiar; si no, 400 (no es del reparto propio, importe negativo) o 409 (ya entregado o devuelto).</summary>
        public static RechazoCambioReembolso Validar(EnviosAgencia envio, decimal reembolso)
        {
            if (envio == null)
            {
                return new RechazoCambioReembolso { Estado = HttpStatusCode.NotFound, Motivo = "No existe el envío." };
            }
            if (!ReembolsoRepartoPropio.EsAgenciaRepartoPropio(envio.Agencia))
            {
                return new RechazoCambioReembolso
                {
                    Estado = HttpStatusCode.BadRequest,
                    Motivo = $"El envío {envio.Numero} no es del reparto propio: su reembolso se cambia con «Modificar datos», que lo " +
                        "comunica a la agencia y lo contabiliza."
                };
            }
            if (reembolso < 0m)
            {
                return new RechazoCambioReembolso { Estado = HttpStatusCode.BadRequest, Motivo = "El reembolso no puede ser negativo." };
            }
            if (envio.Estado >= Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return new RechazoCambioReembolso
                {
                    Estado = HttpStatusCode.Conflict,
                    Motivo = $"El envío {envio.Numero} ya no está en reparto (estado {envio.Estado}): el reembolso ya se cobró o no se " +
                        "va a cobrar, no se puede cambiar."
                };
            }
            return null;
        }

        /// <summary>
        /// Cambia el reembolso y devuelve la fila de la historia (Campo «Reembolso», ValorAnterior el importe anterior), o null si no
        /// cambia nada. No contabiliza: solo el campo, el usuario y la fecha de pago del reparto propio (#778).
        /// </summary>
        public static EnvioHistoria Aplicar(EnviosAgencia envio, decimal reembolso, string observaciones, string usuario, DateTime ahora)
        {
            decimal nuevo = RoundingHelper.DosDecimalesRound(reembolso);
            decimal anterior = envio.Reembolso;
            if (anterior == nuevo)
            {
                return null;
            }
            string quien = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
            if (quien.Length > 50)
            {
                quien = quien.Substring(0, 50);
            }
            envio.Reembolso = nuevo;
            envio.Usuario = quien;
            ReembolsoRepartoPropio.AplicarFechaPago(envio);

            string texto = string.Format(Espanol, "Reembolso del reparto propio cambiado a mano ({0:N2} € → {1:N2} €).", anterior, nuevo);
            if (!string.IsNullOrWhiteSpace(observaciones))
            {
                texto += " " + observaciones.Trim();
            }
            return new EnvioHistoria
            {
                NumeroEnvio = envio.Numero,
                Campo = ReembolsoEtiquetaPendiente.CAMPO_HISTORIA,
                ValorAnterior = anterior.ToString("0.00", CultureInfo.InvariantCulture),
                Observaciones = texto,
                Usuario = quien,
                FechaModificacion = ahora
            };
        }
    }
}
