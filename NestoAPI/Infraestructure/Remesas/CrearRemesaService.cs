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
            // NestoAPI#345: con la MISMA fecha "hasta" con la que el usuario cargó la pantalla —
            // si no, los efectos del fin de semana pasarían la selección pero el POST los
            // rechazaría con "ya no es candidato".
            List<EfectoCandidatoDTO> candidatos = await new SelectorEfectosCobrables(db)
                .CandidatosSepa(peticion.Empresa, hasta: peticion.SeleccionHasta).ConfigureAwait(false);
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
            // NestoAPI#345: en modo RESPETAR el suelo de AGRUPACIÓN es HOY (los vencidos se cobran
            // ya y los futuros conservan su fecha; la FechaCargo del request no pinta nada ahí).
            // En modo forzado, la fecha única elegida — nunca anterior a hoy.
            DateTime fechaCargo = peticion.RespetarVencimientos
                ? DateTime.Today
                : FechaCargoEfectiva(peticion.FechaCargo);
            // NestoAPI#345 (ajuste 24/07/26 tras ver el extracto de La Caixa de la remesa 10901):
            // el banco NUNCA valora un recibo el mismo día de la presentación (SEPA D-1). Los
            // recibos con cargo hoy los abona el SIGUIENTE día laborable, en un apunte SEPARADO
            // (no los funde con los del día siguiente). Por eso la FECHA CONTABLE del apunte tiene
            // como suelo la próxima fecha de cargo (hoy+1 laborable), aunque la agrupación y la
            // ReqdColltnDt del fichero sigan por el vencimiento solicitado. En modo forzado no
            // aplica: la fecha única la elige el usuario.
            DateTime? fechaValorMinima = peticion.RespetarVencimientos
                ? ProximaFechaCargo(DateTime.Today, 1,
                    f => Models.RecursosHumanos.GestorFestivos.EsFestivo(f, Constantes.Almacenes.ALGETE))
                : (DateTime?)null;

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
                        peticion.RespetarVencimientos, fechaCargo, fechaValorMinima);
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

        /// <summary>
        /// NestoAPI#345: fecha "hasta" propuesta para cargar candidatos = hoy + N días de
        /// antelación (parámetro de usuario DiasAntelacionRemesa, default 1), saltando al
        /// siguiente día laborable si cae en fin de semana o festivo. Ejemplos con antelación 1:
        /// jueves→viernes, viernes→lunes, víspera de festivo→siguiente laborable. Pura.
        /// </summary>
        internal static DateTime ProximaFechaCargo(DateTime hoy, int diasAntelacion, Func<DateTime, bool> esNoLaborable)
        {
            DateTime fecha = hoy.Date.AddDays(diasAntelacion < 0 ? 0 : diasAntelacion);
            while (esNoLaborable(fecha))
            {
                fecha = fecha.AddDays(1);
            }
            return fecha;
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
        /// "respetar vencimientos" cada efecto va a la remesa por su fecha de cargo SOLICITADA
        /// (su vencimiento, con suelo hoy) y esa fecha forma un ASIENTO PROPIO (efectos + su
        /// línea de banco) — es la que agrupa las PmtInf del fichero (ReqdColltnDt sale de
        /// FechaVto), así que el banco hace un abono por fecha solicitada. La FECHA CONTABLE
        /// (Fecha), en cambio, es el DÍA DE VALOR: como el banco no cobra el mismo día de la
        /// presentación (SEPA D-1), el grupo de hoy se contabiliza el siguiente laborable
        /// (fechaValorMinima), en un apunte SEPARADO — dos asientos pueden compartir día de
        /// valor sin fundirse porque prdContabilizar numera por el CAMBIO de asiento provisional,
        /// no por la fecha. En modo forzado (default) todo va a fechaCargo en un único asiento.
        /// Pura y estática.
        /// </summary>
        internal static List<PreContabilidad> ConstruirLineasRemesa(int numeroRemesa, string empresa,
            Banco banco, List<ExtractoCliente> efectos, string usuario,
            bool respetarVencimientos = false, DateTime? fechaCargo = null, DateTime? fechaValorMinima = null)
        {
            var lineas = new List<PreContabilidad>();
            string cuentaBanco = banco.Cuenta_Contable?.Trim();
            DateTime suelo = FechaCargoEfectiva(fechaCargo);
            // Suelo de la FECHA CONTABLE (día de valor del banco): la próxima fecha de cargo si
            // se ha calculado (modo respetar), o el propio suelo de agrupación si no (forzado/tests).
            DateTime sueloValor = fechaValorMinima.HasValue && fechaValorMinima.Value.Date > suelo
                ? fechaValorMinima.Value.Date : suelo;

            // AGRUPACIÓN por la fecha de cargo SOLICITADA (vencimiento con suelo hoy): los recibos
            // del día quedan en un asiento distinto de los de mañana, y el fichero SEPA (que agrupa
            // las PmtInf por FechaVto) genera un abono por cada fecha solicitada. prdContabilizar
            // exige UNA fecha por asiento ("El Asiento 1 tiene diferentes fechas", 23/07/26): cada
            // grupo es su propio asiento provisional (1..N por orden) con número definitivo distinto.
            var totalesPorFecha = new SortedDictionary<DateTime, decimal>();
            foreach (ExtractoCliente efecto in efectos)
            {
                DateTime fechaSolicitada = respetarVencimientos
                    ? VencimientoEfectivo(efecto.FechaVto, suelo)
                    : suelo;
                totalesPorFecha[fechaSolicitada] = (totalesPorFecha.TryGetValue(fechaSolicitada, out decimal acumulado)
                    ? acumulado : 0) + efecto.ImportePdte;
            }
            Dictionary<DateTime, int> asientoPorFecha = totalesPorFecha.Keys
                .Select((fecha, indice) => new { fecha, indice })
                .ToDictionary(x => x.fecha, x => x.indice + 1);

            foreach (ExtractoCliente efecto in efectos)
            {
                DateTime fechaSolicitada = respetarVencimientos
                    ? VencimientoEfectivo(efecto.FechaVto, suelo)
                    : suelo;
                // El día de VALOR (fecha contable): el banco no abona antes de la próxima fecha de
                // cargo, así que el grupo de hoy sube al siguiente laborable; los futuros conservan
                // su fecha. VencimientoEfectivo = max(fechaSolicitada, sueloValor).
                DateTime fechaValor = VencimientoEfectivo(fechaSolicitada, sueloValor);

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
                    Fecha = fechaValor,
                    FechaVto = fechaSolicitada,
                    Asiento = asientoPorFecha[fechaSolicitada],
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

            // Un apunte de banco por fecha de cargo SOLICITADA (una sola en modo forzado): el
            // banco hace un abono en cuenta por cada fecha solicitada, con el DÍA DE VALOR como
            // fecha contable — así cuadra apunte a apunte contra el extracto.
            string conceptoBanco = $"Remesa:{numeroRemesa}. Al Banco: {cuentaBanco}";
            foreach (KeyValuePair<DateTime, decimal> grupo in totalesPorFecha)
            {
                DateTime fechaValor = VencimientoEfectivo(grupo.Key, sueloValor);
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
                    Fecha = fechaValor,
                    FechaVto = fechaValor,
                    Asiento = asientoPorFecha[grupo.Key],
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
        /// NestoAPI#345: true = cada efecto va por su vencimiento original (con suelo hoy) y el
        /// asiento lleva un apunte de banco POR FECHA solicitada, pero contabilizado en su DÍA
        /// DE VALOR (el grupo de hoy sube al siguiente laborable, imitando al banco); false
        /// (default, comportamiento de siempre) = todos los efectos se fuerzan a FechaCargo.
        /// </summary>
        public bool RespetarVencimientos { get; set; }

        /// <summary>
        /// NestoAPI#345: fecha de cargo ÚNICA del modo forzado (default hoy; nunca anterior a
        /// hoy). En modo respetar se ignora: el suelo de los vencimientos es siempre hoy.
        /// </summary>
        public DateTime? FechaCargo { get; set; }

        /// <summary>
        /// NestoAPI#345: la fecha "hasta" con la que el usuario cargó los candidatos
        /// (vencimientos incluidos hasta esa fecha). El servidor revalida con la MISMA fecha.
        /// Null = solo vencidos a hoy (comportamiento clásico).
        /// </summary>
        public DateTime? SeleccionHasta { get; set; }
    }

    public class CrearRemesaResponse
    {
        public int NumeroRemesa { get; set; }
        public decimal Importe { get; set; }
        public int NumeroEfectos { get; set; }
        public int ResultadoContabilizacion { get; set; }
    }
}
