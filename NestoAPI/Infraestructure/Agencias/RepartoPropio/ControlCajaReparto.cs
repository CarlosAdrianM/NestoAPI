using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#844 (Carlos 09/11/26): el cierre de caja del repartidor deja de ser opcional. Miguel no lo había usado nunca y
    /// Administración hacía los traspasos a mano. La regla (pura), con la MISMA lectura de la caja que MisCobros y el cierre
    /// (<see cref="CajaReparto.Leer"/>, empresas 1 y 3):
    /// <list type="bullet">
    /// <item>para terminar la jornada, la caja tiene que estar a cero (<see cref="MotivoParaNoTerminar"/>);</item>
    /// <item>para empezar el día siguiente (la primera carga, recogida o cobro), no puede quedar dinero de días anteriores
    /// (<see cref="MotivoParaNoEmpezar"/>).</item>
    /// </list>
    /// Solo cuenta el dinero que lleva (saldo positivo). Con una empresa en negativo no se bloquea nada: el repartidor no puede
    /// arreglarlo (el cierre le dice «véalo con Administración») y dejarle parado no ayuda.
    /// </summary>
    public static class ReglaCajaSinCerrar
    {
        /// <summary>El código del 409 (error.code): Ariadna lo reconoce para ofrecer «Cerrar caja» y no perder un cobro.</summary>
        public const string CODIGO = "CAJA_SIN_CERRAR";

        private static decimal R(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero);

        private static List<CobrosEmpresaRepartoDTO> Limpias(IEnumerable<CobrosEmpresaRepartoDTO> empresas)
            => (empresas ?? Enumerable.Empty<CobrosEmpresaRepartoDTO>()).Where(e => e != null).ToList();

        private static bool AlgunaEnNegativo(IEnumerable<CobrosEmpresaRepartoDTO> empresas) => empresas.Any(e => R(e.SaldoFinal) < 0);

        /// <summary>Lo que hay ahora en la caja (todas las empresas).</summary>
        public static decimal Saldo(IEnumerable<CobrosEmpresaRepartoDTO> empresas) => R(Limpias(empresas).Sum(e => e.SaldoFinal));

        /// <summary>
        /// Lo que queda en la caja de días anteriores en una empresa: el saldo anterior a hoy menos lo que ha SALIDO hoy de la caja
        /// (un traspaso de Administración o un cierre de hoy). Lo cobrado hoy no lo tapa.
        /// </summary>
        public static decimal DeDiasAnteriores(CobrosEmpresaRepartoDTO empresa)
        {
            if (empresa == null)
            {
                return 0m;
            }
            decimal anterior = R(empresa.SaldoAnterior);
            if (anterior <= 0)
            {
                return 0m;
            }
            decimal salidas = (empresa.Apuntes ?? new List<ApunteCajaRepartoDTO>()).Where(a => a != null && a.Importe < 0).Sum(a => -a.Importe);
            return Math.Max(0m, R(anterior - salidas));
        }

        public static decimal DeDiasAnteriores(IEnumerable<CobrosEmpresaRepartoDTO> empresas) => R(Limpias(empresas).Sum(DeDiasAnteriores));

        /// <summary>«Antes de terminar, cierre la caja: lleva 100,50 € (empresa 1: 80,00 €, empresa 3: 20,50 €).» Null: puede terminar.</summary>
        public static string MotivoParaNoTerminar(IEnumerable<CobrosEmpresaRepartoDTO> empresas)
        {
            List<CobrosEmpresaRepartoDTO> lista = Limpias(empresas);
            if (lista.Count == 0 || AlgunaEnNegativo(lista))
            {
                return null;
            }
            decimal total = Saldo(lista);
            return total <= 0 ? null : $"Antes de terminar, cierre la caja: lleva {ReglaCierreCaja.Euros(total)}{Desglose(lista)}.";
        }

        /// <summary>«Tiene 85,40 € de días anteriores sin cerrar en su caja: ciérrela antes de empezar.» Null: puede empezar.</summary>
        public static string MotivoParaNoEmpezar(IEnumerable<CobrosEmpresaRepartoDTO> empresas)
        {
            List<CobrosEmpresaRepartoDTO> lista = Limpias(empresas);
            if (lista.Count == 0 || AlgunaEnNegativo(lista))
            {
                return null;
            }
            decimal anteriores = DeDiasAnteriores(lista);
            return anteriores <= 0 ? null
                : $"Tiene {ReglaCierreCaja.Euros(anteriores)} de días anteriores sin cerrar en su caja: ciérrela antes de empezar.";
        }

        /// <summary>« (empresa 1: 80,00 €, empresa 3: 20,50 €)»; vacío si solo hay saldo en la empresa 1.</summary>
        internal static string Desglose(IEnumerable<CobrosEmpresaRepartoDTO> empresas)
        {
            List<CobrosEmpresaRepartoDTO> conSaldo = Limpias(empresas).Where(e => R(e.SaldoFinal) != 0).ToList();
            if (conSaldo.Count == 0 || (conSaldo.Count == 1 && conSaldo[0].Empresa?.Trim() == Constantes.Empresas.EMPRESA_POR_DEFECTO))
            {
                return string.Empty;
            }
            return " (" + string.Join(", ", conSaldo.Select(e => $"empresa {e.Empresa?.Trim()}: {ReglaCierreCaja.Euros(e.SaldoFinal)}")) + ")";
        }

        /// <summary>El 409 con su código (error.code = <see cref="CODIGO"/>) y Message, como los demás de Ariadna.</summary>
        public static ResultadoReparto<T> Conflicto<T>(string motivo)
            => new ResultadoReparto<T> { Tipo = TipoResultadoReparto.Conflicto, Mensaje = motivo, Codigo = CODIGO };
    }

    /// <summary>
    /// NestoAPI#844: lee la caja del repartidor (su CajaDefecto; sin ella no se mira nada) y aplica <see cref="ReglaCajaSinCerrar"/>.
    /// Nunca bloquea por un fallo propio: si no se puede leer la caja, ELMAH y adelante. Una caja que hoy ya estaba limpia no se
    /// vuelve a leer en todo el día (la regla mira el dinero de días anteriores, que ya no cambia): así solo cuesta la primera
    /// carga del día, no cada bulto.
    /// </summary>
    public class ControlCajaReparto
    {
        private static readonly ConcurrentDictionary<string, DateTime> LimpiasHoy = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly ILectorParametrosUsuario parametros;
        private readonly Func<IInformesService> informes;
        private readonly Func<DateTime> ahora;
        private readonly ConcurrentDictionary<string, DateTime> limpias;
        private readonly Action<Exception> log;

        /// <param name="limpias">Las cajas ya limpias hoy (caja → día). Null: la compartida de la aplicación.</param>
        public ControlCajaReparto(ILectorParametrosUsuario parametros = null, Func<IInformesService> informes = null, Func<DateTime> ahora = null,
            ConcurrentDictionary<string, DateTime> limpias = null, Action<Exception> log = null)
        {
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.informes = informes ?? (() => new InformesService());
            this.ahora = ahora ?? (() => DateTime.Now);
            this.limpias = limpias ?? LimpiasHoy;
            this.log = log ?? ElmahHelper.Log;
        }

        /// <summary>Terminar la jornada: el motivo para no dejarle, o null.</summary>
        public async Task<string> MotivoParaNoTerminar(string usuario)
        {
            string caja = string.IsNullOrWhiteSpace(usuario) ? null : CajaReparto.CajaDelUsuario(parametros, usuario);
            if (caja == null)
            {
                return null;
            }
            try
            {
                MisCobrosRepartoDTO leida = await CajaReparto.Leer(informes(), caja, ahora().Date).ConfigureAwait(false);
                return ReglaCajaSinCerrar.MotivoParaNoTerminar(leida?.Empresas);
            }
            catch (Exception ex)
            {
                Apuntar(ex, usuario, caja, "terminar la jornada");
                return null;
            }
        }

        /// <summary>
        /// La carga, la recogida o el cobro: el motivo para no dejarle, o null. Lo hecho otro día que llega tarde por la cola
        /// (<paramref name="fechaHora"/> de antes de hoy) pasa siempre: es de aquel día.
        /// </summary>
        public async Task<string> MotivoParaNoEmpezar(string usuario, DateTime? fechaHora)
        {
            DateTime hoy = ahora().Date;
            if (fechaHora.HasValue && fechaHora.Value.Date < hoy)
            {
                return null;
            }
            string caja = string.IsNullOrWhiteSpace(usuario) ? null : CajaReparto.CajaDelUsuario(parametros, usuario);
            if (caja == null)
            {
                return null;
            }
            if (limpias.TryGetValue(caja, out DateTime dia) && dia == hoy)
            {
                return null;
            }
            try
            {
                MisCobrosRepartoDTO leida = await CajaReparto.Leer(informes(), caja, hoy).ConfigureAwait(false);
                string motivo = ReglaCajaSinCerrar.MotivoParaNoEmpezar(leida?.Empresas);
                if (motivo == null)
                {
                    limpias[caja] = hoy;
                    foreach (string vieja in limpias.Where(kv => kv.Value != hoy).Select(kv => kv.Key).ToList())
                    {
                        _ = limpias.TryRemove(vieja, out _);
                    }
                }
                return motivo;
            }
            catch (Exception ex)
            {
                Apuntar(ex, usuario, caja, "empezar el día");
                return null;
            }
        }

        private void Apuntar(Exception ex, string usuario, string caja, string que)
        {
            try
            {
                log(new Exception($"[Cierre de caja #844] No se ha podido leer la caja {caja} de {CajaReparto.SinDominio(usuario)} para {que} " +
                    $"(se le deja seguir): {ex.Message}", ex));
            }
            catch
            {
                // Apuntarlo nunca bloquea al repartidor
            }
        }
    }

    // ================================================================ el cierre que no cuadra

    /// <summary>NestoAPI#844: un cierre que no cuadra (lo que se avisa a Administración).</summary>
    public class DescuadreCierreCaja
    {
        public string Usuario { get; set; }
        public string Caja { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Lo que ha contado (y tecleado) el repartidor.</summary>
        public decimal Contado { get; set; }
        /// <summary>Lo que dice la contabilidad (empresas 1 y 3).</summary>
        public decimal Saldo { get; set; }
        public List<SaldoCajaEmpresa> Saldos { get; set; } = new List<SaldoCajaEmpresa>();
        /// <summary>La versión de Ariadna del repartidor (X-Ariadna-Version), si se sabe.</summary>
        public string VersionAriadna { get; set; }
    }

    /// <summary>NestoAPI#844: a quién se avisa del descuadre y cómo se cierra el aviso. Nunca lanza.</summary>
    public interface IAvisadorCierreCaja
    {
        /// <summary>Campana URGENTE de Nesto a Administración y ELMAH.</summary>
        Task Descuadre(DescuadreCierreCaja descuadre);

        /// <summary>La caja se ha cerrado: «notificacionResuelta» para que la ventana roja se cierre en todos los Nesto.</summary>
        void Cerrada(string caja, DateTime fecha, string usuario);
    }

    /// <summary>NestoAPI#844: los textos (de usted) del aviso urgente del descuadre.</summary>
    public static class TextosCierreCajaDescuadrado
    {
        public const string TIPO_NOTIFICACION = "CierreCajaDescuadrado";
        private const char SEPARADOR = '|';

        /// <summary>«57000032|20261109»: una por caja y día (los intentos del mismo día son la misma ventana en Nesto).</summary>
        public static string Referencia(string caja, DateTime fecha)
            => $"{caja?.Trim()}{SEPARADOR}{fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

        public static bool LeerReferencia(string referencia, out string caja, out DateTime fecha)
        {
            caja = null;
            fecha = DateTime.MinValue;
            string[] partes = (referencia ?? string.Empty).Split(SEPARADOR);
            if (partes.Length != 2 || string.IsNullOrWhiteSpace(partes[0])
                || !DateTime.TryParseExact(partes[1].Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            {
                return false;
            }
            caja = partes[0].Trim();
            return true;
        }

        /// <summary>«le faltan 5,40 €» / «le sobran 2,00 €».</summary>
        public static string Diferencia(decimal contado, decimal saldo)
        {
            decimal diferencia = Math.Round(contado - saldo, 2, MidpointRounding.AwayFromZero);
            return diferencia < 0 ? $"le faltan {ReglaCierreCaja.Euros(-diferencia)}" : $"le sobran {ReglaCierreCaja.Euros(diferencia)}";
        }

        public static NotificacionPushDTO Aviso(DescuadreCierreCaja d)
        {
            string quien = CajaReparto.SinDominio(d.Usuario) ?? "El repartidor";
            string detalle = string.Join(", ", (d.Saldos ?? new List<SaldoCajaEmpresa>())
                .Select(s => $"empresa {s.Empresa?.Trim()}: {ReglaCierreCaja.Euros(s.Saldo)}"));
            return new NotificacionPushDTO
            {
                Titulo = $"Cierre de caja que no cuadra: {quien} ha contado {ReglaCierreCaja.Euros(d.Contado)} y su caja tiene {ReglaCierreCaja.Euros(d.Saldo)}",
                Cuerpo = $"{quien} ha intentado cerrar su caja {d.Caja?.Trim()} con {ReglaCierreCaja.Euros(d.Contado)}, pero la contabilidad dice " +
                    $"{ReglaCierreCaja.Euros(d.Saldo)}" + (detalle.Length == 0 ? string.Empty : $" ({detalle})") +
                    $": {Diferencia(d.Contado, d.Saldo)}. La caja NO se ha cerrado. Llámele y revísenlo juntos." +
                    (string.IsNullOrWhiteSpace(d.VersionAriadna) ? string.Empty : $" (Ariadna {d.VersionAriadna.Trim()})"),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    [NotificacionesUrgentes.DATO_TIPO] = TIPO_NOTIFICACION,
                    [NotificacionesUrgentes.DATO_REFERENCIA] = Referencia(d.Caja, d.Fecha),
                    ["caja"] = d.Caja?.Trim() ?? string.Empty,
                    ["repartidor"] = quien,
                    ["fecha"] = d.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["importe"] = d.Contado.ToString("0.00", CultureInfo.InvariantCulture),
                    ["saldo"] = d.Saldo.ToString("0.00", CultureInfo.InvariantCulture),
                    ["versionAriadna"] = d.VersionAriadna?.Trim() ?? string.Empty
                }
            };
        }
    }

    /// <summary>NestoAPI#844: el de producción (buzón de Nesto de los de Administración; urgente por su tipo, #775).</summary>
    public class AvisadorCierreCaja : IAvisadorCierreCaja
    {
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, List<string>> miembros;
        private readonly IAvisosTiempoReal tiempoReal;
        private readonly Action<Exception> log;

        public AvisadorCierreCaja() : this(null, null, null, null)
        {
        }

        internal AvisadorCierreCaja(IServicioNotificacionesPush notificaciones, Func<string, List<string>> miembros, IAvisosTiempoReal tiempoReal,
            Action<Exception> log)
        {
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.miembros = miembros ?? MiembrosGrupoDominio.Leer;
            this.tiempoReal = tiempoReal ?? new AvisosTiempoReal();
            this.log = log ?? ElmahHelper.Log;
        }

        /// <summary>Los de Administración, con dominio y sin repetidos.</summary>
        internal List<string> Destinatarios()
            => (miembros(Constantes.GruposSeguridad.ADMINISTRACION) ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public async Task Descuadre(DescuadreCierreCaja descuadre)
        {
            if (descuadre == null)
            {
                return;
            }
            NotificacionPushDTO aviso = TextosCierreCajaDescuadrado.Aviso(descuadre);
            Apuntar(new Exception($"[Cierre de caja #844] {aviso.Cuerpo}"));
            try
            {
                foreach (string usuario in Destinatarios())
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, aviso).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Apuntar(new Exception($"[Cierre de caja #844] No se ha podido avisar a Administración del descuadre de la caja {descuadre.Caja}: {ex.Message}", ex));
            }
        }

        public void Cerrada(string caja, DateTime fecha, string usuario)
        {
            try
            {
                List<string> destinatarios = Destinatarios();
                // El aviso pudo ser de hoy o de ayer (el urgente vive 24 h)
                foreach (DateTime dia in new[] { fecha.Date, fecha.Date.AddDays(-1) })
                {
                    tiempoReal.NotificacionResuelta(destinatarios, new NotificacionResueltaDTO
                    {
                        Tipo = TextosCierreCajaDescuadrado.TIPO_NOTIFICACION,
                        Referencia = TextosCierreCajaDescuadrado.Referencia(caja, dia),
                        Respondio = CajaReparto.SinDominio(usuario),
                        Resultado = "Cerrada"
                    });
                }
            }
            catch (Exception ex)
            {
                Apuntar(new Exception($"[Cierre de caja #844] No se ha podido cerrar en Nesto el aviso de la caja {caja}: {ex.Message}", ex));
            }
        }

        private void Apuntar(Exception ex)
        {
            try
            {
                log(ex);
            }
            catch
            {
                // Avisar nunca tumba el cierre
            }
        }
    }

    /// <summary>NestoAPI#844: lo que mira el comprobador de los avisos urgentes de un descuadre.</summary>
    public interface IEstadoCajaReparto
    {
        /// <summary>El saldo de la caja ahora (empresas 1 y 3).</summary>
        Task<decimal> Saldo(string caja);

        /// <summary>Si desde ese día se ha hecho algún cierre de esa caja (Modificaciones, Tabla «CierreCajaReparto»).</summary>
        Task<bool> CerradaDesde(string caja, DateTime desde);
    }

    /// <summary>
    /// NestoAPI#844: un aviso de descuadre sigue pendiente mientras la caja tenga saldo y no se haya cerrado desde aquel día (si
    /// Administración la deja a cero con un traspaso a mano, o el repartidor la cierra bien, deja de sonar).
    /// </summary>
    public class ComprobadorCierresCajaDescuadrados : IComprobadorUrgentesPendientes
    {
        private readonly IEstadoCajaReparto estado;

        public ComprobadorCierresCajaDescuadrados(IEstadoCajaReparto estado)
        {
            this.estado = estado ?? throw new ArgumentNullException(nameof(estado));
        }

        public string Tipo => TextosCierreCajaDescuadrado.TIPO_NOTIFICACION;

        public async Task<ISet<string>> Pendientes(IReadOnlyCollection<string> referencias)
        {
            var pendientes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string referencia in (referencias ?? new List<string>()).Distinct())
            {
                if (!TextosCierreCajaDescuadrado.LeerReferencia(referencia, out string caja, out DateTime fecha))
                {
                    continue;
                }
                if (await estado.CerradaDesde(caja, fecha).ConfigureAwait(false))
                {
                    continue;
                }
                if (Math.Round(await estado.Saldo(caja).ConfigureAwait(false), 2, MidpointRounding.AwayFromZero) != 0)
                {
                    _ = pendientes.Add(referencia);
                }
            }
            return pendientes;
        }
    }

    public class EstadoCajaReparto : IEstadoCajaReparto
    {
        private readonly NVEntities db;
        private readonly Func<IInformesService> informes;
        private readonly Func<DateTime> ahora;

        public EstadoCajaReparto(NVEntities db, Func<IInformesService> informes = null, Func<DateTime> ahora = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.informes = informes ?? (() => new InformesService(db));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<decimal> Saldo(string caja)
            => (await CajaReparto.Leer(informes(), caja, ahora().Date).ConfigureAwait(false))?.SaldoFinal ?? 0m;

        public async Task<bool> CerradaDesde(string caja, DateTime desde)
        {
            // La auditoría del cierre (AuditoriaCierreCaja en JSON) lleva «"Cuenta":"57000032"»
            string marca = "\"Cuenta\":\"" + caja?.Trim() + "\"";
            DateTime dia = desde.Date;
            return await db.Modificaciones
                .AnyAsync(m => m.Tabla == RepositorioCierresCajaReparto.TABLA_MODIFICACIONES && m.Fecha >= dia && m.Nuevo.Contains(marca))
                .ConfigureAwait(false);
        }
    }
}
