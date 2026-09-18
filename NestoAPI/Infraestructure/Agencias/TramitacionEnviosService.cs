using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Nesto#340 (Agencias, slice A4.1): CIERRE de un envío ya registrado en la agencia — pasarlo a
    /// Tramitado en nuestra BD y contabilizar su reembolso. Es el segundo paso de "tramitar": el
    /// primero (hablar con la agencia y traer la etiqueta) vive en
    /// <c>EnviosAgenciasController.TramitarEnvio</c>.
    ///
    /// Portado de <c>AgenciaService.TramitarEnvio</c> / <c>ContabilizarReembolso</c> del cliente
    /// (Nesto#415), que eran de los últimos sitios donde Nesto escribía en la BD con Entity Framework
    /// y, además, el único que llamaba a <c>prdContabilizar</c> por su cuenta.
    ///
    /// El SP se ejecuta por <see cref="IContabilidadService.ContabilizarDiario"/>, que ya trae las
    /// validaciones adelantadas de #343 (gasto sin centro de coste) y #296 (RAISERROR de negocio
    /// enterrado en el ruido de transacciones): así el SP conserva un único call site.
    /// </summary>
    public class TramitacionEnviosService : ITramitacionEnviosService
    {
        private readonly NVEntities _db;
        private readonly IContabilidadService _contabilidad;
        private readonly Func<DateTime> _hoy;

        public TramitacionEnviosService(NVEntities db)
            : this(db, new ContabilidadService(), () => DateTime.Today)
        {
        }

        // El reloj se inyecta porque la fecha del apunte y la de entrega prevista son parte del
        // contrato que verifican los tests de paridad con el cliente.
        public TramitacionEnviosService(NVEntities db, IContabilidadService contabilidad, Func<DateTime> hoy)
        {
            _db = db;
            _contabilidad = contabilidad;
            _hoy = hoy;
        }

        /// <summary>
        /// Cierra el envío (Estado Tramitado, fecha de hoy, entrega prevista mañana) y, si lleva
        /// reembolso, lo contabiliza. Devuelve el asiento (0 si no había reembolso que contabilizar).
        /// </summary>
        public async Task<ResultadoTramitacionEnvio> TramitarAsync(int numeroEnvio, string usuario)
        {
            // OJO con la forma de esta consulta. Con Include + SingleOrDefaultAsync(predicado),
            // EF6 envuelve todo en una subconsulta TOP(2) llamada "Limit1" y ahí chocan las
            // columnas que se llaman igual en las tablas incluidas (Empresas.Número): SQL Server
            // responde "La columna 'Número' se ha especificado varias veces para 'Limit1'" y no
            // se puede tramitar NADA. Pasó el 28/08/2026, 17 veces en cuatro minutos.
            //
            // Con Where + ToListAsync no hay subconsulta: sale un SELECT plano con sus JOIN.
            // Numero es la clave (identity), así que como mucho viene una fila.
            //
            // Esto NO lo pueden pillar los tests: usan dobles en memoria y el error es del SQL
            // que genera EF. Cualquier cambio aquí hay que probarlo tramitando un envío de verdad.
            List<EnviosAgencia> envios = await _db.EnviosAgencias
                .Include(e => e.AgenciasTransporte)
                .Include(e => e.Empresa1)
                .Where(e => e.Numero == numeroEnvio)
                .ToListAsync()
                .ConfigureAwait(false);
            EnviosAgencia envio = envios.SingleOrDefault();
            if (envio == null)
            {
                throw new NestoBusinessException($"No existe el envío {numeroEnvio}.");
            }

            // Issue #135: el sentinel "no cobrar reembolso" (-1) no puede viajar a la agencia como
            // importe negativo. Se convierte a 0 ANTES de tramitar, igual que hacía el cliente.
            if (envio.Reembolso < 0)
            {
                envio.Reembolso = 0;
            }

            DateTime hoy = _hoy();
            envio.Estado = Constantes.Agencias.ESTADO_TRAMITADO;
            envio.Fecha = hoy;
            envio.FechaEntrega = hoy.AddDays(1);    // se entrega al día siguiente

            // El cambio de estado y la contabilización del reembolso van juntos o no van: si el
            // asiento falla, el envío NO puede quedar tramitado (el cliente lo resolvía con un
            // TransactionScope alrededor de los dos contextos).
            using (DbContextTransaction transaccion = _db.Database.BeginTransaction())
            {
                try
                {
                    int asiento = 0;
                    if (envio.Reembolso != 0)
                    {
                        asiento = await ContabilizarReembolsoAsync(envio, usuario).ConfigureAwait(false);
                    }
                    _ = await _db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return new ResultadoTramitacionEnvio
                    {
                        Numero = envio.Numero,
                        Asiento = asiento,
                        Mensaje = $"Envío del pedido {envio.Pedido} tramitado correctamente."
                    };
                }
                catch (Exception)
                {
                    // #291: rollback seguro — con una transacción zombi (SP abortado por dentro) el
                    // Rollback normal lanzaría y taparía la excepción de verdad.
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        /// <summary>
        /// Contabiliza el reembolso del envío: crea el apunte de PreContabilidad y lanza el diario.
        /// Devuelve el número de asiento.
        /// </summary>
        public async Task<int> ContabilizarReembolsoAsync(EnviosAgencia envio, string usuario)
        {
            if (envio.AgenciasTransporte == null || string.IsNullOrWhiteSpace(envio.AgenciasTransporte.CuentaReembolsos))
            {
                throw new NestoBusinessException(
                    "Esta agencia no tiene establecida una cuenta de reembolsos. No se puede contabilizar.");
            }

            ExtractoCliente movimientoLiq = await CalcularMovimientoLiqAsync(envio).ConfigureAwait(false);
            PreContabilidad linea = ConstruirApunteReembolso(envio, movimientoLiq, _hoy(), usuario);
            // NestoAPI#431: el apunte entra por la puerta canónica (ContabilidadService.CrearLineas),
            // no con un Add a pelo. La puerta normaliza FechaVto, Fecha_Modificación y el largo del
            // concepto — los campos que aquí se fueron descubriendo de uno en uno el 31/08/26, a
            // despliegue por campo. ConstruirApunteReembolso los sigue asignando (es el contrato de
            // paridad con el cliente), y la puerta queda de red para el siguiente campo que aparezca.
            // Con empresa distinta de la 1 la puerta ejecuta además prdCopiarCliente, como el resto
            // de apuntes de cliente (decidido 01/09/26; medido: el 100% de los envíos del último año
            // son de la empresa 1, así que hoy esa rama no llega a ejecutarse).
            _ = await _contabilidad.CrearLineas(_db, new List<PreContabilidad> { linea }).ConfigureAwait(false);

            int asiento = await _contabilidad
                .ContabilizarDiario(_db, linea.Empresa, Constantes.Contabilidad.Diarios.DIARIO_REEMBOLSOS, usuario)
                .ConfigureAwait(false);
            if (asiento <= 0)
            {
                throw new Exception($"No se ha podido contabilizar el reembolso del envío {envio.Numero}.");
            }
            return asiento;
        }


        // ===== Nesto#415 / Nesto#340 (Agencias A4.3): pago de reembolsos =====

        /// <summary>
        /// La agencia liquida los reembolsos que cobró: un apunte al HABER de la cuenta de reembolsos
        /// de la agencia de cada envío y uno al DEBE del cliente al que se le paga por la suma, en el
        /// diario _PagoReemb; después prdContabilizar y, en la MISMA transacción,
        /// FechaPagoReembolso = hoy en los envíos. Sustituye a <c>AgenciasViewModel.OnContabilizarReembolso</c>
        /// (Nesto), que hacía todo esto por Entity Framework y era el último sitio fuera de la API que
        /// llamaba a prdContabilizar (Nesto#415: sin ELMAH, sin reintento ante deadlock, con el
        /// login de Windows del usuario en vez del canal centralizado).
        ///
        /// Como TramitarAsync, la parte que toca la base de datos NO la pillan los tests (dobles en
        /// memoria); la construcción de los apuntes y las validaciones son estáticas y sí.
        /// </summary>
        public async Task<ResultadoPagoReembolsos> PagarReembolsosAsync(PagoReembolsosDTO datos, string usuario)
        {
            if (datos == null || datos.NumerosEnvio == null || datos.NumerosEnvio.Count == 0)
            {
                throw new NestoBusinessException("No hay ningún reembolso seleccionado.");
            }
            if (string.IsNullOrWhiteSpace(datos.Empresa) || string.IsNullOrWhiteSpace(datos.Cliente))
            {
                throw new NestoBusinessException("Faltan la empresa o el cliente al que se le paga.");
            }

            string empresaId = datos.Empresa.Trim();
            string cliente = datos.Cliente.Trim();
            List<int> numeros = datos.NumerosEnvio.Distinct().ToList();

            // Where + ToListAsync, nunca SingleOrDefaultAsync con Include: ver la nota de TramitarAsync (Limit1).
            List<EnviosAgencia> envios = await _db.EnviosAgencias
                .Include(e => e.AgenciasTransporte)
                .Where(e => numeros.Contains(e.Numero))
                .ToListAsync()
                .ConfigureAwait(false);
            string error = ErrorEnviosAPagar(envios, numeros, empresaId);
            if (error != null)
            {
                throw new NestoBusinessException(error);
            }

            // Los filtros van en SQL, que ignora el relleno de los char (reference_migracion_ef_padding_cadenas).
            Empresa empresa = (await _db.Empresas.Where(e => e.Número == empresaId).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            if (empresa == null)
            {
                throw new NestoBusinessException($"No existe la empresa {empresaId}.");
            }
            AgenciaTransporte agenciaCobra = (await _db.AgenciasTransportes.Where(a => a.Numero == datos.Agencia).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            if (agenciaCobra == null)
            {
                throw new NestoBusinessException($"No existe la agencia {datos.Agencia}.");
            }
            // El cliente al que se paga tiene que estar de alta como principal (el ViewModel lo
            // comprobaba antes de contabilizar; mismo criterio que GET api/Clientes/ExistePrincipalActivo).
            bool existeCliente = await Controllers.ClientesController
                .PrincipalesActivos(_db.Clientes, empresaId, cliente)
                .AnyAsync()
                .ConfigureAwait(false);
            if (!existeCliente)
            {
                throw new NestoBusinessException($"El cliente {cliente} no existe en la empresa {empresaId} (o no está de alta como principal).");
            }

            DateTime hoy = _hoy();
            List<PreContabilidad> lineas = ConstruirApuntesPagoReembolsos(envios, agenciaCobra, empresa, cliente, hoy, usuario);
            decimal importe = envios.Sum(e => e.Reembolso);

            // El asiento y la fecha de pago de los envíos van juntos o no van (el cliente lo resolvía
            // con un TransactionScope alrededor de los dos).
            using (DbContextTransaction transaccion = _db.Database.BeginTransaction())
            {
                try
                {
                    _ = await _contabilidad.CrearLineas(_db, lineas).ConfigureAwait(false);
                    int asiento = await _contabilidad
                        .ContabilizarDiario(_db, empresaId, Constantes.Contabilidad.Diarios.DIARIO_PAGO_REEMBOLSOS, usuario)
                        .ConfigureAwait(false);
                    if (asiento <= 0)
                    {
                        throw new Exception($"No se ha podido contabilizar el pago de los reembolsos al cliente {cliente}.");
                    }
                    foreach (EnviosAgencia envio in envios)
                    {
                        envio.FechaPagoReembolso = hoy;
                        envio.Usuario = usuario;
                    }
                    _ = await _db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return new ResultadoPagoReembolsos
                    {
                        Asiento = asiento,
                        Envios = envios.Count,
                        Importe = importe,
                        Mensaje = $"Pagados {envios.Count} reembolsos ({importe:N2} €) al cliente {cliente}. Asiento {asiento}."
                    };
                }
                catch (Exception)
                {
                    // #291: rollback seguro — con una transacción zombi (SP abortado por dentro) el
                    // Rollback normal lanzaría y taparía la excepción de verdad.
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        /// <summary>
        /// Por qué NO se pueden pagar estos envíos, o null si todo está bien. Se acumulan todos los
        /// motivos, que el usuario selecciona varias filas y conviene que vea la lista entera.
        /// </summary>
        internal static string ErrorEnviosAPagar(List<EnviosAgencia> envios, List<int> numeros, string empresa)
        {
            List<string> errores = new List<string>();
            foreach (int numero in numeros)
            {
                EnviosAgencia envio = envios.FirstOrDefault(e => e.Numero == numero);
                if (envio == null)
                {
                    errores.Add($"No existe el envío {numero}.");
                    continue;
                }
                if (!string.Equals(envio.Empresa?.Trim(), empresa?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    errores.Add($"El envío {numero} es de la empresa {envio.Empresa?.Trim()}, no de la {empresa?.Trim()}.");
                }
                if (envio.FechaPagoReembolso.HasValue)
                {
                    errores.Add($"El reembolso del envío {numero} (pedido {envio.Pedido}) ya se pagó el {envio.FechaPagoReembolso.Value:dd/MM/yyyy}.");
                }
                if (envio.Reembolso == 0)
                {
                    errores.Add($"El envío {numero} (pedido {envio.Pedido}) no tiene reembolso.");
                }
                if (envio.AgenciasTransporte == null || string.IsNullOrWhiteSpace(envio.AgenciasTransporte.CuentaReembolsos))
                {
                    errores.Add($"La agencia del envío {numero} no tiene establecida una cuenta de reembolsos.");
                }
            }
            return errores.Count == 0 ? null : string.Join(" ", errores);
        }

        /// <summary>
        /// Los apuntes de PreContabilidad del pago, campo a campo como los construía el cliente
        /// (Nesto: <c>AgenciasViewModel.OnContabilizarReembolso</c>): uno al haber de la cuenta de
        /// reembolsos de la agencia de CADA envío (Nº documento = 10 primeras letras del nombre de
        /// esa agencia) y uno al debe del cliente por la suma, con la forma de pago en efectivo de
        /// la empresa y el vendedor general. Delegación y forma de venta iban a pelo en el cliente
        /// ("ALG"/"VAR"), no los "varios" de la empresa; se respeta. Es el contrato que fija el test
        /// de paridad, porque una sola diferencia descuadra el cuadre de reembolsos.
        /// </summary>
        internal static List<PreContabilidad> ConstruirApuntesPagoReembolsos(List<EnviosAgencia> envios,
            AgenciaTransporte agenciaCobra, Empresa empresa, string cliente, DateTime hoy, string usuario)
        {
            string empresaId = empresa.Número.Trim();
            List<PreContabilidad> lineas = new List<PreContabilidad>();
            foreach (EnviosAgencia envio in envios)
            {
                lineas.Add(new PreContabilidad
                {
                    Empresa = empresaId,
                    Diario = Constantes.Contabilidad.Diarios.DIARIO_PAGO_REEMBOLSOS,
                    Asiento = 1,
                    Fecha = hoy,
                    FechaVto = hoy,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                    Nº_Cuenta = envio.AgenciasTransporte.CuentaReembolsos.Trim(),
                    Concepto = Recortar50($"Pago reembolso {envio.Cliente?.Trim()}"),
                    Haber = envio.Reembolso,
                    Nº_Documento = NumeroDocumento(envio.AgenciasTransporte.Nombre),
                    Delegación = Constantes.Almacenes.ALGETE,
                    FormaVenta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }
            lineas.Add(new PreContabilidad
            {
                Empresa = empresaId,
                Diario = Constantes.Contabilidad.Diarios.DIARIO_PAGO_REEMBOLSOS,
                Asiento = 1,
                Fecha = hoy,
                FechaVto = hoy,
                TipoApunte = Constantes.ExtractosCliente.TiposApunte.PAGO,
                TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                Nº_Cuenta = cliente.Trim(),
                Contacto = "0",
                Concepto = Recortar50($"Pago reembolso {agenciaCobra.Nombre?.Trim()}"),
                Debe = envios.Sum(e => e.Reembolso),
                Nº_Documento = NumeroDocumento(agenciaCobra.Nombre),
                Delegación = Constantes.Almacenes.ALGETE,
                FormaVenta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                FormaPago = empresa.FormaPagoEfectivo,
                Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL,
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now
            });
            return lineas;
        }

        private static string NumeroDocumento(string nombreAgencia)
        {
            string nombre = nombreAgencia?.Trim() ?? string.Empty;
            return nombre.Length > 10 ? nombre.Substring(0, 10) : nombre;
        }

        private static string Recortar50(string concepto)
        {
            return concepto.Length > 50 ? concepto.Substring(0, 50) : concepto;
        }

        /// <summary>
        /// Apunte de PreContabilidad del reembolso, campo a campo como lo construía el cliente
        /// (Nesto: <c>AgenciaService.ContabilizarReembolso</c>). Es el contrato que fija el test de
        /// paridad, porque una sola diferencia aquí descuadra la contabilidad. Lo único que no es
        /// puro es <c>Fecha Modificación</c>, que es la marca de CUÁNDO se escribió la fila (y por
        /// eso va con la hora, a diferencia de <c>Fecha</c>, que es la fecha contable).
        ///
        /// El USUARIO hay que ponerlo a mano, y es justo lo que faltaba (31/08/2026, primer día
        /// del piloto de tramitación por API): `PreContabilidad.Usuario` es NOT NULL con DEFAULT
        /// `suser_sname()`. Desde Nesto viejo el default lo rellenaba SQL Server con las
        /// credenciales Windows del usuario; por la API, EF6 VALIDA ANTES DE ENVIAR, ve el campo
        /// obligatorio vacío y revienta con "El campo Usuario es obligatorio" — la sentencia no
        /// llega nunca al servidor y el default no tiene ocasión de actuar.
        ///
        /// Efecto: la agencia aceptaba el envío pero el reembolso no se contabilizaba y el envío
        /// se quedaba ABIERTO. Exactamente la señal de alarma que buscábamos (agencia OK + envío
        /// sin cerrar), y solo en los envíos CON reembolso.
        ///
        /// Se pone el usuario del Identity, que además es mejor dato que el `suser_sname()` de
        /// antes: identifica a la persona, no a la cuenta con la que se conecta la aplicación.
        /// </summary>
        internal static PreContabilidad ConstruirApunteReembolso(EnviosAgencia envio, ExtractoCliente movimientoLiq, DateTime hoy, string usuario)
        {
            Empresa empresa = envio.Empresa1;
            PreContabilidad linea = new PreContabilidad
            {
                Empresa = envio.Empresa.Trim(),
                Diario = Constantes.Contabilidad.Diarios.DIARIO_REEMBOLSOS,
                TipoApunte = Constantes.ExtractosCliente.TiposApunte.PAGO,
                TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                Nº_Cuenta = envio.Cliente.Trim(),
                Contacto = envio.Contacto.Trim(),
                // La fecha es la de HOY, no la del envío: la etiqueta puede ser del día anterior.
                Fecha = hoy,
                FechaVto = hoy,
                Haber = envio.Reembolso,
                Concepto = GenerarConcepto(envio),
                Contrapartida = envio.AgenciasTransporte.CuentaReembolsos.Trim(),
                Asiento_Automático = false,
                FormaPago = empresa?.FormaPagoEfectivo,
                Vendedor = envio.Vendedor,
                Usuario = usuario,
                // MISMA trampa que Usuario, y la de al lado en la tabla: `Fecha Modificación` es
                // NOT NULL con DEFAULT (getdate()), pero la propiedad es DateTime no nullable, así
                // que sin asignarla EF manda 01/01/0001 — y SQL Server no puede convertir ese
                // datetime2 a datetime, cuyo mínimo es 1753. El default no llega a actuar nunca.
                //
                // No se vio el 31/08 a la vez que el Usuario porque la VALIDACIÓN de EF cortaba
                // antes de llegar al SQL: arreglado el primero, apareció el segundo. Comprobado
                // que ya no queda ningún otro campo obligatorio sin asignar (Nº Orden es identity,
                // Debe y CajaLiquidada valen con su valor por defecto de C#).
                Fecha_Modificación = DateTime.Now
            };

            if (movimientoLiq == null)
            {
                // Sin movimiento que liquidar (cliente especial, o no se encontró la deuda): el pago
                // queda suelto contra el pedido, con los datos "varios" de la empresa.
                linea.Nº_Documento = envio.Pedido.ToString();
                linea.Delegación = empresa?.DelegaciónVarios;
                linea.FormaVenta = empresa?.FormaVentaVarios;
            }
            else
            {
                linea.Nº_Documento = movimientoLiq.Nº_Documento;
                linea.Liquidado = movimientoLiq.Nº_Orden;
                linea.Delegación = movimientoLiq.Delegación;
                linea.FormaVenta = movimientoLiq.FormaVenta;
                linea.Ruta = movimientoLiq.Ruta;
                linea.Efecto = movimientoLiq.Efecto;
            }
            return linea;
        }

        /// <summary>Concepto del apunte, recortado a los 50 caracteres que admite la columna.</summary>
        internal static string GenerarConcepto(EnviosAgencia envio)
        {
            string concepto = $"S/Pago pedido {envio.Pedido} a {envio.AgenciasTransporte?.Nombre?.Trim()} c/{envio.Cliente?.Trim()}";
            return concepto.Length > 50 ? concepto.Substring(0, 50) : concepto;
        }

        // Busca en el extracto del cliente la deuda que este reembolso liquida.
        private async Task<ExtractoCliente> CalcularMovimientoLiqAsync(EnviosAgencia envio)
        {
            string cliente = envio.Cliente?.Trim();
            if (cliente == Constantes.ClientesEspeciales.AMAZON || cliente == Constantes.ClientesEspeciales.TIENDA_ONLINE)
            {
                // Sus cobros no se liquidan contra el extracto: van por el circuito de canales externos.
                return null;
            }

            bool positivos = envio.Reembolso > 0;
            List<ExtractoCliente> movimientos = await LeerExtractoPendienteAsync(envio.Empresa, envio.Cliente, positivos)
                .ConfigureAwait(false);
            return ElegirMovimientoLiq(movimientos, envio.Reembolso, _hoy());
        }

        /// <summary>
        /// Elección del movimiento a liquidar, calcada del cliente. Pura para poder testear las
        /// tres ramas (uno solo, varios con importe exacto, varios sin coincidencia).
        /// </summary>
        internal static ExtractoCliente ElegirMovimientoLiq(List<ExtractoCliente> movimientos, decimal reembolso, DateTime hoy)
        {
            if (movimientos == null || movimientos.Count == 0)
            {
                return null;
            }
            if (movimientos.Count == 1)
            {
                return movimientos.Single();
            }

            List<ExtractoCliente> conImporte = reembolso > 0
                ? movimientos.Where(m => m.ImportePdte == reembolso).ToList()
                // Con reembolso negativo (devolución) se exige además que sea de hoy: con la fecha del
                // envío había problemas cuando la etiqueta era del día anterior.
                : movimientos.Where(m => m.ImportePdte == reembolso && m.Fecha == hoy).ToList();

            return conImporte.Count == 0 ? movimientos.Last() : conImporte.Last();
        }

        // Apuntes vivos del cliente: pendientes del signo que toca, en estado normal y sin los
        // cursos (serie CV), que se cobran aparte.
        private async Task<List<ExtractoCliente>> LeerExtractoPendienteAsync(string empresa, string cliente, bool positivos)
        {
            IQueryable<ExtractoCliente> query = _db.ExtractosCliente
                .Where(e => e.Empresa == empresa && e.Número == cliente
                    && (e.Estado == Constantes.ExtractosCliente.Estados.NORMAL || e.Estado == null)
                    && !e.Nº_Documento.StartsWith(Constantes.Series.SERIE_CURSOS));
            query = positivos
                ? query.Where(e => e.ImportePdte > 0)
                : query.Where(e => e.ImportePdte < 0);
            return await query.ToListAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Resultado de tramitar un envío: lo que la ventana de Agencias enseña al usuario.</summary>
    public class ResultadoTramitacionEnvio
    {
        public int Numero { get; set; }
        public int Asiento { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>Nesto#415 (A4.3): qué reembolsos paga la agencia y a qué cliente.</summary>
    public class PagoReembolsosDTO
    {
        public string Empresa { get; set; }
        /// <summary>Cliente al que se le paga (normalmente la propia agencia como cliente).</summary>
        public string Cliente { get; set; }
        /// <summary>Agencia seleccionada en la pestaña: da el concepto y el Nº documento del apunte del cliente.</summary>
        public int Agencia { get; set; }
        public List<int> NumerosEnvio { get; set; }
    }

    public class ResultadoPagoReembolsos
    {
        public int Asiento { get; set; }
        public int Envios { get; set; }
        public decimal Importe { get; set; }
        public string Mensaje { get; set; }
    }

    public interface ITramitacionEnviosService
    {
        Task<ResultadoTramitacionEnvio> TramitarAsync(int numeroEnvio, string usuario);
        Task<int> ContabilizarReembolsoAsync(EnviosAgencia envio, string usuario);
        Task<ResultadoPagoReembolsos> PagarReembolsosAsync(PagoReembolsosDTO datos, string usuario);
    }
}
