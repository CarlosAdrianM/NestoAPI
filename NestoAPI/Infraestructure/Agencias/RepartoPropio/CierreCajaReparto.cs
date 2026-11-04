using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Informes;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#802 (Ariadna#85): POST api/RepartoPropio/Caja/Cierre. El repartidor entrega lo que lleva en la caja (solo el importe,
    /// sin arqueo) y se traspasa a la caja pendiente de recibir (57000016), como Nesto → Cajas → Traspasar de las tiendas.
    /// </summary>
    public class CierreCajaRepartoDTO
    {
        /// <summary>Lo que lleva, contado.</summary>
        public decimal Importe { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>Cuándo se cerró en el aparato (hora local). Da la fecha del asiento; nunca después de hoy.</summary>
        public DateTime? FechaHora { get; set; }
    }

    public class ResultadoCierreCajaDTO
    {
        public DateTime Fecha { get; set; }
        public string Cuenta { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Un asiento por empresa con saldo (la 1 primero).</summary>
        public List<AsientoCierreCajaDTO> Asientos { get; set; } = new List<AsientoCierreCajaDTO>();
    }

    public class AsientoCierreCajaDTO
    {
        public string Empresa { get; set; }
        public int Asiento { get; set; }
        public decimal Importe { get; set; }
    }

    /// <summary>El saldo de la caja en una empresa a la fecha del cierre.</summary>
    public class SaldoCajaEmpresa
    {
        public string Empresa { get; set; }
        public decimal Saldo { get; set; }
    }

    /// <summary>
    /// Lo que se apunta en <c>Modificaciones.Nuevo</c> del cierre: el resultado (lo que se devuelve a un reintento, por eso hereda
    /// de <see cref="ResultadoCierreCajaDTO"/>) más quién, los saldos y qué empresas se han punteado. <c>Modificaciones.Usuario</c>
    /// es Computed en el EDMX, así que el usuario va aquí.
    /// </summary>
    public class AuditoriaCierreCaja : ResultadoCierreCajaDTO
    {
        public string Usuario { get; set; }
        public List<SaldoCajaEmpresa> Saldos { get; set; } = new List<SaldoCajaEmpresa>();
        public List<string> EmpresasPunteadas { get; set; } = new List<string>();
    }

    /// <summary>
    /// La caja del repartidor (su CajaDefecto) por empresa: lo comparten MisCobros y el cierre, para que lo que ve en la PDA sea
    /// exactamente lo que se cierra.
    /// </summary>
    public static class CajaReparto
    {
        /// <summary>Las empresas en las que puede haber movimientos de la caja (la 1 y la espejo).</summary>
        public static readonly IReadOnlyList<string> EMPRESAS = new[]
        {
            Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO
        };

        private const char SEPARADOR_DOMINIO = (char)92;

        public static string SinDominio(string usuario)
            => string.IsNullOrWhiteSpace(usuario) ? null : usuario.Substring(usuario.IndexOf(SEPARADOR_DOMINIO) + 1).Trim();

        /// <summary>
        /// La CajaDefecto del usuario (empresa 1, sin dominio), SOLO de su propia fila: la de «(defecto)» sería la caja de otro.
        /// Null si no la tiene.
        /// </summary>
        public static string CajaDelUsuario(ILectorParametrosUsuario parametros, string usuario)
        {
            string sinDominio = SinDominio(usuario);
            if (string.IsNullOrEmpty(sinDominio))
            {
                return null;
            }
            string caja = parametros.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, sinDominio, ServicioRepartoPropio.PARAMETRO_CAJA)?.Trim();
            return string.IsNullOrEmpty(caja) ? null : caja;
        }

        /// <summary>El saldo anterior, los apuntes del día y el saldo final de la caja en una empresa.</summary>
        public static async Task<CobrosEmpresaRepartoDTO> LeerEmpresa(IInformesService informes, string empresa, string cuenta, DateTime dia)
        {
            decimal saldoAnterior = await informes.LeerSaldoContableAnteriorAsync(empresa, cuenta, dia).ConfigureAwait(false);
            List<ExtractoContableDTO> lineas = await informes.LeerExtractoContableAsync(empresa, cuenta, dia, dia).ConfigureAwait(false)
                ?? new List<ExtractoContableDTO>();
            List<ApunteCajaRepartoDTO> apuntes = lineas
                .OrderBy(l => l.Fecha).ThenBy(l => l.Id)
                .Select(l => new ApunteCajaRepartoDTO
                {
                    Fecha = l.Fecha,
                    Concepto = l.Concepto?.Trim(),
                    Importe = l.Debe - l.Haber,
                    Asiento = l.Asiento,
                    Documento = l.Documento?.Trim(),
                    Empresa = empresa
                })
                .ToList();
            decimal totalDia = apuntes.Sum(a => a.Importe);
            return new CobrosEmpresaRepartoDTO
            {
                Empresa = empresa,
                SaldoAnterior = saldoAnterior,
                Apuntes = apuntes,
                TotalDia = totalDia,
                SaldoFinal = saldoAnterior + totalDia
            };
        }

        /// <summary>
        /// MisCobros: siempre la empresa 1; las demás solo si tienen apuntes ese día o saldo. Los campos de arriba, los totales
        /// (los apuntes de todas por fecha; a igual fecha, primero la 1).
        /// </summary>
        public static async Task<MisCobrosRepartoDTO> Leer(IInformesService informes, string cuenta, DateTime dia)
        {
            var empresas = new List<CobrosEmpresaRepartoDTO>();
            foreach (string empresa in EMPRESAS)
            {
                CobrosEmpresaRepartoDTO caja = await LeerEmpresa(informes, empresa, cuenta, dia).ConfigureAwait(false);
                if (empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || caja.Apuntes.Count > 0 || caja.SaldoAnterior != 0 || caja.SaldoFinal != 0)
                {
                    empresas.Add(caja);
                }
            }
            return new MisCobrosRepartoDTO
            {
                Cuenta = cuenta,
                Fecha = dia,
                SaldoAnterior = empresas.Sum(e => e.SaldoAnterior),
                Apuntes = empresas.SelectMany(e => e.Apuntes).OrderBy(a => a.Fecha).ToList(),
                TotalDia = empresas.Sum(e => e.TotalDia),
                SaldoFinal = empresas.Sum(e => e.SaldoFinal),
                Empresas = empresas
            };
        }
    }

    public enum TipoDecisionCierreCaja
    {
        Cuadra,
        NadaQueCerrar,
        Negativo,
        NoCuadra
    }

    public class DecisionCierreCaja
    {
        public TipoDecisionCierreCaja Tipo { get; set; }
        public string Mensaje { get; set; }

        /// <summary>Cómo se responde: Ok si cuadra, 400 si no hay nada que cerrar, 409 en negativo o si no cuadra.</summary>
        public TipoResultadoReparto TipoResultado
        {
            get
            {
                switch (Tipo)
                {
                    case TipoDecisionCierreCaja.Cuadra:
                        return TipoResultadoReparto.Ok;
                    case TipoDecisionCierreCaja.NadaQueCerrar:
                        return TipoResultadoReparto.Invalido;
                    default:
                        return TipoResultadoReparto.Conflicto;
                }
            }
        }
    }

    /// <summary>La regla del cierre (pura): con los saldos de la caja por empresa y lo que lleva, ¿se puede cerrar?</summary>
    public static class ReglaCierreCaja
    {
        public const string MENSAJE_NADA_QUE_CERRAR = "No hay nada que cerrar: la caja está a cero";

        private static readonly NumberFormatInfo FormatoEspanol = new NumberFormatInfo
        {
            NumberDecimalSeparator = ",",
            NumberGroupSeparator = ".",
            NumberGroupSizes = new[] { 3 },
            NegativeSign = "-"
        };

        /// <summary>Dos decimales y formato español: 1234.5 → «1.234,50 €».</summary>
        public static string Euros(decimal importe)
            => Math.Round(importe, 2, MidpointRounding.AwayFromZero).ToString("#,##0.00", FormatoEspanol) + " €";

        public static DecisionCierreCaja Decidir(IReadOnlyList<SaldoCajaEmpresa> saldos, decimal importe)
        {
            List<SaldoCajaEmpresa> redondeados = (saldos ?? new List<SaldoCajaEmpresa>())
                .Select(s => new SaldoCajaEmpresa { Empresa = s.Empresa?.Trim(), Saldo = Math.Round(s.Saldo, 2, MidpointRounding.AwayFromZero) })
                .ToList();
            if (redondeados.All(s => s.Saldo == 0))
            {
                return new DecisionCierreCaja { Tipo = TipoDecisionCierreCaja.NadaQueCerrar, Mensaje = MENSAJE_NADA_QUE_CERRAR };
            }
            SaldoCajaEmpresa negativo = redondeados.FirstOrDefault(s => s.Saldo < 0);
            if (negativo != null)
            {
                return new DecisionCierreCaja
                {
                    Tipo = TipoDecisionCierreCaja.Negativo,
                    Mensaje = $"La caja de la empresa {negativo.Empresa} está en negativo (−{Euros(-negativo.Saldo)}): " +
                        "véalo con Administración antes de cerrar"
                };
            }
            decimal lleva = Math.Round(importe, 2, MidpointRounding.AwayFromZero);
            decimal total = redondeados.Sum(s => s.Saldo);
            if (lleva != total)
            {
                string detalle = string.Join(", ", redondeados.Select(s => $"empresa {s.Empresa}: {Euros(s.Saldo)}"));
                return new DecisionCierreCaja
                {
                    Tipo = TipoDecisionCierreCaja.NoCuadra,
                    Mensaje = $"La caja no cuadra: lleva {Euros(lleva)} y la contabilidad dice {Euros(total)} ({detalle}). " +
                        "Véalo con Administración antes de cerrar."
                };
            }
            return new DecisionCierreCaja { Tipo = TipoDecisionCierreCaja.Cuadra };
        }
    }

    /// <summary>
    /// Los apuntes del cierre: los MISMOS que Nesto → Cajas → Traspasar de las tiendas (<c>CajasViewModel.OnContabilizarTraspaso</c>,
    /// usuario no Administración), uno por empresa con saldo: Haber a la caja por su saldo, contrapartida la caja pendiente de recibir.
    /// </summary>
    public static class ConstructorApuntesCierreCaja
    {
        public const string DOCUMENTO = "TRASP_CAJA";
        internal const int MAXIMO_CONCEPTO = 50;

        public static List<PreContabilidad> Construir(IEnumerable<SaldoCajaEmpresa> saldos, string caja, DateTime fecha, string delegacion,
            string formaVenta, string usuario, DateTime ahora)
        {
            string concepto = $"Cierre de caja de {CajaReparto.SinDominio(usuario)}";
            if (concepto.Length > MAXIMO_CONCEPTO)
            {
                concepto = concepto.Substring(0, MAXIMO_CONCEPTO);
            }
            return (saldos ?? Enumerable.Empty<SaldoCajaEmpresa>())
                .Where(s => Math.Round(s.Saldo, 2, MidpointRounding.AwayFromZero) != 0)
                .Select(s => new PreContabilidad
                {
                    Empresa = s.Empresa,
                    TipoApunte = Constantes.TiposApunteExtracto.PASO_A_CARTERA,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                    Nº_Cuenta = caja,
                    Concepto = concepto,
                    Debe = 0,
                    Haber = Math.Round(s.Saldo, 2, MidpointRounding.AwayFromZero),
                    Fecha = fecha.Date,
                    FechaVto = fecha.Date,
                    Nº_Documento = DOCUMENTO,
                    Asiento = 1,
                    Diario = Constantes.Contabilidad.Diarios.DIARIO_TRASPASOS_CAJA,
                    Delegación = delegacion,
                    FormaVenta = formaVenta,
                    Contrapartida = Constantes.Cuentas.CAJA_PENDIENTE_RECIBIR_TIENDAS,
                    Origen = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                })
                .ToList();
        }
    }

    /// <summary>Lo que el cierre necesita de la base de datos (fuera de los tests).</summary>
    public interface IRepositorioCierresCaja
    {
        /// <summary>El cierre ya registrado con esa clave, o null.</summary>
        Task<ResultadoCierreCajaDTO> LeerPorClave(Guid clave);

        /// <summary>
        /// En UNA transacción: las líneas a PreContabilidad, el diario de traspasos contabilizado por empresa (prdContabilizar), el
        /// punteo por importe de la caja en cada empresa y la marca de la clave. Devuelve un asiento por empresa.
        /// </summary>
        /// <exception cref="ClaveIdempotenciaRepetidaException">Otra petición ya registró esa clave.</exception>
        Task<List<AsientoCierreCajaDTO>> Registrar(Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCierreCajaDTO resultado);
    }

    public class ServicioCierreCajaReparto
    {
        internal const string PARAMETRO_DELEGACION = "DelegaciónDefecto";
        internal const string PARAMETRO_FORMA_VENTA = "FormaVentaDefecto";

        private readonly IRepositorioCierresCaja repositorio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly Func<IInformesService> informes;
        private readonly Func<DateTime> ahora;

        public ServicioCierreCajaReparto(IRepositorioCierresCaja repositorio, ILectorParametrosUsuario parametros = null,
            Func<IInformesService> informes = null, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.informes = informes ?? (() => new InformesService());
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<ResultadoReparto<ResultadoCierreCajaDTO>> Cerrar(CierreCajaRepartoDTO peticion, string usuario)
        {
            if (peticion == null)
            {
                return Invalido("Indique el importe que lleva.");
            }
            if (peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la clave de idempotencia del cierre de caja.");
            }

            ResultadoCierreCajaDTO yaCerrado = await repositorio.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (yaCerrado != null)
            {
                return ResultadoReparto<ResultadoCierreCajaDTO>.Bien(yaCerrado);
            }

            string caja = CajaReparto.CajaDelUsuario(parametros, usuario);
            if (caja == null)
            {
                return Invalido(ServicioRepartoPropio.MENSAJE_SIN_CAJA);
            }
            DateTime hoy = ahora();
            DateTime fecha = (peticion.FechaHora.HasValue && peticion.FechaHora.Value.Date <= hoy.Date ? peticion.FechaHora.Value : hoy).Date;

            IInformesService servicioInformes = informes();
            var saldos = new List<SaldoCajaEmpresa>();
            foreach (string empresa in CajaReparto.EMPRESAS)
            {
                CobrosEmpresaRepartoDTO cajaEmpresa = await CajaReparto.LeerEmpresa(servicioInformes, empresa, caja, fecha).ConfigureAwait(false);
                saldos.Add(new SaldoCajaEmpresa { Empresa = empresa, Saldo = cajaEmpresa.SaldoFinal });
            }

            DecisionCierreCaja decision = ReglaCierreCaja.Decidir(saldos, peticion.Importe);
            if (decision.Tipo != TipoDecisionCierreCaja.Cuadra)
            {
                return ResultadoReparto<ResultadoCierreCajaDTO>.Mal(decision.TipoResultado, decision.Mensaje);
            }

            string sinDominio = CajaReparto.SinDominio(usuario);
            string empresaDefecto = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string delegacion = parametros.LeerParametro(empresaDefecto, sinDominio, PARAMETRO_DELEGACION)?.Trim();
            string formaVenta = parametros.LeerParametro(empresaDefecto, sinDominio, PARAMETRO_FORMA_VENTA)?.Trim();
            List<PreContabilidad> lineas = ConstructorApuntesCierreCaja.Construir(saldos, caja, fecha, delegacion, formaVenta, usuario, hoy);

            var auditoria = new AuditoriaCierreCaja
            {
                Fecha = fecha,
                Cuenta = caja,
                Importe = Math.Round(peticion.Importe, 2, MidpointRounding.AwayFromZero),
                Usuario = usuario,
                Saldos = saldos
            };
            try
            {
                List<AsientoCierreCajaDTO> asientos = await repositorio.Registrar(peticion.ClaveIdempotencia, usuario, lineas, auditoria).ConfigureAwait(false);
                if (asientos == null || asientos.Count == 0 || asientos.Any(a => a.Asiento <= 0))
                {
                    return Invalido("No se ha podido contabilizar el cierre de caja.");
                }
                auditoria.Asientos = asientos;
            }
            catch (ClaveIdempotenciaRepetidaException)
            {
                // Dos peticiones con la misma clave a la vez: la otra ganó; se devuelve lo suyo
                ResultadoCierreCajaDTO otra = await repositorio.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
                return otra != null ? ResultadoReparto<ResultadoCierreCajaDTO>.Bien(otra)
                    : ResultadoReparto<ResultadoCierreCajaDTO>.Mal(TipoResultadoReparto.Conflicto, "Este cierre de caja ya se está registrando.");
            }
            catch (NestoBusinessException ex)
            {
                return Invalido(ex.Message);
            }
            return ResultadoReparto<ResultadoCierreCajaDTO>.Bien(new ResultadoCierreCajaDTO
            {
                Fecha = auditoria.Fecha,
                Cuenta = auditoria.Cuenta,
                Importe = auditoria.Importe,
                Asientos = auditoria.Asientos
            });
        }

        private static ResultadoReparto<ResultadoCierreCajaDTO> Invalido(string mensaje)
            => ResultadoReparto<ResultadoCierreCajaDTO>.Mal(TipoResultadoReparto.Invalido, mensaje);
    }

    /// <summary>
    /// La clave del cierre se apunta en <c>Modificaciones</c> (Tabla «CierreCajaReparto», Anterior = la clave, Nuevo = el resultado
    /// en JSON) en la misma transacción que los asientos y el punteo: sin script de BD. Un <c>sp_getapplock</c> por clave evita que
    /// dos reintentos a la vez contabilicen dos veces (el mismo patrón que <see cref="RepositorioCobrosReparto"/>).
    /// </summary>
    public class RepositorioCierresCajaReparto : IRepositorioCierresCaja
    {
        internal const string TABLA_MODIFICACIONES = "CierreCajaReparto";
        private const string PUNTO_GUARDADO_PUNTEO = "PunteoCierreCaja";

        private readonly NVEntities db;
        private readonly IContabilidadService contabilidad;

        public RepositorioCierresCajaReparto(NVEntities db, IContabilidadService contabilidad = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.contabilidad = contabilidad ?? new ContabilidadService();
        }

        public async Task<ResultadoCierreCajaDTO> LeerPorClave(Guid clave)
        {
            string texto = clave.ToString("D", CultureInfo.InvariantCulture);
            string nuevo = (await db.Modificaciones
                .Where(m => m.Tabla == TABLA_MODIFICACIONES && m.Anterior == texto)
                .Select(m => m.Nuevo)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            return nuevo == null ? null : DelTexto(nuevo);
        }

        /// <summary>Lo del contrato a partir de <c>Modificaciones.Nuevo</c> (sin la auditoría).</summary>
        internal static ResultadoCierreCajaDTO DelTexto(string nuevo)
        {
            ResultadoCierreCajaDTO leido = JsonConvert.DeserializeObject<ResultadoCierreCajaDTO>(nuevo);
            return new ResultadoCierreCajaDTO { Fecha = leido.Fecha, Cuenta = leido.Cuenta, Importe = leido.Importe, Asientos = leido.Asientos };
        }

        public async Task<List<AsientoCierreCajaDTO>> Registrar(Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCierreCajaDTO resultado)
        {
            if (lineas == null || lineas.Count == 0)
            {
                throw new NestoBusinessException(ReglaCierreCaja.MENSAJE_NADA_QUE_CERRAR);
            }
            AuditoriaCierreCaja auditoria = resultado as AuditoriaCierreCaja ?? new AuditoriaCierreCaja
            {
                Fecha = resultado.Fecha,
                Cuenta = resultado.Cuenta,
                Importe = resultado.Importe,
                Usuario = usuario
            };
            string texto = clave.ToString("D", CultureInfo.InvariantCulture);
            db.Database.CommandTimeout = 120;
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    var bloqueo = new SqlParameter("@r", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000",
                        bloqueo, new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = TABLA_MODIFICACIONES + ":" + texto }).ConfigureAwait(false);
                    if (bloqueo.Value is int r && r < 0)
                    {
                        throw new ClaveIdempotenciaRepetidaException(null);
                    }
                    bool existe = await db.Modificaciones.AnyAsync(m => m.Tabla == TABLA_MODIFICACIONES && m.Anterior == texto).ConfigureAwait(false);
                    if (existe)
                    {
                        throw new ClaveIdempotenciaRepetidaException(null);
                    }

                    _ = await contabilidad.CrearLineas(db, lineas).ConfigureAwait(false);
                    var asientos = new List<AsientoCierreCajaDTO>();
                    foreach (PreContabilidad linea in lineas)
                    {
                        string empresa = linea.Empresa;
                        int asiento = await contabilidad.ContabilizarDiario(db, empresa, linea.Diario, usuario).ConfigureAwait(false);
                        if (asiento <= 0)
                        {
                            throw new NestoBusinessException($"No se ha podido contabilizar el cierre de caja de la empresa {empresa?.Trim()}.");
                        }
                        asientos.Add(new AsientoCierreCajaDTO { Empresa = empresa?.Trim(), Asiento = asiento, Importe = linea.Haber - linea.Debe });
                    }
                    // Como Nesto: después de contabilizar, PuntearPorImporte(empresa, caja, importe). Aquí, en la MISMA transacción
                    // (el de api/Contabilidades/PuntearPorImporte abre otra conexión y se quedaría esperando a esta).
                    foreach (PreContabilidad linea in lineas)
                    {
                        if (await PuntearPorImporte(transaccion, linea.Empresa, linea.Nº_Cuenta, linea.Haber).ConfigureAwait(false))
                        {
                            auditoria.EmpresasPunteadas.Add(linea.Empresa?.Trim());
                        }
                    }
                    auditoria.Asientos = asientos;

                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = TABLA_MODIFICACIONES,
                        Anterior = texto,
                        Nuevo = JsonConvert.SerializeObject(auditoria),
                        // Computed en el EDMX: EF no lo graba (lo pone la BD). El usuario va en Nuevo (AuditoriaCierreCaja).
                        Usuario = usuario != null && usuario.Length > 50 ? usuario.Substring(0, 50) : usuario,
                        Fecha = DateTime.Now
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return asientos;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        /// <summary>
        /// La misma regla que <see cref="ContabilidadService.PuntearPorImporte"/>: si hay UN apunte sin puntear de la cuenta con ese
        /// Debe y UNO con ese Haber, se puntean los dos (prdPuntearContabilidad). Si no, no se puntea y no pasa nada (en Nesto
        /// tampoco). Un fallo del punteo vuelve a su punto de guardado y no tumba el cierre.
        /// </summary>
        private async Task<bool> PuntearPorImporte(DbContextTransaction transaccion, string empresa, string cuenta, decimal importe)
        {
            List<int> debe = await db.Contabilidades
                .Where(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Debe == importe && c.Punteado == false)
                .Select(c => c.Nº_Orden).Take(2).ToListAsync().ConfigureAwait(false);
            List<int> haber = await db.Contabilidades
                .Where(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Haber == importe && c.Punteado == false)
                .Select(c => c.Nº_Orden).Take(2).ToListAsync().ConfigureAwait(false);
            if (debe.Count != 1 || haber.Count != 1)
            {
                return false;
            }
            var sql = transaccion.UnderlyingTransaction as SqlTransaction;
            sql?.Save(PUNTO_GUARDADO_PUNTEO);
            try
            {
                foreach (int orden in new[] { debe[0], haber[0] })
                {
                    _ = await db.Database.ExecuteSqlCommandAsync("EXEC [dbo].[prdPuntearContabilidad] @NºOrden, @Punteado",
                        new SqlParameter("@NºOrden", orden), new SqlParameter("@Punteado", true)).ConfigureAwait(false);
                }
                return true;
            }
            catch (SqlException)
            {
                if (sql == null)
                {
                    throw;
                }
                sql.Rollback(PUNTO_GUARDADO_PUNTEO);
                return false;
            }
        }
    }
}
