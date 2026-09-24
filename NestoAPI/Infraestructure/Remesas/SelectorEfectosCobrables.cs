using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// NestoAPI#332 (diseñado el 21/07/26 junto a #181): selector de efectos cobrables =
    /// NÚCLEO COMÚN (cartera, pendiente, vencido, gating de entrega #172, puerta de neteo)
    /// + ESTRATEGIA por medio de cobro. La estrategia SEPA exige CCC; la de tarjeta (#181)
    /// exigirá FormaPago=TAR + CCC vacío + token activo y CONSUMIRÁ ESTE MISMO selector:
    /// el gating y el neteo no deben reimplementarse por canal.
    /// Este es también el "modo simulación" de #332: devuelve TODO lo que está en juego,
    /// con motivo, sin tocar nada.
    /// </summary>
    public class SelectorEfectosCobrables
    {
        private readonly NVEntities db;
        private readonly Func<string, Task<List<string>>> leerEstadosQueBloquean;
        private readonly GatingEntregaFacturas gatingEntrega;

        public SelectorEfectosCobrables(NVEntities db, Func<string, Task<List<string>>> leerEstadosQueBloquean = null,
            Func<int[]> leerAgenciasConSeguimiento = null)
        {
            this.db = db;
            this.leerEstadosQueBloquean = leerEstadosQueBloquean ?? (e => LeerEstadosQueBloqueanBd(db, e));
            // El gating de entrega (#172) vive en GatingEntregaFacturas para que lo compartan la
            // remesa y el aviso de facturas vencidas (#534). Agencias con seguimiento inyectables.
            gatingEntrega = new GatingEntregaFacturas(db, leerAgenciasConSeguimiento);
        }

        // EstadosExtracto no está en el EDMX (SQL crudo, patrón Cargos). Inyectable para tests.
        // Internal y estático para que el aviso de facturas vencidas (#534) use los MISMOS estados.
        internal static async Task<List<string>> LeerEstadosQueBloqueanBd(NVEntities db, string empresa)
        {
            return await db.Database.SqlQuery<string>(
                "SELECT LTRIM(RTRIM([Número])) FROM EstadosExtracto WHERE Empresa = @p0 AND [BloquearLiquidación] = 1",
                empresa).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Candidatos SEPA (recibo bancario): cartera pendiente y vencida con CCC.
        /// Preseleccionado = entra; con Motivo = retenido/queda fuera; ClienteConNegativos =
        /// requiere pasar por la puerta de revisión (liquidar antes de remesar, #333).
        /// </summary>
        /// <param name="hoy">Ancla temporal (solo para tests; default el día real).</param>
        /// <param name="hasta">NestoAPI#345: límite de VENCIMIENTO incluido (default = hoy).
        /// Permite girar con antelación los efectos del fin de semana/festivos. El filtro de
        /// Fecha del movimiento sigue anclado a hoy: lo facturado hoy queda fuera siempre.</param>
        public async Task<List<EfectoCandidatoDTO>> CandidatosSepa(string empresa, DateTime? hoy = null, DateTime? hasta = null)
        {
            DateTime fechaHoy = (hoy ?? DateTime.Today).Date;
            DateTime fechaHasta = (hasta ?? fechaHoy).Date;
            if (fechaHasta < fechaHoy)
            {
                fechaHasta = fechaHoy;
            }

            // NÚCLEO COMÚN: cartera (TipoApunte 2), algo pendiente, fecha anterior a hoy
            // (margen para lo facturado hoy, criterio Carlos 20/07) y vencimiento dentro del
            // límite. ESTRATEGIA SEPA: con CCC (el char relleno equivale a '' en SQL).
            List<ExtractoCliente> efectos = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa
                    && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.CARTERA
                    && e.ImportePdte != 0
                    && e.CCC != null && e.CCC != ""
                    && e.Fecha < fechaHoy
                    && e.FechaVto != null && e.FechaVto <= fechaHasta
                    && e.ImportePdte > 0)
                .OrderBy(e => e.Número).ThenBy(e => e.FechaVto)
                .ToListAsync().ConfigureAwait(false);

            if (!efectos.Any())
            {
                return new List<EfectoCandidatoDTO>();
            }

            // Estados del extracto que bloquean (matiz de Carlos 21/07): efecto con Estado
            // NULL entra; con Estado informado solo entra si EstadosExtracto no lo bloquea
            // (BloquearLiquidación = 0). Bloqueado = retenido con motivo.
            HashSet<string> estadosBloqueados = new HashSet<string>(
                await leerEstadosQueBloquean(empresa).ConfigureAwait(false),
                StringComparer.OrdinalIgnoreCase);

            // Gating de entrega (#172): ver GatingEntregaFacturas (núcleo compartido con #534).
            Dictionary<string, short> peorEstadoPorFactura = await gatingEntrega
                .PeorEstadoSinEntregarPorFactura(empresa, efectos.Select(e => e.Nº_Documento))
                .ConfigureAwait(false);

            List<string> clientes = efectos.Select(e => e.Número?.Trim()).Distinct().ToList();

            // NestoAPI#381: la ficha bancaria (CCC) de cada efecto, para ADELANTAR la validación
            // del IBAN a la selección. El SP del fichero (prdCrearRemesaIso20022) compone el IBAN
            // como pais+dc_iban+entidad+oficina+dc+cuenta y falla con "no tiene un IBAN correcto"
            // si algún componente es NULL — pero eso salta al GENERAR EL FICHERO, cuando la remesa
            // ya está creada y contabilizada (caso real cliente 14986, 07/08/26). Peor aún: si el
            // registro CCC no existe, el INNER JOIN del SP descarta el efecto EN SILENCIO (iría
            // remesado sin recibo en el fichero). Aquí se retiene con motivo; ValidarSeleccion lo
            // revalida en el POST y el SP conserva su comprobación como última red.
            List<CCC> fichasCcc = await db.CCCs
                .Where(c => c.Empresa == empresa && clientes.Contains(c.Cliente.Trim()))
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, CCC> cccPorClave = fichasCcc
                .GroupBy(c => ClaveCcc(c.Cliente, c.Contacto, c.Número))
                .ToDictionary(g => g.Key, g => g.First());

            // Puerta de neteo (#332): clientes de la selección con movimientos NEGATIVOS
            // pendientes (abonos, pagos a cuenta) de cualquier tipo → revisar/liquidar antes.
            List<string> clientesConNegativos = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && clientes.Contains(e.Número.Trim())
                    && e.ImportePdte < 0)
                .Select(e => e.Número.Trim())
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            HashSet<string> conNegativos = new HashSet<string>(clientesConNegativos, StringComparer.OrdinalIgnoreCase);

            return efectos.Select(e =>
            {
                string documento = e.Nº_Documento?.Trim();
                string cliente = e.Número?.Trim();
                string motivo = null;
                bool forzable = false;

                string estadoEfecto = e.Estado?.Trim();
                if (!string.IsNullOrEmpty(estadoEfecto) && estadosBloqueados.Contains(estadoEfecto))
                {
                    motivo = $"El estado '{estadoEfecto}' del movimiento bloquea la liquidación: no se puede remesar.";
                }
                if (motivo == null)
                {
                    _ = cccPorClave.TryGetValue(ClaveCcc(cliente, e.Contacto, e.CCC), out CCC fichaCcc);
                    motivo = MotivoRetencionIban(fichaCcc, e.CCC?.Trim());
                }
                if (motivo == null && documento != null
                    && peorEstadoPorFactura.TryGetValue(documento, out short peorEstado))
                {
                    motivo = GatingEntregaFacturas.MotivoRetencion(peorEstado);
                    forzable = GatingEntregaFacturas.EsForzable(peorEstado);
                }

                return new EfectoCandidatoDTO
                {
                    Id = e.Nº_Orden,
                    Cliente = cliente,
                    Contacto = e.Contacto?.Trim(),
                    Documento = documento,
                    Efecto = e.Efecto?.Trim(),
                    Fecha = e.Fecha,
                    Vencimiento = e.FechaVto,
                    ImportePendiente = e.ImportePdte,
                    Ccc = e.CCC?.Trim(),
                    ClienteConNegativos = conNegativos.Contains(cliente ?? string.Empty),
                    Preseleccionado = motivo == null,
                    Motivo = motivo,
                    Forzable = forzable
                };
            }).ToList();
        }

        private static string ClaveCcc(string cliente, string contacto, string numero)
            => $"{cliente?.Trim()}|{contacto?.Trim()}|{numero?.Trim()}";

        /// <summary>
        /// NestoAPI#381: motivo de retención de un efecto por su ficha bancaria, o null si el
        /// IBAN está bien. Replica la regla del SP (componentes NULL = IBAN nulo) y añade la
        /// validación mod-97 (un IBAN completo pero mal formado lo rechazaría el banco).
        /// Pura y estática para testear sin BD.
        /// </summary>
        internal static string MotivoRetencionIban(CCC fichaCcc, string codigoCcc)
        {
            if (fichaCcc == null)
            {
                return $"Retenido: el CCC '{codigoCcc}' del efecto no existe en la ficha bancaria " +
                    "del cliente — el fichero SEPA lo descartaría en silencio (#381).";
            }
            // NestoAPI#502: la ficha de baja se gira igual que cualquier otra (ni el selector ni el
            // SP miraban el Estado), y si se dio de baja porque el cliente revocó el mandato o
            // cerró la cuenta, el recibo vuelve y la reclamación SEPA puede llegar 13 meses
            // después. NO es forzable a propósito (Carlos, 21/09/26): quien quiera girar ahí que
            // le cambie antes el estado a la ficha.
            if (fichaCcc.Estado < 0)
            {
                return $"Retenido: la ficha bancaria (CCC '{codigoCcc}') está DE BAJA — cámbiele el " +
                    "estado antes de remesar o gire por otra cuenta (#502).";
            }
            if (fichaCcc.Pais == null || fichaCcc.DC_IBAN == null || fichaCcc.Entidad == null
                || fichaCcc.Oficina == null || fichaCcc.DC == null || fichaCcc.Nº_Cuenta == null)
            {
                return "Retenido: el IBAN de la ficha bancaria está incompleto — el fichero SEPA " +
                    "fallaría al generarse; corregir la ficha antes de remesar (#381).";
            }
            string compuesto = Iban.ComponerIban(fichaCcc).Trim();
            try
            {
                if (!string.IsNullOrWhiteSpace(compuesto) && new Iban(compuesto).EsValido)
                {
                    return null;
                }
            }
            catch
            {
                // El propio constructor de Iban lanza con IBANes rotos: mismo tratamiento.
            }
            return $"Retenido: el IBAN '{compuesto}' de la ficha bancaria no es válido — " +
                "corregirlo antes de remesar (#381).";
        }
    }
}
