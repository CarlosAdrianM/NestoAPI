using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    // ============================================================================================================================
    // Cobro de la señal de un evento en Cajas (Carlos, 19/11/26; caso del cliente 33895). La tienda (efectivo o datáfono) o
    // Administración (transferencia) cobra la reserva desde la pestaña «Cobros» de Cajas, la misma de siempre, y queda todo como
    // cuando llega el enlace pagado: el cobro a cuenta con el documento «INS{id}» (el mismo que el del enlace) contra su caja, la
    // inscripción Pagada (en la MISMA transacción que el cobro), y después, por el camino de siempre (ConfirmadorSenalEvento), el
    // pedido y la factura exenta del curso, la liquidación del cobro contra el efecto de la factura y el cupón.
    // ============================================================================================================================

    /// <summary>GET api/Eventos/Inscripciones/SinPagar: una reserva de evento sin pagar, cobrable en Cajas.</summary>
    public class ReservaEventoSinPagarDTO
    {
        public int InscripcionId { get; set; }
        public int EventoId { get; set; }
        public string Evento { get; set; }
        public DateTime FechaEvento { get; set; }
        /// <summary>REI o ALC.</summary>
        public string Almacen { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public EstadoInscripcionEvento Estado { get; set; }
        public string EstadoTexto { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ModalidadInscripcionEvento Modalidad { get; set; }
        /// <summary>La señal de su modalidad: lo único que se puede cobrar (ni más ni menos).</summary>
        public decimal Importe { get; set; }
        public DateTime? FechaLimitePago { get; set; }
        /// <summary>«Señal Masterclass Rosácea (24/11)», para la lista de Cajas.</summary>
        public string Descripcion { get; set; }
        /// <summary>«INS123»: el documento del cobro en el extracto.</summary>
        public string Documento { get; set; }
        /// <summary>Se puede cobrar ahora en Cajas. Si no, <see cref="Motivo"/> dice por qué (de usted).</summary>
        public bool Cobrable { get; set; }
        public string Motivo { get; set; }
        /// <summary>
        /// Ya hay en el extracto un cobro «INS{id}» (un cobro de Cajas que se cortó antes de terminar): al cobrarla NO se vuelve a
        /// contabilizar, solo se termina lo que faltaba.
        /// </summary>
        public bool CobroYaApuntado { get; set; }
    }

    /// <summary>POST api/Eventos/Inscripciones/{id}/CobroCaja.</summary>
    public class CobrarSenalCajaDTO
    {
        /// <summary>EFC, TAR o TRN (las de Cajas) o Efectivo, Tarjeta, Transferencia.</summary>
        public string FormaPago { get; set; }
        /// <summary>Lo cobrado: tiene que ser la señal exacta (400 si no).</summary>
        public decimal Importe { get; set; }
        /// <summary>La cuenta de tesorería (57…) del cobro. Si no viene: CajaDefecto en efectivo; CuentaBancoTarjeta en tarjeta y transferencia.</summary>
        public string CuentaCobro { get; set; }
        /// <summary>Día del cobro (no futuro). Si no viene, hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>Opcionales: los de la caja del usuario (Nesto los manda). Si no vienen, los de sus parámetros.</summary>
        public string Diario { get; set; }
        public string Delegacion { get; set; }
        public string FormaVenta { get; set; }
        /// <summary>El «Concepto adicional» de Cajas (se añade al concepto de los apuntes, como en un cobro normal).</summary>
        public string ConceptoAdicional { get; set; }
    }

    /// <summary>Lo que contesta el cobro de la señal en Cajas.</summary>
    public class ResultadoCobroSenalCajaDTO
    {
        public InscripcionEventoDTO Inscripcion { get; set; }
        /// <summary>El asiento del cobro (0 si ya estaba contabilizado de antes: no se ha vuelto a cobrar).</summary>
        public int Asiento { get; set; }
        public bool CobroYaEstaba { get; set; }
        public string Documento { get; set; }
        public decimal Importe { get; set; }
        public string FormaPago { get; set; }
        public string Cuenta { get; set; }
        public int? Pedido { get; set; }
        public string Factura { get; set; }
        /// <summary>EnCurso, Facturada, Error o NoAutomatizable (la de EventosInscripcionesCurso); null si no se ha podido leer.</summary>
        public string EstadoFacturacion { get; set; }
        public string EstadoLiquidacion { get; set; }
        public string Cupon { get; set; }
        public decimal? ImporteCupon { get; set; }
        /// <summary>Lo que se ha hecho, de usted, para enseñarlo tal cual.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>Las reglas del cobro de la señal en Cajas (sin base de datos).</summary>
    public static class ReglasCobroSenalCaja
    {
        public const string DIARIO_CAJA_ADMINISTRACION = "_Caja";
        public const string PREFIJO_TESORERIA = "57";
        internal const string PARAMETRO_CAJA = "CajaDefecto";
        internal const string PARAMETRO_CUENTA_TARJETA = "CuentaBancoTarjeta";
        internal const string PARAMETRO_DIARIO = "DiarioCaja";
        internal const string PARAMETRO_DELEGACION = "DelegaciónDefecto";
        internal const string PARAMETRO_FORMA_VENTA = "FormaVentaDefecto";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>EFC/TAR/TRN (Cajas) o Efectivo/Tarjeta/Transferencia. Null con cualquier otra.</summary>
        public static FormaPagoInscripcionEvento? FormaPago(string texto)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            switch (limpio.ToUpperInvariant())
            {
                case Constantes.FormasPago.EFECTIVO: return FormaPagoInscripcionEvento.Efectivo;
                case Constantes.FormasPago.TARJETA: return FormaPagoInscripcionEvento.Tarjeta;
                case Constantes.FormasPago.TRANSFERENCIA: return FormaPagoInscripcionEvento.Transferencia;
            }
            return ReglasInscripcionEvento.FormaPago(limpio);
        }

        /// <summary>La forma de pago de los apuntes (la de Cajas).</summary>
        public static string CodigoFormaPago(FormaPagoInscripcionEvento forma) => ReglasFacturacionCurso.FormaPagoPedido(forma.ToString());

        /// <summary>Se enseña en Cajas: sin pagar (Reservada o Caducada), de pago, con señal y de un evento de hoy en adelante.</summary>
        public static bool EsReservaSinPagar(EventoInscripcion inscripcion, Evento evento, DateTime hoy)
        {
            if (inscripcion == null || evento == null)
            {
                return false;
            }
            EstadoInscripcionEvento? estado = ReglasInscripcionEvento.Estado(inscripcion.Estado);
            if (estado != EstadoInscripcionEvento.Reservada && estado != EstadoInscripcionEvento.Caducada)
            {
                return false;
            }
            if (!ReglasInscripcionEvento.PuedePasar(estado.Value, EstadoInscripcionEvento.Pagada))
            {
                return false;
            }
            if (ReglasModalidadEvento.DeInscripcion(inscripcion) == ModalidadInscripcionEvento.Gratuita)
            {
                return false;
            }
            return ReglasModalidadEvento.ImporteDe(inscripcion, evento) > 0 && evento.Fecha.Date >= hoy.Date;
        }

        /// <summary>«Señal Masterclass Rosácea (24/11)» (con «no reembolsable» si lo es).</summary>
        public static string Descripcion(Evento evento, ModalidadInscripcionEvento modalidad) =>
            $"Señal {evento?.Titulo?.Trim()} ({evento?.Fecha:dd/MM})" +
            (modalidad == ModalidadInscripcionEvento.NoReembolsable ? ", no reembolsable" : string.Empty);

        /// <summary>
        /// Por qué no se puede cobrar en Cajas (de usted), o null. En este orden: ya hay señal por transferencia (#591), una
        /// transferencia propuesta como su señal (#615), el enlace ya cobrado, inscripción de un pedido de cursos (#691) y, al
        /// final, que el curso no se fuera a facturar solo.
        /// </summary>
        public static string MotivoNoCobrable(EventoInscripcion inscripcion, Evento evento, bool tieneSenalTransferencia,
            bool tienePropuestaSenal, bool enlaceCobrado, bool dePedidoDeCursos, int? pedidoDeCursos, string motivoFacturacion)
        {
            string titulo = evento?.Titulo?.Trim();
            if (tieneSenalTransferencia || inscripcion?.EventoSenalId != null)
            {
                return $"La señal de «{titulo}» ya está apuntada por transferencia (Administración la marcó como señal): no la cobre otra vez.";
            }
            if (tienePropuestaSenal)
            {
                return $"Hay una transferencia de esta clienta propuesta como señal de «{titulo}»: Administración tiene que confirmarla " +
                    "en Señales. No la cobre aquí.";
            }
            if (enlaceCobrado)
            {
                return $"La clienta ya ha pagado la señal de «{titulo}» con el enlace de pago: no la cobre otra vez. Si sigue como " +
                    "reservada, avise a Administración.";
            }
            if (dePedidoDeCursos)
            {
                return $"Esta inscripción va en el pedido de cursos{(pedidoDeCursos.HasValue ? " " + pedidoDeCursos.Value : string.Empty)}: se cobra con el enlace de ese pedido. Si la " +
                    "clienta paga en la tienda, avise a Administración.";
            }
            return string.IsNullOrWhiteSpace(motivoFacturacion) ? null : motivoFacturacion.Trim();
        }

        /// <summary>Carlos (19/11/26): la señal se cobra entera o no se cobra (nada de cobros a cuenta). Null si cuadra.</summary>
        public static string MotivoImporte(decimal cobrado, decimal senal, Evento evento)
        {
            decimal c = Math.Round(cobrado, 2, MidpointRounding.AwayFromZero);
            decimal s = Math.Round(senal, 2, MidpointRounding.AwayFromZero);
            if (c == s)
            {
                return null;
            }
            return $"La señal de «{evento?.Titulo?.Trim()}» es de {Euros(s)} € y ha puesto {Euros(c)} €: la señal se cobra entera o " +
                "no se cobra. Ponga el importe exacto.";
        }

        /// <summary>El texto final para Cajas (de usted).</summary>
        public static string Mensaje(ResultadoCobroSenalCajaDTO r, string evento)
        {
            if (r == null)
            {
                return null;
            }
            var partes = new List<string>
            {
                r.CobroYaEstaba
                    ? $"La señal de «{evento}» ya estaba cobrada ({r.Documento}): no se ha vuelto a cobrar; se ha terminado lo que faltaba."
                    : $"Cobrada la señal de «{evento}»: {Euros(r.Importe)} € ({TextoForma(r.FormaPago)}), asiento {r.Asiento}."
            };
            if (string.Equals(r.EstadoFacturacion, EstadosFacturacionCurso.FACTURADA, StringComparison.OrdinalIgnoreCase))
            {
                partes.Add($"Factura del curso {r.Factura} (pedido {r.Pedido}), cobrada.");
            }
            else if (!string.IsNullOrWhiteSpace(r.EstadoFacturacion))
            {
                partes.Add("La inscripción está pagada, pero la factura del curso no ha terminado de salir: se reintenta sola cada " +
                    "mañana y Administración ya está avisada.");
            }
            if (!string.IsNullOrWhiteSpace(r.Cupon))
            {
                partes.Add($"Cupón {r.Cupon}" + (r.ImporteCupon.HasValue ? $" de {Euros(r.ImporteCupon.Value)} €." : "."));
            }
            return string.Join(" ", partes);
        }

        private static string TextoForma(string forma)
        {
            switch (FormaPago(forma))
            {
                case FormaPagoInscripcionEvento.Efectivo: return "efectivo";
                case FormaPagoInscripcionEvento.Tarjeta: return "tarjeta";
                case FormaPagoInscripcionEvento.Transferencia: return "transferencia";
                default: return forma?.Trim();
            }
        }

        internal static string Euros(decimal importe) => importe.ToString("N2", es);
    }

    /// <summary>La caja con la que se cobra (la de la ventana de Cajas del usuario).</summary>
    public class CajaCobroSenal
    {
        public string Cuenta { get; set; }
        public string Diario { get; set; }
        public string Delegacion { get; set; }
        public string FormaVenta { get; set; }
        /// <summary>El usuario sin dominio para «… a Reina» (vacío en Administración, como en Cajas).</summary>
        public string AQuien { get; set; }
    }

    /// <summary>
    /// Los apuntes del cobro de la señal: los MISMOS que un cobro «a cuenta» de la ventana de Cajas
    /// (<c>CajasViewModel.ContabilizarCobro</c> de Nesto sin deudas marcadas): HABER al cliente (pago) y DEBE a la cuenta de cobro
    /// (caja, banco del datáfono o de la transferencia), mismo diario, delegación, forma de venta y forma de pago. Lo único
    /// distinto es el documento, «INS{id}» (el del enlace de pago), por el que se liquida contra la factura del curso.
    /// </summary>
    public static class ConstructorApuntesCobroSenalCaja
    {
        public static List<PreContabilidad> Construir(EventoInscripcion inscripcion, Evento evento, decimal importe, FormaPagoInscripcionEvento forma,
            DateTime fecha, CajaCobroSenal caja, string conceptoAdicional, string usuario, DateTime ahora)
        {
            string empresa = inscripcion.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = inscripcion.Cliente?.Trim();
            string contacto = inscripcion.Contacto?.Trim();
            string documento = ServicioInscripcionesEventos.DocumentoPago(inscripcion.Id);
            string formaPago = ReglasCobroSenalCaja.CodigoFormaPago(forma);
            decimal cobrado = Math.Round(importe, 2, MidpointRounding.AwayFromZero);
            string aQuien = string.IsNullOrWhiteSpace(caja.AQuien) ? string.Empty : " a " + caja.AQuien.Trim();
            string adicional = string.IsNullOrWhiteSpace(conceptoAdicional) ? string.Empty : " " + conceptoAdicional.Trim();

            var lineas = new List<PreContabilidad>
            {
                new PreContabilidad
                {
                    Empresa = empresa,
                    TipoApunte = Constantes.TiposExtractoCliente.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    Nº_Cuenta = cliente,
                    Contacto = contacto,
                    Concepto = Concepto(ConstructorApuntesCobroDeuda.ConAQuien($"S/Pago señal {documento} {evento?.Titulo?.Trim()}", aQuien), adicional),
                    Haber = cobrado,
                    Fecha = fecha.Date,
                    FechaVto = fecha.Date,
                    Nº_Documento = documento,
                    Asiento = 1,
                    Diario = caja.Diario,
                    Delegación = caja.Delegacion,
                    FormaVenta = caja.FormaVenta,
                    FormaPago = formaPago,
                    Origen = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                },
                new PreContabilidad
                {
                    Empresa = empresa,
                    TipoApunte = Constantes.TiposExtractoCliente.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                    Nº_Cuenta = caja.Cuenta,
                    Concepto = Concepto(ConstructorApuntesCobroDeuda.ConAQuien($"Pago c/{cliente}/{contacto} señal {documento}", aQuien), adicional),
                    Debe = cobrado,
                    Fecha = fecha.Date,
                    FechaVto = fecha.Date,
                    Nº_Documento = documento,
                    Asiento = 1,
                    Diario = caja.Diario,
                    Delegación = caja.Delegacion,
                    FormaVenta = caja.FormaVenta,
                    FormaPago = formaPago,
                    Origen = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                }
            };
            return lineas;
        }

        private static string Concepto(string concepto, string adicional)
        {
            string texto = (concepto ?? string.Empty) + adicional;
            return texto.Length > ConstructorApuntesCobroDeuda.MAXIMO_CONCEPTO ? texto.Substring(0, ConstructorApuntesCobroDeuda.MAXIMO_CONCEPTO) : texto;
        }
    }

    /// <summary>El cobro en la base de datos: transacción, contabilidad y lectura de lo ya cobrado.</summary>
    public interface IRepositorioCobroSenalCaja
    {
        /// <summary>Los documentos de esos (de «INS…») que ya tienen un cobro (TipoApunte pago) en el extracto del cliente.</summary>
        Task<HashSet<string>> DocumentosConCobro(string empresa, string cliente, IEnumerable<string> documentos);

        /// <summary>
        /// En UNA transacción, bajo un bloqueo por inscripción: comprueba que sigue sin pagar (si no, 409 y nada cambia); si no hay
        /// ya un cobro con ese documento, anula los enlaces de pago pendientes y contabiliza las líneas (prdContabilizar, por
        /// empresa, el núcleo de siempre); llama a <paramref name="anotarPagada"/> (la inscripción Pagada, guardada en el mismo
        /// contexto); apunta el rastro en Modificaciones y confirma. Si algo falla, no queda nada (ni cobro ni pagada).
        /// </summary>
        Task<RegistroCobroSenalCaja> Registrar(int inscripcionId, string empresa, string cliente, string documento, List<PreContabilidad> lineas,
            IReadOnlyCollection<int> enlacesAAnular, string usuario, string rastro, Func<Task> anotarPagada);
    }

    public class RegistroCobroSenalCaja
    {
        public int Asiento { get; set; }
        public bool CobroYaEstaba { get; set; }
    }

    public class RepositorioCobroSenalCaja : IRepositorioCobroSenalCaja
    {
        internal const string TABLA_MODIFICACIONES = "CobroSenalCaja";

        internal const string SQL_ESTADO = "SELECT Estado FROM dbo.EventosInscripciones WITH (UPDLOCK, HOLDLOCK) WHERE Id = @p0";

        private readonly NVEntities db;
        private readonly IContabilidadService contabilidad;

        public RepositorioCobroSenalCaja(NVEntities db, IContabilidadService contabilidad = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.contabilidad = contabilidad ?? new ContabilidadService();
        }

        public async Task<HashSet<string>> DocumentosConCobro(string empresa, string cliente, IEnumerable<string> documentos)
        {
            List<string> lista = (documentos ?? Enumerable.Empty<string>()).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct().ToList();
            if (!lista.Any())
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            List<string> con = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && e.Número == cliente && e.TipoApunte == Constantes.TiposExtractoCliente.PAGO
                    && lista.Contains(e.Nº_Documento))
                .Select(e => e.Nº_Documento).ToListAsync().ConfigureAwait(false);
            return new HashSet<string>(con.Where(d => d != null).Select(d => d.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        public async Task<RegistroCobroSenalCaja> Registrar(int inscripcionId, string empresa, string cliente, string documento, List<PreContabilidad> lineas,
            IReadOnlyCollection<int> enlacesAAnular, string usuario, string rastro, Func<Task> anotarPagada)
        {
            db.Database.CommandTimeout = 120;
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    var bloqueo = new SqlParameter("@r", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000",
                        bloqueo, new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = TABLA_MODIFICACIONES + ":" + inscripcionId }).ConfigureAwait(false);
                    if (bloqueo.Value is int r && r < 0)
                    {
                        throw new NestoBusinessException("Otra persona está cobrando ahora mismo esta señal. Espere un momento y recargue.")
                        { StatusCode = HttpStatusCode.Conflict };
                    }

                    string estado = (await db.Database.SqlQuery<string>(SQL_ESTADO, new SqlParameter("@p0", inscripcionId))
                        .ToListAsync().ConfigureAwait(false)).FirstOrDefault()?.Trim();
                    EstadoInscripcionEvento? leido = ReglasInscripcionEvento.Estado(estado);
                    if (leido != EstadoInscripcionEvento.Reservada && leido != EstadoInscripcionEvento.Caducada)
                    {
                        throw new NestoBusinessException($"La inscripción {inscripcionId} ya no está pendiente de pago " +
                            $"({(leido.HasValue ? ReglasInscripcionEvento.Texto(leido.Value).ToLowerInvariant() : "no existe")}): no se ha cobrado nada. Recargue.")
                        { StatusCode = HttpStatusCode.Conflict };
                    }

                    bool yaEstaba = await db.ExtractosCliente.AnyAsync(e => e.Empresa == empresa && e.Número == cliente
                        && e.Nº_Documento == documento && e.TipoApunte == Constantes.TiposExtractoCliente.PAGO).ConfigureAwait(false);
                    int asiento = 0;
                    if (!yaEstaba)
                    {
                        if (enlacesAAnular != null && enlacesAAnular.Any())
                        {
                            _ = await Pagos.AnulacionEnlacesPago.Anular(db, empresa, enlacesAAnular).ConfigureAwait(false);
                        }
                        _ = await contabilidad.CrearLineas(db, lineas).ConfigureAwait(false);
                        // El mismo núcleo que el cobro de la caja y el del reparto (un prdContabilizar por empresa y diario)
                        asiento = await ContabilidadService.ContabilizarPorEmpresaYDiario(lineas,
                            (e, d) => contabilidad.ContabilizarDiario(db, e, d, usuario)).ConfigureAwait(false);
                        if (asiento <= 0)
                        {
                            throw new NestoBusinessException("No se ha podido contabilizar el cobro de la señal.");
                        }
                    }

                    await anotarPagada().ConfigureAwait(false);

                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = TABLA_MODIFICACIONES,
                        Anterior = documento,
                        Nuevo = rastro + (yaEstaba ? " (el cobro ya estaba en el extracto: no se ha vuelto a contabilizar)" : $" Asiento {asiento}."),
                        Usuario = usuario != null && usuario.Length > 50 ? usuario.Substring(0, 50) : usuario,
                        Fecha = DateTime.Now
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return new RegistroCobroSenalCaja { Asiento = asiento, CobroYaEstaba = yaEstaba };
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }
    }

    public partial class ServicioInscripcionesEventos
    {
        private IRepositorioCobroSenalCaja repositorioCobroCaja;

        /// <summary>19/11/26: el cobro de la señal en Cajas en la base de datos. Por defecto, el de SQL con este contexto.</summary>
        internal IRepositorioCobroSenalCaja RepositorioCobroCaja
        {
            get => repositorioCobroCaja ?? (repositorioCobroCaja = new RepositorioCobroSenalCaja(db));
            set => repositorioCobroCaja = value;
        }

        /// <summary>19/11/26: (empresa, cliente) → las propuestas de señal (#615) pendientes de ese cliente. Sustituible en tests.</summary>
        internal Func<string, string, Task<List<PropuestaSenalEvento>>> PropuestasSenalPendientes { get; set; }

        /// <summary>19/11/26: (empresa, usuario sin dominio, clave) → el parámetro del usuario. Sustituible en tests.</summary>
        internal Func<string, string, string, string> ParametroUsuario { get; set; }

        /// <summary>19/11/26: inscripción → su cupón (para el mensaje). Null: no se lee.</summary>
        internal Func<int, Task<CuponLeido>> CuponDeInscripcion { get; set; }

        private async Task<List<PropuestaSenalEvento>> LeerPropuestasPendientes(string empresa, string cliente)
        {
            try
            {
                if (PropuestasSenalPendientes != null)
                {
                    return await PropuestasSenalPendientes(empresa, cliente).ConfigureAwait(false) ?? new List<PropuestaSenalEvento>();
                }
                return (await new RepositorioPropuestasSenales(db).Pendientes(empresa).ConfigureAwait(false) ?? new List<PropuestaSenalEvento>())
                    .Where(p => Limpiar(p.Cliente) == cliente).ToList();
            }
            catch (Exception ex)
            {
                // Sin poder mirarlo no se cobra (podría ser la señal de una transferencia): se trata como «hay propuesta»
                ElmahHelper.Log(new Exception($"[Cobro de señal en Cajas] No se han podido leer las propuestas de señal del cliente {cliente}: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>
        /// Las reservas de eventos sin pagar del cliente (todos sus contactos) que Cajas enseña para cobrar: Reservada o Caducada
        /// (si aún puede pasar a Pagada), de pago, de un evento de hoy en adelante. Cada una dice si se puede cobrar y, si no, por
        /// qué. Quien no cobra señales en Cajas recibe la lista vacía; el cliente de la tienda online, 403.
        /// </summary>
        public async Task<List<ReservaEventoSinPagarDTO>> LeerReservasSinPagar(string empresa, string cliente, IPrincipal usuario)
        {
            if (PermisosEventos.ClienteDelToken(usuario) != null)
            {
                throw Prohibido(PermisosEventos.MENSAJE_SIN_PERMISO_COBRAR_SENAL);
            }
            string clienteLimpio = Limpiar(cliente) ?? throw new NestoBusinessException("Indique el cliente.");
            if (!PermisosEventos.PuedeCobrarSenalEnCaja(usuario))
            {
                return new List<ReservaEventoSinPagarDTO>();
            }
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            DateTime hoy = reloj().Date;
            string reservada = EstadoInscripcionEvento.Reservada.ToString();
            string caducada = EstadoInscripcionEvento.Caducada.ToString();
            List<EventoInscripcion> candidatas = (await db.EventosInscripciones
                .Where(i => i.Cliente == clienteLimpio && (i.Estado == reservada || i.Estado == caducada))
                .ToListAsync().ConfigureAwait(false))
                .Where(i => EmpresaOPorDefecto(i.Empresa) == empresaLimpia)
                .ToList();
            if (!candidatas.Any())
            {
                return new List<ReservaEventoSinPagarDTO>();
            }
            List<int> idsEventos = candidatas.Select(i => i.EventoId).Distinct().ToList();
            Dictionary<int, Evento> eventos = (await db.Eventos.Where(e => idsEventos.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Id);
            List<EventoInscripcion> reservas = candidatas
                .Where(i => eventos.TryGetValue(i.EventoId, out Evento e) && ReglasCobroSenalCaja.EsReservaSinPagar(i, e, hoy))
                .OrderBy(i => eventos[i.EventoId].Fecha).ThenBy(i => i.Id)
                .ToList();
            if (!reservas.Any())
            {
                return new List<ReservaEventoSinPagarDTO>();
            }

            ContextoCobroSenal contexto = await LeerContextoCobro(empresaLimpia, clienteLimpio, reservas, eventos).ConfigureAwait(false);
            return reservas.Select(i =>
            {
                Evento evento = eventos[i.EventoId];
                EstadoInscripcionEvento estado = EstadoDe(i);
                ModalidadInscripcionEvento modalidad = ReglasModalidadEvento.DeInscripcion(i);
                string motivo = contexto.Motivo(i, evento);
                return new ReservaEventoSinPagarDTO
                {
                    InscripcionId = i.Id,
                    EventoId = evento.Id,
                    Evento = evento.Titulo?.Trim(),
                    FechaEvento = evento.Fecha.Date,
                    Almacen = Limpiar(evento.Almacen),
                    Empresa = empresaLimpia,
                    Cliente = clienteLimpio,
                    Contacto = Limpiar(i.Contacto),
                    Estado = estado,
                    EstadoTexto = ReglasInscripcionEvento.Texto(estado),
                    Modalidad = modalidad,
                    Importe = Math.Round(ReglasModalidadEvento.ImporteDe(i, evento), 2, MidpointRounding.AwayFromZero),
                    FechaLimitePago = i.FechaLimitePago,
                    Descripcion = ReglasCobroSenalCaja.Descripcion(evento, modalidad),
                    Documento = DocumentoPago(i.Id),
                    Cobrable = motivo == null,
                    Motivo = motivo,
                    CobroYaApuntado = contexto.ConCobro.Contains(DocumentoPago(i.Id))
                };
            }).ToList();
        }

        /// <summary>Lo que hace falta mirar en la base de datos para decidir si unas reservas se pueden cobrar (una consulta por cosa).</summary>
        private class ContextoCobroSenal
        {
            public Func<EventoInscripcion, Evento, string> Motivo { get; set; }
            public HashSet<string> ConCobro { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<ContextoCobroSenal> LeerContextoCobro(string empresa, string cliente, List<EventoInscripcion> reservas, Dictionary<int, Evento> eventos)
        {
            List<int> idsEventos = reservas.Select(i => i.EventoId).Distinct().ToList();
            // #591: señales por transferencia ya marcadas de esos eventos para este cliente
            List<EventoSenal> senales = (await db.EventosSenales.Where(s => idsEventos.Contains(s.EventoId) && s.Cliente == cliente)
                .ToListAsync().ConfigureAwait(false));
            // #615: transferencias propuestas como señal y sin confirmar
            List<PropuestaSenalEvento> propuestas = await LeerPropuestasPendientes(empresa, cliente).ConfigureAwait(false);
            // Enlaces de pago (raíz y reintentos): cobrados y compartidos por varias inscripciones (pedido de cursos #691)
            List<int> raices = reservas.Where(i => i.PagoTPVId.HasValue).Select(i => i.PagoTPVId.Value).Distinct().ToList();
            var raicesCobradas = new HashSet<int>();
            if (raices.Any())
            {
                var enlaces = await db.PagosTPV.Where(p => raices.Contains(p.Id) || (p.PagoOriginalId.HasValue && raices.Contains(p.PagoOriginalId.Value)))
                    .Select(p => new { p.Id, p.PagoOriginalId, p.Estado }).ToListAsync().ConfigureAwait(false);
                foreach (var enlace in enlaces.Where(p => string.Equals(p.Estado?.Trim(), Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase)))
                {
                    _ = raicesCobradas.Add(raices.Contains(enlace.Id) ? enlace.Id : enlace.PagoOriginalId ?? enlace.Id);
                }
            }
            Dictionary<int, int> inscripcionesPorEnlace = raices.Any()
                ? (await db.EventosInscripciones.Where(i => i.PagoTPVId.HasValue && raices.Contains(i.PagoTPVId.Value))
                    .Select(i => i.PagoTPVId.Value).ToListAsync().ConfigureAwait(false))
                    .GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count())
                : new Dictionary<int, int>();
            // #691: la fila de la facturación dice si es de un pedido de cursos de la plantilla
            Dictionary<int, FacturacionCursoInscripcion> filas = facturacion == null
                ? new Dictionary<int, FacturacionCursoInscripcion>()
                : await facturacion.Leer(reservas.Select(i => i.Id)).ConfigureAwait(false) ?? new Dictionary<int, FacturacionCursoInscripcion>();
            HashSet<string> conCobro = await RepositorioCobroCaja.DocumentosConCobro(empresa, cliente, reservas.Select(i => DocumentoPago(i.Id)))
                .ConfigureAwait(false) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IConfirmadorSenalEvento confirmador = procesador as IConfirmadorSenalEvento;

            return new ContextoCobroSenal
            {
                ConCobro = conCobro,
                Motivo = (i, evento) =>
                {
                    string contacto = Limpiar(i.Contacto);
                    bool senal = senales.Any(s => s.EventoId == i.EventoId && Limpiar(s.Contacto) == contacto);
                    bool propuesta = propuestas == null
                        || propuestas.Any(p => p.EventoId == i.EventoId && (p.InscripcionId == i.Id || Limpiar(p.Contacto) == contacto));
                    bool cobrado = i.PagoTPVId.HasValue && raicesCobradas.Contains(i.PagoTPVId.Value);
                    filas.TryGetValue(i.Id, out FacturacionCursoInscripcion fila);
                    // #691: un pedido de cursos de la plantilla (su fila lo dice, o comparte el enlace con otras inscripciones)
                    bool dePedidoDeCursos = ConfirmadorSenalEvento.EsDePlantilla(fila)
                        || (i.PagoTPVId.HasValue && inscripcionesPorEnlace.TryGetValue(i.PagoTPVId.Value, out int cuantas) && cuantas > 1);
                    string motivoFacturacion = confirmador == null
                        ? "La facturación automática de los cursos no está disponible: la señal no se puede cobrar en Cajas."
                        : confirmador.MotivoNoFacturaSolo(i, evento, fila);
                    return ReglasCobroSenalCaja.MotivoNoCobrable(i, evento, senal, propuesta, cobrado, dePedidoDeCursos, fila?.Pedido, motivoFacturacion);
                }
            };
        }

        /// <summary>
        /// Cobra la señal de una reserva en Cajas y deja todo como cuando llega el enlace pagado. En este orden:
        /// <list type="number">
        /// <item>Comprueba permisos (tiendas en efectivo o tarjeta; Administración y Dirección, también transferencia), que sigue
        /// sin pagar, que el importe es la señal exacta (si no, 400), que no hay ya señal por transferencia, propuesta de señal ni
        /// enlace cobrado, y que el curso se va a facturar solo. Si algo falla, no se toca nada.</item>
        /// <item>En UNA transacción: el cobro a cuenta «INS{id}» contra la cuenta de cobro de la caja (los apuntes de un cobro a
        /// cuenta de Cajas) y la inscripción Pagada. Si ya había cobro «INS{id}» (un intento anterior cortado), no se repite.</item>
        /// <item>Después, el camino de siempre (<see cref="TrasPagar"/>): pedido y factura exenta del curso, liquidación del cobro
        /// contra el efecto de la factura (LIQUIDADA), Facturada y el cupón. Si falla, se queda Pagada con el cobro, va a ELMAH,
        /// avisa a Administración y lo reintenta el job (o «Facturar» en la inscripción): lo hecho no se repite.</item>
        /// </list>
        /// Relanzar con una inscripción ya pagada por Cajas (cobro «INS{id}» en el extracto) termina lo que falte sin cobrar otra vez.
        /// </summary>
        public async Task<ResultadoCobroSenalCajaDTO> CobrarSenalEnCaja(int id, CobrarSenalCajaDTO peticion, IPrincipal usuario)
        {
            if (PermisosEventos.ClienteDelToken(usuario) != null)
            {
                throw Prohibido(PermisosEventos.MENSAJE_SIN_PERMISO_COBRAR_SENAL);
            }
            FormaPagoInscripcionEvento forma = ReglasCobroSenalCaja.FormaPago(peticion?.FormaPago)
                ?? throw new NestoBusinessException("Indique cómo cobra la señal: efectivo, tarjeta o transferencia.");
            if (!PermisosEventos.PuedeCobrarSenalEnCaja(usuario, forma))
            {
                throw Prohibido(forma == FormaPagoInscripcionEvento.Transferencia && PermisosEventos.PuedeCobrarSenalEnCaja(usuario)
                    ? PermisosEventos.MENSAJE_TRANSFERENCIA_SOLO_ADMINISTRACION
                    : PermisosEventos.MENSAJE_SIN_PERMISO_COBRAR_SENAL);
            }

            EventoInscripcion inscripcion = await LeerInscripcion(id).ConfigureAwait(false);
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            string empresa = EmpresaOPorDefecto(inscripcion.Empresa);
            string cliente = Limpiar(inscripcion.Cliente);
            string documento = DocumentoPago(inscripcion.Id);
            string titulo = evento.Titulo?.Trim();
            DateTime ahora = reloj();
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            ComprobarQueNoEsGratuita(inscripcion, evento);

            if (ReglasInscripcionEvento.EN_LISTA_ASISTENCIA.Contains(estado))
            {
                // Ya pagada: si fue por Cajas (su cobro está en el extracto), se termina lo que falte; si no, no se cobra otra vez
                bool porCaja = (await RepositorioCobroCaja.DocumentosConCobro(empresa, cliente, new[] { documento }).ConfigureAwait(false))?.Contains(documento) == true;
                if (!porCaja)
                {
                    throw new NestoBusinessException($"La inscripción a «{titulo}» ya está pagada" +
                        (inscripcion.FechaPago.HasValue ? $" (el {inscripcion.FechaPago.Value:dd/MM/yyyy})" : string.Empty) +
                        ": no se cobra otra vez.") { StatusCode = HttpStatusCode.Conflict };
                }
                if (estado == EstadoInscripcionEvento.Pagada && procesador is IConfirmadorSenalEvento confirmadorRelanzar)
                {
                    try
                    {
                        _ = await confirmadorRelanzar.ConfirmarSenal(inscripcion, evento, aMano: false).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Cobro de señal en Cajas] No se ha podido terminar la facturación del curso de la inscripción {id}: {ex.Message}", ex));
                    }
                }
                return await ComponerResultadoCobro(inscripcion, evento, new RegistroCobroSenalCaja { CobroYaEstaba = true },
                    inscripcion.ImportePagado ?? ReglasModalidadEvento.ImporteDe(inscripcion, evento), inscripcion.FormaPago, null).ConfigureAwait(false);
            }
            if (estado != EstadoInscripcionEvento.Reservada && estado != EstadoInscripcionEvento.Caducada)
            {
                throw new NestoBusinessException($"La inscripción a «{titulo}» está {ReglasInscripcionEvento.Texto(estado).ToLowerInvariant()}: no se puede cobrar.");
            }
            if (!ReglasCobroSenalCaja.EsReservaSinPagar(inscripcion, evento, ahora))
            {
                throw new NestoBusinessException($"El evento «{titulo}» ya ha pasado: su señal no se cobra en Cajas. Hable con Administración.");
            }
            decimal senal = Math.Round(ReglasModalidadEvento.ImporteDe(inscripcion, evento), 2, MidpointRounding.AwayFromZero);
            string motivoImporte = ReglasCobroSenalCaja.MotivoImporte(peticion.Importe, senal, evento);
            if (motivoImporte != null)
            {
                throw new NestoBusinessException(motivoImporte);
            }
            DateTime fecha = (peticion.Fecha ?? ahora).Date;
            if (fecha > ahora.Date)
            {
                throw new NestoBusinessException("La fecha del cobro no puede ser futura.");
            }

            ContextoCobroSenal contexto = await LeerContextoCobro(empresa, cliente, new List<EventoInscripcion> { inscripcion },
                new Dictionary<int, Evento> { [evento.Id] = evento }).ConfigureAwait(false);
            string motivo = contexto.Motivo(inscripcion, evento);
            if (motivo != null)
            {
                throw new NestoBusinessException(motivo) { StatusCode = HttpStatusCode.Conflict };
            }

            string usuarioAuditoria = UsuarioDe(usuario);
            CajaCobroSenal caja = CajaDelUsuario(peticion, forma, empresa, usuario);
            List<PreContabilidad> lineas = ConstructorApuntesCobroSenalCaja.Construir(inscripcion, evento, senal, forma, fecha, caja,
                peticion.ConceptoAdicional, usuarioAuditoria, ahora);
            List<int> enlaces = await EnlacesPendientes(inscripcion).ConfigureAwait(false);
            DateTime fechaPago = fecha == ahora.Date ? ahora : fecha;
            string rastro = $"Señal de «{titulo}» (inscripción {inscripcion.Id}, cliente {cliente}/{Limpiar(inscripcion.Contacto)}) cobrada en Cajas: " +
                $"{ReglasCobroSenalCaja.Euros(senal)} € {forma.ToString().ToLowerInvariant()} contra la cuenta {caja.Cuenta} (diario {caja.Diario}).";

            RegistroCobroSenalCaja registro = await RepositorioCobroCaja.Registrar(inscripcion.Id, empresa, cliente, documento, lineas, enlaces,
                usuarioAuditoria, rastro, async () =>
                {
                    AnotarPago(inscripcion, forma, senal, fechaPago, usuarioAuditoria, null);
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

            // Lo de después, como el enlace pagado: corte 2 (pedido, factura, liquidación, cupón), avisos y pago fuera de plazo
            await TrasPagar(inscripcion, evento, estado).ConfigureAwait(false);
            return await ComponerResultadoCobro(inscripcion, evento, registro, senal, forma.ToString(), caja.Cuenta).ConfigureAwait(false);
        }

        /// <summary>Los enlaces de pago pendientes de la inscripción (el suyo y sus reintentos), para anularlos al cobrar en caja.</summary>
        private async Task<List<int>> EnlacesPendientes(EventoInscripcion inscripcion)
        {
            if (!inscripcion.PagoTPVId.HasValue)
            {
                return new List<int>();
            }
            int raiz = inscripcion.PagoTPVId.Value;
            string pendiente = Constantes.EstadosPagoTPV.PENDIENTE;
            return (await db.PagosTPV.Where(p => p.Id == raiz || p.PagoOriginalId == raiz)
                .Select(p => new { p.Id, p.Estado }).ToListAsync().ConfigureAwait(false))
                .Where(p => string.Equals(p.Estado?.Trim(), pendiente, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id).ToList();
        }

        /// <summary>
        /// La caja del cobro: la que manda Cajas y, si no viene, la de los parámetros del usuario (como la ventana de Cajas:
        /// CajaDefecto en efectivo, CuentaBancoTarjeta con tarjeta o transferencia; diario «_Caja» en Administración).
        /// </summary>
        private CajaCobroSenal CajaDelUsuario(CobrarSenalCajaDTO peticion, FormaPagoInscripcionEvento forma, string empresa, IPrincipal usuario)
        {
            string sinDominio = PermisosEventos.SinDominio(usuario?.Identity?.Name);
            bool administracion = PermisosEventos.EsDireccion(usuario) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION);
            Func<string, string, string, string> parametro = ParametroUsuario
                ?? ((e, u, clave) => u == null ? null : Controllers.ParametrosUsuarioController.LeerParametro(e, u, clave));
            string Leer(string clave) => Limpiar(parametro(empresa, sinDominio, clave));

            string cuenta = Limpiar(peticion.CuentaCobro)
                ?? Leer(forma == FormaPagoInscripcionEvento.Efectivo ? ReglasCobroSenalCaja.PARAMETRO_CAJA : ReglasCobroSenalCaja.PARAMETRO_CUENTA_TARJETA);
            if (string.IsNullOrEmpty(cuenta) || !cuenta.StartsWith(ReglasCobroSenalCaja.PREFIJO_TESORERIA, StringComparison.Ordinal))
            {
                throw new NestoBusinessException(string.IsNullOrEmpty(cuenta)
                    ? "No tiene cuenta de caja para cobrar (parámetro CajaDefecto o CuentaBancoTarjeta): pida a Administración que se la ponga."
                    : $"La cuenta {cuenta} no es de tesorería (57…): elija la caja o el banco del cobro.");
            }
            string diario = Limpiar(peticion.Diario) ?? (administracion ? ReglasCobroSenalCaja.DIARIO_CAJA_ADMINISTRACION : Leer(ReglasCobroSenalCaja.PARAMETRO_DIARIO));
            if (string.IsNullOrEmpty(diario))
            {
                throw new NestoBusinessException("No tiene diario de caja (parámetro DiarioCaja): pida a Administración que se lo ponga para poder cobrar.");
            }
            return new CajaCobroSenal
            {
                Cuenta = cuenta,
                Diario = diario,
                Delegacion = Limpiar(peticion.Delegacion) ?? Leer(ReglasCobroSenalCaja.PARAMETRO_DELEGACION),
                FormaVenta = Limpiar(peticion.FormaVenta) ?? Leer(ReglasCobroSenalCaja.PARAMETRO_FORMA_VENTA),
                // Como en Cajas: «… a Reina» en los apuntes de las tiendas, no en los de Administración
                AQuien = administracion ? null : sinDominio
            };
        }

        private async Task<ResultadoCobroSenalCajaDTO> ComponerResultadoCobro(EventoInscripcion inscripcion, Evento evento, RegistroCobroSenalCaja registro,
            decimal importe, string forma, string cuenta)
        {
            var resultado = new ResultadoCobroSenalCajaDTO
            {
                Asiento = registro.Asiento,
                CobroYaEstaba = registro.CobroYaEstaba,
                Documento = DocumentoPago(inscripcion.Id),
                Importe = importe,
                FormaPago = Limpiar(forma),
                Cuenta = cuenta
            };
            FacturacionCursoInscripcion fila = null;
            try
            {
                fila = facturacion == null ? null : await facturacion.Leer(inscripcion.Id).ConfigureAwait(false);
                if (fila != null)
                {
                    resultado.Pedido = fila.Pedido;
                    resultado.Factura = Limpiar(fila.Factura);
                    resultado.EstadoFacturacion = Limpiar(fila.Estado);
                    resultado.EstadoLiquidacion = Limpiar(fila.EstadoLiquidacion);
                }
                CuponLeido cupon = CuponDeInscripcion == null ? null : await CuponDeInscripcion(inscripcion.Id).ConfigureAwait(false);
                if (cupon != null)
                {
                    resultado.Cupon = Limpiar(cupon.Codigo);
                    resultado.ImporteCupon = cupon.Importe;
                }
            }
            catch (Exception ex)
            {
                // El cobro ya está hecho: lo que no se pueda leer solo falta en el mensaje
                ElmahHelper.Log(new Exception($"[Cobro de señal en Cajas] No se ha podido leer cómo ha quedado la inscripción {inscripcion.Id}: {ex.Message}", ex));
            }
            resultado.Inscripcion = Componer(inscripcion, evento, null);
            resultado.Inscripcion.Facturacion = FacturacionCursoDTO.De(fila);
            resultado.Mensaje = ReglasCobroSenalCaja.Mensaje(resultado, evento.Titulo?.Trim());
            return resultado;
        }
    }
}
