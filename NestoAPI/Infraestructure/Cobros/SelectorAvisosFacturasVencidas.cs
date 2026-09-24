using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>
    /// NestoAPI#534: criterio del aviso automático de facturas vencidas por transferencia
    /// (Carlos y Laura, 24/09/26). Entra un efecto si:
    /// - es cartera (TipoApunte 2) con importe pendiente positivo,
    /// - su forma de pago es transferencia (TRN),
    /// - su factura NO es de prepago ni de contado (la «transferencia inmediata»): ahí el correo
    ///   podría llegar antes que el pedido,
    /// - venció hace <c>diasUmbral</c> días o más (5 por defecto: margen para quien paga en el
    ///   cajero en vez de por la aplicación),
    /// - y vence el 01/07/2026 o después (lo anterior lo sigue administración a mano).
    ///
    /// De los que entran, se quedan fuera CON MOTIVO (para que en el modo sombra se vea por qué):
    /// los que no tienen factura, los que tienen un estado del extracto que bloquea la liquidación
    /// (retenido, abogado, rehusado...: los mismos que la remesa), los de clientes con cobros o
    /// abonos pendientes de liquidar (puede que ya hayan pagado), los que la agencia aún no ha
    /// entregado (el MISMO gating que la remesa, <see cref="GatingEntregaFacturas"/>) y los que no
    /// tienen correo al que escribir.
    ///
    /// Solo lee: no manda nada ni registra nada. El «una vez por efecto» llegará en el corte 2.
    /// </summary>
    public class SelectorAvisosFacturasVencidas
    {
        public const int DIAS_UMBRAL_POR_DEFECTO = 5;

        /// <summary>Solo se avisan vencimientos desde esta fecha; lo anterior va a mano.</summary>
        public static readonly DateTime FECHA_CORTE_VENCIMIENTOS = new DateTime(2026, 7, 1);

        /// <summary>
        /// Plazos de pago de la factura que NO se avisan aunque la forma de pago sea transferencia:
        /// prepago y contado (vencen el mismo día de la factura, así que el aviso podría adelantarse
        /// al pedido).
        /// </summary>
        public static readonly string[] PLAZOS_EXCLUIDOS =
        {
            Constantes.PlazosPago.PREPAGO,
            Constantes.PlazosPago.CONTADO,
            Constantes.PlazosPago.CONTADO_RIGUROSO
        };

        public const string MOTIVO_SIN_FACTURA = "No se avisa: el efecto no tiene una factura asociada.";
        public const string MOTIVO_SIN_CORREO = "No se avisa: la ficha no tiene correo de cobros ni de facturación.";
        public const string MOTIVO_CLIENTE_CON_NEGATIVOS = "No se avisa: el cliente tiene cobros o abonos pendientes de liquidar (puede que ya haya pagado).";

        private readonly NVEntities db;
        private readonly Func<string, Task<List<string>>> leerEstadosQueBloquean;
        private readonly GatingEntregaFacturas gatingEntrega;

        public SelectorAvisosFacturasVencidas(NVEntities db, Func<string, Task<List<string>>> leerEstadosQueBloquean = null,
            Func<int[]> leerAgenciasConSeguimiento = null)
        {
            this.db = db;
            this.leerEstadosQueBloquean = leerEstadosQueBloquean
                ?? (e => SelectorEfectosCobrables.LeerEstadosQueBloqueanBd(db, e));
            gatingEntrega = new GatingEntregaFacturas(db, leerAgenciasConSeguimiento);
        }

        /// <summary>
        /// Todos los efectos que cumplen el criterio, con <c>Motivo</c> null si se avisarían.
        /// </summary>
        /// <param name="hoy">Ancla temporal (solo para tests; por defecto el día real).</param>
        public async Task<List<AvisoFacturaVencidaDTO>> Candidatos(string empresa, int diasUmbral, DateTime? hoy = null)
        {
            DateTime fechaHoy = (hoy ?? DateTime.Today).Date;
            if (diasUmbral < 1)
            {
                diasUmbral = DIAS_UMBRAL_POR_DEFECTO;
            }
            DateTime vencidoComoTarde = fechaHoy.AddDays(-diasUmbral);
            DateTime corte = FECHA_CORTE_VENCIMIENTOS;
            string transferencia = Constantes.FormasPago.TRANSFERENCIA;
            string cartera = Constantes.ExtractosCliente.TiposApunte.CARTERA;

            List<ExtractoCliente> efectos = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa
                    && e.TipoApunte == cartera
                    && e.ImportePdte > 0
                    && e.FormaPago == transferencia
                    && e.FechaVto != null
                    && e.FechaVto >= corte
                    && e.FechaVto <= vencidoComoTarde)
                .ToListAsync().ConfigureAwait(false);
            if (!efectos.Any())
            {
                return new List<AvisoFacturaVencidaDTO>();
            }

            // Factura de cada efecto: su fecha y sus plazos (para quitar prepago y contado)
            List<string> documentos = efectos.Select(e => e.Nº_Documento?.Trim())
                .Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
            var facturas = await db.CabsFacturasVtas
                .Where(f => f.Empresa == empresa && documentos.Contains(f.Número))
                .Select(f => new { f.Número, f.Fecha, f.PlazosPago })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, (DateTime Fecha, string Plazos)> facturaPorNumero = facturas
                .GroupBy(f => f.Número.Trim())
                .ToDictionary(g => g.Key, g => (g.First().Fecha, g.First().PlazosPago?.Trim()));
            HashSet<string> plazosExcluidos = new HashSet<string>(PLAZOS_EXCLUIDOS, StringComparer.OrdinalIgnoreCase);
            efectos = efectos
                .Where(e => !(facturaPorNumero.TryGetValue(e.Nº_Documento?.Trim() ?? string.Empty, out var fra)
                    && fra.Plazos != null && plazosExcluidos.Contains(fra.Plazos)))
                .ToList();
            if (!efectos.Any())
            {
                return new List<AvisoFacturaVencidaDTO>();
            }

            HashSet<string> estadosBloqueados = new HashSet<string>(
                await leerEstadosQueBloquean(empresa).ConfigureAwait(false) ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, short> peorEstadoPorFactura = await gatingEntrega
                .PeorEstadoSinEntregarPorFactura(empresa, documentos)
                .ConfigureAwait(false);

            List<string> clientes = efectos.Select(e => e.Número?.Trim()).Distinct().ToList();
            var fichas = await db.Clientes
                .Where(c => c.Empresa == empresa && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Nombre })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, string> nombrePorClave = fichas
                .GroupBy(c => Clave(c.Nº_Cliente, c.Contacto))
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());

            List<PersonaContactoCliente> personas = await db.PersonasContactoClientes
                .Where(p => p.Empresa == empresa && clientes.Contains(p.NºCliente) && p.CorreoElectrónico != null)
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, List<PersonaContactoCliente>> personasPorClave = personas
                .GroupBy(p => Clave(p.NºCliente, p.Contacto))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Mismo criterio que la «puerta de neteo» de la remesa (#332): si hay algo negativo
            // pendiente (un cobro a cuenta sin liquidar, un abono), puede que ya haya pagado.
            List<string> conNegativosLista = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && clientes.Contains(e.Número) && e.ImportePdte < 0)
                .Select(e => e.Número)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            HashSet<string> conNegativos = new HashSet<string>(
                conNegativosLista.Select(c => c?.Trim()), StringComparer.OrdinalIgnoreCase);

            return efectos.Select(e =>
            {
                string documento = e.Nº_Documento?.Trim();
                string cliente = e.Número?.Trim();
                string clave = Clave(cliente, e.Contacto);
                bool tieneFactura = documento != null && facturaPorNumero.ContainsKey(documento);
                string destinatarios = ResolverDestinatarios(
                    personasPorClave.TryGetValue(clave, out List<PersonaContactoCliente> ps) ? ps : null);

                string motivo = null;
                string estadoEfecto = e.Estado?.Trim();
                if (!tieneFactura)
                {
                    motivo = MOTIVO_SIN_FACTURA;
                }
                else if (!string.IsNullOrEmpty(estadoEfecto) && estadosBloqueados.Contains(estadoEfecto))
                {
                    motivo = $"No se avisa: el estado '{estadoEfecto}' del movimiento bloquea la liquidación.";
                }
                else if (conNegativos.Contains(cliente ?? string.Empty))
                {
                    motivo = MOTIVO_CLIENTE_CON_NEGATIVOS;
                }
                else if (peorEstadoPorFactura.TryGetValue(documento, out short peorEstado))
                {
                    motivo = "No se avisa. " + GatingEntregaFacturas.MotivoRetencion(peorEstado);
                }
                else if (string.IsNullOrEmpty(destinatarios))
                {
                    motivo = MOTIVO_SIN_CORREO;
                }

                return new AvisoFacturaVencidaDTO
                {
                    NOrden = e.Nº_Orden,
                    Cliente = cliente,
                    Contacto = e.Contacto?.Trim(),
                    Nombre = nombrePorClave.TryGetValue(clave, out string nombre) ? nombre : null,
                    Factura = documento,
                    Efecto = e.Efecto?.Trim(),
                    FechaFactura = tieneFactura ? facturaPorNumero[documento].Fecha : (DateTime?)null,
                    Vencimiento = e.FechaVto.Value.Date,
                    Importe = e.ImportePdte,
                    DiasVencida = (fechaHoy - e.FechaVto.Value.Date).Days,
                    Destinatarios = destinatarios,
                    Motivo = motivo
                };
            })
            .OrderBy(a => a.Motivo != null)
            .ThenBy(a => a.Cliente)
            .ThenBy(a => a.Vencimiento)
            .ToList();
        }

        /// <summary>
        /// Correos a los que va el aviso: las personas de contacto con cargo Cobros; si no hay
        /// ninguna, las de Factura por correo (a las que ya va la factura). Solo las activas y con
        /// correo. Vacío si no hay: no se escribe a una persona cualquiera de la ficha.
        /// </summary>
        public static string ResolverDestinatarios(IEnumerable<PersonaContactoCliente> personas)
        {
            List<PersonaContactoCliente> conCorreo = (personas ?? Enumerable.Empty<PersonaContactoCliente>())
                .Where(p => !string.IsNullOrWhiteSpace(p.CorreoElectrónico)
                    && p.Estado >= Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO)
                .ToList();
            List<PersonaContactoCliente> elegidas = conCorreo
                .Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_COBROS).ToList();
            if (!elegidas.Any())
            {
                elegidas = conCorreo
                    .Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO).ToList();
            }
            return string.Join(", ", elegidas
                .Select(p => p.CorreoElectrónico.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string Clave(string cliente, string contacto) => $"{cliente?.Trim()}|{contacto?.Trim()}";
    }
}
