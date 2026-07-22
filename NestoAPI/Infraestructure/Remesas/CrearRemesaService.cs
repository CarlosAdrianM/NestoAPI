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
            // Bug 22/07/26: el cliente manda la empresa con el padding del char(3) ("1  ") y las
            // comparaciones C# exactas aguas abajo (prdCopiarCliente en CrearLineas) se rompían.
            // Normalizar aquí: en SQL el char(3) rellena solo.
            peticion.Empresa = peticion.Empresa.Trim();
            peticion.Banco = peticion.Banco.Trim();
            List<int> idsPedidos = peticion.Efectos.Distinct().ToList();

            await CandadoCrearRemesa.WaitAsync().ConfigureAwait(false);
            try
            {
                // Reintento por deadlock de la operación COMPLETA (mismo criterio que #273):
                // cada intento revalida y abre transacción NUEVA — por eso el retry va POR
                // FUERA de la transacción y nunca al revés.
                return await ContabilidadService.ReintentarSiDeadlock(
                    () => CrearRemesaUnaVez(peticion, idsPedidos, usuario)).ConfigureAwait(false);
            }
            finally
            {
                _ = CandadoCrearRemesa.Release();
            }
        }

        // PATRÓN REUTILIZABLE "alta + contabilización atómicas" (petición de Carlos 21/07,
        // lo necesitaremos en más sitios): TODO sobre el MISMO NVEntities y UNA transacción
        // local — el alta propia por SQL crudo y la contabilización por la sobrecarga
        // CrearLineasYContabilizarDiario(lineas, db), que NO abre conexión ni transacción
        // propias (está documentada para entrar ya en transacción). Nada de TransactionScope
        // ambiente: con dos conexiones escala a MSDTC y rompe el retry por deadlock.
        private async Task<CrearRemesaResponse> CrearRemesaUnaVez(CrearRemesaRequest peticion,
            List<int> idsPedidos, string usuario)
        {
            // Revalidación FRESCA server-side (lección Nesto#397), también en cada reintento:
            // el selector aplica el núcleo completo (cartera, vencida, CCC, gating #172, neteo).
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
            // NestoAPI#345: la fecha de cargo nunca puede ser anterior a hoy
            DateTime fechaCargo = FechaCargoEfectiva(peticion.FechaCargo);

            db.Database.CommandTimeout = 120; // margen para diarios con contención (#322)
            using (System.Data.Entity.DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    // Contador DENTRO de la transacción: sin huecos si algo falla (rollback lo
                    // devuelve). Coste asumido: el lock de la fila de ContadoresGlobales se
                    // retiene hasta el commit (~1-2 s una vez al día; vigilar si molestara a
                    // la creación de pedidos, que usa la misma fila).
                    int numeroRemesa = (await db.Database.SqlQuery<int>(
                        "UPDATE ContadoresGlobales SET Remesas = Remesas + 1 OUTPUT inserted.Remesas")
                        .ToListAsync().ConfigureAwait(false)).Single();
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "INSERT INTO Remesas (Empresa, [Número], Fecha, Importe, Banco) VALUES (@p0, @p1, GETDATE(), @p2, @p3)",
                        new SqlParameter("@p0", peticion.Empresa),
                        new SqlParameter("@p1", numeroRemesa),
                        new SqlParameter("@p2", importeTotal),
                        new SqlParameter("@p3", peticion.Banco)).ConfigureAwait(false);

                    List<PreContabilidad> lineas = ConstruirLineasRemesa(numeroRemesa, peticion.Empresa, banco, efectos, usuario,
                        peticion.RespetarVencimientos, fechaCargo);
                    int resultado = await contabilidad.CrearLineasYContabilizarDiario(lineas, db).ConfigureAwait(false);
                    if (resultado <= 0)
                    {
                        transaccion.Rollback();
                        throw new InvalidOperationException(
                            $"La contabilización de la remesa {numeroRemesa} no devolvió éxito: se ha deshecho todo.");
                    }

                    transaccion.Commit();
                    return new CrearRemesaResponse
                    {
                        NumeroRemesa = numeroRemesa,
                        Importe = importeTotal,
                        NumeroEfectos = efectos.Count,
                        ResultadoContabilizacion = resultado
                    };
                }
                catch (InvalidOperationException)
                {
                    throw; // ya se hizo rollback (o lo hará el using al no haber commit)
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch
                    {
                        // La transacción pudo morir con el propio error (deadlock): el using la limpia.
                    }
                    // Bug 22/07/26: este wrap convierte CUALQUIER error en InvalidOperationException
                    // y el controller lo devuelve como BadRequest — los errores de negocio no deben
                    // ensuciar ELMAH, pero un error INESPERADO (SQL, EF...) quedaba invisible.
                    // Loguearlo aquí antes de envolverlo.
                    ElmahHelper.Log(new Exception(
                        $"CrearRemesa: error inesperado creando la remesa (empresa {peticion.Empresa}, " +
                        $"banco {peticion.Banco}, {idsPedidos.Count} efectos): {ex.Message}", ex));
                    throw new InvalidOperationException(
                        $"No se pudo crear la remesa (no se ha guardado nada): {ex.Message}", ex);
                }
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

        /// <summary>NestoAPI#345: la fecha de cargo nunca puede ser anterior a hoy.</summary>
        internal static DateTime FechaCargoEfectiva(DateTime? fechaCargo)
        {
            DateTime fecha = (fechaCargo ?? DateTime.Today).Date;
            return fecha < DateTime.Today ? DateTime.Today : fecha;
        }

        /// <summary>NestoAPI#345: el vencimiento con el que va el efecto a la remesa — el suyo
        /// original si es posterior al suelo (la fecha de cargo), y el suelo si es anterior
        /// (un vencimiento pasado se cobra ya) o no tiene.</summary>
        internal static DateTime VencimientoEfectivo(DateTime? vencimiento, DateTime suelo)
        {
            return vencimiento.HasValue && vencimiento.Value.Date > suelo ? vencimiento.Value.Date : suelo;
        }

        /// <summary>
        /// Las líneas del diario _REMESA, calcadas del asiento real 1195101 (remesa 10898):
        /// HABER una línea de CLIENTE por efecto ("Pago Factura {doc}  {efecto}") con
        /// Liquidado (la cartera que salda) y Nº_Remesa (que prdContabilizar copia al pago y
        /// prdLiquidar propaga a la cartera), y DEBE el banco. NestoAPI#345: en modo
        /// "respetar vencimientos" cada efecto conserva su fecha (con suelo en fechaCargo) y
        /// hay UNA línea de banco POR FECHA de cargo, con su total y su fecha — el banco hará
        /// un apunte en cuenta por cada fecha. En modo forzado (default) todo va a fechaCargo
        /// y el banco es una sola línea, como siempre. Pura y estática.
        /// </summary>
        internal static List<PreContabilidad> ConstruirLineasRemesa(int numeroRemesa, string empresa,
            Banco banco, List<ExtractoCliente> efectos, string usuario,
            bool respetarVencimientos = false, DateTime? fechaCargo = null)
        {
            var lineas = new List<PreContabilidad>();
            string cuentaBanco = banco.Cuenta_Contable?.Trim();
            DateTime suelo = FechaCargoEfectiva(fechaCargo);

            var totalesPorFecha = new SortedDictionary<DateTime, decimal>();
            foreach (ExtractoCliente efecto in efectos)
            {
                DateTime fechaEfecto = respetarVencimientos
                    ? VencimientoEfectivo(efecto.FechaVto, suelo)
                    : suelo;
                totalesPorFecha[fechaEfecto] = (totalesPorFecha.TryGetValue(fechaEfecto, out decimal acumulado)
                    ? acumulado : 0) + efecto.ImportePdte;

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
                    Fecha = fechaEfecto,
                    FechaVto = fechaEfecto,
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

            // Un apunte de banco por fecha de cargo (una sola en modo forzado): el banco hará
            // un movimiento en cuenta por cada fecha, y así cuadra apunte a apunte.
            string conceptoBanco = $"Remesa:{numeroRemesa}. Al Banco: {cuentaBanco}";
            foreach (KeyValuePair<DateTime, decimal> grupo in totalesPorFecha)
            {
                lineas.Add(new PreContabilidad
                {
                    Empresa = empresa,
                    Nº_Cuenta = cuentaBanco,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                    // TipoApunte es NOT NULL: sin él, DbEntityValidationException al guardar
                    // (bug 22/07). "1" = lo que lleva la línea de banco del asiento real 10898.
                    TipoApunte = Constantes.TiposExtractoCliente.FACTURA,
                    Debe = grupo.Value,
                    Concepto = conceptoBanco.Length > 50 ? conceptoBanco.Substring(0, 50) : conceptoBanco,
                    Nº_Documento = numeroRemesa.ToString(),
                    Diario = DIARIO_REMESA,
                    Fecha = grupo.Key,
                    FechaVto = grupo.Key,
                    Asiento = 1,
                    Asiento_Automático = true,
                    Nº_Remesa = numeroRemesa.ToString(),
                    Origen = empresa,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }

            return lineas;
        }
    }

    public class CrearRemesaRequest
    {
        public string Empresa { get; set; }
        public string Banco { get; set; }
        public List<int> Efectos { get; set; }

        /// <summary>
        /// NestoAPI#345: true = cada efecto conserva su vencimiento original (con suelo en
        /// FechaCargo/hoy) y el asiento lleva un apunte de banco POR FECHA; false (default,
        /// comportamiento de siempre) = todos los efectos se fuerzan a FechaCargo.
        /// </summary>
        public bool RespetarVencimientos { get; set; }

        /// <summary>
        /// NestoAPI#345: fecha de cargo (default hoy). En modo forzado es LA fecha de todos
        /// los efectos; en modo respetar es el SUELO (ningún vencimiento puede ser anterior).
        /// Nunca puede ser anterior a hoy: si lo es, se usa hoy.
        /// </summary>
        public DateTime? FechaCargo { get; set; }
    }

    public class CrearRemesaResponse
    {
        public int NumeroRemesa { get; set; }
        public decimal Importe { get; set; }
        public int NumeroEfectos { get; set; }
        public int ResultadoContabilizacion { get; set; }
    }
}
