using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#606: lo que hace falta de un pedido grabado para calcular su fecha de entrega a la agencia.</summary>
    public class PedidoFechaEntregaAgencia
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public string Ruta { get; set; }
        public byte? ModoServicio { get; set; }
        public bool ServirJunto { get; set; }
        public byte? ModoFacturacion { get; set; }
        public bool MantenerJunto { get; set; }
        public string DiasEnServir { get; set; }
        /// <summary>NestoAPI#764: el código postal de entrega (el del contacto del pedido), para el día de reparto de nuestra ruta.</summary>
        public string CodigoPostal { get; set; }
        /// <summary>Alguna línea ya en albarán o factura.</summary>
        public bool TieneLineasServidas { get; set; }
        /// <summary>Las líneas de producto pendientes (-1 y 1), en el orden del picking.</summary>
        public List<LineaPedidoFechaEntregaAgencia> Lineas { get; set; } = new List<LineaPedidoFechaEntregaAgencia>();
    }

    /// <summary>NestoAPI#606: acceso a datos de <see cref="ServicioFechaEntregaAgencia"/> (sustituible en los tests).</summary>
    public interface IRepositorioFechaEntregaAgencia
    {
        /// <summary>Null si el pedido no existe.</summary>
        Task<PedidoFechaEntregaAgencia> LeerPedido(string empresa, int numero);
        Task<string> LeerDiasEnServir(string empresa, string cliente, string contacto);
        /// <summary>NestoAPI#764: el código postal del contacto (el principal o el primero si no está), o null.</summary>
        Task<string> LeerCodigoPostal(string empresa, string cliente, string contacto);
        /// <summary>Stock de los productos, con los pendientes SIN las líneas de <paramref name="pedidoExcluir"/>.</summary>
        DatosSombraModoServicio LeerStock(string empresa, int? pedidoExcluir, IEnumerable<string> productos);
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);
        TimeSpan LeerHoraCorte(string empresa);
        /// <summary>Null si no hay, o si la columna aún no existe.</summary>
        Task<DateTime?> LeerPrometida(string empresa, int numero);
        /// <summary>Solo si estaba a NULL (se escribe una vez). True si la ha escrito.</summary>
        Task<bool> GuardarPrometida(string empresa, int numero, DateTime fecha);
        /// <summary>Sugerencia 832: la sustituye tenga lo que tenga. True si ha escrito.</summary>
        Task<bool> ReemplazarPrometida(string empresa, int numero, DateTime fecha);
    }

    public interface IServicioFechaEntregaAgencia
    {
        /// <summary>La del pedido grabado (detalle del pedido), con la prometida. Null si el pedido no existe.</summary>
        Task<FechaEntregaAgenciaDTO> CalcularPedido(string empresa, int numero);
        /// <summary>La de la plantilla, sin pedido creado (o modificándolo: sus líneas no cuentan como pendientes de otros).</summary>
        Task<FechaEntregaAgenciaDTO> CalcularPlantilla(PedidoVentaDTO pedido);
        /// <summary>Al crear el pedido: calcula y guarda la prometida. NUNCA lanza (crear el pedido no puede fallar por esto).</summary>
        Task<DateTime?> GuardarPrometidaAlCrear(string empresa, int numero);
        /// <summary>
        /// NestoAPI#606 (correo del pedido, 09/10): lo mismo que <see cref="GuardarPrometidaAlCrear"/>, pero devuelve el cálculo
        /// entero (fecha, desglose y, si se ha podido guardar, la prometida) para enseñarlo en el correo del alta. Si el cálculo
        /// falla devuelve null; si solo falla al guardar, el cálculo sin prometida. NUNCA lanza.
        /// </summary>
        Task<FechaEntregaAgenciaDTO> CalcularYGuardarPrometidaAlCrear(string empresa, int numero);
        /// <summary>
        /// NestoAPI#606 (correo del pedido, 09/10): <see cref="CalcularPedido"/> para el correo de la modificación (con la
        /// prometida de la columna). Null si falla (a ELMAH, como mucho una vez cada media hora). NUNCA lanza.
        /// </summary>
        Task<FechaEntregaAgenciaDTO> CalcularPedidoParaCorreo(string empresa, int numero);
        /// <summary>
        /// Sugerencia 832 (Lidia, 28/10/26; OK de Carlos): el usuario ha ADELANTADO la fecha de entrega de un pedido sin picking.
        /// Se recalcula y, si la nueva fecha de entrega a la agencia es anterior a la prometida (o no había), la prometida pasa a
        /// ser la nueva. Devuelve el cálculo (con la prometida que queda) para el correo; null si falla. NUNCA lanza.
        /// </summary>
        Task<FechaEntregaAgenciaDTO> AdelantarPrometida(string empresa, int numero);
    }

    /// <summary>
    /// NestoAPI#606: orquesta <see cref="RepartidorStockFechaEntregaAgencia"/> y <see cref="CalculadoraFechaEntregaAgencia"/>
    /// con los datos de la BD (pedido, stock, calendario de reposiciones, hora de corte y días que cierra el cliente).
    /// </summary>
    public class ServicioFechaEntregaAgencia : IServicioFechaEntregaAgencia
    {
        private readonly IRepositorioFechaEntregaAgencia repositorio;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly Action<Exception> registrar;
        private readonly IAvisadorProveedorVencido avisadorProveedorVencido;
        private readonly Action<string, IEnumerable<string>, string, IDictionary<string, CausaPromesaRiesgo>> anotarPedidoGrabado;
        /// <summary>NestoAPI#730: el reparto del stock de Algete por antigüedad, el MISMO del picking.</summary>
        private readonly IRepartoStockPorAntiguedad repartoStock;

        public ServicioFechaEntregaAgencia(NVEntities db)
            : this(new RepositorioFechaEntregaAgencia(db), () => DateTime.Now, GestorFestivos.EsFestivo,
                  ex => ElmahHelper.Log(ex), new AvisadorProveedorVencido())
        {
        }

        internal ServicioFechaEntregaAgencia(IRepositorioFechaEntregaAgencia repositorio, Func<DateTime> reloj,
            Func<DateTime, string, bool> esFestivo, Action<Exception> registrar, IAvisadorProveedorVencido avisadorProveedorVencido = null,
            Action<string, IEnumerable<string>, string, IDictionary<string, CausaPromesaRiesgo>> anotarPedidoGrabado = null,
            IRepartoStockPorAntiguedad repartoStock = null)
        {
            this.anotarPedidoGrabado = anotarPedidoGrabado ?? EventosPromesaRiesgo.Anotar;
            this.repartoStock = repartoStock ?? RepartoStockPorAntiguedad.PorDefecto();
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.reloj = reloj ?? (() => DateTime.Now);
            this.esFestivo = esFestivo ?? GestorFestivos.EsFestivo;
            this.registrar = registrar ?? (_ => { });
            this.avisadorProveedorVencido = avisadorProveedorVencido;
        }

        public async Task<FechaEntregaAgenciaDTO> CalcularPedido(string empresa, int numero)
        {
            return (await CalcularPedidoConLineas(empresa, numero).ConfigureAwait(false)).dto;
        }

        private async Task<(FechaEntregaAgenciaDTO dto, PedidoFechaEntregaAgencia pedido)> CalcularPedidoConLineas(string empresa, int numero)
        {
            string empresaLimpia = Empresa(empresa);
            PedidoFechaEntregaAgencia pedido = await repositorio.LeerPedido(empresaLimpia, numero).ConfigureAwait(false);
            if (pedido == null)
            {
                return (null, null);
            }
            FechaEntregaAgenciaDTO dto = await Calcular(empresaLimpia, numero, pedido.Ruta,
                Constantes.Pedidos.ModosServicio.Efectivo(pedido.ModoServicio, pedido.ServirJunto),
                Constantes.Pedidos.ModosFacturacion.Efectivo(pedido.ModoFacturacion, pedido.MantenerJunto),
                pedido.DiasEnServir, pedido.TieneLineasServidas, pedido.Lineas, $"el pedido {numero}", pedido.CodigoPostal).ConfigureAwait(false);
            dto.FechaPrometida = await repositorio.LeerPrometida(empresaLimpia, numero).ConfigureAwait(false);
            return (dto, pedido);
        }

        public async Task<FechaEntregaAgenciaDTO> CalcularPlantilla(PedidoVentaDTO pedido)
        {
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }
            string empresa = Empresa(pedido.empresa);
            string diasEnServir = string.IsNullOrWhiteSpace(pedido.cliente)
                ? null
                : await repositorio.LeerDiasEnServir(empresa, pedido.cliente.Trim(), pedido.contacto?.Trim()).ConfigureAwait(false);
            List<LineaPedidoFechaEntregaAgencia> lineas = (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    && l.estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(l => new LineaPedidoFechaEntregaAgencia
                {
                    Producto = l.Producto,
                    Almacen = string.IsNullOrWhiteSpace(l.almacen) ? Constantes.Almacenes.ALGETE : l.almacen,
                    Cantidad = l.Cantidad - l.recoger,
                    BaseImponible = l.BaseImponible,
                    FechaEntrega = l.fechaEntrega > DateTime.MinValue ? l.fechaEntrega : (DateTime?)null,
                    YaEnPicking = l.picking > 0,
                    // NestoAPI#730: la línea ya grabada conserva su turno si no ha cambiado de cantidad (la nueva va al final)
                    Id = l.id > 0 ? l.id : (int?)null
                })
                .ToList();
            bool tieneLineasServidas = (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Any(l => l != null && l.estado >= Constantes.EstadosLineaVenta.ALBARAN);
            // Como ModosServicio/ModosFacturacion.Normalizar al grabar: el modo informado manda; sin él, el bit.
            byte modoServicio = pedido.modoServicio.HasValue && Constantes.Pedidos.ModosServicio.EsValido(pedido.modoServicio.Value)
                ? pedido.modoServicio.Value
                : Constantes.Pedidos.ModosServicio.Efectivo(null, pedido.servirJunto);
            byte modoFacturacion = pedido.modoFacturacion.HasValue && Constantes.Pedidos.ModosFacturacion.EsValido(pedido.modoFacturacion.Value)
                ? pedido.modoFacturacion.Value
                : Constantes.Pedidos.ModosFacturacion.Efectivo(null, pedido.mantenerJunto);
            // NestoAPI#764: el código postal solo hace falta si va por ruta interna (día de reparto)
            string codigoPostal = string.IsNullOrWhiteSpace(pedido.cliente) || !DiasRepartoCodigosPostales.PuedeTenerDia(pedido.ruta)
                ? null
                : await repositorio.LeerCodigoPostal(empresa, pedido.cliente.Trim(), pedido.contacto?.Trim()).ConfigureAwait(false);
            return await Calcular(empresa, pedido.numero > 0 ? pedido.numero : (int?)null, pedido.ruta, modoServicio, modoFacturacion,
                diasEnServir, tieneLineasServidas, lineas, QuienEspera(pedido), codigoPostal).ConfigureAwait(false);
        }

        public async Task<DateTime?> GuardarPrometidaAlCrear(string empresa, int numero)
        {
            (FechaEntregaAgenciaDTO dto, bool fallo) = await CalcularYGuardar(empresa, numero).ConfigureAwait(false);
            return fallo ? null : dto?.FechaEntregaAgencia;
        }

        public async Task<FechaEntregaAgenciaDTO> CalcularYGuardarPrometidaAlCrear(string empresa, int numero)
        {
            return (await CalcularYGuardar(empresa, numero).ConfigureAwait(false)).dto;
        }

        public async Task<FechaEntregaAgenciaDTO> AdelantarPrometida(string empresa, int numero)
        {
            FechaEntregaAgenciaDTO dto;
            try
            {
                PedidoFechaEntregaAgencia pedido;
                (dto, pedido) = await CalcularPedidoConLineas(empresa, numero).ConfigureAwait(false);
                AnotarPedidoGrabado(empresa, numero, pedido);
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido recalcular la fecha de entrega a la agencia prometida del pedido {numero} al adelantar la entrega", ex);
                return null;
            }
            DateTime? fecha = dto?.FechaEntregaAgencia;
            if (!fecha.HasValue || (dto.FechaPrometida.HasValue && fecha.Value.Date >= dto.FechaPrometida.Value.Date))
            {
                return dto; // sin fecha, o no mejora la promesa: se queda la que había
            }
            try
            {
                if (await repositorio.ReemplazarPrometida(Empresa(empresa), numero, fecha.Value.Date).ConfigureAwait(false))
                {
                    dto.FechaPrometida = fecha.Value.Date;
                }
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido sustituir la fecha de entrega a la agencia prometida del pedido {numero} al adelantar la entrega", ex);
            }
            return dto;
        }

        public async Task<FechaEntregaAgenciaDTO> CalcularPedidoParaCorreo(string empresa, int numero)
        {
            try
            {
                (FechaEntregaAgenciaDTO dto, PedidoFechaEntregaAgencia pedido) = await CalcularPedidoConLineas(empresa, numero).ConfigureAwait(false);
                AnotarPedidoGrabado(empresa, numero, pedido);
                return dto;
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido calcular la fecha de entrega a la agencia del pedido {numero} para el correo de la modificación " +
                    "(el correo sale sin ella)", ex);
                return null;
            }
        }

        /// <summary>
        /// Calcula y guarda la prometida (una sola vez: el UPDATE lleva IS NULL). <c>fallo</c> = algo ha ido mal (calcular o
        /// guardar); si solo ha fallado al guardar, el cálculo se devuelve igual (sin prometida) para el correo.
        /// </summary>
        private async Task<(FechaEntregaAgenciaDTO dto, bool fallo)> CalcularYGuardar(string empresa, int numero)
        {
            FechaEntregaAgenciaDTO dto;
            try
            {
                PedidoFechaEntregaAgencia pedido;
                (dto, pedido) = await CalcularPedidoConLineas(empresa, numero).ConfigureAwait(false);
                AnotarPedidoGrabado(empresa, numero, pedido);
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido calcular ni guardar la fecha de entrega a la agencia prometida del pedido {numero}", ex);
                return (null, true);
            }
            DateTime? fecha = dto?.FechaEntregaAgencia;
            if (!fecha.HasValue)
            {
                return (dto, false);
            }
            try
            {
                if (await repositorio.GuardarPrometida(Empresa(empresa), numero, fecha.Value.Date).ConfigureAwait(false))
                {
                    dto.FechaPrometida = fecha.Value.Date;
                }
                return (dto, false);
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido guardar la fecha de entrega a la agencia prometida del pedido {numero}", ex);
                return (dto, true);
            }
        }

        /// <summary>
        /// NestoAPI#693: un pedido grabado (creado o modificado; todos los caminos pasan por aquí: Nesto, NestoApp, la tienda y
        /// UnirPedidos) cambia lo pendiente de sus productos: se recalculan los pedidos con promesa que los llevan, él incluido.
        /// Con el interruptor apagado no hace nada; nunca lanza.
        /// </summary>
        private void AnotarPedidoGrabado(string empresa, int numero, PedidoFechaEntregaAgencia pedido)
        {
            try
            {
                List<string> productos = (pedido?.Lineas ?? new List<LineaPedidoFechaEntregaAgencia>())
                    .Where(l => l != null && l.Cantidad > 0 && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!productos.Any())
                {
                    return;
                }
                anotarPedidoGrabado(Empresa(empresa), productos, $"pedido {numero} grabado",
                    productos.ToDictionary(p => p, p => new CausaPromesaRiesgo { PedidoOrigen = numero }, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // Ni el aviso puede romper la creación del pedido ni su correo.
            }
        }

        private static TransactionScope SinTransaccion()
            => new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);

        /// <summary>A ELMAH como mucho una vez cada <see cref="INTERVALO_AVISOS"/>. Nunca lanza.</summary>
        private void Registrar(string que, Exception ex)
        {
            try
            {
                if (TocaRegistrar(reloj()))
                {
                    registrar(new Exception($"NestoAPI#606: {que} (se avisa como mucho una vez cada {INTERVALO_AVISOS.TotalMinutes:0} minutos): {ex.Message}", ex));
                }
            }
            catch
            {
                // Ni el registro puede romper la creación del pedido ni su correo.
            }
        }

        /// <summary>Si la columna aún no existe, fallaría al crear CADA pedido: a ELMAH, como mucho una vez cada media hora.</summary>
        internal static readonly TimeSpan INTERVALO_AVISOS = TimeSpan.FromMinutes(30);
        private static readonly object cerrojoAvisos = new object();
        private static DateTime ultimoAviso = DateTime.MinValue;

        internal static bool TocaRegistrar(DateTime ahora)
        {
            lock (cerrojoAvisos)
            {
                if (ahora - ultimoAviso < INTERVALO_AVISOS && ahora >= ultimoAviso)
                {
                    return false;
                }
                ultimoAviso = ahora;
                return true;
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos()
        {
            lock (cerrojoAvisos)
            {
                ultimoAviso = DateTime.MinValue;
            }
        }

        private async Task<FechaEntregaAgenciaDTO> Calcular(string empresa, int? pedidoExcluir, string ruta, byte modoServicio, byte modoFacturacion,
            string diasEnServir, bool tieneLineasServidas, List<LineaPedidoFechaEntregaAgencia> lineas, string quienEspera, string codigoPostal)
        {
            DateTime ahora = reloj();
            List<LineaPedidoFechaEntregaAgencia> deAlgete = (lineas ?? new List<LineaPedidoFechaEntregaAgencia>())
                .Where(l => l != null && l.Cantidad > 0 && RepartidorStockFechaEntregaAgencia.EsDeAlgete(l.Almacen))
                .ToList();
            List<string> productos = deAlgete.Select(l => l.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Sugerencia 579 (Marta, 09/10): dentro del TransactionScope de UnirPedidos (PutPedidoVenta → correo de la
            // modificación), el stock y la hora de corte se leían con conexiones propias (NestoConnection, otro NVEntities)
            // que se alistaban en la transacción y la escalaban a distribuida; MSDTC está deshabilitado, la transacción
            // quedaba muerta y la unión fallaba entera. Son lecturas de apoyo (el stock con NOLOCK): fuera de la transacción.
            DatosSombraModoServicio datos;
            TimeSpan horaCorte;
            Dictionary<LineaPedidoFechaEntregaAgencia, int> enAlgete;
            using (SinTransaccion())
            {
                datos = productos.Any()
                    ? repositorio.LeerStock(empresa, pedidoExcluir, productos)
                    : new DatosSombraModoServicio();
                horaCorte = repositorio.LeerHoraCorte(empresa);
                enAlgete = productos.Any()
                    ? EnAlgetePorAntiguedad(empresa, pedidoExcluir, lineas, ahora)
                    : new Dictionary<LineaPedidoFechaEntregaAgencia, int>();
            }
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresa).ConfigureAwait(false) ?? new List<ReposicionCalendario>();

            List<string> tiendasPorOrden = TiendasPorOrden(new CalculadoraFechaReposicion(esFestivo), calendario, horaCorte, ahora, empresa);

            var entrada = new EntradaFechaEntregaAgencia
            {
                Empresa = empresa,
                Lineas = RepartidorStockFechaEntregaAgencia.Repartir(deAlgete, datos, tiendasPorOrden, ahora.Date, enAlgete),
                ModoServicio = modoServicio,
                ModoFacturacion = modoFacturacion,
                Ruta = ruta?.Trim(),
                DiaRutaInterna = DiasRepartoCodigosPostales.DiaRutaInterna(ruta, codigoPostal),
                DiasEnServir = diasEnServir,
                TieneLineasServidas = tieneLineasServidas,
                Ahora = ahora,
                HoraCorte = horaCorte,
                Calendario = calendario
            };
            ResultadoFechaEntregaAgencia resultado = new CalculadoraFechaEntregaAgencia(esFestivo).Calcular(entrada);

            // Decisión de Carlos (08/10): si la fecha depende de un pedido a proveedor con la fecha prevista vencida, aviso en la
            // campana a Compras (deduplicado por línea y día; nunca lanza).
            if (avisadorProveedorVencido != null && resultado.ProveedorVencido.Any())
            {
                // El aviso escribe en el buzón de Compras con sus propias conexiones: tampoco va en la transacción de quien llama.
                using (SinTransaccion())
                {
                    await avisadorProveedorVencido.Avisar(empresa, resultado.ProveedorVencido, quienEspera).ConfigureAwait(false);
                }
            }

            return new FechaEntregaAgenciaDTO
            {
                PrimeraEntrega = resultado.PrimeraEntrega,
                EntregaCompleta = resultado.EntregaCompleta,
                FechaEntregaAgencia = resultado.FechaQueAplica,
                Aplica = resultado.AplicaCompleta ? FechaEntregaAgenciaDTO.APLICA_COMPLETA : FechaEntregaAgenciaDTO.APLICA_PRIMERA,
                Entregas = resultado.Entregas,
                Motivo = resultado.Motivo,
                Aviso = resultado.Aviso,
                Desglose = resultado.Desglose
            };
        }

        /// <summary>
        /// NestoAPI#730 (sugerencia 861): lo que el picking le daría ahora de Algete a cada línea del pedido, repartiendo por
        /// antigüedad con TODAS las líneas pendientes de sus productos (<see cref="IRepartoStockPorAntiguedad"/>). Van todas las
        /// líneas del pedido, también las de tienda: en el picking compiten igual.
        /// </summary>
        private Dictionary<LineaPedidoFechaEntregaAgencia, int> EnAlgetePorAntiguedad(string empresa, int? pedido,
            List<LineaPedidoFechaEntregaAgencia> lineas, DateTime ahora)
        {
            List<LineaPedidoFechaEntregaAgencia> conCantidad = (lineas ?? new List<LineaPedidoFechaEntregaAgencia>())
                .Where(l => l != null && l.Cantidad != 0 && !string.IsNullOrWhiteSpace(l.Producto))
                .ToList();
            int[] reservadas = repartoStock.UnidadesReservadas(empresa, pedido, conCantidad
                .Select(l => new LineaRepartoStock
                {
                    Id = l.Id,
                    Producto = l.Producto,
                    Almacen = l.Almacen,
                    Cantidad = l.Cantidad,
                    FechaModificacion = l.FechaModificacion
                })
                .ToList(), ahora) ?? new int[0];
            var resultado = new Dictionary<LineaPedidoFechaEntregaAgencia, int>();
            for (int i = 0; i < conCantidad.Count; i++)
            {
                resultado[conCantidad[i]] = i < reservadas.Length ? reservadas[i] : 0;
            }
            return resultado;
        }

        /// <summary>
        /// Decisión de Carlos (08/10): las unidades de las tiendas se cogen primero de la tienda cuya reposición deja salir antes
        /// el pedido y, a igualdad de día, de la que LLEGA antes a Algete (cada fila del calendario tiene su hora de llegada).
        /// Una tienda sin calendario va la última.
        /// </summary>
        internal static List<string> TiendasPorOrden(CalculadoraFechaReposicion calculadora, IEnumerable<ReposicionCalendario> calendario,
            TimeSpan horaCorte, DateTime ahora, string empresa)
        {
            return RepartidorStockFechaEntregaAgencia.TIENDAS
                .Select((tienda, indice) => new
                {
                    Tienda = tienda,
                    Indice = indice,
                    Reposicion = calculadora.Calcular(calendario, tienda, Constantes.Almacenes.ALGETE, horaCorte, ahora, empresa)
                })
                .OrderBy(t => t.Reposicion?.PedidoSaleEl ?? DateTime.MaxValue)
                .ThenBy(t => t.Reposicion?.LlegaEl ?? DateTime.MaxValue)
                .ThenBy(t => t.Indice)
                .Select(t => t.Tienda)
                .ToList();
        }

        private static string QuienEspera(PedidoVentaDTO pedido)
        {
            if (pedido.numero > 0)
            {
                return $"el pedido {pedido.numero}, que se está modificando en la plantilla";
            }
            return string.IsNullOrWhiteSpace(pedido.cliente)
                ? "un pedido que se está montando en la plantilla de venta"
                : $"un pedido del cliente {pedido.cliente.Trim()} que se está montando en la plantilla de venta";
        }

        private static string Empresa(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
