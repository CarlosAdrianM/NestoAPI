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
using NestoAPI.Models;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#766 (Ariadna#67): POST api/RepartoPropio/Cobros. El repartidor cobra en EFECTIVO a un cliente algo que debía,
    /// como en la caja de Nesto: los efectos que elige (o ninguno) y el importe; lo que sobra queda a cuenta.
    /// </summary>
    public class CobroDeudaRepartoDTO
    {
        public string Cliente { get; set; }
        /// <summary>El contacto del «a cuenta» (sin efectos). Vacío: el del primer efecto o «0».</summary>
        public string Contacto { get; set; }
        /// <summary>Los Nº Orden de ExtractoCliente que paga, en el orden en que se eligieron. Vacío: todo a cuenta.</summary>
        public List<int> Efectos { get; set; }
        public decimal Importe { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>Cuándo se cobró en el aparato (hora local). Da la fecha del asiento; nunca después de hoy.</summary>
        public DateTime? FechaHora { get; set; }
    }

    public class ResultadoCobroDeudaDTO
    {
        public int Asiento { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Lo que no ha ido a ningún efecto (queda a cuenta del cliente).</summary>
        public decimal ImporteACuenta { get; set; }
        /// <summary>Cuántos efectos se han liquidado (entera o parcialmente).</summary>
        public int Efectos { get; set; }
        public string Caja { get; set; }
        /// <summary>Lo que conviene saber (p. ej. un efecto que ya no estaba pendiente). Null si nada.</summary>
        public string Aviso { get; set; }
    }

    /// <summary>Un efecto de ExtractoCliente, con lo que hace falta para pagarlo.</summary>
    public class EfectoCobroReparto
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string TipoApunte { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public decimal Importe { get; set; }
        public decimal ImportePendiente { get; set; }
        public DateTime Vencimiento { get; set; }
        public string Delegacion { get; set; }
        public string FormaVenta { get; set; }
        public string Ruta { get; set; }
        public string Vendedor { get; set; }
        public string Estado { get; set; }
    }

    /// <summary>La caja del usuario y los parámetros con los que Nesto monta los apuntes de su caja.</summary>
    public class CajaUsuarioCobro
    {
        public string Caja { get; set; }
        public string Diario { get; set; }
        public string Delegacion { get; set; }
        public string FormaVenta { get; set; }
        /// <summary>El usuario sin dominio (va en el concepto: «… a Miguel»).</summary>
        public string UsuarioSinDominio { get; set; }
    }

    public class ApuntesCobroDeuda
    {
        public List<PreContabilidad> Lineas { get; set; } = new List<PreContabilidad>();
        public decimal ImporteACuenta { get; set; }
        public int EfectosLiquidados { get; set; }
    }

    /// <summary>
    /// Los apuntes del cobro, la MISMA regla que <c>CajasViewModel.ContabilizarCobro</c> de Nesto (Modulos/Cajas), con efectivo
    /// contra la caja del usuario: un pago por efecto (con Liquidado) repartiendo el importe (los abonos primero: suman a lo que hay
    /// que repartir), lo que sobra «a cuenta» (empresa 1) y una contrapartida a la caja por empresa con lo cobrado en ella.
    /// </summary>
    public static class ConstructorApuntesCobroDeuda
    {
        internal const string DOCUMENTO_A_CUENTA = "A CUENTA";

        public static ApuntesCobroDeuda Construir(string cliente, string contacto, IList<EfectoCobroReparto> efectos, decimal importe,
            DateTime fecha, CajaUsuarioCobro caja, string usuario, DateTime ahora)
        {
            var resultado = new ApuntesCobroDeuda();
            List<EfectoCobroReparto> deudas = (efectos ?? new List<EfectoCobroReparto>()).ToList();
            decimal restante = Math.Round(importe, 2, MidpointRounding.AwayFromZero);
            string aQuien = string.IsNullOrWhiteSpace(caja.UsuarioSinDominio) ? string.Empty : $" a {caja.UsuarioSinDominio}";
            var usadas = new List<EfectoCobroReparto>();

            // OrderBy es estable: dentro de cada grupo, el orden en que se eligieron
            foreach (EfectoCobroReparto deuda in deudas.OrderBy(d => d.ImportePendiente < 0 ? 0 : 1))
            {
                decimal importeApunte;
                string aCuenta = restante < deuda.ImportePendiente ? " a cta." : string.Empty;
                if (deuda.ImportePendiente >= restante)
                {
                    importeApunte = restante;
                    restante = 0;
                }
                else
                {
                    importeApunte = deuda.ImportePendiente;
                    restante -= deuda.ImportePendiente;
                }
                string pagoODevolucion = importeApunte > 0 ? "S/Pago" : "Devolución";
                resultado.Lineas.Add(new PreContabilidad
                {
                    Empresa = deuda.Empresa,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    Nº_Cuenta = deuda.Cliente,
                    Contacto = deuda.Contacto,
                    Concepto = $"{pagoODevolucion} {NombreTipo(deuda)} {deuda.Documento}/{deuda.Efecto}{aCuenta}{aQuien}",
                    Debe = importeApunte < 0 ? -importeApunte : 0,
                    Haber = importeApunte > 0 ? importeApunte : 0,
                    Fecha = fecha,
                    FechaVto = deuda.Vencimiento,
                    Nº_Documento = deuda.Documento,
                    Efecto = deuda.Efecto,
                    Asiento = 1,
                    Diario = caja.Diario,
                    Delegación = caja.Delegacion,
                    FormaVenta = deuda.FormaVenta,
                    FormaPago = Constantes.FormasPago.EFECTIVO,
                    Ruta = deuda.Ruta,
                    Origen = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Liquidado = deuda.Id,
                    Vendedor = deuda.Vendedor,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                });
                usadas.Add(deuda);
                if (restante <= 0)
                {
                    break;
                }
            }
            resultado.EfectosLiquidados = usadas.Count;
            resultado.ImporteACuenta = restante;

            if (restante != 0)
            {
                resultado.Lineas.Add(new PreContabilidad
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    Nº_Cuenta = cliente,
                    Contacto = contacto,
                    Concepto = $"S/Pago a cuenta{aQuien}",
                    Debe = restante < 0 ? -restante : 0,
                    Haber = restante > 0 ? restante : 0,
                    Fecha = fecha,
                    FechaVto = fecha,
                    Nº_Documento = DOCUMENTO_A_CUENTA,
                    Asiento = 1,
                    Diario = caja.Diario,
                    Delegación = caja.Delegacion,
                    FormaVenta = caja.FormaVenta,
                    FormaPago = Constantes.FormasPago.EFECTIVO,
                    Origen = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                });
            }

            // Nesto#514: una contrapartida por empresa, cada una por lo cobrado EN ESA empresa
            foreach (string empresa in resultado.Lineas.Select(l => l.Empresa).Distinct().ToList())
            {
                decimal importeEmpresa = resultado.Lineas.Where(l => l.Empresa == empresa).Sum(l => l.Haber - l.Debe);
                List<EfectoCobroReparto> deudasEmpresa = usadas.Where(d => d.Empresa == empresa).ToList();
                EfectoCobroReparto mayor = deudasEmpresa.OrderByDescending(d => d.Importe).FirstOrDefault();
                string pagoODevolucion = importeEmpresa > 0 ? "Pago" : "Devolución";
                var contrapartida = new PreContabilidad
                {
                    Empresa = empresa,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                    Nº_Cuenta = caja.Caja,
                    Debe = importeEmpresa > 0 ? importeEmpresa : 0,
                    Haber = importeEmpresa < 0 ? -importeEmpresa : 0,
                    Fecha = fecha,
                    Asiento = 1,
                    Diario = caja.Diario,
                    FormaPago = Constantes.FormasPago.EFECTIVO,
                    Origen = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                };
                if (mayor != null)
                {
                    string coletilla = string.Empty;
                    if (mayor.ImportePendiente < importeEmpresa || deudasEmpresa.Count > 1)
                    {
                        coletilla = " y otros";
                    }
                    else if (mayor.ImportePendiente > importeEmpresa)
                    {
                        coletilla = " a cta.";
                    }
                    contrapartida.Concepto = $"{pagoODevolucion} c/{mayor.Cliente}/{mayor.Contacto} {NombreTipo(mayor)} {mayor.Documento}/{mayor.Efecto}{coletilla}";
                    contrapartida.FechaVto = mayor.Vencimiento;
                    contrapartida.Nº_Documento = mayor.Documento;
                    contrapartida.Delegación = mayor.Delegacion;
                    contrapartida.FormaVenta = mayor.FormaVenta;
                }
                else
                {
                    contrapartida.Concepto = $"{pagoODevolucion} c/{cliente}/{contacto} a cuenta{aQuien}";
                    contrapartida.FechaVto = fecha;
                    contrapartida.Nº_Documento = DOCUMENTO_A_CUENTA;
                    contrapartida.Delegación = caja.Delegacion;
                    contrapartida.FormaVenta = caja.FormaVenta;
                }
                resultado.Lineas.Add(contrapartida);
            }

            foreach (PreContabilidad linea in resultado.Lineas.Where(l => l.Concepto != null && l.Concepto.Length > 50))
            {
                linea.Concepto = linea.Concepto.Substring(0, 50);
            }
            return resultado;
        }

        private static string NombreTipo(EfectoCobroReparto deuda)
            => deuda.TipoApunte?.Trim() == Constantes.ExtractosCliente.TiposApunte.IMPAGADO ? "Impagado" : "Factura";
    }

    /// <summary>Lo que el cobro necesita de la base de datos (fuera de los tests).</summary>
    public interface IRepositorioCobrosReparto
    {
        /// <summary>El cobro ya registrado con esa clave, o null.</summary>
        Task<ResultadoCobroDeudaDTO> LeerPorClave(Guid clave);
        /// <summary>Esos efectos de ExtractoCliente (los que no existen no vienen).</summary>
        Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids);
        /// <summary>
        /// En UNA transacción: las líneas a PreContabilidad, el diario contabilizado (prdContabilizar, por empresa) y la marca de la
        /// clave. Devuelve el asiento (el de la empresa 1 si hay varias).
        /// </summary>
        /// <exception cref="ClaveIdempotenciaRepetidaException">Otra petición ya registró esa clave.</exception>
        Task<int> Registrar(Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCobroDeudaDTO resultado);
    }

    public class ServicioCobrosReparto
    {
        public const string MENSAJE_SIN_CAJA = "No tiene caja asignada (parámetro CajaDefecto): pida a Administración que se la ponga " +
            "para poder cobrar.";
        internal const string PARAMETRO_DIARIO = "DiarioCaja";
        internal const string PARAMETRO_DELEGACION = "DelegaciónDefecto";
        internal const string PARAMETRO_FORMA_VENTA = "FormaVentaDefecto";

        private readonly IRepositorioCobrosReparto repositorio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly Func<DateTime> ahora;

        public ServicioCobrosReparto(IRepositorioCobrosReparto repositorio, ILectorParametrosUsuario parametros = null, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<ResultadoReparto<ResultadoCobroDeudaDTO>> Cobrar(CobroDeudaRepartoDTO peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return Invalido("Indique el cliente al que cobra.");
            }
            if (peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la clave de idempotencia del cobro.");
            }
            decimal importe = Math.Round(peticion.Importe, 2, MidpointRounding.AwayFromZero);
            if (importe <= 0)
            {
                return Invalido("El importe cobrado tiene que ser mayor que cero.");
            }

            ResultadoCobroDeudaDTO yaCobrado = await repositorio.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (yaCobrado != null)
            {
                return ResultadoReparto<ResultadoCobroDeudaDTO>.Bien(yaCobrado);
            }

            string sinDominio = string.IsNullOrWhiteSpace(usuario) ? null : usuario.Substring(usuario.IndexOf('\\') + 1).Trim();
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            // La caja, SOLO de su fila (la de «(defecto)» sería la caja de otro), como los reembolsos al entregar
            string cuentaCaja = sinDominio == null ? null : parametros.LeerParametroSinDefecto(empresa, sinDominio, ServicioRepartoPropio.PARAMETRO_CAJA)?.Trim();
            if (string.IsNullOrEmpty(cuentaCaja))
            {
                return Invalido(MENSAJE_SIN_CAJA);
            }
            var caja = new CajaUsuarioCobro
            {
                Caja = cuentaCaja,
                Diario = parametros.LeerParametro(empresa, sinDominio, PARAMETRO_DIARIO)?.Trim(),
                Delegacion = parametros.LeerParametro(empresa, sinDominio, PARAMETRO_DELEGACION)?.Trim(),
                FormaVenta = parametros.LeerParametro(empresa, sinDominio, PARAMETRO_FORMA_VENTA)?.Trim(),
                UsuarioSinDominio = sinDominio
            };
            if (string.IsNullOrEmpty(caja.Diario))
            {
                return Invalido("No tiene diario de caja (parámetro DiarioCaja): pida a Administración que se lo ponga para poder cobrar.");
            }

            string cliente = peticion.Cliente.Trim();
            List<int> ids = (peticion.Efectos ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
            List<EfectoCobroReparto> leidos = ids.Count == 0 ? new List<EfectoCobroReparto>()
                : await repositorio.LeerEfectos(ids).ConfigureAwait(false) ?? new List<EfectoCobroReparto>();
            EfectoCobroReparto deOtro = leidos.FirstOrDefault(e => !string.Equals(e.Cliente?.Trim(), cliente, StringComparison.OrdinalIgnoreCase));
            if (deOtro != null)
            {
                return Invalido($"El efecto {deOtro.Documento?.Trim()}/{deOtro.Efecto?.Trim()} es del cliente {deOtro.Cliente?.Trim()}, no del {cliente}.");
            }
            // Lo que ya no está pendiente (lo cobró otro mientras este cobro esperaba en la cola) no falla: el dinero está en la
            // caja, así que lo que le tocaba queda a cuenta.
            var pendientes = new List<EfectoCobroReparto>();
            var yaNoPendientes = new List<int>();
            foreach (int id in ids)
            {
                EfectoCobroReparto efecto = leidos.FirstOrDefault(e => e.Id == id);
                if (efecto == null || efecto.ImportePendiente == 0
                    || efecto.Estado?.Trim() == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
                {
                    yaNoPendientes.Add(id);
                }
                else
                {
                    pendientes.Add(Limpio(efecto));
                }
            }

            string contacto = !string.IsNullOrWhiteSpace(peticion.Contacto) ? peticion.Contacto.Trim()
                : pendientes.FirstOrDefault()?.Contacto ?? leidos.FirstOrDefault()?.Contacto?.Trim() ?? "0";
            DateTime hoy = ahora();
            DateTime fecha = (peticion.FechaHora.HasValue && peticion.FechaHora.Value.Date <= hoy.Date ? peticion.FechaHora.Value : hoy).Date;

            ApuntesCobroDeuda apuntes = ConstructorApuntesCobroDeuda.Construir(cliente, contacto, pendientes, importe, fecha, caja, usuario, hoy);
            var resultado = new ResultadoCobroDeudaDTO
            {
                Importe = importe,
                ImporteACuenta = apuntes.ImporteACuenta,
                Efectos = apuntes.EfectosLiquidados,
                Caja = cuentaCaja,
                Aviso = yaNoPendientes.Count == 0 ? null
                    : (yaNoPendientes.Count == 1 ? "Un efecto ya no estaba pendiente" : $"{yaNoPendientes.Count} efectos ya no estaban pendientes")
                      + ": lo que les tocaba queda a cuenta del cliente."
            };
            try
            {
                resultado.Asiento = await repositorio.Registrar(peticion.ClaveIdempotencia, usuario, apuntes.Lineas, resultado).ConfigureAwait(false);
            }
            catch (ClaveIdempotenciaRepetidaException)
            {
                // Dos peticiones con la misma clave a la vez: la otra ganó; se devuelve lo suyo
                ResultadoCobroDeudaDTO otra = await repositorio.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
                return otra != null ? ResultadoReparto<ResultadoCobroDeudaDTO>.Bien(otra)
                    : ResultadoReparto<ResultadoCobroDeudaDTO>.Mal(TipoResultadoReparto.Conflicto, "Este cobro ya se está registrando.");
            }
            catch (NestoBusinessException ex)
            {
                return Invalido(ex.Message);
            }
            if (resultado.Asiento <= 0)
            {
                return Invalido("No se ha podido contabilizar el cobro.");
            }
            return ResultadoReparto<ResultadoCobroDeudaDTO>.Bien(resultado);
        }

        private static EfectoCobroReparto Limpio(EfectoCobroReparto e) => new EfectoCobroReparto
        {
            Id = e.Id,
            Empresa = e.Empresa?.Trim(),
            Cliente = e.Cliente?.Trim(),
            Contacto = e.Contacto?.Trim(),
            TipoApunte = e.TipoApunte?.Trim(),
            Documento = e.Documento?.Trim(),
            Efecto = e.Efecto?.Trim(),
            Importe = e.Importe,
            ImportePendiente = e.ImportePendiente,
            Vencimiento = e.Vencimiento,
            Delegacion = e.Delegacion?.Trim(),
            FormaVenta = e.FormaVenta?.Trim(),
            Ruta = e.Ruta?.Trim(),
            Vendedor = e.Vendedor?.Trim(),
            Estado = e.Estado?.Trim()
        };

        private static ResultadoReparto<ResultadoCobroDeudaDTO> Invalido(string mensaje)
            => ResultadoReparto<ResultadoCobroDeudaDTO>.Mal(TipoResultadoReparto.Invalido, mensaje);
    }

    /// <summary>
    /// La clave del cobro se apunta en <c>Modificaciones</c> (Tabla «CobroReparto», Anterior = la clave, Nuevo = el resultado en
    /// JSON) en la misma transacción que el asiento: sin script de BD. Un <c>sp_getapplock</c> por clave evita que dos reintentos a
    /// la vez contabilicen dos veces.
    /// </summary>
    public class RepositorioCobrosReparto : IRepositorioCobrosReparto
    {
        internal const string TABLA_MODIFICACIONES = "CobroReparto";

        private readonly NVEntities db;
        private readonly IContabilidadService contabilidad;

        public RepositorioCobrosReparto(NVEntities db, IContabilidadService contabilidad = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.contabilidad = contabilidad ?? new ContabilidadService();
        }

        public async Task<ResultadoCobroDeudaDTO> LeerPorClave(Guid clave)
        {
            string texto = clave.ToString("D", CultureInfo.InvariantCulture);
            string nuevo = (await db.Modificaciones
                .Where(m => m.Tabla == TABLA_MODIFICACIONES && m.Anterior == texto)
                .Select(m => m.Nuevo)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            return nuevo == null ? null : JsonConvert.DeserializeObject<ResultadoCobroDeudaDTO>(nuevo);
        }

        public Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids)
        {
            List<int> lista = ids.ToList();
            return db.ExtractosCliente
                .Where(e => lista.Contains(e.Nº_Orden))
                .Select(e => new EfectoCobroReparto
                {
                    Id = e.Nº_Orden,
                    Empresa = e.Empresa,
                    Cliente = e.Número,
                    Contacto = e.Contacto,
                    TipoApunte = e.TipoApunte,
                    Documento = e.Nº_Documento,
                    Efecto = e.Efecto,
                    Importe = e.Importe,
                    ImportePendiente = e.ImportePdte,
                    Vencimiento = e.FechaVto ?? e.Fecha,
                    Delegacion = e.Delegación,
                    FormaVenta = e.FormaVenta,
                    Ruta = e.Ruta,
                    Vendedor = e.Vendedor,
                    Estado = e.Estado
                })
                .ToListAsync();
        }

        public async Task<int> Registrar(Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCobroDeudaDTO resultado)
        {
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
                    int asiento = 0;
                    foreach (string empresa in lineas.Select(l => l.Empresa).Distinct())
                    {
                        int asientoEmpresa = await contabilidad.ContabilizarDiario(db, empresa, lineas[0].Diario, usuario).ConfigureAwait(false);
                        if (asientoEmpresa <= 0)
                        {
                            throw new NestoBusinessException("No se ha podido contabilizar el cobro.");
                        }
                        if (asiento == 0 || empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                        {
                            asiento = asientoEmpresa;
                        }
                    }
                    resultado.Asiento = asiento;

                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = TABLA_MODIFICACIONES,
                        Anterior = texto,
                        Nuevo = JsonConvert.SerializeObject(resultado),
                        Usuario = usuario != null && usuario.Length > 50 ? usuario.Substring(0, 50) : usuario,
                        Fecha = DateTime.Now
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return asiento;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }
    }
}
