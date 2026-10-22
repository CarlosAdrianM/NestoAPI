using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#612: los interruptores de la facturación automática del curso. Se leen del Web.config del REPO (la
    /// publicación se lleva el suyo: lo que se encienda a mano en el servidor vuelve a su valor en el siguiente deploy).
    /// </summary>
    public static class InterruptoresEventos
    {
        /// <summary>
        /// «true»: al pagarse una inscripción (enlace, señal de #591 marcada o pago a mano) se crea el pedido del curso, se
        /// factura exento, se liquida la señal y la inscripción pasa a Facturada. Apagado si no está o no vale «true»:
        /// entonces todo sigue como en el corte 1 (la inscripción se queda Pagada y la señal la liquida Administración).
        /// </summary>
        public const string CLAVE_FACTURAR_CURSO = "Eventos:FacturarCursoAlCobrar";
        /// <summary>El usuario de auditoría de lo automático (cobro del enlace). Por defecto <see cref="USUARIO_AUTOMATICO"/>.</summary>
        public const string CLAVE_USUARIO_AUTOMATICO = "Eventos:UsuarioAutomatico";
        /// <summary>La forma de venta de la línea del curso. Por defecto <see cref="FORMA_VENTA_CURSO"/>.</summary>
        public const string CLAVE_FORMA_VENTA = "Eventos:FormaVentaCurso";

        /// <summary>Como ReposicionAutomatica: el que firma lo que hace la API sola (issue #612).</summary>
        public const string USUARIO_AUTOMATICO = "SenalesEventos";
        /// <summary>«TIE» (tienda): la que más usan hoy las tiendas para los cursos de CV.</summary>
        public const string FORMA_VENTA_CURSO = "TIE";

        /// <summary>
        /// NestoAPI#614: «true»: Nesto puede pasar una compra del curso en la tienda PrestaShop (CanalesExternos) como pago de la
        /// inscripción (POST api/Eventos/CursoTienda) en vez de facturarla como un pedido normal. Apagado si no está.
        /// </summary>
        public const string CLAVE_CURSOS_TIENDA = "Eventos:CursosDesdePrestashop";

        public static bool FacturarCursoAlCobrar =>
            string.Equals(ConfigurationManager.AppSettings[CLAVE_FACTURAR_CURSO], "true", StringComparison.OrdinalIgnoreCase);

        public static bool CursosDesdePrestashop =>
            string.Equals(ConfigurationManager.AppSettings[CLAVE_CURSOS_TIENDA], "true", StringComparison.OrdinalIgnoreCase);

        public static string UsuarioAutomatico => Valor(CLAVE_USUARIO_AUTOMATICO) ?? USUARIO_AUTOMATICO;

        public static string FormaVentaCurso => Valor(CLAVE_FORMA_VENTA) ?? FORMA_VENTA_CURSO;

        private static string Valor(string clave)
        {
            string valor = ConfigurationManager.AppSettings[clave]?.Trim();
            return string.IsNullOrEmpty(valor) ? null : valor;
        }
    }

    /// <summary>NestoAPI#612: en qué punto está la facturación del curso de una inscripción (EventosInscripcionesCurso.Estado).</summary>
    public static class EstadosFacturacionCurso
    {
        /// <summary>Empezada (pedido creado o factura hecha) y sin terminar: la está procesando alguien o se cortó.</summary>
        public const string EN_CURSO = "EnCurso";
        /// <summary>Pedido, factura y liquidación hechos (o sin cobro que liquidar). La inscripción está Facturada.</summary>
        public const string FACTURADA = "Facturada";
        /// <summary>Ha fallado un paso. Se relanza desde donde se quedó (job diario o a mano); lo hecho no se repite.</summary>
        public const string ERROR = "Error";
        /// <summary>No se automatiza (importe distinto de la señal, evento sin referencia de curso…): lo hace Administración.</summary>
        public const string NO_AUTOMATIZABLE = "NoAutomatizable";
    }

    /// <summary>NestoAPI#612: qué se ha hecho con el cobro de la señal al facturar el curso.</summary>
    public static class EstadosLiquidacionCurso
    {
        public const string PENDIENTE = "Pendiente";
        /// <summary>El cobro (señal de #591 o cobro del enlace) se ha liquidado contra el efecto de la factura.</summary>
        public const string LIQUIDADA = "Liquidada";
        /// <summary>No hay cobro en el extracto que liquidar (efectivo, datáfono o transferencia marcada a mano): la
        /// factura queda pendiente de cobro y se cobra como cualquier otra (caja, Administración).</summary>
        public const string SIN_COBRO = "SinCobro";
    }

    /// <summary>NestoAPI#612: la fila de EventosInscripcionesCurso (SQL directo, sin EDMX).</summary>
    public class FacturacionCursoInscripcion
    {
        public int InscripcionId { get; set; }
        public string EmpresaPedido { get; set; }
        public int? Pedido { get; set; }
        public string EmpresaFactura { get; set; }
        public string Factura { get; set; }
        public string EstadoLiquidacion { get; set; }
        public int? NumOrdenCobro { get; set; }
        public int? NumOrdenEfecto { get; set; }
        public string Estado { get; set; }
        public string Mensaje { get; set; }
        public int Intentos { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#612: lo que ven los clientes (InscripcionEventoDTO.Facturacion).</summary>
    public class FacturacionCursoDTO
    {
        /// <summary>EnCurso, Facturada, Error o NoAutomatizable.</summary>
        public string Estado { get; set; }
        public string EmpresaPedido { get; set; }
        public int? Pedido { get; set; }
        public string EmpresaFactura { get; set; }
        public string Factura { get; set; }
        /// <summary>Pendiente, Liquidada o SinCobro.</summary>
        public string EstadoLiquidacion { get; set; }
        /// <summary>El motivo del error, de que no se automatice o de que no haya nada que liquidar.</summary>
        public string Mensaje { get; set; }
        public int Intentos { get; set; }
        public DateTime FechaModificacion { get; set; }

        internal static FacturacionCursoDTO De(FacturacionCursoInscripcion f) => f == null ? null : new FacturacionCursoDTO
        {
            Estado = f.Estado?.Trim(),
            EmpresaPedido = f.EmpresaPedido?.Trim(),
            Pedido = f.Pedido,
            EmpresaFactura = f.EmpresaFactura?.Trim(),
            Factura = f.Factura?.Trim(),
            EstadoLiquidacion = f.EstadoLiquidacion?.Trim(),
            Mensaje = f.Mensaje,
            Intentos = f.Intentos,
            FechaModificacion = f.FechaModificacion
        };
    }

    /// <summary>El pedido del curso recién creado (la empresa por si algún día no es la de la inscripción).</summary>
    public class PedidoCursoCreado
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
    }

    /// <summary>La factura del curso. <see cref="EmpresaPedido"/> cambia si el pedido se traspasa a la empresa espejo.</summary>
    public class FacturaCursoCreada
    {
        public string EmpresaPedido { get; set; }
        public string EmpresaFactura { get; set; }
        public string Factura { get; set; }
    }

    public class LiquidacionSenalCurso
    {
        public string Estado { get; set; }
        public int? NumOrdenCobro { get; set; }
        public int? NumOrdenEfecto { get; set; }
        /// <summary>Para SinCobro: por qué. Se guarda en la fila.</summary>
        public string Mensaje { get; set; }
        /// <summary>SinCobro que Administración tiene que mirar (había un cobro y no se ha encontrado o no se puede liquidar).</summary>
        public bool AvisarAdministracion { get; set; }
    }

    /// <summary>Lo que devuelve la confirmación (para el endpoint de relanzar y para los tests).</summary>
    public class ResultadoConfirmacionSenal
    {
        public bool Hecho { get; set; }
        public bool Omitido { get; set; }
        public FacturacionCursoInscripcion Facturacion { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#612: EventosInscripcionesCurso, el registro de lo hecho para poder seguir donde se quedó.</summary>
    public interface IRepositorioFacturacionCursos
    {
        Task<FacturacionCursoInscripcion> Leer(int inscripcionId);
        Task<Dictionary<int, FacturacionCursoInscripcion>> Leer(IEnumerable<int> inscripciones);
        /// <summary>Crea la fila si no existe y la reserva para este proceso (ProcesandoDesde). False si la está procesando
        /// otro ahora mismo (un reintento del webhook a la vez que el botón).</summary>
        Task<bool> Reclamar(int inscripcionId, string empresa, string usuario);
        Task Soltar(int inscripcionId);
        /// <summary>Guarda el estado, la factura, la liquidación, el mensaje y los intentos (el pedido lo guarda el creador,
        /// en la misma transacción que el pedido).</summary>
        Task Guardar(FacturacionCursoInscripcion fila);
        /// <summary>Las que han fallado y no han agotado los intentos (para el job).</summary>
        Task<List<int>> ConError(int maxIntentos);
        /// <summary>Las ya facturadas cuyo cupón no consta emitido (para el job del corte 3).</summary>
        Task<List<int>> FacturadasSinCupon();
    }

    /// <summary>Crea el pedido del curso y apunta su número en EventosInscripcionesCurso EN LA MISMA transacción.</summary>
    public interface ICreadorPedidoCurso
    {
        Task<PedidoCursoCreado> Crear(EventoInscripcion inscripcion, Evento evento, string usuario);
    }

    /// <summary>Albarán (si no lo tiene) y factura por ServicioFacturas.CrearFactura, el único sitio que llama a prdCrearFacturaVta.</summary>
    public interface IFacturadorPedidoCurso
    {
        Task<FacturaCursoCreada> Facturar(string empresa, int pedido, string usuario);
    }

    /// <summary>Liquida el cobro de la señal contra el efecto de la factura (ServicioLiquidarEfectos, único sitio de prdLiquidar).</summary>
    public interface ILiquidadorSenalCurso
    {
        Task<LiquidacionSenalCurso> Liquidar(EventoInscripcion inscripcion, string empresaFactura, string factura, string usuario);
    }

    /// <summary>Correo a Administración. Nunca lanza.</summary>
    public interface IAvisadorFacturacionCurso
    {
        Task Avisar(string asunto, string cuerpoHtml);
    }

    /// <summary>NestoAPI#613: el cupón que gana quien paga la señal. Idempotente (una inscripción, un cupón).</summary>
    public interface IEmisorCuponEvento
    {
        Task Emitir(EventoInscripcion inscripcion, Evento evento, FacturacionCursoInscripcion facturacion, string usuario);
    }

    /// <summary>NestoAPI#612: sin el corte 3, no se emite nada.</summary>
    public class EmisorCuponEventoNulo : IEmisorCuponEvento
    {
        public Task Emitir(EventoInscripcion inscripcion, Evento evento, FacturacionCursoInscripcion facturacion, string usuario) => Task.CompletedTask;
    }

    /// <summary>NestoAPI#612: además del enganche, relanzar a mano (Administración) desde donde se quedó.</summary>
    public interface IConfirmadorSenalEvento : IProcesadorInscripcionPagada
    {
        Task<ResultadoConfirmacionSenal> ConfirmarSenal(EventoInscripcion inscripcion, Evento evento, bool aMano);
    }

    /// <summary>NestoAPI#612: las reglas que deciden si se automatiza (sin base de datos).</summary>
    public static class ReglasFacturacionCurso
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Null si se puede automatizar; si no, el motivo para Administración.</summary>
        public static string MotivoNoAutomatizable(EventoInscripcion inscripcion, Evento evento)
        {
            string titulo = evento?.Titulo?.Trim();
            if (string.IsNullOrWhiteSpace(evento?.ProductoCurso))
            {
                return $"El evento «{titulo}» no tiene la referencia del curso (ProductoCurso): no se puede facturar solo. " +
                    "Póngasela al evento y relance la facturación, o facture el curso a mano.";
            }
            if (evento.ImporteSenal <= 0)
            {
                return $"El evento «{titulo}» no tiene importe de señal: no hay nada que facturar.";
            }
            decimal pagado = Math.Round(inscripcion?.ImportePagado ?? 0, 2, MidpointRounding.AwayFromZero);
            decimal senal = Math.Round(evento.ImporteSenal, 2, MidpointRounding.AwayFromZero);
            if (pagado != senal)
            {
                return $"El cliente ha pagado {Euros(pagado)} € y la señal de «{titulo}» es de {Euros(senal)} €: con importes distintos " +
                    "no se factura solo. Facture y liquide a mano lo que corresponda.";
            }
            return null;
        }

        /// <summary>La forma de pago del pedido del curso según cómo pagó la inscripción.</summary>
        public static string FormaPagoPedido(string formaPagoInscripcion)
        {
            switch (ReglasInscripcionEvento.FormaPago(formaPagoInscripcion))
            {
                case FormaPagoInscripcionEvento.Tarjeta: return Constantes.FormasPago.TARJETA;
                case FormaPagoInscripcionEvento.Efectivo: return Constantes.FormasPago.EFECTIVO;
                default: return Constantes.FormasPago.TRANSFERENCIA;
            }
        }

        /// <summary>El usuario que firma: quien marcó el pago o la señal; si fue el enlace (NestoAPI), el automático.</summary>
        public static string UsuarioAuditoria(EventoInscripcion inscripcion, string usuarioAutomatico)
        {
            string usuario = inscripcion?.UsuarioPago?.Trim();
            return string.IsNullOrEmpty(usuario)
                || string.Equals(usuario, ServicioInscripcionesEventos.USUARIO_AUTOMATICO, StringComparison.OrdinalIgnoreCase)
                ? usuarioAutomatico
                : usuario;
        }

        internal static string Euros(decimal importe) => importe.ToString("N2", es);
    }

    /// <summary>
    /// NestoAPI#612 (corte 2 de #610): la señal confirmada crea el pedido del curso, lo factura exento, liquida la señal y
    /// reserva la plaza, sola. Es el enganche del corte 1 (<see cref="IProcesadorInscripcionPagada"/>): llega aquí cualquier
    /// inscripción que pasa a Pagada (enlace cobrado, señal de #591 marcada, pago a mano).
    /// <para>prdCrearAlbaránVta y prdCrearFacturaVta abren su propia transacción y la factura sale a Verifactu, así que no
    /// cabe todo en una transacción: cada paso queda apuntado en EventosInscripcionesCurso y se sigue desde donde se quedó.
    /// El pedido y su número en la fila van juntos (si falla, no queda pedido); la factura nunca se repite (si ya está, se
    /// salta); un fallo deja la inscripción Pagada (visible en la lista de #591), va a ELMAH, avisa a Administración la
    /// primera vez y lo relanza el job diario o Administración (POST api/Eventos/Inscripciones/{id}/Facturar).</para>
    /// </summary>
    public class ConfirmadorSenalEvento : IConfirmadorSenalEvento
    {
        /// <summary>Intentos del job antes de dejarlo para Administración.</summary>
        public const int MAX_INTENTOS_AUTOMATICOS = 5;

        private readonly NVEntities db;
        private readonly Func<bool> interruptor;
        private readonly IRepositorioFacturacionCursos repositorio;
        private readonly ICreadorPedidoCurso creador;
        private readonly IFacturadorPedidoCurso facturador;
        private readonly ILiquidadorSenalCurso liquidador;
        private readonly IAvisadorFacturacionCurso avisador;
        private readonly IEmisorCuponEvento emisor;
        private readonly Func<DateTime> reloj;
        private readonly string usuarioAutomatico;

        public ConfirmadorSenalEvento(NVEntities db)
            : this(db, () => InterruptoresEventos.FacturarCursoAlCobrar, new RepositorioFacturacionCursos(db),
                  new CreadorPedidoCurso(db), new FacturadorPedidoCurso(), new LiquidadorSenalCurso(db),
                  new AvisadorFacturacionCurso(), new Cupones.EmisorCuponesEvento(db), null, InterruptoresEventos.UsuarioAutomatico)
        {
        }

        internal ConfirmadorSenalEvento(NVEntities db, Func<bool> interruptor, IRepositorioFacturacionCursos repositorio,
            ICreadorPedidoCurso creador, IFacturadorPedidoCurso facturador, ILiquidadorSenalCurso liquidador,
            IAvisadorFacturacionCurso avisador, IEmisorCuponEvento emisor, Func<DateTime> reloj, string usuarioAutomatico)
        {
            this.db = db;
            this.interruptor = interruptor ?? (() => false);
            this.repositorio = repositorio;
            this.creador = creador;
            this.facturador = facturador;
            this.liquidador = liquidador;
            this.avisador = avisador;
            this.emisor = emisor ?? new EmisorCuponEventoNulo();
            this.reloj = reloj ?? (() => DateTime.Now);
            this.usuarioAutomatico = string.IsNullOrWhiteSpace(usuarioAutomatico) ? InterruptoresEventos.USUARIO_AUTOMATICO : usuarioAutomatico.Trim();
        }

        public bool Encendido => interruptor();

        /// <summary>El enganche del corte 1. Con el interruptor apagado no hace nada (la inscripción se queda Pagada).</summary>
        public async Task Procesar(EventoInscripcion inscripcion, Evento evento)
        {
            if (!interruptor())
            {
                return;
            }
            _ = await ConfirmarSenal(inscripcion, evento, aMano: false).ConfigureAwait(false);
        }

        public async Task<ResultadoConfirmacionSenal> ConfirmarSenal(EventoInscripcion inscripcion, Evento evento, bool aMano)
        {
            if (inscripcion == null || evento == null)
            {
                throw new ArgumentNullException(inscripcion == null ? nameof(inscripcion) : nameof(evento));
            }
            if (!interruptor())
            {
                throw new NestoBusinessException($"La facturación automática de los cursos está apagada ({InterruptoresEventos.CLAVE_FACTURAR_CURSO}).");
            }
            EstadoInscripcionEvento estado = ReglasInscripcionEvento.Estado(inscripcion.Estado) ?? EstadoInscripcionEvento.Anulada;
            if (!ReglasInscripcionEvento.EN_LISTA_ASISTENCIA.Contains(estado))
            {
                throw new NestoBusinessException(
                    $"La inscripción {inscripcion.Id} está {ReglasInscripcionEvento.Texto(estado).ToLowerInvariant()}: solo se factura el curso de una inscripción pagada.");
            }

            string usuario = ReglasFacturacionCurso.UsuarioAuditoria(inscripcion, usuarioAutomatico);
            FacturacionCursoInscripcion previa = await repositorio.Leer(inscripcion.Id).ConfigureAwait(false);
            if (previa?.Estado == EstadosFacturacionCurso.FACTURADA)
            {
                // Idempotente: una señal ya confirmada no vuelve a facturar. El cupón sí se reintenta (también es idempotente).
                await EmitirCupon(inscripcion, evento, previa, usuario).ConfigureAwait(false);
                return new ResultadoConfirmacionSenal { Hecho = true, Omitido = true, Facturacion = previa, Mensaje = "El curso ya estaba facturado." };
            }

            if (!await repositorio.Reclamar(inscripcion.Id, Limpiar(inscripcion.Empresa), usuario).ConfigureAwait(false))
            {
                string ocupada = $"La facturación del curso de la inscripción {inscripcion.Id} la está haciendo ahora otro proceso.";
                if (aMano)
                {
                    throw new NestoBusinessException(ocupada) { StatusCode = HttpStatusCode.Conflict };
                }
                return new ResultadoConfirmacionSenal { Omitido = true, Mensaje = ocupada };
            }

            FacturacionCursoInscripcion fila = await repositorio.Leer(inscripcion.Id).ConfigureAwait(false)
                ?? new FacturacionCursoInscripcion { InscripcionId = inscripcion.Id, EmpresaPedido = Limpiar(inscripcion.Empresa), Estado = EstadosFacturacionCurso.EN_CURSO };
            try
            {
                string motivo = ReglasFacturacionCurso.MotivoNoAutomatizable(inscripcion, evento);
                if (motivo != null && fila.Pedido == null)
                {
                    bool nuevo = fila.Estado != EstadosFacturacionCurso.NO_AUTOMATIZABLE || fila.Mensaje != motivo;
                    fila.Estado = EstadosFacturacionCurso.NO_AUTOMATIZABLE;
                    fila.Mensaje = motivo;
                    fila.Usuario = usuario;
                    await repositorio.Guardar(fila).ConfigureAwait(false);
                    if (nuevo)
                    {
                        await avisador.Avisar(Asunto(inscripcion, evento, "no se factura solo"), CuerpoAviso(inscripcion, evento, motivo, fila)).ConfigureAwait(false);
                    }
                    if (aMano)
                    {
                        throw new NestoBusinessException(motivo);
                    }
                    return new ResultadoConfirmacionSenal { Facturacion = fila, Mensaje = motivo };
                }

                fila.Estado = EstadosFacturacionCurso.EN_CURSO;
                fila.Usuario = usuario;

                // 1. El pedido del curso (el creador apunta el número en la fila en su misma transacción)
                if (fila.Pedido == null)
                {
                    PedidoCursoCreado pedido = await creador.Crear(inscripcion, evento, usuario).ConfigureAwait(false);
                    fila.EmpresaPedido = pedido.Empresa;
                    fila.Pedido = pedido.Pedido;
                }

                // 2. La factura exenta, al momento (sin esperar a rutas). Nunca se repite: si ya está, se salta.
                if (string.IsNullOrWhiteSpace(fila.Factura))
                {
                    FacturaCursoCreada factura = await facturador.Facturar(fila.EmpresaPedido, fila.Pedido.Value, usuario).ConfigureAwait(false);
                    fila.EmpresaPedido = factura.EmpresaPedido ?? fila.EmpresaPedido;
                    fila.EmpresaFactura = factura.EmpresaFactura;
                    fila.Factura = factura.Factura;
                    fila.EstadoLiquidacion = EstadosLiquidacionCurso.PENDIENTE;
                    await repositorio.Guardar(fila).ConfigureAwait(false);
                }

                // 3. La señal, liquidada contra la factura (nada a favor del cliente ni nada pendiente para Administración)
                if (fila.EstadoLiquidacion != EstadosLiquidacionCurso.LIQUIDADA && fila.EstadoLiquidacion != EstadosLiquidacionCurso.SIN_COBRO)
                {
                    LiquidacionSenalCurso liquidacion = await liquidador.Liquidar(inscripcion, fila.EmpresaFactura, fila.Factura, usuario).ConfigureAwait(false);
                    fila.EstadoLiquidacion = liquidacion.Estado;
                    fila.NumOrdenCobro = liquidacion.NumOrdenCobro;
                    fila.NumOrdenEfecto = liquidacion.NumOrdenEfecto;
                    fila.Mensaje = liquidacion.Mensaje;
                    if (liquidacion.AvisarAdministracion)
                    {
                        await avisador.Avisar(Asunto(inscripcion, evento, "falta liquidar el cobro"),
                            CuerpoAviso(inscripcion, evento, liquidacion.Mensaje, fila)).ConfigureAwait(false);
                    }
                }

                // 4. La plaza: la inscripción pasa a Facturada (si ya se pasó lista, se queda como está)
                if (ReglasInscripcionEvento.Estado(inscripcion.Estado) == EstadoInscripcionEvento.Pagada)
                {
                    inscripcion.Estado = EstadoInscripcionEvento.Facturada.ToString();
                    inscripcion.Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
                    inscripcion.FechaModificacion = reloj();
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                }

                fila.Estado = EstadosFacturacionCurso.FACTURADA;
                await repositorio.Guardar(fila).ConfigureAwait(false);
            }
            catch (NestoBusinessException) when (fila.Estado == EstadosFacturacionCurso.NO_AUTOMATIZABLE)
            {
                throw;
            }
            catch (Exception ex)
            {
                fila.Estado = EstadosFacturacionCurso.ERROR;
                fila.Intentos++;
                fila.Mensaje = Recortar(ex.Message, 1000);
                try
                {
                    await repositorio.Guardar(fila).ConfigureAwait(false);
                }
                catch (Exception exGuardar)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #612] No se ha podido apuntar el error de la inscripción {inscripcion.Id}: {exGuardar.Message}", exGuardar));
                }
                ElmahHelper.Log(new Exception($"[Cursos #612] Inscripción {inscripcion.Id} (cliente {Limpiar(inscripcion.Cliente)}, evento " +
                    $"«{evento.Titulo?.Trim()}»): ha fallado la facturación del curso (intento {fila.Intentos}). Pedido {fila.Pedido?.ToString() ?? "-"}, " +
                    $"factura {Limpiar(fila.Factura) ?? "-"}. {ex.Message}", ex));
                if (fila.Intentos == 1 || aMano)
                {
                    await avisador.Avisar(Asunto(inscripcion, evento, "error al facturar el curso"),
                        CuerpoAviso(inscripcion, evento, "Ha fallado: " + ex.Message + " Lo hecho se queda hecho; se vuelve a intentar " +
                        "desde ese punto cada mañana o con «Facturar» en la inscripción.", fila)).ConfigureAwait(false);
                }
                if (aMano)
                {
                    throw new NestoBusinessException($"No se ha podido terminar la facturación del curso: {ex.Message}");
                }
                return new ResultadoConfirmacionSenal { Facturacion = fila, Mensaje = fila.Mensaje };
            }
            finally
            {
                try
                {
                    await repositorio.Soltar(inscripcion.Id).ConfigureAwait(false);
                }
                catch (Exception exSoltar)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #612] No se ha podido soltar la inscripción {inscripcion.Id}: {exSoltar.Message}", exSoltar));
                }
            }

            // 5. El cupón (corte 3). Si falla, la factura ya está: lo reintenta el job.
            await EmitirCupon(inscripcion, evento, fila, usuario).ConfigureAwait(false);
            return new ResultadoConfirmacionSenal { Hecho = true, Facturacion = fila };
        }

        private async Task EmitirCupon(EventoInscripcion inscripcion, Evento evento, FacturacionCursoInscripcion fila, string usuario)
        {
            try
            {
                await emisor.Emitir(inscripcion, evento, fila, usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #613] El curso de la inscripción {inscripcion.Id} está facturado ({Limpiar(fila.Factura)}), " +
                    $"pero no se ha podido emitir el cupón (lo reintenta el job): {ex.Message}", ex));
            }
        }

        private static string Asunto(EventoInscripcion inscripcion, Evento evento, string que) =>
            $"Curso «{evento.Titulo?.Trim()}», cliente {Limpiar(inscripcion.Cliente)}: {que}";

        internal static string CuerpoAviso(EventoInscripcion inscripcion, Evento evento, string motivo, FacturacionCursoInscripcion fila)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            return "<p>" + Html(motivo) + "</p>" +
                "<ul>" +
                $"<li>Evento: {Html(evento.Titulo?.Trim())} ({evento.Fecha:dd/MM/yyyy}), señal {ReglasFacturacionCurso.Euros(evento.ImporteSenal)} €, " +
                $"curso {Html(Limpiar(evento.ProductoCurso) ?? "sin referencia")}</li>" +
                $"<li>Inscripción {inscripcion.Id}: cliente {Html(Limpiar(inscripcion.Cliente))}/{Html(Limpiar(inscripcion.Contacto))}, " +
                $"pagado {ReglasFacturacionCurso.Euros(inscripcion.ImportePagado ?? 0)} € ({Html(Limpiar(inscripcion.FormaPago))})" +
                (inscripcion.EventoSenalId.HasValue ? $", señal {inscripcion.EventoSenalId}" : string.Empty) + "</li>" +
                $"<li>Pedido: {(fila?.Pedido?.ToString() ?? "sin crear")}; factura: {Html(Limpiar(fila?.Factura) ?? "sin hacer")}</li>" +
                "</ul><p style='color:#666'>NestoAPI#612. La inscripción está en Nesto, en el evento.</p>";
        }

        private static string Recortar(string texto, int max) =>
            texto == null ? null : texto.Length > max ? texto.Substring(0, max) : texto;

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }
}
