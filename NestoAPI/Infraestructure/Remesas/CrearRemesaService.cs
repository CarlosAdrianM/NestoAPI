using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// NestoAPI#332 (slices 2-3): crear la remesa de cobros SEPA. Diseño verificado contra la
    /// remesa real 10898 del 20/07/26 y ajustado a la regla de la casa (Carlos 21/07):
    /// NUNCA se escribe directamente en Contabilidad ni en ExtractoCliente — se componen
    /// líneas de PreContabilidad (diario _REMESA) y se contabiliza por el único call site
    /// (ContabilidadService → prdContabilizar). El pago nace con Nº_Remesa y Liquidado:
    /// prdContabilizar crea el apunte de pago en el extracto, prdLiquidar deja la cartera a
    /// pendiente 0 y PROPAGA el nº de remesa a la cartera — sin ningún UPDATE nuestro.
    /// prdCrearRemesaIso20022 (el fichero SEPA) lee después los pagos por Remesa+TipoApunte=3.
    /// </summary>
    public class CrearRemesaService
    {
        public const string DIARIO_REMESA = "_REMESA";

        // La API corre en un único servidor: el semáforo serializa la creación de remesas
        // (una doble pulsación concurrente pasaría dos veces la validación y contabilizaría
        // dos veces; los raiserror internos de prdLiquidar son de severidad 1 = silenciosos).
        // La numeración además es atómica por el UPDATE...OUTPUT del contador.
        private static readonly SemaphoreSlim CandadoCrearRemesa = new SemaphoreSlim(1, 1);

        private readonly NVEntities db;
        private readonly IContabilidadService contabilidad;

        public CrearRemesaService(NVEntities db, IContabilidadService contabilidad = null)
        {
            this.db = db;
            this.contabilidad = contabilidad ?? new ContabilidadService();
        }

        public async Task<CrearRemesaResponse> CrearRemesa(CrearRemesaRequest peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Empresa)
                || string.IsNullOrWhiteSpace(peticion.Banco)
                || peticion.Efectos == null || !peticion.Efectos.Any())
            {
                throw new ArgumentException("Hay que indicar empresa, banco y al menos un efecto.");
            }
            List<int> idsPedidos = peticion.Efectos.Distinct().ToList();

            await CandadoCrearRemesa.WaitAsync().ConfigureAwait(false);
            try
            {
                // Revalidación FRESCA server-side (lección Nesto#397): el selector aplica el
                // núcleo completo (cartera, pendiente, vencida, CCC, gating #172 y neteo).
                List<EfectoCandidatoDTO> candidatos = await new SelectorEfectosCobrables(db)
                    .CandidatosSepa(peticion.Empresa).ConfigureAwait(false);
                List<string> errores = ValidarSeleccion(idsPedidos, candidatos);
                if (errores.Any())
                {
                    throw new InvalidOperationException(string.Join(" ", errores));
                }

                Banco banco = await db.Bancos.FirstOrDefaultAsync(b =>
                    b.Empresa == peticion.Empresa && b.Número == peticion.Banco).ConfigureAwait(false);
                if (banco == null || string.IsNullOrWhiteSpace(banco.Cuenta_Contable))
                {
                    throw new InvalidOperationException(
                        $"El banco '{peticion.Banco?.Trim()}' no existe o no tiene cuenta contable.");
                }

                List<ExtractoCliente> efectos = await db.ExtractosCliente
                    .Where(e => e.Empresa == peticion.Empresa && idsPedidos.Contains(e.Nº_Orden))
                    .ToListAsync().ConfigureAwait(false);
                decimal importeTotal = efectos.Sum(e => e.ImportePdte);

                // Numeración atómica (la fila de ContadoresGlobales es única) y alta de la
                // remesa. La tabla Remesas no está en el EDMX (SQL crudo, patrón slice 2).
                int numeroRemesa = (await db.Database.SqlQuery<int>(
                    "UPDATE ContadoresGlobales SET Remesas = Remesas + 1 OUTPUT inserted.Remesas")
                    .ToListAsync().ConfigureAwait(false)).Single();
                _ = await db.Database.ExecuteSqlCommandAsync(
                    "INSERT INTO Remesas (Empresa, [Número], Fecha, Importe, Banco) VALUES (@p0, @p1, GETDATE(), @p2, @p3)",
                    new SqlParameter("@p0", peticion.Empresa),
                    new SqlParameter("@p1", numeroRemesa),
                    new SqlParameter("@p2", importeTotal),
                    new SqlParameter("@p3", peticion.Banco)).ConfigureAwait(false);

                List<PreContabilidad> lineas = ConstruirLineasRemesa(numeroRemesa, peticion.Empresa, banco, efectos, usuario);
                int resultado;
                try
                {
                    resultado = await contabilidad.CrearLineasYContabilizarDiario(lineas).ConfigureAwait(false);
                }
                catch (Exception exContabilizar)
                {
                    // Compensación: la contabilización falló ENTERA (transacción única con
                    // rollback), así que el extracto está intacto; solo hay que quitar la
                    // cabecera de la remesa. El hueco del contador es aceptable.
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "DELETE FROM Remesas WHERE Empresa = @p0 AND [Número] = @p1",
                        new SqlParameter("@p0", peticion.Empresa),
                        new SqlParameter("@p1", numeroRemesa)).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"No se pudo contabilizar la remesa {numeroRemesa} (se ha deshecho el alta): {exContabilizar.Message}",
                        exContabilizar);
                }

                return new CrearRemesaResponse
                {
                    NumeroRemesa = numeroRemesa,
                    Importe = importeTotal,
                    NumeroEfectos = efectos.Count,
                    ResultadoContabilizacion = resultado
                };
            }
            finally
            {
                _ = CandadoCrearRemesa.Release();
            }
        }

        /// <summary>
        /// La selección del usuario contra los candidatos FRESCOS: todo lo pedido debe seguir
        /// siendo candidato, no retenido por el gating (#172) y sin la puerta de neteo
        /// pendiente (clientes con negativos: liquidar con #333 o sacar de la remesa).
        /// Pura y estática para testear sin BD.
        /// </summary>
        internal static List<string> ValidarSeleccion(List<int> idsPedidos, List<EfectoCandidatoDTO> candidatos)
        {
            var errores = new List<string>();
            Dictionary<int, EfectoCandidatoDTO> porId = candidatos.ToDictionary(c => c.Id);
            foreach (int id in idsPedidos)
            {
                if (!porId.TryGetValue(id, out EfectoCandidatoDTO candidato))
                {
                    errores.Add($"El efecto {id} ya no es candidato a remesa (cobrado, remesado o modificado): refresque la pantalla.");
                    continue;
                }
                if (!candidato.Preseleccionado)
                {
                    errores.Add($"El efecto {id} está retenido: {candidato.Motivo}");
                }
            }
            List<string> clientesConNegativos = idsPedidos
                .Where(porId.ContainsKey)
                .Select(id => porId[id])
                .Where(c => c.ClienteConNegativos)
                .Select(c => c.Cliente)
                .Distinct()
                .ToList();
            if (clientesConNegativos.Any())
            {
                errores.Add("Estos clientes tienen movimientos negativos pendientes de revisar (liquidar " +
                    $"o sacar de la remesa): {string.Join(", ", clientesConNegativos)}.");
            }
            return errores;
        }

        /// <summary>
        /// Las líneas del diario _REMESA, calcadas del asiento real 1195101 (remesa 10898):
        /// HABER una línea de CLIENTE por efecto ("Pago Factura {doc}  {efecto}") con
        /// Liquidado (la cartera que salda) y Nº_Remesa (que prdContabilizar copia al pago y
        /// prdLiquidar propaga a la cartera), y DEBE el banco por el total. Pura y estática.
        /// </summary>
        internal static List<PreContabilidad> ConstruirLineasRemesa(int numeroRemesa, string empresa,
            Banco banco, List<ExtractoCliente> efectos, string usuario)
        {
            var lineas = new List<PreContabilidad>();
            string cuentaBanco = banco.Cuenta_Contable?.Trim();

            foreach (ExtractoCliente efecto in efectos)
            {
                string documento = efecto.Nº_Documento?.Trim();
                string concepto = $"Pago Factura {documento}  {efecto.Efecto?.Trim()}".TrimEnd();
                if (concepto.Length > 50)
                {
                    concepto = concepto.Substring(0, 50);
                }
                lineas.Add(new PreContabilidad
                {
                    Empresa = empresa,
                    Nº_Cuenta = efecto.Número?.Trim(),
                    Contacto = efecto.Contacto?.Trim() ?? "0",
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    TipoApunte = Constantes.TiposExtractoCliente.PAGO,
                    Haber = efecto.ImportePdte,
                    Concepto = concepto,
                    Nº_Documento = documento,
                    Efecto = efecto.Efecto?.Trim(),
                    Diario = DIARIO_REMESA,
                    Fecha = DateTime.Today,
                    FechaVto = DateTime.Today,
                    Asiento = 1,
                    Asiento_Automático = true,
                    Delegación = efecto.Delegación?.Trim(),
                    FormaVenta = efecto.FormaVenta?.Trim(),
                    FormaPago = efecto.FormaPago?.Trim(),
                    Vendedor = efecto.Vendedor?.Trim(),
                    CCC = efecto.CCC?.Trim(),
                    Liquidado = efecto.Nº_Orden,
                    Nº_Remesa = numeroRemesa.ToString(),
                    Origen = empresa,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }

            string conceptoBanco = $"Remesa:{numeroRemesa}. Al Banco: {cuentaBanco}";
            lineas.Add(new PreContabilidad
            {
                Empresa = empresa,
                Nº_Cuenta = cuentaBanco,
                TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                Debe = efectos.Sum(e => e.ImportePdte),
                Concepto = conceptoBanco.Length > 50 ? conceptoBanco.Substring(0, 50) : conceptoBanco,
                Nº_Documento = numeroRemesa.ToString(),
                Diario = DIARIO_REMESA,
                Fecha = DateTime.Today,
                FechaVto = DateTime.Today,
                Asiento = 1,
                Asiento_Automático = true,
                Nº_Remesa = numeroRemesa.ToString(),
                Origen = empresa,
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now
            });

            return lineas;
        }
    }

    public class CrearRemesaRequest
    {
        public string Empresa { get; set; }
        public string Banco { get; set; }
        public List<int> Efectos { get; set; }
    }

    public class CrearRemesaResponse
    {
        public int NumeroRemesa { get; set; }
        public decimal Importe { get; set; }
        public int NumeroEfectos { get; set; }
        public int ResultadoContabilizacion { get; set; }
    }
}
